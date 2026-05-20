using Velopack;

namespace Padel.Manager;

public static class AppText
{
    // Interned once so all title references share the same string instance.
    public static readonly string MainWindowTitle = string.Intern("Padel Manager");

    public static readonly string Version = GetVersion();

    private static string GetVersion()
    {
        try
        {
            var v = new UpdateManager("https://github.com/LORAY-guy/Padel-Manager").CurrentVersion;
            if (v is not null)
                return $"v{v}";
        }
        catch { }

        var asmVersion = typeof(AppText).Assembly.GetName().Version;
        if (asmVersion is not null)
            return $"v{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}";

        return string.Empty;
    }
}
