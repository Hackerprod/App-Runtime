param([Parameter(Mandatory=$true)][string]$Archive, [Parameter(Mandatory=$true)][string]$Apk)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifacts = Join-Path $root 'artifacts'
$target = Join-Path $artifacts ('portable test Ω ' + [Guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath (Resolve-Path $Archive).Path -DestinationPath $target
    $exe = Join-Path $target 'AndroidRuntime-win-x64\AndroidRuntime.WindowsHost.exe'
    $trace = Join-Path $target 'trace.jsonl'
    $arguments = '"' + (Resolve-Path $Apk).Path + '" --auto-close-ms 500 --trace "' + $trace + '"'
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Published host exited with $($process.ExitCode)." }
    $events = @(Get-Content -LiteralPath $trace | ForEach-Object { $_ | ConvertFrom-Json })
    if ($events.Count -eq 0) { throw 'Published host produced no trace events.' }
    "publishedExit=$($process.ExitCode)"
    "publishedTraceEvents=$($events.Count)"
} finally {
    $fullTarget = [IO.Path]::GetFullPath($target)
    if (-not $fullTarget.StartsWith(([IO.Path]::GetFullPath($artifacts) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) { throw 'Portable cleanup target escaped artifacts.' }
    if (Test-Path -LiteralPath $fullTarget) { Remove-Item -LiteralPath $fullTarget -Recurse -Force }
    "portableDelete=$(-not (Test-Path -LiteralPath $fullTarget))"
}
