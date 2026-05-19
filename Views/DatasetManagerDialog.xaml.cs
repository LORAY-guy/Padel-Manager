using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MessageBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using Padel.Manager.Services;

namespace Padel.Manager.Views;

public partial class DatasetManagerDialog : Window
{
    private DatasetRegistry? _registry;
    private string? _currentPath;

    public DatasetManagerDialog() => InitializeComponent();

    public DatasetManagerDialog(DatasetRegistry registry, string? currentPath)
    {
        InitializeComponent();
        _registry = registry;
        _currentPath = currentPath;
        RefreshList(currentPath);
        UpdateButtonStates();
    }

    private void RefreshList(string? selectPath = null)
    {
        var viewModels = _registry!.Datasets
            .Select(d => new DatasetViewModel(d))
            .ToList();

        DatasetListBox.ItemsSource = viewModels;

        DatasetViewModel? toSelect = null;
        if (selectPath is not null)
        {
            toSelect = viewModels.FirstOrDefault(v => string.Equals(v.Entry.Path, selectPath, StringComparison.OrdinalIgnoreCase));
        }

        toSelect ??= viewModels.Count > 0 ? viewModels[0] : null;
        if (toSelect is not null)
        {
            DatasetListBox.SelectedItem = toSelect;
        }

        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var hasSelection = DatasetListBox.SelectedItem is DatasetViewModel;
        OpenButton.IsEnabled = hasSelection;
        ShowLocationButton.IsEnabled = hasSelection;
        RenameButton.IsEnabled = hasSelection;
        RemoveButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
    }

    private void DatasetListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateButtonStates();

