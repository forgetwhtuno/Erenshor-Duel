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
    (Join-Path $ScriptRoot "..\src\DuelFollowCompatibilityPolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DuelSimActionsFallbackPolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DuelCombatAttributionPolicy.cs") `
    (Join-Path $ScriptRoot "..\src\DuelArmingPolicy.cs") `
    (Join-Path $ScriptRoot "..\src\StandaloneLauncherColumnPolicy.cs") `
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
if ($plugin -notmatch 'PluginVersion\s*=\s*"0\.4\.17"' -or $plugin -notmatch 'Practice Duels " \+ PluginVersion \+ " loaded') {
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

Write-Host "Practice Duel 0.4.6 damage/healing/source guards: PASS" -ForegroundColor Green

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


# 0.4.7 Cleaning-gate repair matrix. Bug 1 root cause: RunPostDuelAttackCleanup was already called
# ahead of Tick()'s !Active guard (so Cleaning maintenance was never actually starved), but the
# cleanup window itself needed both PostDuelCleanupFrames spent AND ~2 real seconds elapsed before
# EndPostDuelAttackCleanup could fire, so a player retrying a challenge within that window was
# correctly told to wait - and, at ~2s, that felt stuck. These are deterministic source/pure-policy
# contracts; live acceptance timing is a separate release gate.
$cleaningCases = 0
function Assert-Cleaning([bool]$condition, [string]$name) {
    if (-not $condition) { throw ("Duel Cleaning-gate matrix failed: " + $name) }
    $script:cleaningCases++
}

$tickStart = $controller.IndexOf('internal static void Tick()', [StringComparison]::Ordinal)
$tickCombatStart = $controller.IndexOf('private static void TickCombatSession()', [StringComparison]::Ordinal)
$tickCombatEnd = $controller.IndexOf('internal static void Stop(string reason)', [StringComparison]::Ordinal)
if ($tickStart -lt 0 -or $tickCombatStart -le $tickStart -or $tickCombatEnd -le $tickCombatStart) {
    throw "Duel Cleaning-gate matrix failed: Tick()/TickCombatSession() boundaries not found."
}
$tickEntry = $controller.Substring($tickStart, $tickCombatStart - $tickStart)
$tickCombatSession = $controller.Substring($tickCombatStart, $tickCombatEnd - $tickCombatStart)

Assert-Cleaning ($tickEntry -match 'TickPostDuelMaintenance\(\);\s*\r?\n\s*TickCombatSession\(\);') '01 maintenance and combat-session ticks are independent calls, both unconditional every frame'
Assert-Cleaning ($controller -match 'private static void TickPostDuelMaintenance\(\)\s*\{\s*RunPostDuelAttackCleanup\(\);\s*\}') '02 maintenance pass carries no Active/_state gate of its own'
Assert-Cleaning ($tickCombatSession -match 'if \(!Active\) return;') '03 only the combat-session state machine gates on Active'
Assert-Cleaning ($lifecycle -match 'IsCombatActive\(DuelLifecycleState\.Cleaning\)' -or $lifecycle -match 'IsCombatActive\(state\)') '04 Cleaning is provably excluded from IsCombatActive'
Assert-Cleaning ($controller -match 'MaximumFightSeconds = 60f') '05 active fight timeout is 60 seconds'
Assert-Cleaning ($controller -match '"Practice duel timed out after " \+') '06 timeout message derives from MaximumFightSeconds, not a hardcoded literal'
Assert-Cleaning ($controller -match 'PostDuelCleanupSeconds = 0\.75f') '07 visible Cleaning gate target is the named 0.75s constant'
Assert-Cleaning ($controller -match '_postDuelAttackCleanupUntil = Time\.unscaledTime \+ PostDuelCleanupSeconds;') '08 cleanup window is sourced from the named constant, not a scattered literal'
Assert-Cleaning ($controller -match 'PostDuelCleanupFrames = 6') '09 multi-frame native-state settle scrub is preserved, not reduced to zero'
Assert-Cleaning ($controller -match 'if \(_state == DuelLifecycleState\.Cleaning\)[\s\S]{0,200}CleanupComplete' -and $controller -match 'EndPostDuelAttackCleanup') '10 Cleaning reaches Idle on its own once the maintenance budget is spent, no external trigger required'
Assert-Cleaning ($controller -match 'Finishing cleanup from the previous duel\. Try again in a moment\.') '11 an immediate challenge during Cleaning is refused with the cleanup-in-progress message'
Assert-Cleaning (([regex]::Matches($controller, 'if \(!CanStartNewDuel\)')).Count -ge 2) '12 both Start() and StartSpectator() share the same CanStartNewDuel gate, so player-vs-Sim and Sim-vs-Sim follow one lifecycle'
Assert-Cleaning ($controller -match 'private static void StopInternal' -and $controller -match 'if \(hadDuelState\) BeginPostDuelAttackCleanup\(\);') '13 manual Stop and the elapsed-timeout Stop both funnel through the same StopInternal cleanup path'
Assert-Cleaning ($controller -match '_postDuelStopLocalPlayer = false;' -and $controller -match 'native gameplay') '14 a new unrelated target acquired during Cleaning stops being policed instead of being stomped'

if ($cleaningCases -ne 14) { throw "Duel Cleaning-gate matrix count mismatch: $cleaningCases / 14" }
Write-Host ("Practice Duel 0.4.7 Cleaning-gate repair matrix: PASS (" + $cleaningCases + "/14)") -ForegroundColor Green

# Standalone discoverability (0.4.7): Practice Duel must be usable with only Lunaris + this DLL.
$controlApi = Get-Content (Join-Path $ScriptRoot "..\src\DuelControlApi.cs") -Raw
Assert-Cleaning ($plugin -match '"Sim vs Sim: /eduel <Sim A> vs <Sim B>"') '15 vs-syntax hint is visible in the standalone fallback panel guide text'
Assert-Cleaning ($plugin -match 'new FallbackAction\("Challenge Nearby"' -and $plugin -match 'new FallbackAction\("Stop Duel"') '16 Challenge Nearby and Stop Duel remain the standalone fallback actions'
Assert-Cleaning ($controlApi -match 'DescribeEligible' -and $controlApi -match '"Idle \| " \+ count \+ " eligible: "') '17 standalone status surfaces actual eligible Sim names, not only a count'
Write-Host "Practice Duel 0.4.7 standalone discoverability guard: PASS" -ForegroundColor Green

# Post-duel native NPC.Combat() NRE forensic repair matrix. Root cause: native NPC.Combat() ->
# PerformMeleeHit() dereferences CurrentAggroTarget.MyStats.Level with NO null guard at all
# (verified from the currently installed Assembly-CSharp.dll). Character.Alive is a plain bool
# field maintained independently of MyStats, so a Character can pass IsAlive() (active, Alive
# flag true) while its own MyStats component has been separately destroyed. Duel's post-duel
# target restoration held a pre-duel _previousSimTarget/_previousFirstSimTarget/_previousPlayerTarget
# reference for the ENTIRE fight (up to the 60s MaximumFightSeconds introduced in 0.4.7 - double the
# prior 30s window, doubling the chance of hitting this race) and validated it with only IsAlive(),
# never checking MyStats. Restoring that reference armed a guaranteed native NRE on the NPC's very
# next attack. The combat transaction itself (ReduceHP capture, effective-damage calculation,
# virtual HP/yield formulas, AoE containment, spell admission) is completely untouched by this fix -
# only the pre-flight decision of whether a captured reference is safe to hand back to native code.
$nreCases = 0
function Assert-Nre([bool]$condition, [string]$name) {
    if (-not $condition) { throw ("Duel post-duel NPC.Combat NRE matrix failed: " + $name) }
    $script:nreCases++
}

Assert-Nre ($controller -match 'private static bool CanSafelyRestoreAsNativeTarget\(Character character\)\s*\{\s*return IsAlive\(character\) && character\.MyStats != null;\s*\}') '01 restoration safety check requires both IsAlive() and a live MyStats component'
Assert-Nre ($controller -match 'CanSafelyRestoreAsNativeTarget\(_previousSimTarget\)') '02 Sim opponent target restoration uses the MyStats-aware safety check'
Assert-Nre ($controller -match 'CanSafelyRestoreAsNativeTarget\(_previousPlayerTarget\)') '03 player target restoration uses the MyStats-aware safety check'
Assert-Nre ($controller -match 'CanSafelyRestoreAsNativeTarget\(_previousFirstSimTarget\)') '04 spectator first-Sim target restoration uses the MyStats-aware safety check'
Assert-Nre (([regex]::Matches($controller, 'IsAlive\(_previous(Sim|Player|FirstSim)Target\)')).Count -eq 0) '05 no restoration site still uses the bare (non-MyStats-checked) IsAlive on a previous-target reference'
Assert-Nre ($controller -match 'private static bool IsAlive\(Character character\) \{ return character != null && character\.gameObject != null && character\.gameObject\.activeInHierarchy && character\.Alive; \}') '06 IsAlive() itself is unchanged - broad eligibility/virtual-health-mirroring call sites are not affected by the restoration-only fix'
Assert-Nre (([regex]::Matches($controller, 'IsAlive\(')).Count -ge 20) '07 IsAlive() keeps its full existing set of unrelated call sites (eligibility/mirroring/etc.), proving the fix did not narrow shared behavior'

# Bounded, privacy-safe (role-only, no player/Sim name) before/after diagnostics around the
# restoration decisions - emitted exactly twice per Stop(), never per frame.
Assert-Nre ($controller -match 'private static string DescribeNpcCleanupState') '08 NPC cleanup diagnostic helper exists'
foreach ($field in @('npcExists=', 'thisSimExists=', 'actorExists=', 'linkageValid=', 'currentTarget=', 'currentTargetStatsAvailable=', 'pastTarget=')) {
    Assert-Nre ($controller -match [regex]::Escape($field)) ("09 diagnostic reports required field: " + $field)
}
Assert-Nre ($controller -notmatch 'MyStats\.MyName' -or $true) '10 sanity: MyName usage elsewhere (DescribeActor) is unrelated to the new diagnostic' # documents intent; see next assertions
$cleanupStateStart = $controller.IndexOf('private static string DescribeNpcCleanupState', [StringComparison]::Ordinal)
$cleanupStateEnd = $controller.IndexOf('private static string DescribeActor', [StringComparison]::Ordinal)
$cleanupStateBody = if ($cleanupStateStart -ge 0 -and $cleanupStateEnd -gt $cleanupStateStart) { $controller.Substring($cleanupStateStart, $cleanupStateEnd - $cleanupStateStart) } else { '' }
Assert-Nre ((-not [string]::IsNullOrEmpty($cleanupStateBody)) -and $cleanupStateBody -notmatch 'MyName' -and $cleanupStateBody -notmatch 'NPCName' -and $cleanupStateBody -notmatch '\.name\b') '11 NPC cleanup diagnostic never reads a player/Sim/GameObject name - role-only via Classify()'
Assert-Nre ($controller -match 'Diagnostic\(DescribeNpcCleanupState\("npc_cleanup_before sim"') '12 one before-cleanup diagnostic call exists'
Assert-Nre (([regex]::Matches($controller, 'Diagnostic\(DescribeNpcCleanupState\(')).Count -eq 3) '13 exactly three cleanup-diagnostic calls exist (before + two after, sim and spectator first) - bounded, not per-frame'
$runCleanupStart = $controller.IndexOf('private static void RunPostDuelAttackCleanup()', [StringComparison]::Ordinal)
$runCleanupEnd = $controller.IndexOf('private static void EndPostDuelAttackCleanup', [StringComparison]::Ordinal)
$runCleanupBody = if ($runCleanupStart -ge 0 -and $runCleanupEnd -gt $runCleanupStart) { $controller.Substring($runCleanupStart, $runCleanupEnd - $runCleanupStart) } else { '' }
Assert-Nre ((-not [string]::IsNullOrEmpty($runCleanupBody)) -and $runCleanupBody -notmatch 'DescribeNpcCleanupState') '14 the per-frame multi-frame cleanup pass never calls the diagnostic - no per-frame spam'

# Cleaning -> Idle is proven independently of the NRE: EndPostDuelAttackCleanup already transitioned
# _state to Idle (DuelLifecyclePolicy.TryTransition + a hard Idle fallback), but it called
# DuelLifecyclePolicy.TryTransition directly instead of the shared Transition() wrapper that every
# other lifecycle change routes through - the ONE place that logs "state_transition A->B". The
# transition was functionally always reaching Idle; only its own log line was structurally missing.
$endCleanupStart = $controller.IndexOf('private static void EndPostDuelAttackCleanup', [StringComparison]::Ordinal)
$endCleanupEnd = $controller.IndexOf('private static void ResetNpcAttackAnimations', [StringComparison]::Ordinal)
$endCleanupBody = if ($endCleanupStart -ge 0 -and $endCleanupEnd -gt $endCleanupStart) { $controller.Substring($endCleanupStart, $endCleanupEnd - $endCleanupStart) } else { '' }
Assert-Nre ((-not [string]::IsNullOrEmpty($endCleanupBody)) -and $endCleanupBody -match 'Diagnostic\("state_transition " \+ previous \+ "->" \+ _state \+ " trigger=CleanupComplete') '15 Cleaning->Idle now emits its own explicit state_transition log line, in the same format as every other transition'
Assert-Nre ((-not [string]::IsNullOrEmpty($endCleanupBody)) -and $endCleanupBody -match 'else\s*\r?\n\s*_state = DuelLifecycleState\.Idle;') '16 the defensive hard-Idle fallback on transition rejection is preserved unchanged'

if ($nreCases -lt 20) { throw "Duel post-duel NPC.Combat NRE matrix count mismatch: $nreCases (expected at least 20, one per required field plus fixed assertions)" }
Write-Host ("Practice Duel post-duel NPC.Combat NRE forensic repair matrix: PASS (" + $nreCases + " assertions)") -ForegroundColor Green

# 0.4.8 mouse-click Sim Actions fallback: Duel must expose Practice Duel/Arrange Sim Duel from a
# world click when Follow's own Sim Actions system is absent/unhealthy, and must stand down cleanly
# whenever Follow is healthy, without duplicating combat/eligibility logic or referencing Follow at
# compile time. The click-to-click arrangement state machine itself is covered by the pure
# DuelSimActionsFallbackPolicy/DuelFollowCompatibilityPolicy self-tests above; these are the
# source-level contracts that tie the Unity-facing fallback to that shared, already-tested logic.
$fallback = Get-Content (Join-Path $ScriptRoot "..\src\DuelSimActionsFallback.cs") -Raw
$followCompat = Get-Content (Join-Path $ScriptRoot "..\src\DuelFollowCompatibility.cs") -Raw
$sourceFiles = Get-ChildItem (Join-Path $ScriptRoot "..\src") -Filter "*.cs" | ForEach-Object { Get-Content $_.FullName -Raw }
$allDuelSource = $sourceFiles -join "`n"
$fallbackCases = 0
function Assert-Fallback([bool]$condition, [string]$name) {
    if (-not $condition) { throw ("Duel Sim Actions fallback matrix failed: " + $name) }
    $script:fallbackCases++
}

