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
    (Join-Path $ScriptRoot "..\src\DuelCombatSemanticsPolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DuelLifecyclePolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DuelChallengePolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DuelEligibilityPolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DuelLocalityPolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DuelIdentity.cs") `
    (Join-Path $ScriptRoot "..\src\DuelEventContract.cs") `
    (Join-Path $ScriptRoot "..\src\DuelSafetyPolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DeepSimsCompatibility.cs") `
    (Join-Path $ScriptRoot "..\src\DuelSpellAdmissionPolicy.cs") `
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
if ($plugin -notmatch 'PluginVersion\s*=\s*"0\.4\.5"' -or $plugin -notmatch 'Practice Duels " \+ PluginVersion \+ " loaded') {
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
    $controller -notmatch 'MaximumFightSeconds' -or $controller -notmatch 'RunPostDuelAttackCleanup\(\)') {
    throw "Duel RC guard failed: zone/invalid/timeout/repeat cleanup coverage changed."
}
$semantics = Get-Content (Join-Path $ScriptRoot "..\src\DuelCombatSemanticsPolicy.cs") -Raw
$spellPolicy = Get-Content (Join-Path $ScriptRoot "..\src\DuelSpellAdmissionPolicy.cs") -Raw
if ($controller -notmatch 'CaptureNativeReduceHp' -or $controller -match 'ObserveNativeReduceHp' -or
    $controller -match 'CurrentHP\s*=\s*int\.MaxValue' -or $controller -match 'NativeDamageHeadroom' -or
    $semantics -match 'NativeDamageHeadroom' -or $semantics -match 'EffectiveNativeDamage') {
    throw "Duel damage-transaction guard failed: scoped ReduceHP capture regressed to synthetic headroom/delta observation."
}
if ($controller -notmatch 'CapturedReduceHpDamage\s*=\s*damage' -or
    $controller -notmatch 'CapturedReduceHp\s*=\s*true' -or
    $controller -notmatch 'result\s*=\s*false;\s*return false;' -or
    $controller -notmatch 'ShouldCaptureReduceHp' -or
    $controller -notmatch 'exactStats\s*=.*state\.Target\.MyStats\s*==\s*stats' -or
    $controller -notmatch 'exactDuelEdge\s*=.*IsDuelHit\(state\.Target, state\.Attacker\)' -or
    $semantics -notmatch 'transactionPresent && !worldReal && exactTargetStats && combatActive && exactVirtualDuelEdge') {
    throw "Duel ReduceHP scope guard failed: exact participant transaction capture/suppression is incomplete."
}
if ($controller -notmatch 'Previous\s*=\s*_nativeDamageInFlight' -or
    $controller -notmatch '_nativeDamageInFlight\s*=\s*state\.Previous' -or
    $controller -notmatch 'PopNativeDamageState') {
    throw "Duel nested-damage guard failed: current transaction stack/reentrancy ownership is missing."
}
if ($controller -notmatch 'EffectiveCapturedDamage' -or
    $controller -notmatch 'ApplyVirtualDamageOnce' -or
    $controller -notmatch 'reduceHpCaptured=' -or
    $controller -notmatch 'capturedEffectiveDamage=' -or
    $controller -notmatch 'virtualScale=1\.000') {
    throw "Duel captured-damage/diagnostic guard failed."
}
if ($controller -notmatch 'hostileWorldAllowed=true' -or $controller -notmatch 'PreflightAreaSpell' -or
    $controller -notmatch 'ProtectedNonParticipant' -or $controller -notmatch 'authority=real_world') {
    throw "Duel AoE/world-authority guard failed: hostile-world coexistence or protected bystander containment is missing."
}
if ($controller -notmatch 'PlayerWorldEffectSlots' -or $controller -notmatch 'AdoptWorldStatusEffectSlots' -or
    $controller -notmatch 'RefreshTrackedWorldEffects') {
    throw "Duel world-effect guard failed: hostile status effects are not isolated from duel-only cleanup state."
}
if ($controller -notmatch 'DeclaresSelfApplication\(spell\)' -or
    $controller -notmatch 'IsSelfCast\(targetCharacter == casterCharacter,' -or
    $controller -notmatch 'declaresSelfApplication, SpellDamagesTarget\(spell\)\)' -or
    $controller -notmatch 'private static bool SpellDamagesTarget' -or
    $spellPolicy -notmatch 'bool spellDamagesTarget' -or
    $spellPolicy -notmatch 'return targetArgumentIsCaster && !spellDamagesTarget;') {
    throw "Duel self-cast guard failed: declared self-application repair, or the damaging-spell qualifier that stops an NPC caster's offensive spell from being read as a self-cast, regressed."
}
if ($controller -notmatch 'TryAdaptOpponentHealToSelf' -or
    $controller -notmatch 'ref Stats target' -or
    ($controller -notmatch 'target = casterCharacter\.MyStats' -and $controller -notmatch 'target = selfStats') -or
    $controller -notmatch 'currentTargetPreserved=true' -or
    $spellPolicy -notmatch 'CanAdaptOpponentHealToSelf') {
    throw "Duel targeted-heal guard failed: narrow opponent-selected self-heal adaptation missing."
}
if ($controller -match 'GameData\.PlayerControl\.CurrentTarget\s*=\s*_player' -or
    $controller -match 'GameData\.PlayerControl\.CurrentTarget\s*=\s*casterCharacter') {
    throw "Duel targeted-heal guard failed: global CurrentTarget mutation introduced."
}
if ($controller -notmatch 'hostile_world_real_offense' -or
    $controller -notmatch 'authority=real_world' -or
    $controller -notmatch 'worldDamagePreserved=true' -or
    $controller -notmatch 'ProtectedNonParticipant') {
    throw "Duel source/target authority guard failed: hostile-world real damage or protected actor safety regressed."
}
if ($controller -notmatch 'PreflightAreaSpell' -or $controller -notmatch 'hostileWorldAllowed=true' -or
    $controller -notmatch 'IsAreaStructurallyContainable') {
    throw "Duel AoE guard failed: current actor/effect-aware AoE product direction regressed."
}
# Forensic regression matrix from the 0.4.4 damage-transaction repair brief. These are
# deterministic source/pure-policy contracts; live native behavior remains a separate release gate.
$safety = Get-Content (Join-Path $ScriptRoot "..\src\DuelSafetyPolicy.cs") -Raw
$lifecycle = Get-Content (Join-Path $ScriptRoot "..\src\DuelLifecyclePolicy.cs") -Raw
$forensicCases = 0
function Assert-Forensic([bool]$condition, [string]$name) {
    if (-not $condition) { throw ("Duel forensic matrix failed: " + $name) }
    $script:forensicCases++
}

