param([int]$Seed = 24301)
$ErrorActionPreference = 'Stop'
$env:ANDROID_RUNTIME_DEX_FUZZ_SEED = $Seed
dotnet test "$PSScriptRoot\..\tests\AndroidRuntime.Core.Tests\AndroidRuntime.Core.Tests.csproj" --filter "FullyQualifiedName~DexVerifierTests.Seeded_header_mutation_corpus" --no-restore
exit $LASTEXITCODE