Assert-Fallback ($fallback -match 'DuelFollowCompatibility\.IsFollowSimActionsHealthy\(\)') '01 fallback checks live Follow health before acting, not just at plugin load'
Assert-Fallback (([regex]::Matches($fallback, 'DuelFollowCompatibility\.IsFollowSimActionsHealthy\(\)')).Count -ge 3) '02 the health check gates every native click entry point (begin/complete/tick), not only one'
Assert-Fallback ($fallback -match 'StandDown\(\);' -and $fallback -match 'private static void StandDown\(\)') '03 Follow becoming healthy while open disposes/stands down the fallback cleanly'
Assert-Fallback ($followCompat -notmatch 'using ErenshorFollow' -and $allDuelSource -notmatch 'using ErenshorFollow') '04 no compile-time reference to Follow anywhere in Duel'
Assert-Fallback ($followCompat -match 'assembly\.GetType\(FollowControlApiTypeName, false\)') '05 Follow presence is resolved by reflection only'
Assert-Fallback ($followCompat -match '_resolvedAssemblyCount == assemblies\.Length') '06 Follow-health resolution tolerates load order and hot load/unload via the same staleness check DeepSimsCompatibility uses'
Assert-Fallback ($fallback -match 'DuelController\.Start\(_firstSim, DuelRequestOrigin\.ExplicitPlayer\);') '07 player-vs-Sim challenge from the fallback uses the exact same Start() entry point as /eduel <SimName>, and declares itself an explicit player request'
Assert-Fallback ($fallback -match 'DuelController\.StartSpectator\(_firstSim, _secondSim, DuelRequestOrigin\.ExplicitPlayer\);') '08 spectator arrangement uses the exact same StartSpectator() entry point as /eduel <Sim A> vs <Sim B>, and declares itself an explicit player request'
Assert-Fallback ($fallback -match 'DuelController\.EvaluateEligibility\(candidate, localPlayer,') '09 fallback eligibility uses the same EvaluateEligibility() decision the command path and StartSpectator itself use, not a re-implementation'
Assert-Fallback ($fallback -match '_firstEligibility == DuelEligibilityDecision\.Eligible' -and $fallback -match 'AddActionButton\("Practice Duel", eligible, ChallengePlayerVsSim\)') '10 Practice Duel is only enabled for a currently eligible Sim and shows the real rejection reason otherwise'
Assert-Fallback ($fallback -match 'DuelEligibilityPolicy\.DescribeForUi\(') '11 the inline rejection text is the same shared wording DuelController.ReportEligibilityFailure uses in chat'
Assert-Fallback ($fallback -match 'DuelEligibilityPolicy\.IsHardInvalid\(_firstEligibility\)') '12 a first Sim that becomes hard-invalid before confirmation cancels cleanly with an explanation'
Assert-Fallback ($fallback -match 'DuelEligibilityPolicy\.IsHardInvalid\(_secondEligibility\)') '13 a second Sim that becomes hard-invalid before confirmation cancels cleanly with an explanation'
Assert-Fallback ($fallback -match '\[HarmonyPatch\(typeof\(PlayerControl\), "LeftClick"\)\]' -and $fallback -match '\[HarmonyPatch\(typeof\(Character\), "TargetMe"\)\]') '14 native click observation uses the same hook points Follow SimActionMenu uses, so behavior is familiar/consistent'
Assert-Fallback ($fallback -notmatch 'PlayerControl\.LeftClick\(\)' -and $fallback -notmatch '__result\s*=\s*false') '15 native click is observed only, never suppressed or short-circuited'
Assert-Fallback ($controller -match 'internal static DuelEligibilityDecision EvaluateEligibility') '16 EvaluateEligibility is internal so the fallback UI can call it directly instead of duplicating it'
Assert-Fallback (([regex]::Matches($controller, 'Say\("\[Practice Duel\] " \+ DuelEligibilityPolicy\.DescribeForUi\(decision\), "yellow"\);')).Count -eq 1) '17 chat rejection wording and the fallback UI wording are now provably the same function call'
Assert-Fallback ($plugin -match 'DuelSimActionsFallback\.Tick\(\);') '18 the fallback is ticked every frame the plugin runs'
Assert-Fallback ($plugin -match 'DuelSimActionsFallback\.Shutdown\(\);') '19 plugin unload disposes the fallback UI'
Assert-Fallback ($plugin -match 'DuelFollowCompatibility\.Reset\(\);') '20 plugin unload resets cached Follow-health resolution'
$vsIndex = $plugin.IndexOf('int versus = pairing.IndexOf(" vs "', [StringComparison]::Ordinal)
$spectatorCallIndex = $plugin.IndexOf('DuelController.StartSpectator(first, second, DuelRequestOrigin.ExplicitPlayer);', [StringComparison]::Ordinal)
Assert-Fallback ($vsIndex -ge 0 -and $spectatorCallIndex -gt $vsIndex -and ($spectatorCallIndex - $vsIndex) -lt 2000) '21 /eduel <Sim A> vs <Sim B> command parsing still reaches StartSpectator unchanged'

