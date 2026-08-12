param(
    [switch]$Undo
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ControllerPath = Join-Path $Root "src\DuelController.cs"
$PluginPath = Join-Path $Root "src\ErenshorDuelPlugin.cs"
$ControllerBackup = "$ControllerPath.pre-duel-0.3.bak"
$PluginBackup = "$PluginPath.pre-duel-0.3.bak"

function Read-Normalized([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "Missing expected file: $Path"
    }

    return ([IO.File]::ReadAllText($Path) -replace "`r`n", "`n")
}

function Write-Utf8NoBom([string]$Path, [string]$Text) {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($Path, ($Text -replace "`n", [Environment]::NewLine), $utf8)
}

function Replace-Exact([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    if (-not $Text.Contains($Old)) {
        throw "Could not find expected source block: $Label`nNo files were intentionally overwritten after this failure."
    }

    $first = $Text.IndexOf($Old)
    if ($Text.IndexOf($Old, $first + $Old.Length) -ge 0) {
        throw "Expected source block appeared more than once: $Label"
    }

    return $Text.Replace($Old, $New)
}

if ($Undo) {
    if (-not (Test-Path $ControllerBackup) -or -not (Test-Path $PluginBackup)) {
        throw "Backup files were not found. Nothing was restored."
    }

    Copy-Item $ControllerBackup $ControllerPath -Force
    Copy-Item $PluginBackup $PluginPath -Force
    Write-Host "Restored pre-0.3 DuelController.cs and ErenshorDuelPlugin.cs."
    exit 0
}

$controller = Read-Normalized $ControllerPath
$plugin = Read-Normalized $PluginPath

if ($plugin.Contains('"0.3.0"') -and $controller.Contains("CaptureVirtualHealing")) {
    Write-Host "The duel abilities/healing update already appears to be applied."
    exit 0
}

# Work entirely in memory first. If any expected block does not match, nothing is written.
$old = @'
        private static int _playerHp;
        private static int _simHp;
        private static int _playerMax;
        private static int _simMax;
        private static int _lastCountdown;
'@
$new = @'
        private static int _playerHp;
        private static int _simHp;
        private static int _playerMax;
        private static int _simMax;
        private static int _playerOriginalHp;
        private static int _simOriginalHp;
        private static int _playerMirroredHp;
        private static int _simMirroredHp;
        private static int _duelCombatActionDepth;
        private static int _lastCountdown;
'@
$controller = Replace-Exact $controller $old $new "virtual-health state fields"

$old = @'
            _playerMax = Math.Max(1, _player.MyStats.CurrentMaxHP);
            _simMax = Math.Max(1, _sim.MyStats.CurrentMaxHP);
            _playerHp = Math.Max(1, Math.Min(_playerMax, _player.MyStats.CurrentHP));
            _simHp = Math.Max(1, Math.Min(_simMax, _sim.MyStats.CurrentHP));
            _previousSimTarget = _simNpc.CurrentAggroTarget;
'@
$new = @'
            _playerMax = Math.Max(1, _player.MyStats.CurrentMaxHP);
            _simMax = Math.Max(1, _sim.MyStats.CurrentMaxHP);
            _playerOriginalHp = Math.Max(1, Math.Min(_playerMax, _player.MyStats.CurrentHP));
            _simOriginalHp = Math.Max(1, Math.Min(_simMax, _sim.MyStats.CurrentHP));
            _playerHp = _playerOriginalHp;
            _simHp = _simOriginalHp;
            _playerMirroredHp = _playerOriginalHp;
            _simMirroredHp = _simOriginalHp;
            _previousSimTarget = _simNpc.CurrentAggroTarget;
'@
$controller = Replace-Exact $controller $old $new "duel starting health snapshot"

$old = @'
            if (Vector3.Distance(_player.transform.position, _sim.transform.position) > MaximumDistance) { Cancel("Tick.Distance", null, null, null, "Duel cancelled because the duelists moved too far apart."); return; }
            if (IsCampActive()) { Cancel("Tick.Camp", null, null, null, "Duel cancelled because camp mode is active."); return; }
            NPC externalAttacker;
'@
$new = @'
            if (Vector3.Distance(_player.transform.position, _sim.transform.position) > MaximumDistance) { Cancel("Tick.Distance", null, null, null, "Duel cancelled because the duelists moved too far apart."); return; }
            if (IsCampActive()) { Cancel("Tick.Camp", null, null, null, "Duel cancelled because camp mode is active."); return; }
            if (_state == DuelState.Fighting) CaptureVirtualHealing();
            NPC externalAttacker;
'@
$controller = Replace-Exact $controller $old $new "healing capture during Tick"

$old = @'
                _simNpc.CurrentAggroTarget = _player;
                GameData.PlayerControl.CurrentTarget = _sim;
                try { _sim.TargetMe(); } catch { }
                Say("[Practice Duel] Fight! First to " + FinishPercent + "% virtual health yields.", "lightblue");
'@
$new = @'
                _simNpc.CurrentAggroTarget = _player;
                GameData.PlayerControl.CurrentTarget = _sim;
                try { _sim.TargetMe(); } catch { }
                MirrorVirtualHealth();
                Say("[Practice Duel] Fight! First to " + FinishPercent + "% virtual health yields.", "lightblue");
'@
$controller = Replace-Exact $controller $old $new "initial virtual-health mirror"

$old = @'
        internal static void Stop(string reason)
        {
            bool hadDuelState = Active || _simNpc != null || _simPlayer != null;
            if (hadDuelState) BeginPostDuelAttackCleanup();
'@
$new = @'
        internal static void Stop(string reason)
        {
            bool hadDuelState = Active || _simNpc != null || _simPlayer != null;
            if (hadDuelState) RestoreOriginalHealth();
            if (hadDuelState) BeginPostDuelAttackCleanup();
'@
$controller = Replace-Exact $controller $old $new "restore real HP before duel cleanup"

$old = @'
        internal static string Status()
        {
            if (!Active) return "[Practice Duel] No duel is active.";
            if (_state != DuelState.Fighting) return "[Practice Duel] Preparing to duel " + _simName + ".";
            return "[Practice Duel] You: " + Percent(_playerHp, _playerMax) + "% | " + _simName + ": " + Percent(_simHp, _simMax) + "% virtual health.";
        }
'@
$new = @'
        internal static string Status()
        {
            if (!Active) return "[Practice Duel] No duel is active.";
            if (_state != DuelState.Fighting) return "[Practice Duel] Preparing to duel " + _simName + ".";
            CaptureVirtualHealing();
            return "[Practice Duel] You: " + Percent(_playerHp, _playerMax) + "% | " + _simName + ": " + Percent(_simHp, _simMax) + "% virtual health.";
        }
'@
$controller = Replace-Exact $controller $old $new "status healing synchronization"

$old = @'
                if (playerHit) _playerHp = Math.Max(1, _playerHp - damage);
                else _simHp = Math.Max(1, _simHp - damage);
                result = damage;
                int hp = playerHit ? _playerHp : _simHp;
                int max = playerHit ? _playerMax : _simMax;
                if (hp * 100 <= max * FinishPercent)
                    Stop((playerHit ? "You yield" : _simName + " yields") + ". Friendly duel complete!");
                return true;
'@
$new = @'
                // Capture any heal, HoT tick, lifesteal gain, or potion that changed the
                // temporarily mirrored CurrentHP before this hit arrived.
                CaptureVirtualHealing();
                if (playerHit) _playerHp = Math.Max(1, _playerHp - damage);
                else _simHp = Math.Max(1, _simHp - damage);
                result = damage;
                int hp = playerHit ? _playerHp : _simHp;
                int max = playerHit ? _playerMax : _simMax;
                if (hp * 100 <= max * FinishPercent)
                    Stop((playerHit ? "You yield" : _simName + " yields") + ". Friendly duel complete!");
                else
                    MirrorVirtualHealth();
                return true;
'@
$controller = Replace-Exact $controller $old $new "damage-to-virtual-HP synchronization"

$old = @'
        internal static bool AllowCombatAction(NPC npc)
        {
            if (!Active || npc == null) return true;
            CombatActorClass actorClass = Classify(npc);
            if (actorClass == CombatActorClass.DuelParticipant) return false;
            if (!IsFriendlyPartyClass(actorClass)) return true;
            CombatActorClass targetClass = Classify(npc.CurrentAggroTarget);
            return !IsDuelParticipantClass(targetClass);
        }

        internal static bool AllowSpellStart(CastSpell caster, Stats target, ref bool result)
        {
            if (!Active || caster == null || target == null) return true;
            CombatActorClass casterClass = Classify(caster.MyChar);
            if (casterClass != CombatActorClass.DuelParticipant &&
                casterClass != CombatActorClass.GroupedLocalSim &&
                casterClass != CombatActorClass.GroupedSimOwnedPet)
                return true;
            if (!IsDuelParticipantClass(Classify(target.Myself))) return true;

            // NPC.CheckHeals and its HOT/buff branches call this overload directly instead of
            // DoAttackSpell. Do not let party AI alter either duelist during a practice duel.
            result = false;
            return false;
        }
'@
$new = @'
        internal static bool BeginCombatAction(NPC npc)
        {
            if (!Active || npc == null) return true;
            CombatActorClass actorClass = Classify(npc);
            if (actorClass == CombatActorClass.DuelParticipant)
            {
                if (_state != DuelState.Fighting || Classify(npc.CurrentAggroTarget) != CombatActorClass.LocalPlayer)
                    return false;

                // Mark this call so StartSpell can distinguish the dueling Sim's offensive
                // spell/skill from CheckHeals trying to treat its opponent as a friendly target.
                _duelCombatActionDepth++;
                return true;
            }

            if (!IsFriendlyPartyClass(actorClass)) return true;
            CombatActorClass targetClass = Classify(npc.CurrentAggroTarget);
            return !IsDuelParticipantClass(targetClass);
        }

        internal static void EndCombatAction(NPC npc)
        {
            if (npc == null || npc != _simNpc || _duelCombatActionDepth <= 0) return;
            _duelCombatActionDepth--;
        }

        internal static bool AllowSpellStart(CastSpell caster, Stats target, ref bool result)
        {
            if (!Active || caster == null || target == null) return true;
            CombatActorClass casterClass = Classify(caster.MyChar);
            CombatActorClass targetClass = Classify(target.Myself);
            bool targetIsDuelist = IsDuelParticipantClass(targetClass);

            if (IsDuelParticipantClass(casterClass))
            {
                // Duelists may affect only themselves or their opponent, and only while fighting.
                if (_state != DuelState.Fighting || !targetIsDuelist)
                {
                    result = false;
                    return false;
                }

                // The Sim and player remain grouped. CheckHeals can otherwise decide that the
                // opponent is a friendly heal target. Sim -> player casts are therefore allowed
                // only while executing the Sim's offensive spell/skill path. Direct CheckHeals
                // casts remain self-only. Player casts are explicit user actions.
                if (casterClass == CombatActorClass.DuelParticipant &&
                    target.Myself == _player &&
                    _duelCombatActionDepth <= 0)
                {
                    result = false;
                    return false;
                }

                return true;
            }

            // Other party members and friendly pets may not heal, buff, debuff, or damage
            // either duelist. Their normal party AI should not turn the duel into group combat.
            if (targetIsDuelist && IsFriendlyPartyClass(casterClass))
            {
                result = false;
                return false;
            }

            // A verified outside hostile casting directly on a duelist is real combat.
            // Cancel first so original real HP is restored, then let Erenshor handle the spell.
            if (targetIsDuelist && casterClass == CombatActorClass.OutsideHostile)
            {
                Cancel("Harmony.CastSpell.StartSpell", caster.MyChar, target.Myself, target.Myself,
                    "Duel cancelled because an outside hostile began casting on a duelist.");
            }

            return true;
        }

        private static void CaptureVirtualHealing()
        {
            if (!Active || _state != DuelState.Fighting) return;

            CaptureVirtualHealing(_player, ref _playerHp, _playerMax, ref _playerMirroredHp);
            CaptureVirtualHealing(_sim, ref _simHp, _simMax, ref _simMirroredHp);
            MirrorVirtualHealth();
        }

        private static void CaptureVirtualHealing(Character character, ref int virtualHp, int virtualMax, ref int mirroredHp)
        {
            if (character == null || character.MyStats == null) return;
            int current = ReadCurrentHp(character.MyStats);
            if (current > mirroredHp)
                virtualHp = Math.Min(virtualMax, virtualHp + (current - mirroredHp));

            // Do not import unexplained CurrentHP decreases. Duel damage is intercepted
            // separately; an unrecognized third-party decrease is not authoritative duel damage.
        }

        private static void MirrorVirtualHealth()
        {
            if (!Active || _state != DuelState.Fighting) return;
            MirrorVirtualHealth(_player, _playerHp, ref _playerMirroredHp);
            MirrorVirtualHealth(_sim, _simHp, ref _simMirroredHp);
        }

        private static void MirrorVirtualHealth(Character character, int hp, ref int mirroredHp)
        {
            if (character == null || character.MyStats == null) return;
            int safeHp = Math.Max(1, Math.Min(character.MyStats.CurrentMaxHP, hp));
            if (WriteCurrentHp(character.MyStats, safeHp))
                mirroredHp = safeHp;
        }

        private static void RestoreOriginalHealth()
        {
            try
            {
                if (_player != null && _player.MyStats != null && _playerOriginalHp > 0)
                    WriteCurrentHp(_player.MyStats, Math.Min(_player.MyStats.CurrentMaxHP, _playerOriginalHp));
            }
            catch { }

            try
            {
                if (_sim != null && _sim.MyStats != null && _simOriginalHp > 0)
                    WriteCurrentHp(_sim.MyStats, Math.Min(_sim.MyStats.CurrentMaxHP, _simOriginalHp));
            }
            catch { }
        }

        private static int ReadCurrentHp(Stats stats)
        {
            try { return stats == null ? 0 : stats.CurrentHP; }
            catch { return 0; }
        }

        private static bool WriteCurrentHp(Stats stats, int value)
        {
            if (stats == null) return false;
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                FieldInfo field = stats.GetType().GetField("CurrentHP", flags);
                if (field != null)
                {
                    field.SetValue(stats, value);
                    return true;
                }

                PropertyInfo property = stats.GetType().GetProperty("CurrentHP", flags);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(stats, value, null);
                    return true;
                }
            }
            catch { }

            return false;
        }
'@
$controller = Replace-Exact $controller $old $new "duelist abilities, spell isolation, and virtual healing"

$old = @'
            _simName = null;
            _scene = null;
            _playerHp = _simHp = _playerMax = _simMax = 0;
            _lastCountdown = 0;
'@
$new = @'
            _simName = null;
            _scene = null;
            _playerHp = _simHp = _playerMax = _simMax = 0;
            _playerOriginalHp = _simOriginalHp = 0;
            _playerMirroredHp = _simMirroredHp = 0;
            _duelCombatActionDepth = 0;
            _lastCountdown = 0;
'@
$controller = Replace-Exact $controller $old $new "clear virtual-health bookkeeping"

$old = @'
    [HarmonyPatch(typeof(NPC), "DoAttackSpell")]
    internal static class DuelAttackSpellPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance) { return DuelController.AllowCombatAction(__instance); }
    }

    [HarmonyPatch(typeof(NPC), "DoAttackSkill")]
    internal static class DuelAttackSkillPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance) { return DuelController.AllowCombatAction(__instance); }
    }
