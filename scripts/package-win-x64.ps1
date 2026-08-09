param(
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\package",
    [string]$SigningHook
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$outputCandidate = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }
$output = [IO.Path]::GetFullPath($outputCandidate)
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be a descendant of the repository-local artifacts directory.'
}
if (Test-Path -LiteralPath $artifactsRoot) {
    $attributes = [IO.File]::GetAttributes($artifactsRoot)
    if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'The artifacts directory must not be a reparse point.' }
}
$relative = $output.Substring($artifactsPrefix.Length)
$cursor = $artifactsRoot
foreach ($segment in $relative.Split([IO.Path]::DirectorySeparatorChar, [StringSplitOptions]::RemoveEmptyEntries)) {
    $cursor = Join-Path $cursor $segment
    if (Test-Path -LiteralPath $cursor) {
        $attributes = [IO.File]::GetAttributes($cursor)
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'OutputDirectory must not traverse a reparse point.' }
    }
}
$publish = Join-Path $output 'publish'
$stage = Join-Path $output 'AndroidRuntime-win-x64'
$archive = Join-Path $output 'AndroidRuntime-win-x64.zip'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $publish,$stage -Force | Out-Null

dotnet publish (Join-Path $root 'AndroidRuntime.WindowsHost\AndroidRuntime.WindowsHost.csproj') `
    -p:PublishProfile=win-x64 -p:ContinuousIntegrationBuild=true -p:Deterministic=true `
    -p:DebugType=None -p:DebugSymbols=false -p:PathMap="$root=/_/src" -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Get-ChildItem -LiteralPath $publish -File | Where-Object Extension -ne '.pdb' | Sort-Object Name | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage $_.Name)
}

$exe = Join-Path $stage 'AndroidRuntime.WindowsHost.exe'
if ($SigningHook) {
    & $SigningHook $exe
    if ($LASTEXITCODE -ne 0) { throw "Signing hook failed with exit code $LASTEXITCODE" }
    Set-Content -LiteralPath (Join-Path $stage 'SIGNING.txt') -Value 'EXTERNALLY_SIGNED=true' -Encoding Ascii
} else {
    Set-Content -LiteralPath (Join-Path $stage 'SIGNING.txt') -Value 'EXTERNALLY_SIGNED=false' -Encoding Ascii
}

$depsPath = Join-Path $stage 'AndroidRuntime.WindowsHost.deps.json'
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
$dependencyLines = @('runtimeTarget=' + $deps.runtimeTarget.name)
$dependencyLines += $deps.libraries.psobject.Properties.Name | Sort-Object | ForEach-Object { 'library=' + $_ }
[IO.File]::WriteAllLines((Join-Path $stage 'DEPENDENCIES.txt'), $dependencyLines, [Text.UTF8Encoding]::new($false))

$files = Get-ChildItem -LiteralPath $stage -File | Sort-Object Name
$inventory = @('scope=all ZIP file entries; SHIPPED-FILES.txt uses SELF because a byte-exact self size/hash is recursive')
$inventory += $files | ForEach-Object { '{0}`t{1}' -f $_.Name,$_.Length }
$inventory += 'SHIPPED-FILES.txt`tSELF'
[IO.File]::WriteAllLines((Join-Path $stage 'SHIPPED-FILES.txt'), $inventory, [Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($archive, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        Get-ChildItem -LiteralPath $stage -File | Sort-Object Name | ForEach-Object {
            $entry = $zip.CreateEntry(('AndroidRuntime-win-x64/' + $_.Name), [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $input = [IO.File]::OpenRead($_.FullName); $target = $entry.Open()
            try { $input.CopyTo($target) } finally { $target.Dispose(); $input.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($archive + '.sha256'), ($archiveHash + '  ' + [IO.Path]::GetFileName($archive) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
Write-Output $archive