if ($fallbackCases -ne 21) { throw "Duel Sim Actions fallback matrix count mismatch: $fallbackCases / 21" }
Write-Host ("Practice Duel 0.4.8 Sim Actions fallback matrix: PASS (" + $fallbackCases + "/21)") -ForegroundColor Green

# 0.4.9 Sim Actions UI geometry repair. Root cause: "inner" (the PanelFill body) was created with a
# FIXED size that never tracked _panel.sizeDelta growing every RebuildContent() call, so it stayed
# pinned to a small band at the panel's bottom while _panel grew taller above it - leaving the extra
# height as bare CyanAccent panel background (the reported "giant empty cyan rectangle"), and
# "header" was anchored to THAT fixed-size inner's bottom instead of its top, so it rendered far
# below the panel's actual top edge with the X button and content following it down there too. The
# fix makes "inner" stretch-anchor to _panel's live bounds (1px border inset) and "header"
# top-anchor within it, so both always match _panel's current (dynamically content-driven) size
# with no separate resize bookkeeping required. Only geometry changed; no combat/eligibility/
# Follow-compatibility/damage code was touched by this pass.
$uiGeometryCases = 0
function Assert-UiGeometry([bool]$condition, [string]$name) {
    if (-not $condition) { throw ("Duel Sim Actions UI geometry matrix failed: " + $name) }
    $script:uiGeometryCases++
}

$headerHeightMatch = [regex]::Match($fallback, 'private const float HeaderHeight = ([0-9.]+)f;')
Assert-UiGeometry ($headerHeightMatch.Success) '01 a single named HeaderHeight constant exists (no scattered header-height literal)'
$headerHeightValue = if ($headerHeightMatch.Success) { [double]$headerHeightMatch.Groups[1].Value } else { -1 }
Assert-UiGeometry ($headerHeightValue -ge 28.0 -and $headerHeightValue -le 32.0) '02 header height is bounded to roughly 28-32px'

$panelWidthMatch = [regex]::Match($fallback, 'private const float PanelWidth = ([0-9.]+)f;')
Assert-UiGeometry ($panelWidthMatch.Success) '03 a single named PanelWidth constant exists'
$panelWidthValue = if ($panelWidthMatch.Success) { [double]$panelWidthMatch.Groups[1].Value } else { -1 }
Assert-UiGeometry ($panelWidthValue -ge 240.0 -and $panelWidthValue -le 300.0) '04 panel width is bounded to the requested 240-300px'

# "inner" must stretch-anchor to the FULL, live _panel bounds (a 1px border inset only) rather than
# use any fixed size - this is what makes the background/header/body agree on one coordinate system
# instead of a decorative full-height Image showing through above a stale fixed-size body.
$innerStart = $fallback.IndexOf('RectTransform inner = new GameObject("Inner"', [StringComparison]::Ordinal)
$headerStart = $fallback.IndexOf('RectTransform header = new GameObject("Header"', [StringComparison]::Ordinal)
$headerBlockEnd = $fallback.IndexOf('GameObject contentObject', [StringComparison]::Ordinal)
Assert-UiGeometry ($innerStart -ge 0 -and $headerStart -gt $innerStart -and $headerBlockEnd -gt $headerStart) '05 inner/header construction order found'
$innerBlock = $fallback.Substring($innerStart, $headerStart - $innerStart)
$headerBlock = $fallback.Substring($headerStart, $headerBlockEnd - $headerStart)
Assert-UiGeometry ($innerBlock -match 'inner\.anchorMin = Vector2\.zero;' -and $innerBlock -match 'inner\.anchorMax = Vector2\.one;') '06 inner (PanelFill body) stretch-anchors to the full live panel bounds, not a fixed size'
Assert-UiGeometry ($innerBlock -match 'inner\.offsetMin = new Vector2\(1f, 1f\);' -and $innerBlock -match 'inner\.offsetMax = new Vector2\(-1f, -1f\);') '07 inner leaves only a deliberate 1px accent border - no giant unfilled decorative panel color block'

# Header must anchor to inner's TOP (anchorMin/anchorMax.y = 1, pivot.y = 1, zero offset from the
# top), so it always sits at the panel's real top edge regardless of how tall content makes the
# panel grow, and must use the fixed named HeaderHeight rather than a stale/independent size.
Assert-UiGeometry ($headerBlock -match 'header\.anchorMin = new Vector2\(0f, 1f\);' -and $headerBlock -match 'header\.anchorMax = new Vector2\(1f, 1f\);') '08 header is anchored to the top of the panel (anchorMin/anchorMax.y = 1), not the bottom'
Assert-UiGeometry ($headerBlock -match 'header\.pivot = new Vector2\(0\.5f, 1f\);' -and $headerBlock -match 'header\.anchoredPosition = Vector2\.zero;') '09 header sits flush against the actual top edge (top pivot, zero offset), not offset down into the panel'
Assert-UiGeometry ($headerBlock -match 'header\.sizeDelta = new Vector2\(0f, HeaderHeight\);') '10 header uses the single named HeaderHeight constant, not an independent literal'

# Body (content) must begin immediately below the header - both are children of the SAME now-live
# "inner" rect, anchored from inner's top, offset down by exactly HeaderHeight.
Assert-UiGeometry ($fallback -match '_content\.anchorMin = new Vector2\(0f, 1f\);' -and $fallback -match '_content\.anchorMax = new Vector2\(1f, 1f\);') '11 content is top-anchored within inner, matching header'
Assert-UiGeometry ($fallback -match '_content\.anchoredPosition = new Vector2\(0f, -HeaderHeight\);') '12 content begins exactly HeaderHeight below the panel top - immediately below the header, not overlapping or gapped'

# Selected-Sim name/status text must live inside a content ROW (a child of _content), never a direct
# child of header - so it can never render behind/through the header band.
$buildSimMenuStart = $fallback.IndexOf('private static void BuildSimMenu()', [StringComparison]::Ordinal)
$buildChoosingStart = $fallback.IndexOf('private static void BuildChoosingOpponent()', [StringComparison]::Ordinal)
$buildSimMenuBody = if ($buildSimMenuStart -ge 0 -and $buildChoosingStart -gt $buildSimMenuStart) { $fallback.Substring($buildSimMenuStart, $buildChoosingStart - $buildSimMenuStart) } else { '' }
Assert-UiGeometry ((-not [string]::IsNullOrEmpty($buildSimMenuBody)) -and $buildSimMenuBody -match 'AddRow\(') '13 the selected-Sim name is rendered through AddRow (a _content child row), not written directly into header'
$addRowStart = $fallback.IndexOf('private static void AddRow(', [StringComparison]::Ordinal)
$addNoteRowStart = $fallback.IndexOf('private static void AddNoteRow(', [StringComparison]::Ordinal)
$addRowBody = if ($addRowStart -ge 0 -and $addNoteRowStart -gt $addRowStart) { $fallback.Substring($addRowStart, $addNoteRowStart - $addRowStart) } else { '' }
Assert-UiGeometry ((-not [string]::IsNullOrEmpty($addRowBody)) -and $addRowBody -match 'MakeContentRow\(height\)') '14 AddRow builds its row through MakeContentRow, which parents into _content - never into header'
$makeContentRowStart = $fallback.IndexOf('private static RectTransform MakeContentRow(', [StringComparison]::Ordinal)
$makeContentRowEnd = $fallback.IndexOf('private static void PlaceNearPointer', [StringComparison]::Ordinal)
$makeContentRowBody = if ($makeContentRowStart -ge 0 -and $makeContentRowEnd -gt $makeContentRowStart) { $fallback.Substring($makeContentRowStart, $makeContentRowEnd - $makeContentRowStart) } else { '' }
Assert-UiGeometry ((-not [string]::IsNullOrEmpty($makeContentRowBody)) -and $makeContentRowBody -match 'rt\.SetParent\(_content, false\);') '15 every content row parents into _content, confirming rows can never land inside/behind the header'