'@
$new = @'
    [HarmonyPatch(typeof(NPC), "DoAttackSpell")]
    internal static class DuelAttackSpellPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance) { return DuelController.BeginCombatAction(__instance); }

        [HarmonyPostfix]
        private static void Postfix(NPC __instance) { DuelController.EndCombatAction(__instance); }
    }

    [HarmonyPatch(typeof(NPC), "DoAttackSkill")]
    internal static class DuelAttackSkillPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance) { return DuelController.BeginCombatAction(__instance); }

        [HarmonyPostfix]
        private static void Postfix(NPC __instance) { DuelController.EndCombatAction(__instance); }
    }
'@
$controller = Replace-Exact $controller $old $new "attack spell/skill Harmony wrappers"

$old = '[BepInPlugin("forgetwhtuno.erenshor.practice-duels", "Erenshor Practice Duels", "0.2.1")]'
$new = '[BepInPlugin("forgetwhtuno.erenshor.practice-duels", "Erenshor Practice Duels", "0.3.0")]'
$plugin = Replace-Exact $plugin $old $new "plugin version"

# Only after every replacement succeeded do we create backups and write files.
if (-not (Test-Path $ControllerBackup)) {
    Copy-Item $ControllerPath $ControllerBackup
}
if (-not (Test-Path $PluginBackup)) {
    Copy-Item $PluginPath $PluginBackup
}

Write-Utf8NoBom $ControllerPath $controller
Write-Utf8NoBom $PluginPath $plugin

Write-Host ""
Write-Host "Practice Duel 0.3 source update applied."
Write-Host "Backups:"
Write-Host "  $ControllerBackup"
Write-Host "  $PluginBackup"
Write-Host ""
Write-Host "Next run:"
Write-Host "  .\BUILD_AND_INSTALL.bat"
Write-Host ""
Write-Host "To undo this source update:"
Write-Host "  powershell -ExecutionPolicy Bypass -File .\apply-duel-0.3.ps1 -Undo"
