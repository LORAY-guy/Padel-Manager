using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Padel.Manager.Models;

public sealed class SelectablePlayer : INotifyPropertyChanged
{
    private bool _isSelected;

    public required PlayerProfile Player { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