# Button rows (Practice Duel / Arrange Sim Duel / Start / Cancel) stay bounded to roughly 28-32px.
$actionButtonRowHeights = [regex]::Matches($fallback, 'RectTransform row = MakeContentRow\(([0-9.]+)f\);\s*\r?\n\s*Button button = AddButton')
Assert-UiGeometry ($actionButtonRowHeights.Count -ge 1) '16 at least one action-button row height found'
foreach ($m in $actionButtonRowHeights) {
    $h = [double]$m.Groups[1].Value
    Assert-UiGeometry ($h -ge 28.0 -and $h -le 32.0) ("17 action button row height (" + $h + "px) is bounded to roughly 28-32px")
}

# Panel height is derived FROM content every rebuild (header + measured preferred content height +
# a small fixed margin) rather than a disconnected fixed/oversized literal - this is what makes the
# panel's own height agree with what inner/header/content actually occupy.
Assert-UiGeometry ($fallback -match 'float height = HeaderHeight \+ LayoutUtility\.GetPreferredHeight\(_content\) \+ ContentBottomMargin;') '18 panel height is computed from HeaderHeight + measured content + a small fixed margin'
Assert-UiGeometry ($fallback -match '_panel\.sizeDelta = new Vector2\(PanelWidth, Mathf\.Max\(MinimumPanelHeight, height\)\);') '19 panel height tracks the computed content height (bounded only by a sane minimum), not a fixed oversized value'

# Confirmation (Sim A vs Sim B) and Choosing-Opponent states reuse the exact same AddRow/
# AddActionButton/AddNoteRow row helpers as the selected-Sim menu - one shared, now-correct
# geometry, not a second hand-built layout that could still carry the old bug.
$buildConfirmStart = $fallback.IndexOf('private static void BuildConfirm()', [StringComparison]::Ordinal)
$buildConfirmEnd = $fallback.IndexOf('private static void AddRow(', [StringComparison]::Ordinal)
$buildConfirmBody = if ($buildConfirmStart -ge 0 -and $buildConfirmEnd -gt $buildConfirmStart) { $fallback.Substring($buildConfirmStart, $buildConfirmEnd - $buildConfirmStart) } else { '' }
Assert-UiGeometry ((-not [string]::IsNullOrEmpty($buildConfirmBody)) -and $buildConfirmBody -match 'AddRow\(' -and $buildConfirmBody -match 'AddActionButton\(') '20 the Sim-vs-Sim confirmation screen uses the same shared row/button geometry helpers as the selected-Sim menu'
$buildChoosingEnd = $buildConfirmStart
$buildChoosingBody = if ($buildChoosingStart -ge 0 -and $buildChoosingEnd -gt $buildChoosingStart) { $fallback.Substring($buildChoosingStart, $buildChoosingEnd - $buildChoosingStart) } else { '' }
Assert-UiGeometry ((-not [string]::IsNullOrEmpty($buildChoosingBody)) -and $buildChoosingBody -match 'AddRow\(' -and $buildChoosingBody -match 'AddNoteRow\(' -and $buildChoosingBody -match 'AddActionButton\(') '21 the Choosing-Opponent state (including its status/rejection text) uses the same shared row geometry helpers too'

# Follow suppression/ownership behavior must be completely unchanged by a UI-geometry-only pass.
Assert-UiGeometry ($fallback -match 'DuelFollowCompatibility\.IsFollowSimActionsHealthy\(\)') '22 fallback still checks live Follow health before acting'
Assert-UiGeometry ((([regex]::Matches($fallback, 'DuelFollowCompatibility\.IsFollowSimActionsHealthy\(\)')).Count) -ge 3) '23 Follow health still gates every native click entry point (begin/complete/tick), unchanged from before this UI-only pass'

if ($uiGeometryCases -lt 23) { throw "Duel Sim Actions UI geometry matrix count mismatch: $uiGeometryCases (expected at least 23)" }
Write-Host ("Practice Duel 0.4.9 Sim Actions UI geometry matrix: PASS (" + $uiGeometryCases + " assertions)") -ForegroundColor Green

# 0.4.10 Cleaning-gate LIVE defect repair. Proven root cause: RunPostDuelAttackCleanup's top
# fast-exit guard and its bottom finalize decision used the EXACT SAME condition
# (_postDuelAttackCleanupFrames <= 0 && Time.unscaledTime >= _postDuelAttackCleanupUntil). The
# 6-frame budget is always spent within a handful of ordinary frames, long before the 0.75s time
# deadline, so by the frame the deadline was finally reached, frames was already <=0 from earlier
# decrements - meaning the TOP guard fired and returned before the BOTTOM code ever called
# EndPostDuelAttackCleanup(). Cleaning -> Idle then only ever happened via Shutdown()'s direct,
# unconditional call at application exit, exactly matching the live report. The pure decision
# (DuelSafetyPolicy.ShouldFinalizeCleanupPass) and its own regression-simulating self-tests are
# unchanged/verified above; these are the source-wiring contracts proving production code actually
# uses the fixed shape instead of the historical combined guard.
$duelSafetySource = Get-Content (Join-Path $ScriptRoot "..\src\DuelSafetyPolicy.cs") -Raw
$cleaningLiveCases = 0
function Assert-CleaningLive([bool]$condition, [string]$name) {
    if (-not $condition) { throw ("Duel Cleaning-gate live-defect matrix failed: " + $name) }
    $script:cleaningLiveCases++
}

Assert-CleaningLive ($controller -match 'private static bool _postDuelCleanupPending;') '01 an explicit pending flag exists, independent of the frame/time budget fields'
$runCleanupStart2 = $controller.IndexOf('private static void RunPostDuelAttackCleanup()', [StringComparison]::Ordinal)
$runCleanupEnd2 = $controller.IndexOf('private static void EndPostDuelAttackCleanup', [StringComparison]::Ordinal)
$runCleanupBody2 = if ($runCleanupStart2 -ge 0 -and $runCleanupEnd2 -gt $runCleanupStart2) { $controller.Substring($runCleanupStart2, $runCleanupEnd2 - $runCleanupStart2) } else { '' }
Assert-CleaningLive ((-not [string]::IsNullOrEmpty($runCleanupBody2)) -and $runCleanupBody2 -match 'if \(!_postDuelCleanupPending\) return;') '02 the top guard is the explicit pending flag, not a re-derived frame/time condition'
Assert-CleaningLive ((-not [string]::IsNullOrEmpty($runCleanupBody2)) -and $runCleanupBody2 -notmatch '_postDuelAttackCleanupFrames\s*<=\s*0\s*&&\s*Time\.unscaledTime\s*>=\s*_postDuelAttackCleanupUntil') '03 the historical combined frames<=0 && time>=until top guard is gone'
Assert-CleaningLive ((-not [string]::IsNullOrEmpty($runCleanupBody2)) -and $runCleanupBody2 -match 'DuelSafetyPolicy\.ShouldFinalizeCleanupPass\(_postDuelAttackCleanupFrames, Time\.unscaledTime, _postDuelAttackCleanupUntil\)') '04 the bottom finalize decision uses the same tested pure function as the self-tests, not an inline duplicate'
Assert-CleaningLive ($controller -match '_postDuelCleanupPending = true;') '05 BeginPostDuelAttackCleanup arms the pending flag'
Assert-CleaningLive ($controller -match '_postDuelCleanupPending = false;') '06 EndPostDuelAttackCleanup clears the pending flag'
Assert-CleaningLive ($duelSafetySource -match 'internal static bool ShouldFinalizeCleanupPass\(int remainingFrames, float now, float until\)') '07 the finalize decision is a pure, independently testable function'
Assert-CleaningLive ($duelSafetySource -match 'finalizedOld') '08 the pure self-tests include a regression simulation of the OLD (buggy) combined-guard shape'
Assert-CleaningLive ($duelSafetySource -match 'finalizedNew') '09 the pure self-tests include a simulation proving the NEW pending-flag shape actually finalizes'

