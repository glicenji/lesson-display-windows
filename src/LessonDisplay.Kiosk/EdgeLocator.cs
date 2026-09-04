using Microsoft.Win32;

namespace LessonDisplay.Kiosk;

/// <summary>Finds msedge.exe without assuming a fixed install path.</summary>
internal static class EdgeLocator
{
    public static string? Find()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        try
        {
            var regValue = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Clients\StartMenuInternet\Microsoft Edge\shell\open\command",
                null, null) as string;
            if (!string.IsNullOrEmpty(regValue))
            {
                var path = regValue.Trim('"');
                if (File.Exists(path)) return path;
            }
        }
        catch
        {
            // registry key not present on this machine — fall through
        }

        return null;
    }
}