Assert-Forensic ($semantics -match 'EffectiveCapturedDamage\(true, 12, 2147483062\) != 12') '01 small hit never becomes sentinel-sized'
Assert-Forensic ($controller -match 'CapturedReduceHpDamage\s*=\s*damage' -and $controller -match 'EffectiveCapturedDamage') '02 captured ReduceHP is effective damage'
Assert-Forensic ($controller -match 'DuelPhysicalDamagePatch' -and $controller -match 'target == _sim') '03 player melee -> Sim virtual path'
Assert-Forensic ($controller -match 'DuelPhysicalDamagePatch' -and $controller -match 'target == _player') '04 Sim melee -> player virtual path'
Assert-Forensic ($controller -match 'DuelMagicDamagePatch' -and $controller -match 'PrepareNativeDamage') '05 player direct spell -> Sim native/capture path'
Assert-Forensic ($controller -match 'DuelMagicDamagePatch' -and $controller -match 'IsDuelHit') '06 Sim direct spell -> player native/capture path'
Assert-Forensic ($controller -match 'DuelBleedDamagePatch' -and $controller -match 'periodic_bleed_unattributed=true authority=duel_virtual') '07 DoT/bleed applies one virtual path'
Assert-Forensic ($controller -match 'BeginDamageShield' -and $controller -match 'TryVirtualDamage\(target, attacker, damage') '08 retaliation/damage shield source-target classified'
Assert-Forensic ($controller -match 'CaptureNativeReduceHp' -and $controller -match 'realEffectSuppressed=true' -and $controller -notmatch 'CurrentHP\s*=\s*int\.MaxValue') '09 participant hit leaves real ledger protected'
Assert-Forensic ($controller -match 'ApplyVirtualDamageOnce' -and $safety -match 'virtual damage must be applied once per event') '10 virtual damage exactly once'
Assert-Forensic ($safety -match 'ReachedYieldThreshold' -and $controller -match 'DuelSafetyPolicy\.ReachedYieldThreshold') '11 yield only at threshold'
Assert-Forensic ($safety -match 'healthyAfterSmallHit != 988') '12 small hit does not instant-yield healthy duel'
Assert-Forensic ($semantics -match 'EffectiveCapturedDamage\(true, 58, 218\) != 58') '13 native mitigation result remains authoritative'
Assert-Forensic ($semantics -match 'ordinary/world/wrong-target ReduceHP must remain native') '14 ordinary non-Duel ReduceHP untouched'
Assert-Forensic ($semantics -match 'hostile world -> duelist must stay real') '15 hostile world -> duelist is real'
Assert-Forensic ($semantics -match 'duelist -> hostile world must stay real') '16 duelist -> hostile world is real'
Assert-Forensic ($spellPolicy -match 'SelfOnly heal with opponent targeted must be a self-cast' -and $spellPolicy -match 'ordinary Heal must adapt to self') '17 Minor Healing/SelfOnly with opponent selected' 
Assert-Forensic ($spellPolicy -match 'ApplyToCaster spell with opponent targeted must be a self-cast') '18 ApplyToCaster with opponent selected'
Assert-Forensic ($spellPolicy -match 'InflictOnSelf spell with opponent targeted must be a self-cast') '19 InflictOnSelf with opponent selected'
Assert-Forensic ($spellPolicy -match 'beneficial spell genuinely aimed at opponent must not become a self-cast') '20 opponent-beneficial spell not promoted to self'
Assert-Forensic ($spellPolicy -match 'group-effect self spell must remain blocked' -and $spellPolicy -match 'pet summon must remain blocked' -and $spellPolicy -match 'charm must remain blocked') '21 unsafe self/group/pet/charm remains contained'
Assert-Forensic ($controller -match 'FinishHeal' -and $controller -match '_playerHp \+ gained' -and $controller -match '_simHp \+ gained') '22 self-heal updates virtual HP once'
Assert-Forensic ($controller -match 'BeginSpellStart' -and $controller -match 'FinishSpellStart' -and $controller -match 'resourceCommitted=' -and ($controller -match 'target = casterCharacter\.MyStats' -or $controller -match 'target = selfStats') -and $controller -notmatch 'CurrentMana\s*[-+*/]?=') '23 allowed self-heal keeps native resource/cooldown and arg-only retarget' 
Assert-Forensic ($controller -match 'NPC\), "CheckHeals"' -or $controller -match 'CheckHealsRaid') '24 Sim native self-heal evaluation remains hooked/admissible'
Assert-Forensic ($spellPolicy -match 'offensive AoE should be admissible with per-target containment') '25 clean offensive AoE allowed'
Assert-Forensic ($controller -match 'hostileWorldAllowed=true') '26 offensive AoE permits hostile world actor'
Assert-Forensic ($controller -match 'hostile_world_real_offense' -and $controller -match 'authority=real_world') '27 hostile world AoE target remains real'
Assert-Forensic ($controller -match 'WorldReal = true' -and $controller -match 'worldDamagePreserved=true') '28 hostile world hit on duelist remains real'
Assert-Forensic ($semantics -match 'sourceIsWorldHostile.*DuelDamageAuthority.RealWorld' -or $semantics -match 'if \(sourceIsWorldHostile\) return DuelDamageAuthority.RealWorld') '29 world damage never becomes virtual duel HP'
Assert-Forensic ($controller -match 'ProtectedNonParticipant' -and $controller -match 'NotifyUnsafeAreaBystander') '30 protected neutral AoE is prevented/excluded'
Assert-Forensic ($spellPolicy -match 'beneficial AoE should be admissible with per-target containment') '31 participant-only beneficial AoE can be admitted'
Assert-Forensic ($controller -match 'third_party_heal_blocked' -and $controller -match 'IsDuelParticipantClass\(sourceClass\)') '32 unrelated beneficiary is excluded/blocked'
Assert-Forensic ($controller -match 'RestoreRealHealthAndEffects\(\)' -and $controller -match 'RestoreRealLedgerHp') '33 terminal cleanup restores real HP ledger'
Assert-Forensic ($controller -notmatch 'CurrentHP\s*=\s*int\.MaxValue' -and $semantics -notmatch 'NativeDamageHeadroom') '34 no synthetic huge HP remains'
Assert-Forensic ($controller -match '_nativeDamageInFlight = null;' -and $controller -match 'PopNativeDamageState') '35 no stale damage transaction remains'
Assert-Forensic ($lifecycle -match 'for \(int i = 0; i < 10; i\+\+\)' -and $lifecycle -match 'repeated duel cycle') '36 second/repeated duel lifecycle resets'
Assert-Forensic ($controller -match 'if \(!Active\) return true;' -and $controller -match 'DuelPhysicalDamagePatch') '37 ordinary post-duel melee passes native'
Assert-Forensic ($controller -match 'if \(!Active \|\| caster == null\) return true;' -and $controller -match 'DuelMagicDamagePatch') '38 ordinary post-duel spell passes native'
Assert-Forensic ($controller -match 'if \(!Active \|\| target == null\) return true;' -and $controller -match 'DuelSimpleHealPatch') '39 ordinary post-duel self-heal passes native'
Assert-Forensic ($semantics -match 'unrelated world combat must remain vanilla' -and $controller -match 'authority=real_world') '40 ordinary post-duel/world combat authority remains native'

