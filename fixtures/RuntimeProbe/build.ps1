[CmdletBinding()]
param(
    [string] $AndroidSdk = $(if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } elseif ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }),
    [string] $JavaHome = $env:JAVA_HOME
)

$ErrorActionPreference = 'Stop'
$fixtureRoot = $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $fixtureRoot '..\..')).Path
$buildRoot = Join-Path $fixtureRoot 'build'
$testFixtureRoot = Join-Path $projectRoot 'tests\AndroidRuntime.Core.Tests\Fixtures'

if (-not $JavaHome) {
    $javac = Get-Command javac.exe -ErrorAction SilentlyContinue
    if ($javac) {
        $JavaHome = Split-Path (Split-Path $javac.Source)
    } else {
        $localJdk = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'CodexToolchains\jdk17') -Directory -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $_.FullName 'bin\javac.exe') } |
            Sort-Object Name -Descending |
            Select-Object -First 1
        if ($localJdk) { $JavaHome = $localJdk.FullName }
    }
}

if (-not $JavaHome -or -not (Test-Path (Join-Path $JavaHome 'bin\javac.exe'))) {
    throw 'A JDK is required. Set JAVA_HOME to a JDK containing bin\javac.exe.'
}
if (-not (Test-Path $AndroidSdk)) {
    throw "Android SDK not found at '$AndroidSdk'. Set ANDROID_SDK_ROOT or pass -AndroidSdk."
}

$buildTools = Get-ChildItem (Join-Path $AndroidSdk 'build-tools') -Directory |
    Where-Object {
        (Test-Path (Join-Path $_.FullName 'aapt2.exe')) -and
        (Test-Path (Join-Path $_.FullName 'd8.bat')) -and
        (Test-Path (Join-Path $_.FullName 'zipalign.exe')) -and
        (Test-Path (Join-Path $_.FullName 'apksigner.bat'))
    } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1
if (-not $buildTools) { throw 'No Android build-tools installation with aapt2, d8, zipalign, and apksigner was found.' }

$platform = Get-ChildItem (Join-Path $AndroidSdk 'platforms') -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'android.jar') } |
    Sort-Object Name -Descending |
    Select-Object -First 1
if (-not $platform) { throw 'No Android platform containing android.jar was found.' }

if (Test-Path $buildRoot) {
    $resolvedBuild = (Resolve-Path $buildRoot).Path
    if (-not $resolvedBuild.StartsWith($fixtureRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean build directory outside fixture root: $resolvedBuild"
    }
    Remove-Item -LiteralPath $resolvedBuild -Recurse -Force
}

$classes = New-Item -ItemType Directory -Force (Join-Path $buildRoot 'classes')
$dex = New-Item -ItemType Directory -Force (Join-Path $buildRoot 'dex')
$unsignedApk = Join-Path $buildRoot 'RuntimeProbe-unsigned.apk'
$alignedApk = Join-Path $buildRoot 'RuntimeProbe-aligned.apk'
$signedApk = Join-Path $testFixtureRoot 'RuntimeProbe.apk'
$unimplementedSourceRoot = Join-Path $buildRoot 'unimplemented-src'
$unimplementedClasses = Join-Path $buildRoot 'unimplemented-classes'
$unimplementedDex = Join-Path $buildRoot 'unimplemented-dex'
$unimplementedUnsignedApk = Join-Path $buildRoot 'UnimplementedApiProbe-unsigned.apk'
$unimplementedAlignedApk = Join-Path $buildRoot 'UnimplementedApiProbe-aligned.apk'
$unimplementedSignedApk = Join-Path $testFixtureRoot 'UnimplementedApiProbe.apk'
$keystore = Join-Path $buildRoot 'runtimeprobe.keystore'

$env:JAVA_HOME = $JavaHome
$env:PATH = (Join-Path $JavaHome 'bin') + [IO.Path]::PathSeparator + $env:PATH

& (Join-Path $JavaHome 'bin\javac.exe') --release 8 -classpath (Join-Path $platform.FullName 'android.jar') -d $classes.FullName (Join-Path $fixtureRoot 'src\org\example\runtimeprobe\MainActivity.java') (Join-Path $fixtureRoot 'src\org\example\runtimeprobe\ExceptionProbe.java') (Join-Path $fixtureRoot 'src\org\example\runtimeprobe\ServicesProbe.java') (Join-Path $fixtureRoot 'src\org\example\runtimeprobe\PowerProbe.java')
if ($LASTEXITCODE) { throw "javac failed with exit code $LASTEXITCODE" }

$runtimeClasses = Get-ChildItem $classes.FullName -Recurse -Filter '*.class' | ForEach-Object { $_.FullName }
& (Join-Path $buildTools.FullName 'd8.bat') --min-api 21 --lib (Join-Path $platform.FullName 'android.jar') --output $dex.FullName $runtimeClasses
if ($LASTEXITCODE) { throw "d8 failed with exit code $LASTEXITCODE" }

& (Join-Path $buildTools.FullName 'aapt2.exe') link -o $unsignedApk --manifest (Join-Path $fixtureRoot 'AndroidManifest.xml') -I (Join-Path $platform.FullName 'android.jar')
if ($LASTEXITCODE) { throw "aapt2 failed with exit code $LASTEXITCODE" }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($unsignedApk, [IO.Compression.ZipArchiveMode]::Update)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $dex.FullName 'classes.dex'), 'classes.dex', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
} finally {
    $archive.Dispose()
}

