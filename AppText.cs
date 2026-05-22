using System.Reflection;

namespace Padel.Manager;

public static class AppText
{
    public static readonly string MainWindowTitle = string.Intern("Padel Manager");

    public static readonly string Version = GetVersion();

    private static string GetVersion()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version;
        if (v is null) return string.Empty;
        return v.Build > 0 ? $"v{v.Major}.{v.Minor}.{v.Build}" : $"v{v.Major}.{v.Minor}";
    }
}