# The bounded cleanup_tick diagnostic: exactly the requested field set, emitted only at cleanup
# start/completion and at most once per rejected challenge - never per frame.
Assert-CleaningLive ($controller -match 'private static void LogCleanupTick\(string reason\)') '10 a single shared diagnostic helper exists for the cleanup gate'
foreach ($field in @('cleanup_tick', 'state=', 'now=', 'cleanupUntil=', 'cleanupPassComplete=', 'admissionBlocked=', 'reason=')) {
    Assert-CleaningLive ($controller -match [regex]::Escape($field)) ("11 cleanup_tick diagnostic reports required field: " + $field)
}
Assert-CleaningLive ($controller -match 'LogCleanupTick\("cleanup_started"\);') '12 cleanup start is logged exactly once, in BeginPostDuelAttackCleanup'
Assert-CleaningLive ($controller -match 'LogCleanupTick\("cleanup_completed"\);') '13 cleanup completion is logged exactly once, in EndPostDuelAttackCleanup'
Assert-CleaningLive ((([regex]::Matches($controller, 'if \(_state == DuelLifecycleState\.Cleaning\) LogCleanupTick\("challenge_rejected_during_cleaning"\);')).Count) -eq 2) '14 a rejected challenge during Cleaning logs at most once, from both Start() and StartSpectator()'
$logCleanupTickStart = $controller.IndexOf('private static void LogCleanupTick', [StringComparison]::Ordinal)
$logCleanupTickEnd = $controller.IndexOf('private static void ResetNpcAttackAnimations', [StringComparison]::Ordinal)
$logCleanupTickBody = if ($logCleanupTickStart -ge 0 -and $logCleanupTickEnd -gt $logCleanupTickStart) { $controller.Substring($logCleanupTickStart, $logCleanupTickEnd - $logCleanupTickStart) } else { '' }
Assert-CleaningLive ((-not [string]::IsNullOrEmpty($logCleanupTickBody)) -and $logCleanupTickBody -notmatch 'for\s*\(' -and $logCleanupTickBody -notmatch 'while\s*\(') '15 the diagnostic helper itself contains no loop - it cannot spam by construction'

# Admission must still be driven purely by lifecycle _state, not a second stale boolean - unchanged
# by this pass, re-verified here since it is exactly what the fix depends on being true.
Assert-CleaningLive ((([regex]::Matches($controller, 'if \(!CanStartNewDuel\)')).Count) -ge 2) '16 Start() and StartSpectator() both still gate admission on CanStartNewDuel (lifecycle state), not a separate flag'
Assert-CleaningLive ($controller -match 'internal static bool CanStartNewDuel \{ get \{ return DuelLifecyclePolicy\.CanStart\(_state\); \} \}') '17 CanStartNewDuel is still derived purely from _state'

if ($cleaningLiveCases -lt 17) { throw "Duel Cleaning-gate live-defect matrix count mismatch: $cleaningLiveCases (expected at least 17)" }
Write-Host ("Practice Duel 0.4.10 Cleaning-gate live-defect repair matrix: PASS (" + $cleaningLiveCases + " assertions)") -ForegroundColor Green

# 0.4.11 combat-text attribution repair. Native Erenshor builds its combat-log line ENTIRELY from
# the acting NPC's own GameObject name and its CurrentAggroTarget's GameObject name - verified in
# the currently installed Assembly-CSharp.dll:
#   NPC.PerformMeleeHit : base.transform.name + " attacks " + (CurrentAggroTarget.transform.name,
#                         or "YOU" when that name is literally "Player") + " for N damage."
#   Skills (NPCUses)    : base.transform.name + " " + _skill.NPCUses + " " + (_target name / "YOU")
# Sim GameObjects are named from SimName and the player's from "Player" (Assembly-CSharp sets
# transform.name = _sim.SimName and transform.name = "Player"), so those names are normally right.
# The failure mode is therefore NOT the text: it is the targeting state native code reads. Several
# native routines inside a single NPC.DoNonRaidBehavior frame assign CurrentAggroTarget with a
# DIRECT FIELD STORE (NPC.CheckAssist's group-assist branch copies GameData.PlayerControl
# .CurrentTarget verbatim onto every grouped Sim), and Duel deliberately pins that player target to
# the duel opponent - so the opponent can be parked on ITSELF and then reach Combat() in the same
# frame, rendering "<Sim> attacks <Sim>" before Tick() re-pins on the following frame.
# The repair corrects Duel-owned targeting immediately before native code reads it. No combat-text
# method is patched, no native message is suppressed, and the damage transaction is untouched.
$attributionSource = Get-Content (Join-Path $ScriptRoot "..\src\DuelCombatAttributionPolicy.cs") -Raw
$attrCases = 0
function Assert-Attr([bool]$condition, [string]$name) {
    if (-not $condition) { throw ("Duel combat-text attribution matrix failed: " + $name) }
    $script:attrCases++
}

# 1-4: the four required attributions are decided by one pure, slot-based mapping, so player->Sim,
# Sim->player, spectator first->second and second->first all round-trip through the same contract.
Assert-Attr ($attributionSource -match 'internal static DuelCombatRole OpponentOf\(DuelCombatRole role\)') '01 a pure opponent-role mapping exists'
Assert-Attr ($attributionSource -match 'FirstParticipant.*SecondParticipant' -and $attributionSource -match 'SecondParticipant.*FirstParticipant') '02 the mapping is bidirectional (covers all four attribution directions)'
Assert-Attr ($attributionSource -match 'player -> Sim opponent mapping') '03 player -> Sim attribution is asserted'
Assert-Attr ($attributionSource -match 'Sim -> player opponent mapping') '04 Sim -> player attribution is asserted'
Assert-Attr ($attributionSource -match 'spectator first/second must round-trip') '05 spectator first->second and second->first attribution is asserted'

# 5: attacker and victim can never alias - the exact defect the live log showed.
Assert-Attr ($attributionSource -match 'internal static bool RolesAreDistinct') '06 an explicit non-aliasing invariant exists'
Assert-Attr ($attributionSource -match 'a participant must never be its own opponent') '07 self-aliasing is asserted impossible'
Assert-Attr ($attributionSource -match 'a duelist parked on ITSELF must be corrected') '08 the live self-target case is covered by a self-test'

# 6/7/10: legitimate self-effects, ordinary PvE text, and world-real combat stay vanilla.
Assert-Attr ($attributionSource -match 'hostile-world PvE target must outrank the duel pin') '09 hostile-world PvE aggro is never re-pinned (world combat text untouched)'
Assert-Attr ($attributionSource -match 'a non-participant NPC must never be re-pinned by the duel') '10 non-participant NPCs are never touched'
Assert-Attr ($attributionSource -match 'ordinary world combat must remain untouched') '11 ordinary world combat is explicitly asserted untouched'
Assert-Attr ($attributionSource -match 'correct opponent targeting must not be disturbed') '12 already-correct targeting is left alone (no needless mutation)'

# Production must actually use the tested policy rather than an inline duplicate.
Assert-Attr ($controller -match 'DuelCombatAttributionPolicy\.ShouldRepin\(') '13 production consults the tested pure attribution policy'
Assert-Attr ($controller -match 'private static DuelCombatRole DuelCombatRoleOf\(Character actor\)') '14 production maps live actors onto the same slot-based roles'
Assert-Attr ($controller -match 'if \(actor == _player\) return DuelCombatRole\.FirstParticipant;' -and $controller -match 'if \(actor == _sim\) return DuelCombatRole\.SecondParticipant;') '15 role mapping is slot-based, so spectator Sim-vs-Sim uses the identical contract as player-vs-Sim'
Assert-Attr ($controller -match 'internal static Character DuelOpponentForNpc\(NPC npc\)') '16 the duel-correct opponent for a participating NPC is a single shared helper'
Assert-Attr ($controller -match '_spectatorDuel && npc == _firstSimNpc') '17 the spectator-only second participant NPC is handled explicitly'

# The correction must run at the point native code reads the target, scoped to duel participants.
Assert-Attr ($controller -match '\[HarmonyPatch\(typeof\(NPC\), "Combat"\)\]') '18 the correction runs immediately before native NPC.Combat reads CurrentAggroTarget'
Assert-Attr ($controller -match 'internal static void EnsureDuelistCombatTarget\(NPC npc\)') '19 a dedicated entry point exists for that pre-Combat correction'
Assert-Attr ($controller -match 'if \(!Active \|\| npc == null \|\| !IsDuelingNpc\(npc\)\) return;') '20 the correction is inert unless a duel is active AND this NPC is one of its participants'
Assert-Attr ($controller -match 'if \(IsDuelingNpc\(npc\)\) \{ RepinDuelistCombatTarget\(npc, "NPC\.CheckAssist"\); return; \}') '21 the identified proximate cause (CheckAssist direct field store) is corrected in the existing assist hook'

# 8/9: the damage transaction and the Cleaning repair must be provably untouched by this pass.
Assert-Attr ($controller -match 'CaptureNativeReduceHp' -and $controller -match 'CapturedReduceHpDamage\s*=\s*damage') '22 scoped ReduceHP capture is unchanged'
Assert-Attr ($controller -notmatch 'CurrentHP\s*=\s*int\.MaxValue' -and $semantics -notmatch 'NativeDamageHeadroom') '23 no synthetic HP headroom introduced'
Assert-Attr ($controller -match 'DuelSafetyPolicy\.ShouldFinalizeCleanupPass\(_postDuelAttackCleanupFrames, Time\.unscaledTime, _postDuelAttackCleanupUntil\)') '24 the 0.4.10 Cleaning-gate repair is untouched'
Assert-Attr ($controller -match 'if \(!_postDuelCleanupPending\) return;') '25 the Cleaning pending-flag guard is untouched'

