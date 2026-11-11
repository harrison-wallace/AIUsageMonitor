using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AIUsageMonitor.Services;

// Runs `claude auth login` in the background for one Claude config folder. The CLI never opens
// a browser on its own: BROWSER points at a command that does nothing, so the app can show the
// sign-in link and let the user choose where to open it. The user pastes the code from the
// sign-in page back into the app, and the app writes it to the CLI's stdin.
public sealed partial class ClaudeLoginSession : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();
    private readonly TaskCompletionSource<string> _authorizeUrl = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _outputLock = new();
    private bool _disposed;

    private ClaudeLoginSession(Process process)
    {
        _process = process;
    }

    // The link the user opens to sign in. Completes once the CLI prints it.
    public Task<string> AuthorizeUrl => _authorizeUrl.Task;

    public string Output
    {
        get { lock (_outputLock) return _output.ToString(); }
    }

    public static ClaudeLoginSession Start(string claudePath, string configDirectory, string? emailHint, string noBrowserCommandPath)
    {
        var process = new Process { StartInfo = BuildStartInfo(claudePath, configDirectory, emailHint, noBrowserCommandPath), EnableRaisingEvents = true };
        var session = new ClaudeLoginSession(process);
        process.OutputDataReceived += (_, e) => session.OnOutput(e.Data);
        process.ErrorDataReceived += (_, e) => session.OnOutput(e.Data);
        process.Exited += (_, _) => session._authorizeUrl.TrySetException(
            new InvalidOperationException(DescribeEarlyExit(session.Output)));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return session;
    }

    internal static ProcessStartInfo BuildStartInfo(string claudePath, string configDirectory, string? emailHint, string noBrowserCommandPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = claudePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = configDirectory
        };
        startInfo.ArgumentList.Add("auth");
        startInfo.ArgumentList.Add("login");
        if (!string.IsNullOrWhiteSpace(emailHint))
        {
            startInfo.ArgumentList.Add("--email");
            startInfo.ArgumentList.Add(emailHint.Trim());
        }

        startInfo.Environment["CLAUDE_CONFIG_DIR"] = configDirectory;
        startInfo.Environment["BROWSER"] = noBrowserCommandPath;
        return startInfo;
    }

    // A command that accepts the URL and does nothing. Claude Code runs $BROWSER with the
    // sign-in link instead of opening the default browser.
    public static string EnsureNoBrowserCommand(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "no-browser.cmd");
        const string content = "@exit /b 0\r\n";
        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            File.WriteAllText(path, content, Encoding.ASCII);
        }

        return path;
    }

    internal static string? ExtractAuthorizeUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = AuthorizeUrlRegex().Match(text);
        return match.Success ? match.Value : null;
    }

    public async Task SubmitCodeAsync(string code)
    {
        await _process.StandardInput.WriteLineAsync(code.Trim()).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync().ConfigureAwait(false);
    }

    // Returns the CLI exit code once it finishes, or throws when the wait is cancelled.
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return _process.ExitCode;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        _process.Dispose();
    }

    private void OnOutput(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_outputLock)
        {
            _output.AppendLine(line);
        }

        if (ExtractAuthorizeUrl(line) is { } url)
        {
            _authorizeUrl.TrySetResult(url);
        }
    }

    private static string DescribeEarlyExit(string output)
    {
        var lastLine = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return string.IsNullOrWhiteSpace(lastLine)
            ? "claude auth login stopped before it showed a sign-in link."
            : $"claude auth login stopped before it showed a sign-in link: {lastLine}";
    }

    [GeneratedRegex(@"https://\S+/oauth/authorize\?\S+")]
    private static partial Regex AuthorizeUrlRegex();
}