& (Join-Path $buildTools.FullName 'zipalign.exe') -f 4 $unsignedApk $alignedApk
if ($LASTEXITCODE) { throw "zipalign failed with exit code $LASTEXITCODE" }

& (Join-Path $JavaHome 'bin\keytool.exe') -genkeypair -noprompt -keystore $keystore -storepass runtimeprobe -keypass runtimeprobe -alias runtimeprobe -dname 'CN=Runtime Probe, OU=Tests, O=Android Runtime, L=Test, S=Test, C=US' -keyalg RSA -keysize 2048 -validity 10000
if ($LASTEXITCODE) { throw "keytool failed with exit code $LASTEXITCODE" }

New-Item -ItemType Directory -Force $testFixtureRoot | Out-Null
if (Test-Path $signedApk) { Remove-Item -LiteralPath $signedApk -Force }
$signatureSidecar = $signedApk + '.idsig'
if (Test-Path $signatureSidecar) { Remove-Item -LiteralPath $signatureSidecar -Force }
& (Join-Path $buildTools.FullName 'apksigner.bat') sign --v4-signing-enabled false --ks $keystore --ks-key-alias runtimeprobe --ks-pass pass:runtimeprobe --key-pass pass:runtimeprobe --out $signedApk $alignedApk
if ($LASTEXITCODE) { throw "apksigner failed with exit code $LASTEXITCODE" }

& (Join-Path $buildTools.FullName 'aapt2.exe') dump xmltree $signedApk --file AndroidManifest.xml
if ($LASTEXITCODE) { throw "aapt2 xmltree verification failed with exit code $LASTEXITCODE" }

& (Join-Path $buildTools.FullName 'aapt2.exe') dump badging $signedApk
if ($LASTEXITCODE) { throw "aapt2 badging verification failed with exit code $LASTEXITCODE" }

& (Join-Path $buildTools.FullName 'apksigner.bat') verify --verbose --print-certs $signedApk
if ($LASTEXITCODE) { throw "apksigner verification failed with exit code $LASTEXITCODE" }

# Build a real missing-API variant from the same source with a deliberately deferred Throwable overload.
$variantJava = Join-Path $unimplementedSourceRoot 'org\example\runtimeprobe\MainActivity.java'
New-Item -ItemType Directory -Force (Split-Path $variantJava) | Out-Null
$sourceText = [IO.File]::ReadAllText((Join-Path $fixtureRoot 'src\org\example\runtimeprobe\MainActivity.java'))
$utf8NoBom = New-Object Text.UTF8Encoding -ArgumentList $false
[IO.File]::WriteAllText(
    $variantJava,
    ($sourceText.Replace('Log.i("RuntimeProbe", builtText)', 'Log.w("RuntimeProbe", builtText, (Throwable)null)')),
    $utf8NoBom)
