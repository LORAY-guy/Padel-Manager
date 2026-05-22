using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Padel.Manager.Services;

public static class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/LORAY-guy/Padel-Manager/releases/latest";
    private const string InstallerAssetName = "PadelManager-win-x64-Setup.exe";

    public record UpdateInfo(string Version, string DownloadUrl);

    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var current = Assembly.GetEntryAssembly()?.GetName().Version;
        if (current is null) return null;

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PadelManager-Updater");

        string json;
        try { json = await http.GetStringAsync(ApiUrl); }
        catch { return null; }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var vStr = tag.TrimStart('v');

        if (!Version.TryParse(vStr, out var latest)) return null;
        if (!IsNewer(latest, current)) return null;

        if (!root.TryGetProperty("assets", out var assets)) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == InstallerAssetName)
            {
                var url = asset.GetProperty("browser_download_url").GetString();
                if (url is not null) return new UpdateInfo(vStr, url);
            }
        }

        return null;
    }

    public static async Task DownloadAndLaunchAsync(string downloadUrl)
    {
        var dest = Path.Combine(Path.GetTempPath(), InstallerAssetName);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PadelManager-Updater");

        var bytes = await http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(dest, bytes);

        Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true });
    }

    private static bool IsNewer(Version latest, Version current)
    {
        if (latest.Major != current.Major) return latest.Major > current.Major;
        if (latest.Minor != current.Minor) return latest.Minor > current.Minor;
        var lb = Math.Max(0, latest.Build);
        var cb = Math.Max(0, current.Build);
        if (lb != cb) return lb > cb;
        return Math.Max(0, latest.Revision) > Math.Max(0, current.Revision);
    }
}
