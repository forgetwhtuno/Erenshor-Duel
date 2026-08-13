$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-Csc {
    foreach ($path in @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )) {
        if (Test-Path $path) { return $path }
    }
    throw "csc.exe not found. Install the .NET Framework Developer Pack or Visual Studio Build Tools."
}

$csc = Find-Csc
$out = Join-Path $env:TEMP "ErenshorDuel.Tests.exe"

# Only the pure-logic files behind DuelSelfTests.RunAll() (no UnityEngine/Lunaris/game
# dependency) plus a small Main entry point, so this suite can run outside the game.
& $csc /nologo /target:exe ("/out:{0}" -f $out) `
    (Join-Path $ScriptRoot "..\src\DuelChallengePolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DuelEligibilityPolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DuelLocalityPolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DuelIdentity.cs") `
    (Join-Path $ScriptRoot "..\src\DuelEventContract.cs") `
    (Join-Path $ScriptRoot "..\src\DuelSafetyPolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DeepSimsCompatibility.cs") `
    (Join-Path $ScriptRoot "..\src\DuelSelfTests.cs") `
    (Join-Path $ScriptRoot "RunAllTests.cs")
if ($LASTEXITCODE -ne 0) {
    throw "Test compilation failed."
}

try {
    & $out
    if ($LASTEXITCODE -ne 0) {
        throw "Erenshor Duel tests failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item $out -Force -ErrorAction SilentlyContinue
}
