using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AIUsageMonitor.Services;

// Opens a link in a private window of the user's default browser. A private window has no saved
// claude.ai sign-in, so the sign-in page always asks which account to use. Falls back to
// Microsoft Edge InPrivate when the default browser has no known private-window flag.
public static class PrivateBrowserLauncher
{
    public sealed record PrivateBrowser(string Name, string ExecutablePath, string PrivateFlag);

    // Known https ProgIds (by prefix) and the flag that opens a private window.
    private static readonly (string ProgIdPrefix, string Name, string Flag)[] KnownBrowsers =
    [
        ("BraveHTML", "Brave", "--incognito"),
        ("BraveBHTML", "Brave Beta", "--incognito"),
        ("ChromeHTML", "Chrome", "--incognito"),
        ("MSEdgeHTM", "Edge", "--inprivate"),
        ("FirefoxURL", "Firefox", "-private-window"),
        ("FirefoxHTML", "Firefox", "-private-window"),
        ("VivaldiHTM", "Vivaldi", "--incognito"),
        ("OperaStable", "Opera", "--private")
    ];

    public static PrivateBrowser? Find()
    {
        var progId = ReadDefaultHttpsProgId();
        if (!string.IsNullOrWhiteSpace(progId) &&
            MatchKnownBrowser(progId) is { } known &&
            ParseCommandExecutable(ReadOpenCommand(progId)) is { } executable &&
            File.Exists(executable))
        {
            return new PrivateBrowser(known.Name, executable, known.Flag);
        }

        var edge = ReadAppPath("msedge.exe");
        return edge is not null && File.Exists(edge)
            ? new PrivateBrowser("Edge", edge, "--inprivate")
            : null;
    }

    public static void Open(PrivateBrowser browser, string url)
    {
        var startInfo = new ProcessStartInfo { FileName = browser.ExecutablePath, UseShellExecute = false };
        startInfo.ArgumentList.Add(browser.PrivateFlag);
        startInfo.ArgumentList.Add(url);
        Process.Start(startInfo)?.Dispose();
    }

    internal static (string Name, string Flag)? MatchKnownBrowser(string progId)
    {
        foreach (var (prefix, name, flag) in KnownBrowsers)
        {
            if (progId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return (name, flag);
            }
        }

        return null;
    }

    // `"C:\Path\brave.exe" --single-argument %1` -> C:\Path\brave.exe
    internal static string? ParseCommandExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }

        var exeEnd = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeEnd > 0 ? trimmed[..(exeEnd + 4)] : null;
    }

    private static string? ReadDefaultHttpsProgId()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
        return key?.GetValue("ProgId") as string;
    }

    private static string? ReadOpenCommand(string progId)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        return key?.GetValue(null) as string;
    }

    private static string? ReadAppPath(string executableName)
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
            if (key?.GetValue(null) is string path && !string.IsNullOrWhiteSpace(path))
            {
                return path.Trim('"');
            }
        }

        return null;
    }
}