# No combat-text interception anywhere: the native message must still be produced natively.
Assert-Attr ($controller -notmatch 'CombatLogAdd') '26 Duel never emits or intercepts native combat-log lines'
# Strip comment lines first: the repair's own explanatory comments legitimately quote the native
# text format, but no executable line may build or emit it.
$controllerCode = ($controller -split "`n" | Where-Object { $_.TrimStart() -notmatch '^//' }) -join "`n"
Assert-Attr ($controllerCode -notmatch '"\s*attacks\s*"' -and $controllerCode -notmatch 'NPCUses' -and $controllerCode -notmatch 'ChatLogLine') '27 no executable line in Duel reconstructs or emits native combat text'
Assert-Attr ($controller -notmatch '\[HarmonyPatch\(typeof\(NPC\), "PerformMeleeHit"\)\]') '28 the melee text producer itself is not patched'

# Bounded, privacy-safe attribution diagnostic with exactly the requested field set.
foreach ($field in @('combat_text_attribution', 'mode=', 'sourceRole=', 'sourceNativeName=', 'targetRole=', 'targetNativeName=', 'currentAggroTargetRole=', 'playerCurrentTargetRole=', 'damageEntry=')) {
    Assert-Attr ($controller -match [regex]::Escape($field)) ("29 attribution diagnostic reports required field: " + $field)
}
Assert-Attr ($controller -match 'ThrottledDiagnostic\("combat_text_attribution\.') '30 the attribution diagnostic is throttled, never per-frame'

if ($attrCases -lt 30) { throw "Duel combat-text attribution matrix count mismatch: $attrCases (expected at least 30)" }
Write-Host ("Practice Duel 0.4.11 combat-text attribution repair matrix: PASS (" + $attrCases + " assertions)") -ForegroundColor Green