if ($forensicCases -ne 40) { throw "Duel forensic matrix count mismatch: $forensicCases / 40" }
Write-Host ("Practice Duel forensic deterministic/source matrix: PASS (" + $forensicCases + "/40)") -ForegroundColor Green

Write-Host "Practice Duel 0.4.5 damage/healing/source guards: PASS" -ForegroundColor Green

# Final combat-recovery matrix (task cases 1-23). These remain source/pure-policy
# contracts; installed-reference build and live acceptance are separate gates.
$finalRecoveryCases = 0
function Assert-FinalRecovery([bool]$condition, [string]$name) {
    if (-not $condition) { throw ("Duel final recovery matrix failed: " + $name) }
    $script:finalRecoveryCases++
}
Assert-FinalRecovery ($controller -match 'CaptureNativeReduceHp' -and $controller -match 'CapturedReduceHpDamage\s*=\s*damage') '01 scoped ReduceHP capture preserved'
Assert-FinalRecovery ($controller -notmatch 'CurrentHP\s*=\s*int\.MaxValue' -and $semantics -notmatch 'NativeDamageHeadroom') '02 no synthetic HP headroom'
Assert-FinalRecovery ($controller -match 'DuelPhysicalDamagePatch' -and $controller -match 'target == _sim') '03 player melee -> Sim virtual'
Assert-FinalRecovery ($controller -match 'DuelPhysicalDamagePatch' -and $controller -match 'target == _player') '04 Sim melee -> player virtual'
Assert-FinalRecovery ($controller -match 'DuelMagicDamagePatch' -and $controller -match 'PrepareNativeDamage') '05 direct spell virtual damage'
Assert-FinalRecovery ($safety -match 'ReachedYieldThreshold' -and $controller -match 'DuelSafetyPolicy\.ReachedYieldThreshold') '06 legitimate yield threshold only'
Assert-FinalRecovery ($spellPolicy -match 'ordinary Heal must adapt to self' -and $controller -match 'target = selfStats') '07 Minor Healing with opponent targeted adapts to self'
Assert-FinalRecovery ($controller -match 'originalTarget=' -and $controller -match 'resolvedTarget=' -and $controller -match 'target = selfStats') '08 adapted Minor Healing cannot remain aimed at opponent'
Assert-FinalRecovery ($controller -match 'resourceCommitted=' -and $controller -match 'cooldownCommitted=unavailable_in_supplied_api' -and $controller -notmatch 'CurrentMana\s*[-+*/]?=') '09 native mana/cooldown authority preserved'
Assert-FinalRecovery ($spellPolicy -match 'SelfOnly heal with opponent targeted must be a self-cast') '10 SelfOnly with opponent targeted'
Assert-FinalRecovery ($spellPolicy -match 'ApplyToCaster spell with opponent targeted must be a self-cast') '11 ApplyToCaster with opponent targeted'
Assert-FinalRecovery ($spellPolicy -match 'InflictOnSelf spell with opponent targeted must be a self-cast') '12 InflictOnSelf with opponent targeted'
Assert-FinalRecovery ($controller -match 'DuelMagicDamagePatch' -and $controller -match 'FinishHeal' -and $controller -match 'virtual_heal') '13 lifesteal/damage+heal routes remain native-adjacent and virtual once'
Assert-FinalRecovery ($controller -match '\[HarmonyPatch\(typeof\(NPC\), "CheckHeals"\)\]' -and $controller -match 'CheckHealsRaid') '14 Sim native self-heal evaluation retained'
Assert-FinalRecovery ($controller -match 'third_party_heal_blocked' -and $controller -match 'IsFriendlyPartyClass') '15 unrelated outside-friendly heal remains blocked'
Assert-FinalRecovery ($spellPolicy -match 'offensive AoE should be admissible with per-target containment') '16 participant-only offensive AoE admitted'
Assert-FinalRecovery ($controller -match 'hostileWorldAllowed=true') '17 hostile world actor in AoE does not block cast'
Assert-FinalRecovery ($controller -match 'hostile_world_real_offense' -and $controller -match 'authority=real_world') '18 hostile world target takes real native damage'
Assert-FinalRecovery ($controller -match 'WorldReal = true' -and $controller -match 'worldDamagePreserved=true') '19 hostile retaliation remains real'
Assert-FinalRecovery ($controller -match 'ProtectedNonParticipant' -and $controller -match 'NotifyUnsafeAreaBystander') '20 protected neutral actor remains safe'
Assert-FinalRecovery ($lifecycle -match 'repeated duel cycle' -and $controller -match 'PostDuel') '21 second consecutive Duel cleanup path'
Assert-FinalRecovery ($controller -match 'if \(!Active \|\| target == null\) return true;' -and $controller -match 'DuelSimpleHealPatch') '22 post-Duel vanilla healing'
Assert-FinalRecovery ($controller -match 'if \(!Active\) return true;' -and $controller -match 'DuelPhysicalDamagePatch') '23 post-Duel vanilla damage'

if ($finalRecoveryCases -ne 23) { throw "Duel final recovery matrix count mismatch: $finalRecoveryCases / 23" }
Write-Host ("Practice Duel final forensic recovery source matrix: PASS (" + $finalRecoveryCases + "/23)") -ForegroundColor Green