New-Item -ItemType Directory -Force $unimplementedClasses, $unimplementedDex | Out-Null
& (Join-Path $JavaHome 'bin\javac.exe') --release 8 -classpath (Join-Path $platform.FullName 'android.jar') -d $unimplementedClasses $variantJava
if ($LASTEXITCODE) { throw "variant javac failed with exit code $LASTEXITCODE" }
& (Join-Path $buildTools.FullName 'd8.bat') --min-api 21 --lib (Join-Path $platform.FullName 'android.jar') --output $unimplementedDex (Join-Path $unimplementedClasses 'org\example\runtimeprobe\MainActivity.class')
if ($LASTEXITCODE) { throw "variant d8 failed with exit code $LASTEXITCODE" }
& (Join-Path $buildTools.FullName 'aapt2.exe') link -o $unimplementedUnsignedApk --manifest (Join-Path $fixtureRoot 'AndroidManifest.xml') -I (Join-Path $platform.FullName 'android.jar')
if ($LASTEXITCODE) { throw "variant aapt2 failed with exit code $LASTEXITCODE" }
$variantArchive = [IO.Compression.ZipFile]::Open($unimplementedUnsignedApk, [IO.Compression.ZipArchiveMode]::Update)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($variantArchive, (Join-Path $unimplementedDex 'classes.dex'), 'classes.dex', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
} finally { $variantArchive.Dispose() }
& (Join-Path $buildTools.FullName 'zipalign.exe') -f 4 $unimplementedUnsignedApk $unimplementedAlignedApk
if ($LASTEXITCODE) { throw "variant zipalign failed with exit code $LASTEXITCODE" }
if (Test-Path $unimplementedSignedApk) { Remove-Item -LiteralPath $unimplementedSignedApk -Force }
& (Join-Path $buildTools.FullName 'apksigner.bat') sign --v4-signing-enabled false --ks $keystore --ks-key-alias runtimeprobe --ks-pass pass:runtimeprobe --key-pass pass:runtimeprobe --out $unimplementedSignedApk $unimplementedAlignedApk
if ($LASTEXITCODE) { throw "variant apksigner failed with exit code $LASTEXITCODE" }
& (Join-Path $buildTools.FullName 'aapt2.exe') dump xmltree $unimplementedSignedApk --file AndroidManifest.xml
if ($LASTEXITCODE) { throw "variant aapt2 xmltree verification failed with exit code $LASTEXITCODE" }
& (Join-Path $buildTools.FullName 'aapt2.exe') dump badging $unimplementedSignedApk
if ($LASTEXITCODE) { throw "variant aapt2 verification failed with exit code $LASTEXITCODE" }
& (Join-Path $buildTools.FullName 'apksigner.bat') verify --verbose --print-certs $unimplementedSignedApk
if ($LASTEXITCODE) { throw "variant apksigner verification failed with exit code $LASTEXITCODE" }

Write-Host "Generated and verified $signedApk"
Write-Host "Generated and verified $unimplementedSignedApk"
Copy-Item -Force $signedApk (Join-Path $testFixtureRoot 'WideProbe.apk')
Copy-Item -Force $signedApk (Join-Path $testFixtureRoot 'WideClockProbe.apk')
Copy-Item -Force $signedApk (Join-Path $testFixtureRoot 'ExceptionProbe.apk')
Copy-Item -Force $signedApk (Join-Path $testFixtureRoot 'ServicesProbe.apk')
$missingManifest = Join-Path $buildRoot 'AndroidManifest-missing-permission.xml'
$missingUnsigned = Join-Path $buildRoot 'ServicesProbeMissingPermission-unsigned.apk'
$missingAligned = Join-Path $buildRoot 'ServicesProbeMissingPermission-aligned.apk'
$missingSigned = Join-Path $testFixtureRoot 'ServicesProbeMissingPermission.apk'
$manifestText = [Text.RegularExpressions.Regex]::Replace([IO.File]::ReadAllText((Join-Path $fixtureRoot 'AndroidManifest.xml')), '(?m)^\s*<uses-permission android:name="android\.permission\.ACCESS_NETWORK_STATE"\s*/>\r?\n?', '')
[IO.File]::WriteAllText($missingManifest, $manifestText, $utf8NoBom)
& (Join-Path $buildTools.FullName 'aapt2.exe') link -o $missingUnsigned --manifest $missingManifest -I (Join-Path $platform.FullName 'android.jar')
if ($LASTEXITCODE) { throw "missing-permission aapt2 failed with exit code $LASTEXITCODE" }
$missingArchive = [IO.Compression.ZipFile]::Open($missingUnsigned, [IO.Compression.ZipArchiveMode]::Update)
try { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($missingArchive, (Join-Path $dex.FullName 'classes.dex'), 'classes.dex', [IO.Compression.CompressionLevel]::Optimal) | Out-Null } finally { $missingArchive.Dispose() }
& (Join-Path $buildTools.FullName 'zipalign.exe') -f 4 $missingUnsigned $missingAligned
if ($LASTEXITCODE) { throw "missing-permission zipalign failed with exit code $LASTEXITCODE" }
& (Join-Path $buildTools.FullName 'apksigner.bat') sign --v4-signing-enabled false --ks $keystore --ks-key-alias runtimeprobe --ks-pass pass:runtimeprobe --key-pass pass:runtimeprobe --out $missingSigned $missingAligned
if ($LASTEXITCODE) { throw "missing-permission signing failed with exit code $LASTEXITCODE" }
& (Join-Path $buildTools.FullName 'apksigner.bat') verify --verbose $missingSigned
if ($LASTEXITCODE) { throw "missing-permission verification failed with exit code $LASTEXITCODE" }