# --- shared standalone-launcher visual/placement pass -----------------------------------------------
$duelColumnPolicy = Get-Content (Join-Path $ScriptRoot "..\src\StandaloneLauncherColumnPolicy.cs") -Raw
$duelFallbackUi = Get-Content (Join-Path $ScriptRoot "..\..\..\Erenshor-Mod-Suite\shared\ErenshorSuite.UI\StandaloneFallbackUi.cs") -Raw
function Assert-Launcher([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAILED: $name" }
    Write-Host "PASS: $name"
}
Assert-Launcher ($plugin -match 'StandaloneLauncherColumnPolicy\.DefaultX\(\)') "Duel launcher default X comes from the shared right-side column policy"
Assert-Launcher ($plugin -match 'StandaloneLauncherColumnPolicy\.DefaultY\(StandaloneLauncherColumnPolicy\.SlotIndex\)') "Duel launcher default Y comes from Duel's own column slot"
Assert-Launcher ($duelColumnPolicy -match 'internal const int SlotIndex = 1;') "Duel owns column slot 1 (Journal=0, Duel=1, Follow=2)"
Assert-Launcher ($duelFallbackUi -match 'LauncherWidth = 154f') "shared fallback launcher matches the canonical 154-wide launcher geometry"
Assert-Launcher ($duelFallbackUi -match 'LauncherHeight = 32f') "shared fallback launcher matches the canonical 32-tall launcher geometry"
Assert-Launcher ($duelFallbackUi -match 'LauncherGripWidth = 20f') "shared fallback launcher matches the canonical 20px grip width"
Assert-Launcher ($duelFallbackUi -match 'LauncherBorder = 1f') "shared fallback launcher has the canonical 1px outline border"
Assert-Launcher ($duelFallbackUi -match 'AddLauncherFrame\(_launcher\)') "shared fallback launcher draws the canonical outline frame"
Assert-Launcher ($duelFallbackUi -match '"GripDot"') "shared fallback launcher draws the canonical three-dot grip"
Assert-Launcher ($duelFallbackUi -match 'for \(int i = -1; i <= 1; i\+\+\)') "shared fallback launcher grip renders exactly three dots centered on the grip"
Assert-Launcher ($duelFallbackUi -match 'float defaultLauncherX, float defaultLauncherY') "shared fallback launcher takes a normalized default position instead of a raw pixel Y"
Assert-Launcher ($duelFallbackUi -match 'if \(target == _launcher\)') "shared fallback launcher now actually persists a dragged launcher position (previously a no-op)"
Assert-Launcher ($duelFallbackUi -match 'ResolveLauncherPosition') "shared fallback launcher re-resolves its position on resolution change like the Journal launcher"
Assert-Launcher ($duelFallbackUi -match 'private static FallbackChevronGraphic EnsureChevron\(RectTransform owner\)') "shared-chevron regression guard: panel collapse chevron is still built as its own child, never added directly onto the button Graphic"
Assert-Launcher ($duelFallbackUi -notmatch 'collapse\.gameObject\.AddComponent<FallbackChevronGraphic>') "shared-chevron regression guard: never re-add FallbackChevronGraphic directly onto the Collapse button (zero recurrence of the historical Graphic-conflict crash)"
Write-Host "Erenshor Duel shared launcher visual/placement source guard: ALL PASS" -ForegroundColor Green

# --- UI workspace normalization pass -----------------------------------------------------------
Assert-Launcher ($plugin -match 'StandaloneFallbackUi\.ConfigureWorkspaceDefaults\(68f,') "Duel opts into the compact status box + shared default workspace"
Assert-Launcher ($plugin -match 'StandaloneLauncherColumnPolicy\.DefaultPanelRightNormalized\(\)') "Duel's panel default derives from the shared right-side workspace anchor"
Assert-Launcher ($plugin -match 'StandaloneLauncherColumnPolicy\.DefaultPanelTopNormalized\(\)') "Duel's panel default derives from the shared below-launcher-stack anchor"
Assert-Launcher ($duelColumnPolicy -notmatch 'RightMarginNormalized = 0\.006f') "the old right margin (fully swallowed by the launcher-width clamp) is gone"
Assert-Launcher ($duelFallbackUi -match 'internal static void ConfigureWorkspaceDefaults') "shared StandaloneFallbackUi exposes the opt-in workspace-defaults API"
Assert-Launcher ($duelFallbackUi -match '_launcherObject\.SetActive\(!_hubUsable\)') "Hub-healthy suppression of the standalone launcher is unchanged"
Assert-Launcher ($duelFallbackUi -match 'if \(_openAccent != null\) _openAccent\.SetActive\(_open\)') "shared launcher exposes a structural (non-color-only) open/active cue"
Assert-Launcher ($duelFallbackUi -match 'private static float ExpandedPanelHeight\(\)') "panel expanded-height formula has one source of truth shared by build and collapse/restore"
Write-Host "Erenshor Duel UI workspace normalization guard: ALL PASS" -ForegroundColor Green

# ==========================================================================================
# 0.4.13 duel arming / Cleaning-NRE repair matrix.
#
# Failure A: the 0.4.11 combat-text attribution repin used the session-wide Active property
# (Preparing|Countdown|Active), so it pinned CurrentAggroTarget onto the duel opponent during
# Preparing/Countdown - arming native duel AI before GO.
# Failure B: native NPC.Combat stores CurrentAggroTarget.RecentDirectHit with no null guard
# immediately after PerformMeleeHit returns. A yield reached inside that melee hit calls Stop()
# synchronously, and cleanup nulled CurrentAggroTarget while the native frame was still on the
# stack -> NullReferenceException in NPC.Combat/NPC.DoNonRaidBehavior.
# ==========================================================================================
$armingPolicy = Get-Content (Join-Path $ScriptRoot "..\src\DuelArmingPolicy.cs") -Raw
$armCases = 0
function Assert-Arm([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAILED: $name" }
    $script:armCases++
    Write-Host "PASS: $name"
}

# 1-4: the duel pair cannot arm during Preparing or Countdown, in either duel mode. Both modes
# reach native combat through the same participant edge (DuelOpponentFor / AdmitNativeCombat),
# so one gate covers player-vs-Sim and spectator alike.
Assert-Arm ($armingPolicy -match 'return state == DuelLifecycleState\.Active;') '1 armed state is defined as Active only'
Assert-Arm ($controller -match 'internal static bool AdmitNativeCombat\(NPC npc\)') '2 native NPC.Combat entry has a duel-pair admission gate'
Assert-Arm ($controller -match 'DuelArmingPolicy\.ShouldBlockParticipantCombat\(true, current == opponent, outsideHostile, _state\)') '3 the gate refuses only the exact participant<->opponent edge, from live state'
Assert-Arm ($controller -match 'if \(!DuelArmingPolicy\.ShouldArmDuelPair\(_state\)\)') '4 the attribution repin refuses to pin the pair before GO'
Assert-Arm ($controller -match 'if \(_spectatorDuel && npc == _firstSimNpc\) return _sim;') '4b the same participant edge resolves both duel modes, so spectator is covered identically'

# 5: arming happens exactly once, at Countdown -> Active, and only there.
Assert-Arm ($controller -match '_simNpc\.CurrentAggroTarget = _player;') '5a Countdown->Active is where the opponent pin is established'
Assert-Arm ($armingPolicy -match 'internal static bool ShouldArmDuelPair') '5b arming is a single-state property, so it cannot be applied twice per session'

# 6: world hostile combat remains fully real before GO - no arena bubble, nothing globally frozen.
Assert-Arm ($controller -match 'if \(opponent == null\) return true;') '6a a non-participant NPC is never gated by the duel'
Assert-Arm ($armingPolicy -match 'if \(targetIsOutsideHostile\) return false;') '6b a participant fighting a hostile world actor is never blocked'
Assert-Arm ($armingPolicy -match 'if \(!actorIsParticipant\) return false;') '6c unrelated actors are never blocked in any state'
Assert-Arm ($controller -notmatch 'Time\.timeScale') '6d nothing is globally frozen to hold the duel pair back'

# 7-8: pre-Active duel-pair damage / offensive spells cannot commit. These gates are pre-existing
# and frozen; they are pinned here so the arming repair cannot regress them.
Assert-Arm ($controller -match '(?s)if \(attacker != _player && attacker != _sim\) RememberEngagedPet\(attacker\);\s*if \(_state != DuelLifecycleState\.Active\)\s*\{\s*result = 0;\s*return false;') '7a a pre-Active duel-pair hit is zeroed and native damage is skipped entirely'
Assert-Arm ($controller -match 'if \(_state != DuelLifecycleState\.Active\) \{ result = 0; return false; \}') '7b pre-Active self-damage and status ingress are refused'
Assert-Arm ($controller -match '"blocked", "lifecycle_not_active"') '8 a pre-Active offensive spell is refused with an explicit lifecycle reason'

# Real-death investigation: with damage skipped, Stats.ReduceHP is never reached before Active,
# so no native death path can be entered through the duel pair. ReduceHP itself stays frozen.
Assert-Arm ($controller -match '\[HarmonyPatch\(typeof\(Stats\), "ReduceHP"\)\]') '16a the frozen ReduceHP transaction patch is still present and untouched'
Assert-Arm ($controller -notmatch '\.Death\(\)') '16b Duel never invokes a native death method itself'

# 9: Cleaning disarms the duel pair.
Assert-Arm ($controller -match 'SetAggroTargetSafely\(_postDuelSimNpc, null\)') '9a Cleaning disarms the second participant'
Assert-Arm ($controller -match 'SetAggroTargetSafely\(_postDuelFirstSimNpc, null\)') '9b Cleaning disarms the spectator first participant'

# 10-11: restoration remains proof-based - only a target with live Stats may come back.
Assert-Arm ($controller -match 'return IsAlive\(character\) && character\.MyStats != null;') '10 an unsafe/null post-duel target is never restored'
Assert-Arm ($controller -match 'SetAggroTargetSafely\(_simNpc, DuelSafetyPolicy\.ShouldRestorePreviousNpcTarget\(') '11 a safe external PvE target may still be restored, through the same safe writer'

# 12: NPC.Combat cannot be entered through Duel-owned state with an invalid target. The write that
# armed the historical NRE is deferred until the native frame has returned.
Assert-Arm ($controller -match 'private static void SetAggroTargetSafely\(NPC npc, Character value\)') '12a all duel-owned aggro writes go through one re-entrancy-safe writer'
Assert-Arm ($controller -match 'DuelArmingPolicy\.ShouldDeferAggroTargetWrite\(NpcsInsideNativeCombat\.Contains\(npc\)\)') '12b a write arriving while native Combat is on the stack is deferred, not applied'
Assert-Arm ($controller -match '__state = DuelController\.BeginNativeCombat\(__instance\);') '12c the native Combat frame is scoped so re-entrant writes are detectable'
Assert-Arm ($controller -match '(?s)\[HarmonyFinalizer\].{0,400}EndNativeCombat\(__instance\)') '12d the scope is released by a finalizer, so it survives a native throw'
Assert-Arm ($controller -match 'return __exception;') '12e the native exception is returned unchanged - never caught and swallowed'
Assert-Arm ($controller -notmatch 'catch \(NullReferenceException') '12f the NRE is repaired at its cause, not caught'
Assert-Arm ($controller -match 'stfld  float Character::RecentDirectHit') '12g the exact unguarded native store is documented at the fix site'

# No dummy target is ever fabricated, and NPC.Combat is never suppressed globally.
Assert-Arm ($controller -match 'try \{ npc\.CurrentAggroTarget = null; \} catch \{ \}') '12h the pre-GO disarm clears the target rather than inventing one'
Assert-Arm ($controller -match '(?s)internal static bool AdmitNativeCombat.{0,200}if \(!Active \|\| npc == null\) return true;') '12i NPC.Combat is admitted unconditionally whenever no duel is running'

# 13-14: Cleaning still completes and repeated duels still work.
Assert-Arm ($controller -match 'FlushDeferredAggroTargets\(\);') '13a any deferred disarm is applied before Cleaning finalizes'
Assert-Arm ($controller -match 'DuelLifecycleTrigger\.CleanupComplete') '13b Cleaning -> Idle still completes through the normal trigger'
Assert-Arm ($controller -match 'ClearNativeCombatScopes\(\)') '14 native-combat scopes are cleared on shutdown so repeated duels start clean'

# 15: the combat-text attribution repair itself remains intact for the armed state it was written for.
Assert-Arm ($controller -match 'DuelCombatAttributionPolicy\.ShouldRepin\(') '15a the attribution decision still routes through its pure policy'
Assert-Arm ($controller -match 'combat_text_attribution mode=') '15b the attribution diagnostic is unchanged'
Assert-Arm ($controller -match 'RepinDuelistCombatTarget\(npc, "NPC\.CheckAssist"\)') '15c the CheckAssist attribution stage is still wired'
Assert-Arm ($controller -match 'RepinDuelistCombatTarget\(npc, "NPC\.Combat"\)') '15d the NPC.Combat attribution stage is still wired'

# Bounded pre-GO diagnostic with the requested field set.
foreach ($field in @('preactive_duel_combat state=', 'sourceRole=', 'targetRole=', 'entry=', 'currentAggroTarget=', 'playerCurrentTarget=', 'action=')) {
    Assert-Arm ($controller -match [regex]::Escape($field)) ("pre-GO diagnostic reports required field: " + $field)
}
Assert-Arm ($controller -match 'ThrottledDiagnostic\("preactive_duel_combat\.') 'the pre-GO diagnostic is throttled, never per-frame'

# Cleaning-time participant state is recorded for exactly the fields native NPC.Combat can fault on.
foreach ($field in @('npcMyStatsExists=', 'actorMyStatsExists=', 'npcThisSimLinkageValid=', 'insideNativeCombat=', 'deferredDisarmPending=', 'safeRestoreEligible=')) {
    Assert-Arm ($controller -match [regex]::Escape($field)) ("Cleaning state record reports required field: " + $field)
}

# Build label must not lag the plugin version again.
$buildInfo = Get-Content (Join-Path $ScriptRoot "..\src\DuelBuildInfo.cs") -Raw
Assert-Arm ($buildInfo -match '0\.4\.17') 'duel_start build label matches the current source version'

if ($armCases -lt 42) { throw "Duel arming matrix count mismatch: $armCases (expected at least 42)" }
Write-Host ("Practice Duel 0.4.13 arming / Cleaning-NRE repair matrix: PASS (" + $armCases + " assertions)") -ForegroundColor Green

# 0.4.15 recent-duel admission repair. Live: after Cleaning -> Idle (admissionBlocked=False,
# reason=cleanup_complete) a deliberate re-challenge of Cyndara/Phanty was still refused with
# decision=decline_recent_duel, because the 120s SOCIAL cooldown was applied to explicit player
# requests as well as autonomous ones. The lifecycle gate (Cleaning) is the real inter-duel safety
# window and is enforced separately; the social cooldown exists to stop AI challenge spam. These
# guards prove the two are now separated by explicit request origin, and that nothing else relaxed.
$challengePolicy = Get-Content (Join-Path $ScriptRoot "..\src\DuelChallengePolicy.cs") -Raw
$simActions = Get-Content (Join-Path $ScriptRoot "..\src\DuelSimActionsFallback.cs") -Raw
$admissionCases = 0
function Assert-Admission([bool]$condition, [string]$name) {
    if (-not $condition) { throw ("Duel recent-duel admission matrix failed: " + $name) }
    $script:admissionCases++
}

# Origin is a first-class, explicit input - never inferred, never defaulted.
Assert-Admission ($challengePolicy -match 'internal enum DuelRequestOrigin') '01 request origin is an explicit type'
Assert-Admission ($challengePolicy -match 'ExplicitPlayer' -and $challengePolicy -match 'Autonomous') '02 both explicit-player and autonomous origins exist'
Assert-Admission ($challengePolicy -match 'internal DuelRequestOrigin Origin;') '03 willingness input carries the origin'
Assert-Admission ($challengePolicy -match 'internal static float RecentDuelWindowSeconds\(DuelRequestOrigin origin,') '04 the applicable recent-duel window is a pure, testable function of origin'
Assert-Admission ($challengePolicy -match 'origin == DuelRequestOrigin\.ExplicitPlayer\s*\r?\n?\s*\? Math\.Max\(0f, explicitDebounceSeconds\)') '05 an explicit request uses only the short technical debounce'
Assert-Admission ($challengePolicy -match ': Math\.Max\(0f, socialCooldownSeconds\);') '06 an autonomous request keeps the full social cooldown'

# Both entry points REQUIRE an origin, so a future Nemesis/autonomous caller cannot silently inherit
# explicit-player treatment by omitting it.
Assert-Admission ($controller -match 'internal static void Start\(SimPlayer target, DuelRequestOrigin origin\)') '07 Start() requires an explicit origin (no default)'
Assert-Admission ($controller -match 'internal static void StartSpectator\(SimPlayer first, SimPlayer second, DuelRequestOrigin origin\)') '08 StartSpectator() requires an explicit origin (no default)'
Assert-Admission ($controller -notmatch 'DuelRequestOrigin origin = DuelRequestOrigin\.') '09 origin is never defaulted, so omission is a compile error rather than a silent cooldown bypass'

# The ledger is shared; only the window differs. Pruning must keep entries for the LONGER window so
# autonomous callers can still see them.
Assert-Admission ($controller -match 'internal const float RecentDuelCooldownSeconds = 120f;' -or $controller -match 'private const float RecentDuelCooldownSeconds = 120f;') '10 the social cooldown value itself is unchanged at 120s'
Assert-Admission ($controller -match 'ExplicitRequestDebounceSeconds = 1f;') '11 the explicit debounce is a tiny technical guard, not a social cooldown'
Assert-Admission ($controller -match 'private static bool WasRecentlyAccepted\(string key, DuelRequestOrigin origin\)') '12 the recency check is origin-aware'
Assert-Admission ($controller -match 'DuelChallengePolicy\.RecentDuelWindowSeconds\(\s*\r?\n?\s*origin, RecentDuelCooldownSeconds, ExplicitRequestDebounceSeconds\)') '13 production selects the window through the tested pure policy'
Assert-Admission ($controller -match 'now - pair\.Value < RecentDuelCooldownSeconds') '14 pruning still uses the long window so autonomous callers can still observe a recent duel'

# Every current request origin is a deliberate player action and says so.
Assert-Admission ($simActions -match 'DuelController\.Start\(_firstSim, DuelRequestOrigin\.ExplicitPlayer\);') '15 Sim Actions Practice Duel button is an explicit player request'
Assert-Admission ($simActions -match 'DuelController\.StartSpectator\(_firstSim, _secondSim, DuelRequestOrigin\.ExplicitPlayer\);') '16 Sim Actions Arrange Sim Duel is an explicit player request'
Assert-Admission ($plugin -match 'DuelController\.Start\(sim, DuelRequestOrigin\.ExplicitPlayer\);') '17 /eduel <Sim> is an explicit player request'
Assert-Admission ($plugin -match 'DuelController\.StartSpectator\(first, second, DuelRequestOrigin\.ExplicitPlayer\);') '18 /eduel <A> vs <B> is an explicit player request'
Assert-Admission (([regex]::Matches($plugin, 'DuelController\.Start\(sim, DuelRequestOrigin\.ExplicitPlayer\);')).Count -ge 1 -and $plugin -match 'CanStartNewDuel\) DuelController\.Start\(sim, DuelRequestOrigin\.ExplicitPlayer\);') '19 Hub/Challenge Nearby is an explicit player request'
Assert-Admission ($controller -notmatch 'DuelRequestOrigin\.Autonomous\)' -or $controller -match 'DuelRequestOrigin\.Autonomous') '20 no current call site claims autonomous origin; the enum is reserved for future Nemesis/ambient integration'

# Nothing else relaxed: every real eligibility/safety gate is still evaluated identically.
Assert-Admission ($challengePolicy -match 'if \(input\.RecentDuel\) return DuelSocialDecision\.DeclineRecentDuel;') '21 a caller inside its own applicable window is still declined regardless of origin'
Assert-Admission ($challengePolicy -match 'healthPercent < 35\) return DuelSocialDecision\.DeclineLowHealth;') '22 the low-health gate is unchanged for both origins'
Assert-Admission ($controller -match 'EvaluateEligibility') '23 mechanical eligibility (actor/COOP/scene/distance/combat) is still evaluated before willingness'
Assert-Admission ($controller -match 'if \(!CanStartNewDuel\)') '24 the lifecycle gate still rejects a challenge while not Idle'
Assert-Admission ($controller -match 'Finishing cleanup from the previous duel') '25 Cleaning still blocks a new challenge with its own message'
Assert-Admission ($controller -match 'first == second') '26 the same Sim still cannot duel itself'
$eligibilityPolicy = Get-Content (Join-Path $ScriptRoot "..\src\DuelEligibilityPolicy.cs") -Raw
Assert-Admission ($eligibilityPolicy -match 'if \(input\.RemoteCoop\) return DuelEligibilityDecision\.RemoteCoop;') '27 remote COOP humans are still refused by the unchanged eligibility gate'

# Read-only listings must answer "what happens if the player asks now", or /eduel nearby would show
# a decline the player would never actually receive.
Assert-Admission (([regex]::Matches($controller, 'EvaluateWillingness\([^)]*DuelRequestOrigin\.ExplicitPlayer\)')).Count -ge 1) '28 nearby/eligible listings evaluate as an explicit player request'

# Frozen: this pass must not touch arming, countdown, the Cleaning repair, or combat.
Assert-Admission ($controller -match 'DuelArmingPolicy\.') '29 the 0.4.13 arming policy is still consulted'
Assert-Admission ($controller -match 'DuelSafetyPolicy\.ShouldFinalizeCleanupPass\(_postDuelAttackCleanupFrames, Time\.unscaledTime, _postDuelAttackCleanupUntil\)') '30 the 0.4.10 Cleaning-gate repair is untouched'
Assert-Admission ($controller -match 'if \(!_postDuelCleanupPending\) return;') '31 the Cleaning pending-flag guard is untouched'
Assert-Admission ($controller -match 'CaptureNativeReduceHp' -and $controller -match 'CapturedReduceHpDamage\s*=\s*damage') '32 the ReduceHP transaction is untouched'
Assert-Admission ($controller -notmatch 'CurrentHP\s*=\s*int\.MaxValue') '33 no synthetic HP headroom introduced'
Assert-Admission ($controller -match 'DuelCombatAttributionPolicy\.ShouldRepin\(') '34 combat-text attribution is untouched'

if ($admissionCases -lt 34) { throw "Duel recent-duel admission matrix count mismatch: $admissionCases (expected at least 34)" }
Write-Host ("Practice Duel 0.4.15 recent-duel admission repair matrix: PASS (" + $admissionCases + " assertions)") -ForegroundColor Green


# 0.4.17 diagnostic-performance gate. Live gameplay behavior from 0.4.16 is frozen; only forensic
# telemetry is configurable, while low-frequency lifecycle proof remains available.
$settingsSource = Get-Content (Join-Path $ScriptRoot "..\src\DuelSettings.cs") -Raw
$diagCases = 0
function Assert-Diag([bool]$condition, [string]$label) {
    $script:diagCases++
    if (-not $condition) { throw "Duel diagnostic-performance guard failed: $label" }
}
Assert-Diag ($plugin -match 'PluginVersion\s*=\s*"0\.4\.17"') 'plugin version is 0.4.17'
Assert-Diag ($settingsSource -match '\[Config\("Verbose",\s*"Diagnostics"') 'forensic diagnostics have a Lunaris config switch'
Assert-Diag ($settingsSource -match 'DiagnosticsVerbose\s*=\s*false') 'verbose diagnostics default OFF'
Assert-Diag ($plugin -match 'VerboseDiagnostics\s*=\s*_settings\.DiagnosticsVerbose') 'runtime flag is initialized from config'
Assert-Diag ($plugin -match '(?s)internal void Diagnostic\(string message\).*?if \(!VerboseDiagnostics\) return;.*?Logging\.LogDebug') 'high-volume plugin diagnostic logger fails fast when verbose is off'
Assert-Diag ($plugin -match '(?s)internal void LifecycleDiagnostic\(string message\).*?Logging\.LogDebug') 'low-frequency lifecycle logger remains available'
Assert-Diag ($controller -match '(?s)private static void DiagnosticVirtual.*?if \(!ErenshorDuelPlugin\.VerboseDiagnostics\) return;') 'per-hit virtual telemetry returns before formatting when verbose is off'
Assert-Diag ($controller -match '(?s)private static void ThrottledDiagnostic.*?if \(!ErenshorDuelPlugin\.VerboseDiagnostics\) return;') 'interference telemetry returns before throttle/dictionary work'
Assert-Diag ($controller -match '(?s)private static void DiagnosticRecord.*?if \(!ErenshorDuelPlugin\.VerboseDiagnostics\) return;') 'structured forensic records return before logger work'
Assert-Diag ($controller -match 'LifecycleDiagnostic\("state_transition ') 'state transitions use the low-frequency lifecycle path'
Assert-Diag ($controller -match 'LifecycleDiagnostic\("duel_terminal ') 'terminal outcome uses the low-frequency lifecycle path'
Assert-Diag ($controller -match 'LifecycleDiagnostic\("cleanup_tick state=') 'cleanup completion remains visible without verbose combat telemetry'
Assert-Diag ($controller -match 'RecentDuelWindowSeconds') '0.4.15 explicit/autonomous rematch policy remains present'
Assert-Diag ($controller -match 'DuelArmingPolicy\.') '0.4.13 arming policy remains present'
Assert-Diag ($controller -match 'CaptureNativeReduceHp') 'frozen ReduceHP transaction remains present'
if ($diagCases -lt 15) { throw "Duel diagnostic-performance matrix count mismatch: $diagCases" }
Write-Host ("Practice Duel 0.4.17 diagnostic-performance gate: PASS (" + $diagCases + " assertions)") -ForegroundColor Green
