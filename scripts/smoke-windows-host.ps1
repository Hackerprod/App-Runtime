[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [int] $AutoCloseMilliseconds = 5000,
    [switch] $ExerciseCleanupOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$exe = (Resolve-Path (Join-Path $projectRoot "AndroidRuntime.WindowsHost\bin\$Configuration\net8.0-windows\AndroidRuntime.WindowsHost.exe")).Path
$apk = (Resolve-Path (Join-Path $projectRoot 'tests\AndroidRuntime.Core.Tests\Fixtures\RuntimeProbe.apk')).Path
$evidenceDirectory = Join-Path $projectRoot 'artifacts\smoke'
New-Item -ItemType Directory -Force $evidenceDirectory | Out-Null
$trace = Join-Path $evidenceDirectory 'api-trace.jsonl'
Remove-Item -LiteralPath $trace -Force -ErrorAction SilentlyContinue

$arguments = @(
    ('"{0}"' -f $apk),
    '--auto-close-ms',
    $AutoCloseMilliseconds,
    '--trace',
    ('"{0}"' -f $trace)
)
$process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru

function Stop-ProcessSafely([System.Diagnostics.Process] $Target) {
    if ($Target.HasExited) { return }
    try { $Target.Kill() } catch { Stop-Process -Id $Target.Id -Force -ErrorAction SilentlyContinue }
    if (-not $Target.WaitForExit(10000)) {
        Stop-Process -Id $Target.Id -Force -ErrorAction SilentlyContinue
        if (-not $Target.WaitForExit(5000)) { throw "Process $($Target.Id) survived cleanup." }
    }
}

$title = $null
$handle = [IntPtr]::Zero
$toastObserved = $false
$deadline = [DateTime]::UtcNow.AddSeconds(10)
while ([DateTime]::UtcNow -lt $deadline -and -not $process.HasExited) {
    Start-Sleep -Milliseconds 100
    $process.Refresh()
    if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
        $handle = $process.MainWindowHandle
        $title = $process.MainWindowTitle
        if ($title -eq 'RuntimeProbe DEX') {
            Add-Type -AssemblyName UIAutomationClient
            try {
                $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
                $condition = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty,
                    'value=41true!')
                $toast = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
                if ($null -ne $toast -and -not $toast.Current.IsOffscreen) { $toastObserved = $true; break }
            } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
        }
    }
}

if ($handle -eq [IntPtr]::Zero -or $title -ne 'RuntimeProbe DEX' -or -not $toastObserved) {
    Stop-ProcessSafely $process
    throw "Expected HWND title 'RuntimeProbe DEX'; observed handle '$handle' and title '$title'."
}
$toastHidden = $false
$toastDeadline = [DateTime]::UtcNow.AddSeconds(5)
while ([DateTime]::UtcNow -lt $toastDeadline -and -not $process.HasExited) {
    Start-Sleep -Milliseconds 100
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
        $toast = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $toast -or $toast.Current.IsOffscreen) { $toastHidden = $true; break }
    } catch [System.Windows.Automation.ElementNotAvailableException] {
        if ($process.HasExited) { $toastHidden = $true; break }
    }
}
if (-not $toastHidden) { Stop-ProcessSafely $process; throw 'Toast overlay did not hide within five seconds.' }
if ($ExerciseCleanupOnly) {
    Stop-ProcessSafely $process
    if (-not $process.HasExited) { throw 'Cleanup probe left the Windows host running.' }
    "cleanupProcessId=$($process.Id)"
    "cleanupExited=$($process.HasExited)"
    return
}
if (-not $process.WaitForExit(15000)) {
    Stop-ProcessSafely $process
    throw 'Windows host did not auto-close within 15 seconds.'
}
$process.WaitForExit()
$process.Refresh()
$exitCode = $process.ExitCode
if ($exitCode -ne 0) {
    throw "Windows host exited with ${exitCode}."
}
if (-not (Test-Path -LiteralPath $trace)) {
    throw 'Windows host did not write the requested trace file.'
}

$traceLines = @(Get-Content -LiteralPath $trace | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$parsedEvents = @($traceLines | ForEach-Object { $_ | ConvertFrom-Json })
if ($parsedEvents.Count -eq 0) { throw 'Trace file contains no JSON events.' }
$sessions = @($parsedEvents | Select-Object -ExpandProperty session -Unique)
if ($sessions.Count -ne 1 -or [string]::IsNullOrWhiteSpace($sessions[0])) { throw 'Trace events do not have one consistent session.' }
$groups = @($parsedEvents | Group-Object invocationId)
foreach ($group in $groups) {
    $kinds = @($group.Group | Select-Object -ExpandProperty kind)
    if ($kinds.Count -ne 2 -or $kinds[0] -ne 'Requested' -or $kinds[1] -notin @('Completed', 'Unimplemented', 'Failed', 'Cancelled')) {
        throw "Invocation $($group.Name) does not have a Requested-to-terminal pair: $($kinds -join ',')."
    }
}
$titleCompleted = @($parsedEvents | Where-Object { $_.kind -eq 'Completed' -and $_.resolved -eq 'Landroid/app/Activity;->setTitle(Ljava/lang/CharSequence;)V' })
if ($titleCompleted.Count -ne 1) { throw "Expected one completed setTitle binding, observed $($titleCompleted.Count)." }
$traceEvents = $parsedEvents.Count
$evidence = @(
    "command=$exe `"$apk`" --auto-close-ms $AutoCloseMilliseconds --trace `"$trace`"",
    "hwnd=$handle",
    "title=$title",
    "toastVisible=$toastObserved",
    "toastHidden=$toastHidden",
    "exitCode=$exitCode",
    "traceEvents=$traceEvents"
    "traceSession=$($sessions[0])"
    "traceInvocations=$($groups.Count)"
)
$evidence | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'windows-host-smoke.txt') -Encoding utf8
$evidence
