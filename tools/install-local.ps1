<#
.SYNOPSIS
Build the app and install it on this PC. This is the whole release flow for this fork.

.DESCRIPTION
1. Publishes a self-contained win-x64 single-file build to artifacts\publish.
   A failed build stops here, so the installed copy keeps running.
2. Stops the installed copy only (matched by its full path), then copies the new exe in.
   Settings, logs, and saved accounts are not touched: monitor.settings.json sits next to the
   exe, and accounts live under %LOCALAPPDATA%\AIUsageMonitor.
3. Starts the installed exe, detached from the calling shell's Windows job, so an agent's
   command session cannot take the tray app down when it closes.

The build is not code-signed. It is for this PC only.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install-local.ps1
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\AIUsageMonitor'),
    [switch]$NoStart,
    [ValidateRange(1, 60)]
    [int]$StartupWaitSeconds = 5
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repo 'AIUsageMonitor.csproj'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Project not found: $project" }
if (-not $InstallDir) { throw 'InstallDir is empty.' }
$exe = Join-Path $InstallDir 'AIUsageMonitor.exe'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet is not on PATH. Install the .NET 10 SDK: winget install Microsoft.DotNet.SDK.10' }
if (-not ((& dotnet --list-sdks) -match '^10\.')) { throw 'The .NET 10 SDK is not installed. Run: winget install Microsoft.DotNet.SDK.10' }

$version = 'unknown'
$metadata = Get-Content -LiteralPath (Join-Path $repo 'Services\AppMetadata.cs') -Raw
if ($metadata -match 'VersionText\s*=\s*"([^"]+)"') { $version = $Matches[1] }

# 1. Publish to a staging folder. The literal 'artifacts\publish' is spelled at the delete.
$staging = Join-Path $repo 'artifacts\publish'
if (Test-Path -LiteralPath (Join-Path $repo 'artifacts\publish')) {
    Remove-Item -LiteralPath (Join-Path $repo 'artifacts\publish') -Recurse -Force
}
& dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE. The installed app was left running." }
if (-not (Test-Path -LiteralPath (Join-Path $staging 'AIUsageMonitor.exe') -PathType Leaf)) { throw 'Publish did not produce AIUsageMonitor.exe.' }

# 2. Stop the installed copy only, then replace it.
$running = @(Get-Process -Name AIUsageMonitor -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
foreach ($process in $running) {
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit(10000) | Out-Null
}
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Get-ChildItem -LiteralPath $staging -File |
    Where-Object { $_.Name -notin @('usage.fake.json', 'monitor.settings.json') -and $_.Extension -notin @('.pdb', '.xml', '.jsonl', '.log') } |
    Copy-Item -Destination $InstallDir -Force
"INSTALLED $version -> $exe"

if ($NoStart) { return }

# 3. Start it outside the caller's Windows job. Start-Process can leave the app in a kill-on-close
#    job, so it would close when an agent's command session ends.
if (-not ('AIUsageMonitor.DetachedLauncher' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AIUsageMonitor
{
    public static class DetachedLauncher
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfo
        {
            public uint Size;
            public IntPtr Reserved, Desktop, Title;
            public uint X, Y, XSize, YSize, XChars, YChars, FillAttribute, Flags;
            public ushort ShowWindow, ReservedSize;
            public IntPtr ReservedBytes, StdInput, StdOutput, StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInfo
        {
            public IntPtr Process, Thread;
            public uint ProcessId, ThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcessW(string application, StringBuilder command,
            IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
            uint creationFlags, IntPtr environment, string directory,
            ref StartupInfo startup, out ProcessInfo process);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        public static int Start(string executable, string directory, bool breakaway)
        {
            var startup = new StartupInfo { Size = (uint)Marshal.SizeOf(typeof(StartupInfo)) };
            ProcessInfo process;
            const uint BreakawayFromJob = 0x01000000;
            const uint NoConsoleWindow = 0x08000000;
            uint flags = NoConsoleWindow | (breakaway ? BreakawayFromJob : 0);
            if (!CreateProcessW(executable, new StringBuilder("\"" + executable + "\""),
                IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, directory,
                ref startup, out process))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return checked((int)process.ProcessId); }
            finally
            {
                CloseHandle(process.Thread);
                CloseHandle(process.Process);
            }
        }
    }
}
'@
}

try {
    $launchedId = [AIUsageMonitor.DetachedLauncher]::Start($exe, $InstallDir, $true)
    $detached = $true
}
catch {
    # Breakaway is denied when the caller's job forbids it. Fall back to a normal start and say so.
    Write-Warning "Detached start was denied ($($_.Exception.Message)). Starting normally; the app may close when this shell closes."
    $launchedId = [AIUsageMonitor.DetachedLauncher]::Start($exe, $InstallDir, $false)
    $detached = $false
}

$launched = Get-Process -Id $launchedId
try {
    if ($launched.WaitForExit($StartupWaitSeconds * 1000)) {
        throw "The app exited during startup with code $($launched.ExitCode). See monitor.log.jsonl in $InstallDir."
    }
    "STARTED PID=$launchedId DETACHED=$detached"
}
finally {
    $launched.Dispose()
}
