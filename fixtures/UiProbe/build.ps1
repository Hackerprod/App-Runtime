[CmdletBinding()]
param(
    [string] $AndroidSdk = $(if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } elseif ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }),
    [string] $JavaHome = $env:JAVA_HOME
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $root '..\..')).Path
$build = Join-Path $root 'build'
$output = Join-Path $projectRoot 'tests\AndroidRuntime.Core.Tests\Fixtures\UiProbe.apk'
if (-not $JavaHome) {
    $javac = Get-Command javac.exe -ErrorAction SilentlyContinue
    if ($javac) { $JavaHome = Split-Path (Split-Path $javac.Source) }
    else {
        $jdk = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'CodexToolchains\jdk17') -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
        if ($jdk) { $JavaHome = $jdk.FullName }
    }
}
if (-not $JavaHome -or -not (Test-Path (Join-Path $JavaHome 'bin\javac.exe'))) { throw 'JDK with javac.exe is required.' }
$env:JAVA_HOME = $JavaHome
$env:PATH = (Join-Path $JavaHome 'bin') + [IO.Path]::PathSeparator + $env:PATH
$tools = Get-ChildItem (Join-Path $AndroidSdk 'build-tools') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'aapt2.exe') } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$platform = Get-ChildItem (Join-Path $AndroidSdk 'platforms') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'android.jar') } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $tools -or -not $platform) { throw 'Android SDK build-tools and platform are required.' }
if (Test-Path $build) { Remove-Item -LiteralPath $build -Recurse -Force }
$compiled = New-Item -ItemType Directory -Force (Join-Path $build 'compiled')
$generated = New-Item -ItemType Directory -Force (Join-Path $build 'generated')
$classes = New-Item -ItemType Directory -Force (Join-Path $build 'classes')
$dex = New-Item -ItemType Directory -Force (Join-Path $build 'dex')
$unsigned = Join-Path $build 'UiProbe-unsigned.apk'
$withDex = Join-Path $build 'UiProbe-with-dex.apk'
$aligned = Join-Path $build 'UiProbe-aligned.apk'
$keystore = Join-Path $build 'uiprobe.keystore'
$aapt2 = Join-Path $tools.FullName 'aapt2.exe'
& $aapt2 compile --dir (Join-Path $root 'res') -o (Join-Path $compiled.FullName 'resources.zip')
if ($LASTEXITCODE) { throw "aapt2 compile failed: $LASTEXITCODE" }
& $aapt2 link -o $unsigned --manifest (Join-Path $root 'AndroidManifest.xml') -I (Join-Path $platform.FullName 'android.jar') --java $generated.FullName --min-sdk-version 21 --target-sdk-version 35 (Join-Path $compiled.FullName 'resources.zip')
if ($LASTEXITCODE) { throw "aapt2 link failed: $LASTEXITCODE" }
$java = @((Join-Path $root 'src\org\example\uiprobe\MainActivity.java')) + @(Get-ChildItem $generated.FullName -Recurse -Filter '*.java' | ForEach-Object FullName)
& (Join-Path $JavaHome 'bin\javac.exe') --release 8 -classpath (Join-Path $platform.FullName 'android.jar') -d $classes.FullName $java
if ($LASTEXITCODE) { throw "javac failed: $LASTEXITCODE" }
& (Join-Path $tools.FullName 'd8.bat') --min-api 21 --lib (Join-Path $platform.FullName 'android.jar') --output $dex.FullName @(Get-ChildItem $classes.FullName -Recurse -Filter '*.class' | ForEach-Object FullName)
if ($LASTEXITCODE) { throw "d8 failed: $LASTEXITCODE" }
Copy-Item $unsigned $withDex
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($withDex, [IO.Compression.ZipArchiveMode]::Update)
try { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $dex.FullName 'classes.dex'), 'classes.dex', [IO.Compression.CompressionLevel]::Optimal) | Out-Null } finally { $archive.Dispose() }
& (Join-Path $tools.FullName 'zipalign.exe') -f 4 $withDex $aligned
if ($LASTEXITCODE) { throw "zipalign failed: $LASTEXITCODE" }
& (Join-Path $JavaHome 'bin\keytool.exe') -genkeypair -noprompt -keystore $keystore -storepass uiprobe -keypass uiprobe -alias uiprobe -dname 'CN=UI Probe, O=Android Runtime, C=US' -keyalg RSA -keysize 2048 -validity 10000
if ($LASTEXITCODE) { throw "keytool failed: $LASTEXITCODE" }
New-Item -ItemType Directory -Force (Split-Path $output) | Out-Null
if (Test-Path $output) { Remove-Item $output -Force }
& (Join-Path $tools.FullName 'apksigner.bat') sign --v4-signing-enabled false --ks $keystore --ks-key-alias uiprobe --ks-pass pass:uiprobe --key-pass pass:uiprobe --out $output $aligned
if ($LASTEXITCODE) { throw "apksigner sign failed: $LASTEXITCODE" }
& $aapt2 dump xmltree $output --file res/layout/main.xml
if ($LASTEXITCODE) { throw "aapt2 xmltree failed: $LASTEXITCODE" }
& $aapt2 dump resources $output
if ($LASTEXITCODE) { throw "aapt2 resource dump failed: $LASTEXITCODE" }
& (Join-Path $tools.FullName 'apksigner.bat') verify --verbose --print-certs $output
if ($LASTEXITCODE) { throw "apksigner verify failed: $LASTEXITCODE" }
Write-Host "Generated and verified $output"
