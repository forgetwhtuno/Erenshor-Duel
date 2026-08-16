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
    (Join-Path $ScriptRoot "..\src\DuelLifecyclePolicy.cs") `
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

# Deterministic source-contract checks bind every terminal route to the same restoration and
# cleanup primitives. These complement the Unity-free lifecycle/safety policy assertions above.
$plugin = Get-Content (Join-Path $ScriptRoot "..\src\ErenshorDuelPlugin.cs") -Raw
$controller = Get-Content (Join-Path $ScriptRoot "..\src\DuelController.cs") -Raw
if ($plugin -notmatch 'PluginVersion\s*=\s*"0\.4\.1"' -or $plugin -notmatch 'Practice Duels " \+ PluginVersion \+ " loaded') {
    throw "Duel RC guard failed: startup output is not exact-version identifiable."
}
$emergencyStart = $controller.IndexOf('private static void EmergencyCleanup', [StringComparison]::Ordinal)
$emergencyEnd = $controller.IndexOf('private static void ClearSessionState', [StringComparison]::Ordinal)
$emergency = if ($emergencyStart -ge 0 -and $emergencyEnd -gt $emergencyStart) { $controller.Substring($emergencyStart, $emergencyEnd - $emergencyStart) } else { '' }
if ([string]::IsNullOrEmpty($emergency) -or $emergency -notmatch 'RestoreRealHealthAndEffects\(\)' -or
    $emergency -notmatch 'ReleaseEngagedPets\(\)' -or $emergency -notmatch 'RestoreInitialNearbyEnemyMembership\(\)' -or
    $emergency -notmatch 'RestorePartyMovementOwnership\(\)' -or $emergency -notmatch 'BeginPostDuelAttackCleanup\(\)' -or
    $emergency -notmatch 'ClearSessionState\(\)') {
    throw "Duel RC guard failed: emergency terminal cleanup does not cover every owned state."
}
if ($controller -notmatch 'private\s+static\s+void\s+RestorePartyMovementOwnership' -or
    $controller -notmatch '_previousGuardSpot' -or $controller -notmatch '_previousFirstGuardSpot') {
    throw "Duel RC guard failed: party Guard/follow restoration helper missing."
}
if ($controller -notmatch 'CancelForSceneMismatch' -or $controller -notmatch '!ParticipantsAreValid\(\)' -or
    $controller -notmatch 'TryGetExternalAttacker' -or $controller -notmatch 'MaximumFightSeconds' -or
    $controller -notmatch 'RunPostDuelAttackCleanup\(\)') {
    throw "Duel RC guard failed: zone/invalid/external-combat/timeout/repeat cleanup coverage changed."
}
Write-Host "Practice Duel RC terminal cleanup source guards: PASS" -ForegroundColor Green