    private void DatasetListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DatasetListBox.SelectedItem is DatasetViewModel)
        {
            Open();
        }
    }

    private void OpenButton_OnClick(object? sender, RoutedEventArgs e) => Open();

    private void ShowLocationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DatasetListBox.SelectedItem is DatasetViewModel vm)
            OpenFileLocation(vm.Entry.Path);
    }

    private static void OpenFileLocation(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            else if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", filePath } });
            else
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(filePath) ?? filePath) { UseShellExecute = true });
        }
        catch { }
    }

    private void Open()
    {
        if (DatasetListBox.SelectedItem is not DatasetViewModel vm)
        {
            return;
        }

        Close(vm.Entry);
    }

    private void NewButton_OnClick(object? sender, RoutedEventArgs e)
    {
        NewDatasetPanel.IsVisible = true;
        NewDatasetNameTextBox.Text = string.Empty;
        NewDatasetNameTextBox.Focus();
    }

    private void CancelNewDatasetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        NewDatasetPanel.IsVisible = false;
    }

    private void NewDatasetNameTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CreateDataset();
        }
        else if (e.Key == Key.Escape)
        {
            NewDatasetPanel.IsVisible = false;
        }
    }

    private void CreateDatasetButton_OnClick(object? sender, RoutedEventArgs e) => CreateDataset();

    private void CreateDataset()
    {
        var name = NewDatasetNameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var path = _registry!.BuildNewDatasetPath(name);
        var entry = new DatasetEntry { Name = name, Path = path };
        _registry.AddOrUpdate(entry);
        _registry.Save();

        NewDatasetPanel.IsVisible = false;
        RefreshList(path);
    }

    private async void ImportXlsxButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var xlsxFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Sélectionner le fichier XLSX à importer",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Fichier Excel") { Patterns = new[] { "*.xlsx" } } }
        });

        if (xlsxFiles.Count == 0)
        {
            return;
        }

        var xlsxPath = xlsxFiles[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(xlsxPath))
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(xlsxPath);
        var destPath = _registry!.BuildNewDatasetPath(name);

        try
        {
            await Task.Run(() => new WorkbookService(destPath, xlsxPath));

            var entry = new DatasetEntry
            {
                Name = name,
                Path = destPath,
                LastOpenedUtc = DateTime.UtcNow
            };
            _registry.AddOrUpdate(entry);
            _registry.Save();

            RefreshList(destPath);

            var box = MessageBoxManager.GetMessageBoxStandard(
                "Import terminé",
                $"Le fichier XLSX a été importé dans le dataset « {name} ».",
                ButtonEnum.Ok, MsBoxIcon.Success);
            await box.ShowWindowDialogAsync(this);
        }
        catch (Exception ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Erreur d'import", ex.Message, ButtonEnum.Ok, MsBoxIcon.Error);
            await box.ShowWindowDialogAsync(this);
        }
    }

    private async void OpenFileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Ouvrir un fichier dataset (.padel)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Dataset Padel") { Patterns = new[] { "*.padel" } } }
        });

        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var existing = _registry!.TryGetByPath(path);
        if (existing is null)
        {
            existing = new DatasetEntry
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Path = path
            };
            _registry.AddOrUpdate(existing);
            _registry.Save();
        }

        RefreshList(path);
    }

    private async void RenameButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DatasetListBox.SelectedItem is not DatasetViewModel vm)
        {
            return;
        }

        var newName = await ShowRenameDialogAsync(vm.Entry.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == vm.Entry.Name)
        {
            return;
        }

        _registry!.Rename(vm.Entry.Id, newName);
        _registry.Save();
        RefreshList(vm.Entry.Path);
    }

    private async void RemoveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DatasetListBox.SelectedItem is not DatasetViewModel vm)
        {
            return;
        }

        var box = MessageBoxManager.GetMessageBoxStandard(
            "Confirmation",
            $"Retirer « {vm.Entry.Name} » de la liste ?\n\nLe fichier ne sera pas supprimé.",
            ButtonEnum.YesNo, MsBoxIcon.Question);
        var result = await box.ShowWindowDialogAsync(this);

        if (result != ButtonResult.Yes)
        {
            return;
        }

        _registry!.Remove(vm.Entry.Id);
        _registry.Save();
        RefreshList();
    }

    private async void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DatasetListBox.SelectedItem is not DatasetViewModel vm)
        {
            return;
        }

        var box = MessageBoxManager.GetMessageBoxStandard(
            "Suppression définitive",
            $"Supprimer définitivement le dataset « {vm.Entry.Name} » ?\n\nFichier : {vm.Entry.Path}\n\nCette action est irréversible.",
            ButtonEnum.YesNo, MsBoxIcon.Warning);
        var result = await box.ShowWindowDialogAsync(this);

        if (result != ButtonResult.Yes)
        {
            return;
        }

        try
        {
            if (File.Exists(vm.Entry.Path))
            {
                File.Delete(vm.Entry.Path);
            }
        }
        catch (Exception ex)
        {
            var errBox = MessageBoxManager.GetMessageBoxStandard("Erreur de suppression", ex.Message, ButtonEnum.Ok, MsBoxIcon.Error);
            await errBox.ShowWindowDialogAsync(this);
            return;
        }

        _registry!.Remove(vm.Entry.Id);
        _registry.Save();
        RefreshList();
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private async Task<string?> ShowRenameDialogAsync(string currentName)
    {
        var dialog = new Window
        {
            Title = "Renommer le dataset",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Brushes.White
        };

        var tb = new TextBox { Text = currentName, Margin = new Avalonia.Thickness(0, 0, 0, 10) };
        string? result = null;

        var okBtn = new Button { Content = "OK", MinWidth = 80, Padding = new Avalonia.Thickness(14, 6) };
        var cancelBtn = new Button { Content = "Annuler", MinWidth = 80, Padding = new Avalonia.Thickness(14, 6), Margin = new Avalonia.Thickness(8, 0, 0, 0) };
        okBtn.Click += (_, _) => { result = tb.Text?.Trim(); dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        btns.Children.Add(okBtn);
        btns.Children.Add(cancelBtn);

        var root = new StackPanel { Margin = new Avalonia.Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Nouveau nom :", Margin = new Avalonia.Thickness(0, 0, 0, 6) });
        root.Children.Add(tb);
        root.Children.Add(btns);
        dialog.Content = root;

        await dialog.ShowDialog(this);
        return result;
    }

    public sealed class DatasetViewModel
    {
        public DatasetViewModel(DatasetEntry entry) => Entry = entry;
        public DatasetEntry Entry { get; }
        public string Name => Entry.Name;
        public string Path => Entry.Path;
        public string LastOpenedText => Entry.LastOpenedUtc == default
            ? "Jamais ouvert"
            : $"Dernière ouverture : {Entry.LastOpenedUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
    }
}
