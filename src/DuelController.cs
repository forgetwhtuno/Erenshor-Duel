using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorDuel
{
    internal static class DuelController
    {
        private enum CombatActorClass
        {
            DuelParticipant,
            LocalPlayer,
            GroupedLocalSim,
            GroupedSimOwnedPet,
            OutsideHostile,
            ProtectedNonParticipant,
            Unknown
        }

        private enum PeriodicDamageAuthority
        {
            DuelVirtual,
            WorldReal,
            Ambiguous
        }

        private static Character _player;
        private static Character _sim;
        private static bool _spectatorDuel;
        private static SimPlayer _firstSimPlayer;
        private static NPC _firstSimNpc;
        private static Character _previousFirstSimTarget;
        private static Spell _previousFirstNpcProc;
        private static float _previousFirstNpcProcChance;
        private static bool _previousFirstGuardSpot;
        private static Vector3 _previousFirstGuardPosition;
        private static bool _firstSimWasParty;
        private static string _firstSimName;
        private static string _firstSimStableKey;
        private static SimPlayer _simPlayer;
        private static NPC _simNpc;
        private static Character _previousSimTarget;
        private static Character _previousPlayerTarget;
        private static Spell _previousNpcProc;
        private static float _previousNpcProcChance;
        private static bool _previousGuardSpot;
        private static Vector3 _previousGuardPosition;
        private static bool _simWasParty;
        private static string _simName;
        private static string _simStableKey;
        private static string _scene;
        private static int _sceneHandle;
        private static int _playerHp;
        private static int _simHp;
        private static int _playerMax;
        private static int _simMax;
        private static int _playerRealHp;
        private static int _simRealHp;
        // Keyed by StatusEffects slot index, not slot identity. Stats.Awake pre-allocates every
        // slot object for the whole game session, so the slot reference never changes; only its
        // .Effect field does. Capturing slot objects would make "was this added during the duel"
        // untestable (every slot always "already existed").
        private static readonly Dictionary<int, Spell> PlayerInitialEffects = new Dictionary<int, Spell>();
        private static readonly Dictionary<int, Spell> SimInitialEffects = new Dictionary<int, Spell>();
        private static readonly Dictionary<int, EffectSlotSnapshot> PlayerInitialEffectState = new Dictionary<int, EffectSlotSnapshot>();
        private static readonly Dictionary<int, EffectSlotSnapshot> SimInitialEffectState = new Dictionary<int, EffectSlotSnapshot>();
        // Real hostile-world status effects admitted during the duel are tracked by fixed status-slot
        // index and exact Spell reference. Only these slots are allowed to advance the real cleanup
        // baseline; duel-owned buffs/debuffs must never leak into that baseline.
        private static readonly Dictionary<int, Spell> PlayerWorldEffectSlots = new Dictionary<int, Spell>();
        private static readonly Dictionary<int, Spell> SimWorldEffectSlots = new Dictionary<int, Spell>();
        private static int _playerInitialSpellShield;
        private static int _simInitialSpellShield;
        private static Character _playerInitialLastHitBy;
        private static Character _simInitialLastHitBy;
        private static float _playerInitialRecentDmg;
        private static float _simInitialRecentDmg;
        private static float _playerInitialRecentDmgByPlayer;
        private static float _simInitialRecentDmgByPlayer;
        // Snapshot of loaded local Sim enemy-list membership at duel start. This lets the combat
        // loop remove duelists from independent-group/bystander candidate lists without permanently
        // deleting a relation that already existed before the duel.
        private static readonly Dictionary<Character, byte> InitialNearbyEnemyMembership = new Dictionary<Character, byte>();
        private static bool _playerInitiallyHadSimEnemy;
        private static readonly Dictionary<string, float> LastAcceptedDuelBySim =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private static readonly Dictionary<string, float> LastInterferenceLog =
            new Dictionary<string, float>(StringComparer.Ordinal);
        // Only pets that already existed when the challenge was accepted may participate. This
        // closes summon paths that do not pass through the spell-shape guard.
        private static readonly HashSet<Character> AllowedDuelPets = new HashSet<Character>();
        // Pets that actually engaged in the match, so their aggro can be released on exit.
        private static readonly HashSet<NPC> EngagedPets = new HashSet<NPC>();
        // A pet can enter native combat without traversing every damage/spell hook that records
        // EngagedPets. Keep the accepted-duel pet set alive through the terminal cleanup window.
        private static readonly HashSet<NPC> PostDuelPetNpcs = new HashSet<NPC>();
        [ThreadStatic]
        private static Character _effectTickOwner;
        // Set only while one of Erenshor's ordinary damage methods is resolving a contained edge.
        // Duel-participant hits run natively against temporary non-lethal HP headroom and are
        // measured by delta. Hostile-world hits temporarily expose the real-world HP ledger so they
        // remain genuine native combat, then virtual HP is remirrored if the duelist survives.
        [ThreadStatic]
        private static NativeDamageState _nativeDamageInFlight;
        [ThreadStatic]
        private static StandaloneWorldDamageState _standaloneWorldDamageInFlight;
        private static int _lastCountdown;
        private static DuelLifecycleState _state;
        private static float _stateStartedAt;
        private static bool _cancellationLogged;
        private static string _cancellationReasonToken;
        private static bool _cachedIntegrationCampActive;
        private static float _nextIntegrationCampCheck;
        private static Character _postDuelPlayer;
        private static Character _postDuelSim;
        private static NPC _postDuelSimNpc;
        private static NPC _postDuelFirstSimNpc;
        private static bool _postDuelStopLocalPlayer;
        private static int _postDuelAttackCleanupFrames;
        private static float _postDuelAttackCleanupUntil;
        private static readonly MethodInfo ResetNpcAttackAnimationsMethod = AccessTools.Method(typeof(NPC), "ResetAttackAnimations");
        private static readonly FieldInfo NpcCombatantsField = AccessTools.Field(typeof(NPC), "Combatants");
        private static readonly MethodInfo CountStatusEffectsMethod = AccessTools.Method(typeof(Stats), "CountStatusEffects");
        private static readonly FieldInfo PlayerAutoattackField = AccessTools.Field(typeof(PlayerCombat), "Autoattack");
        private const int FinishPercent = 5;
        private const float ChallengeDistance = 25f;
        private const float MaximumDistance = 35f;
        private const float MaximumFightSeconds = 30f;
        private const float RecentDuelCooldownSeconds = 120f;
        // Matches DuelChallengePolicy's DeclineLowHealth threshold for the target Sim, applied
        // symmetrically to the player as a Start() precondition.
        private const int MinimumPlayerHealthPercent = 35;

        internal static bool Active { get { return DuelLifecyclePolicy.IsSessionActive(_state); } }
        internal static bool CanStartNewDuel { get { return DuelLifecyclePolicy.CanStart(_state); } }

        private static bool Transition(DuelLifecycleTrigger trigger, string reason)
        {
            DuelLifecycleState next;
            if (!DuelLifecyclePolicy.TryTransition(_state, trigger, out next))
            {
                Diagnostic("state_transition_rejected from=" + _state + " trigger=" + trigger + " reason=" + SafeLabel(reason));
                return false;
            }
            DuelLifecycleState previous = _state;
            _state = next;
            _stateStartedAt = Time.unscaledTime;
            Diagnostic("state_transition " + previous + "->" + next + " trigger=" + trigger + " reason=" + SafeLabel(reason));
            return true;
        }

        internal static SimPlayer FindSim(string name, out bool ambiguous)
        {
            ambiguous = false;
            if (string.IsNullOrWhiteSpace(name)) return null;

            SimPlayer exact = null;
            SimPlayer partial = null;
            int exactMatches = 0;
            int partialMatches = 0;
            Character player = null;
            try { player = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself; } catch { }
            // The player's own Character GameObject is persistent (DontDestroyOnLoad) and is never
            // a member of the loaded zone scene, so player locality is judged by aliveness only --
            // never by comparing the player's own scene against the active zone. See DuelLocalityPolicy.
            if (!IsAlive(player)) return null;

            foreach (SimPlayer sim in UnityEngine.Object.FindObjectsOfType<SimPlayer>())
            {
                if (CoopCompatibility.IsRemoteHuman(sim)) continue;
                // Current party membership is authoritative locality/scope proof for the party
                // path and must not be additionally gated by the nearby-Sim same-scene predicate
                // (IsUsableSim/IsSimLocalToActiveZone). Nearby non-party Sims still require it.
                bool candidatePartySim = IsPlayerPartySim(sim);
                if (!candidatePartySim && !IsUsableSim(sim)) continue;
                if (candidatePartySim && (sim == null || sim.gameObject == null || !sim.gameObject.activeInHierarchy || sim.MyStats == null)) continue;
                Character candidateCharacter = null;
                try { candidateCharacter = sim.MyStats.Myself; } catch { }
                if (!IsAlive(candidateCharacter)) continue;
                if (!candidatePartySim && (!IsSimLocalToActiveZone(sim.gameObject, player) ||
                    !IsSimLocalToActiveZone(candidateCharacter.gameObject, player))) continue;
                if (Vector3.Distance(player.transform.position, candidateCharacter.transform.position) > ChallengeDistance) continue;

                string candidate = ReadName(sim);
                if (candidate.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    exactMatches++;
                    if (exactMatches == 1) exact = sim;
                    continue;
                }
                if (candidate.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                partialMatches++;
                if (partialMatches == 1) partial = sim;
            }

            if (exactMatches > 1) { ambiguous = true; return null; }
            if (exactMatches == 1) return exact;
            if (partialMatches > 1) { ambiguous = true; return null; }
            return partial;
        }

        internal static void Start(SimPlayer target)
        {
            if (!CanStartNewDuel)
            {
                Say(_state == DuelLifecycleState.Cleaning
                    ? "[Practice Duel] Finishing cleanup from the previous duel. Try again in a moment."
                    : "[Practice Duel] Finish or stop the current duel before issuing another challenge.", "yellow");
                return;
            }

            Character player = null;
            try { player = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself; } catch { }
            // See FindSim: the player's persistent GameObject is not a member of the loaded zone
            // scene, so this is an aliveness check, not a scene-locality check.
            if (!IsAlive(player))
            {
                Say("[Practice Duel] You are not in a safe state to challenge a Sim.", "yellow");
                return;
            }
            if (!PlayerHealthAllowsDuel(player))
            {
                Say("[Practice Duel] You are too injured to start a duel.", "yellow");
                Diagnostic("eligibility=player_low_health");
                return;
            }

            Character simCharacter;
            NPC simNpc;
            bool partySim;
            DuelEligibilityDecision eligibility = EvaluateEligibility(target, player, out simCharacter, out simNpc, out partySim);
            if (eligibility != DuelEligibilityDecision.Eligible)
            {
                ReportEligibilityFailure(eligibility, target, player);
                return;
            }

            string stableKey = StableSimKey(target);
            DuelSocialDecision decision = EvaluateWillingness(target, player, simCharacter, partySim, stableKey);
            string simName = ReadName(target);

            Say("[Practice Duel] You challenge " + simName + ".", "lightblue");
            NotifyDuelEvent(DuelEventFactory.Challenge(simName, partySim), 20, false, 0.0);

            if (decision != DuelSocialDecision.Accept)
            {
                string token = DuelChallengePolicy.Token(decision);
                Say("[Practice Duel] " + simName + " declines.", "lightblue");
                Diagnostic("event=duel_declined sim=" + SafeLabel(simName) + " scope=" +
                    (partySim ? "party" : "nearby") + " decision=" + token);
                NotifyDuelEvent(DuelEventFactory.Declined(simName, partySim, token), 25, false, 0.0);
                return;
            }

            _player = player;
            _simPlayer = target;
            _sim = simCharacter;
            _simNpc = simNpc;
            _simWasParty = partySim;
            _simName = simName;
            _simStableKey = stableKey;
            SnapshotAllowedDuelPets();
            SnapshotNearbyEnemyMembership();
            _playerMax = Math.Max(1, _player.MyStats.CurrentMaxHP);
            _simMax = Math.Max(1, _sim.MyStats.CurrentMaxHP);
            _playerRealHp = Math.Max(1, Math.Min(_playerMax, _player.MyStats.CurrentHP));
            _simRealHp = Math.Max(1, Math.Min(_simMax, _sim.MyStats.CurrentHP));
            // Virtual HP starts full regardless of real HP at challenge time. Real HP is restored
            // independently from the snapshots above on every exit path.
            _playerHp = _playerMax;
            _simHp = _simMax;
            SnapshotEffects(_player.MyStats, PlayerInitialEffects, PlayerInitialEffectState);
            SnapshotEffects(_sim.MyStats, SimInitialEffects, SimInitialEffectState);
            _playerInitialSpellShield = _player.MyStats.SpellShield;
            _simInitialSpellShield = _sim.MyStats.SpellShield;
            _playerInitialLastHitBy = _player.LastHitBy;
            _simInitialLastHitBy = _sim.LastHitBy;
            _playerInitialRecentDmg = _player.MyStats.RecentDmg;
            _simInitialRecentDmg = _sim.MyStats.RecentDmg;
            _playerInitialRecentDmgByPlayer = _player.MyStats.RecentDmgByPlayer;
            _simInitialRecentDmgByPlayer = _sim.MyStats.RecentDmgByPlayer;
            _previousSimTarget = _simNpc.CurrentAggroTarget;
            _previousPlayerTarget = GameData.PlayerControl.CurrentTarget;
            _previousNpcProc = _simNpc.NPCProcOnHit;
            _previousNpcProcChance = _simNpc.NPCProcOnHitChance;
            _previousGuardSpot = target.GuardSpot;
            _previousGuardPosition = target.GetGuardPos();
            // The Character is persistent (DontDestroyOnLoad); zone transitions are tracked by
            // the authoritative currently loaded Erenshor scene instead.
            Scene activeZone = SceneManager.GetActiveScene();
            _scene = activeZone.name;
            _sceneHandle = activeZone.handle;
            if (!Transition(DuelLifecycleTrigger.ChallengeAccepted, "player challenge accepted"))
            {
                EmergencyCleanup("Start.StateTransition");
                return;
            }
            Diagnostic("duel_start build=" + DuelBuildInfo.Id +
                " playerReal=" + _playerRealHp + "/" + _playerMax +
                " opponentReal=" + _simRealHp + "/" + _simMax +
                " playerVirtual=" + _playerHp + "/" + _playerMax +
                " opponentVirtual=" + _simHp + "/" + _simMax +
                " yieldThreshold=" + YieldThreshold(_playerMax) +
                " scope=" + (_simWasParty ? "party" : "nearby"));
            try
            {
                _simNpc.NPCProcOnHit = null;
                _simNpc.NPCProcOnHitChance = 0f;
            }
            catch
            {
                Cancel("Start.NpcProcGuard", null, null, null, "The duel could not start safely.");
            }
        }

        internal static void Tick()
        {
            RunPostDuelAttackCleanup();
            if (!Active) return;
            if (DuelSafetyPolicy.CancelForSceneMismatch(true, PlayerStillInStartingScene())) { Cancel("Tick.Zone", null, null, null, "Duel cancelled after changing zones."); return; }
            if (!ParticipantsAreValid()) { Cancel("Tick.Participants", null, null, null, "Duel cancelled because a duelist is no longer available."); return; }
            if (!ParticipantScopesStillMatch()) { Cancel("Tick.PartyScope", null, null, null, "Duel cancelled because a participant's party membership changed."); return; }
            if (Vector3.Distance(_player.transform.position, _sim.transform.position) > MaximumDistance) { Cancel("Tick.Distance", null, null, null, "Duel cancelled because the duelists moved too far apart."); return; }
            if (IsCampActive(false)) { Cancel("Tick.Camp", null, null, null, "Duel cancelled because Hunt Camp is active."); return; }
            // Hostile-world PvE may overlap an active Practice Duel. Mere hostile aggro/presence
            // is not interference: exact hostile-world edges remain native/real and are never
            // translated into virtual duel health. Friendly/unknown assistance stays contained at
            // the per-action hooks below.

            float elapsed = Time.unscaledTime - _stateStartedAt;
            if (_state == DuelLifecycleState.Preparing && elapsed >= 1f)
            {
                if (!Transition(DuelLifecycleTrigger.PreparationElapsed, "acceptance delay elapsed"))
                {
                    Cancel("Tick.StatePreparing", null, null, null, "Duel cancelled after an invalid preparation transition.");
                    return;
                }
                _lastCountdown = 4;
                Say("[Practice Duel] " + _simName + " accepts.", "lightblue");
                RememberAcceptedDuel(_simStableKey);
                if (_spectatorDuel) RememberAcceptedDuel(_firstSimStableKey);
                Diagnostic("event=duel_accepted sim=" + SafeLabel(_simName) + " scope=" +
                    (_simWasParty ? "party" : "nearby") + " decision=accept");
                if (!_spectatorDuel)
                    NotifyDuelEvent(DuelEventFactory.Accepted(_simName, _simWasParty), 25, false, 0.0);
                return;
            }
            if (_state == DuelLifecycleState.Countdown)
            {
                int count = Math.Max(1, 3 - (int)elapsed);
                if (count != _lastCountdown)
                {
                    _lastCountdown = count;
                    Say("[Practice Duel] " + count + "...", "lightblue");
                }
                if (elapsed < 3f) return;
                if (!Transition(DuelLifecycleTrigger.CountdownElapsed, "countdown complete"))
                {
                    Cancel("Tick.StateCountdown", null, null, null, "Duel cancelled after an invalid countdown transition.");
                    return;
                }
                MirrorVirtualHealth();
                _simNpc.CurrentAggroTarget = _player;
                if (_spectatorDuel)
                    _firstSimNpc.CurrentAggroTarget = _sim;
                else
                {
                    GameData.PlayerControl.CurrentTarget = _sim;
                    try { _sim.TargetMe(); } catch { }
                }
                Say("[Practice Duel] Fight! First to " + FinishPercent + "% virtual health yields.", "lightblue");
                Diagnostic("event=duel_started sim=" + SafeLabel(_simName) + " scope=" +
                    (_simWasParty ? "party" : "nearby"));
                if (!_spectatorDuel)
                    NotifyDuelEvent(DuelEventFactory.Started(_simName, _simWasParty), 35, false, 0.0);
                return;
            }
            if (_state == DuelLifecycleState.Active)
            {
                MirrorVirtualHealth();
                if (elapsed >= MaximumFightSeconds)
                {
                    Stop("Practice duel timed out after 30 seconds. No winner.");
                    return;
                }
                // Keep the duel target only while native AI has not selected a verified hostile
                // world enemy. Hostile PvE target ownership is real gameplay and outranks the duel
                // pin until native combat clears it; friendly/unknown transient targets do not.
                Character simTarget = _simNpc.CurrentAggroTarget;
                bool simFightingWorld = Classify(simTarget) == CombatActorClass.OutsideHostile;
                if (!simFightingWorld) _simNpc.CurrentAggroTarget = _player;
                bool firstFightingWorld = false;
                if (_spectatorDuel)
                {
                    Character firstTarget = _firstSimNpc.CurrentAggroTarget;
                    firstFightingWorld = Classify(firstTarget) == CombatActorClass.OutsideHostile;
                    if (!firstFightingWorld) _firstSimNpc.CurrentAggroTarget = _sim;
                }
                PurgeDuelistsFromNearbyEnemies();
                if (!simFightingWorld) try { _simNpc.HighPriorityNavUpdate(_player.transform.position); } catch { }
                if (_spectatorDuel && !firstFightingWorld) try { _firstSimNpc.HighPriorityNavUpdate(_sim.transform.position); } catch { }
            }
        }

        internal static void Stop(string reason)
        {
            // Stop is reached directly from Harmony prefixes on core combat methods (via
            // TryVirtualDamage) with no surrounding try/catch at the call site. An exception here
            // must not escape into the patched game method mid-call, and duel state must not be
            // left stuck Active if cleanup throws partway through.
            try
            {
                StopInternal(reason);
            }
            catch (Exception ex)
            {
                try { Diagnostic("Stop.Exception " + ex.GetType().Name); } catch { }
                EmergencyCleanup("Stop.Exception." + ex.GetType().Name);
            }
        }

        internal static void Shutdown()
        {
            Stop(null);
            EndPostDuelAttackCleanup();
            LastAcceptedDuelBySim.Clear();
            LastInterferenceLog.Clear();
            AllowedDuelPets.Clear();
            EngagedPets.Clear();
            PostDuelPetNpcs.Clear();
            _effectTickOwner = null;
        }

        private static void StopInternal(string reason)
        {
            bool wasActive = Active;
            bool hasResidualParticipantState = _player != null || _sim != null || _simNpc != null || _simPlayer != null ||
                _firstSimNpc != null || _firstSimPlayer != null;
            bool hadDuelState = DuelSafetyPolicy.ShouldRunCleanup(Active, hasResidualParticipantState);
            // Shutdown and repeated stop requests are normal lifecycle paths. If there is no active
            // duel and no residual participant state, there is nothing to restore and nothing worth
            // emitting as a terminal diagnostic.
            if (!hadDuelState) return;

            if (wasActive)
            {
                if (!Transition(DuelLifecycleTrigger.Terminal, "terminal cleanup"))
                    _state = DuelLifecycleState.Cleaning;
            }
            else if (_state == DuelLifecycleState.Idle)
            {
                // Residual participant references without an active session are unexpected, but
                // cleanup still owns them. Enter Cleaning explicitly so no new duel can start on
                // top of a half-restored session.
                _state = DuelLifecycleState.Cleaning;
                _stateStartedAt = Time.unscaledTime;
            }

            bool autoAttackBefore = ReadPlayerAutoattack();
            string targetBefore = DescribeActor(GameData.PlayerControl == null ? null : GameData.PlayerControl.CurrentTarget);
            bool externalCombatPresent = HasUnsafeRealCombat(_player, _sim, _simNpc);
            Diagnostic("duel_terminal reason=" + SafeLabel(reason) + " active=" + wasActive +
                " cleanup=" + hadDuelState + " autoAttackBefore=" + autoAttackBefore +
                " targetBefore=" + targetBefore + " externalCombatPresent=" + externalCombatPresent);
            if (hadDuelState) BeginPostDuelAttackCleanup();
            if (wasActive && !string.IsNullOrWhiteSpace(reason) &&
                reason.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0 && !_cancellationLogged)
                LogCancellation("Stop.Fallback", null, null, null, reason);
            bool timedOut = !string.IsNullOrWhiteSpace(reason) &&
                            reason.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0;
            bool completed = timedOut || (!string.IsNullOrWhiteSpace(reason) &&
                             reason.IndexOf("Friendly duel complete", StringComparison.OrdinalIgnoreCase) >= 0);
            bool playerWon = completed && !timedOut && reason.IndexOf(" yields", StringComparison.OrdinalIgnoreCase) >= 0 &&
                             reason.IndexOf(_simName == null ? "" : _simName + " yields", StringComparison.OrdinalIgnoreCase) >= 0;
            bool simWon = completed && !timedOut && !playerWon;
            string completedSimName = _simName;
            bool restorePartyMovement = _simWasParty;
            bool restoreFirstPartyMovement = _firstSimWasParty;
            RestoreRealHealthAndEffects();
            PurgeDuelistsFromNearbyEnemies();
            ReleaseEngagedPets();
            if (_simNpc != null || _simPlayer != null)
            {
                try
                {
                    if (_simNpc != null)
                    {
                        Character currentTarget = _simNpc.CurrentAggroTarget;
                        bool currentTargetIsDuelOwned = currentTarget == _player || currentTarget == _sim;
                        bool previousTargetIsDuelist = _previousSimTarget == _player || _previousSimTarget == _sim;
                        if (currentTargetIsDuelOwned)
                        {
                            _simNpc.CurrentAggroTarget = DuelSafetyPolicy.ShouldRestorePreviousNpcTarget(
                                true, IsAlive(_previousSimTarget), previousTargetIsDuelist) ? _previousSimTarget : null;
                        }
                        if (_simNpc.PastAggroTarget == _player || _simNpc.PastAggroTarget == _sim)
                            _simNpc.PastAggroTarget = null;
                        ResetNpcAttackAnimations(_simNpc);
                    }
                }
                catch { }
                try
                {
                    if (_simNpc != null)
                    {
                        _simNpc.NPCProcOnHit = _previousNpcProc;
                        _simNpc.NPCProcOnHitChance = _previousNpcProcChance;
                    }
                }
                catch { }
                try
                {
                    Character currentPlayerTarget = GameData.PlayerControl == null ? null : GameData.PlayerControl.CurrentTarget;
                    bool currentPlayerTargetIsDuelOwned = currentPlayerTarget == _player || currentPlayerTarget == _sim;
                    if (!_spectatorDuel && DuelSafetyPolicy.ShouldSuppressPostDuelAutoAttack(
                        currentPlayerTargetIsDuelOwned, currentPlayerTarget == null))
                        ForceStopPlayerAttack();
                    if (!_spectatorDuel && GameData.PlayerControl != null && GameData.PlayerControl.CurrentTarget == _sim)
                    {
                        // The usual pre-duel target is the Sim the player clicked to challenge.
                        // Restoring that exact duel opponent makes the native attack loop retain a
                        // valid hostile target after terminal cleanup, which is the observed
                        // repeated "deal no damage" path. Preserve a genuinely different target
                        // (including an external combat target), never a duelist.
                        bool priorIsDuelist = _previousPlayerTarget == _player || _previousPlayerTarget == _sim;
                        GameData.PlayerControl.CurrentTarget = DuelSafetyPolicy.ShouldRestorePreviousTarget(
                            IsAlive(_previousPlayerTarget), priorIsDuelist) ? _previousPlayerTarget : null;
                    }
                }
                catch { }
            }

            if (_spectatorDuel && (_firstSimNpc != null || _firstSimPlayer != null))
            {
                try
                {
                    if (_firstSimNpc != null)
                    {
                        Character currentTarget = _firstSimNpc.CurrentAggroTarget;
                        bool currentTargetIsDuelOwned = currentTarget == _player || currentTarget == _sim;
                        bool previousTargetIsDuelist = _previousFirstSimTarget == _player || _previousFirstSimTarget == _sim;
                        if (currentTargetIsDuelOwned)
                        {
                            _firstSimNpc.CurrentAggroTarget = DuelSafetyPolicy.ShouldRestorePreviousNpcTarget(
                                true, IsAlive(_previousFirstSimTarget), previousTargetIsDuelist) ? _previousFirstSimTarget : null;
                        }
                        if (_firstSimNpc.PastAggroTarget == _player || _firstSimNpc.PastAggroTarget == _sim)
                            _firstSimNpc.PastAggroTarget = null;
                        _firstSimNpc.NPCProcOnHit = _previousFirstNpcProc;
                        _firstSimNpc.NPCProcOnHitChance = _previousFirstNpcProcChance;
                        ResetNpcAttackAnimations(_firstSimNpc);
                    }
                }
                catch { }
            }

            RestorePartyMovementOwnership();

            RestoreInitialNearbyEnemyMembership();

            DuelSemanticEvent lifecycleEvent = null;
            if (wasActive && completed && !_spectatorDuel)
            {
                if (timedOut)
                    lifecycleEvent = DuelEventFactory.Completed(completedSimName, restorePartyMovement,
                        "timeout", string.Empty, string.Empty);
                else if (playerWon)
                    lifecycleEvent = DuelEventFactory.Completed(completedSimName, restorePartyMovement,
                        "yield", "player", "opponent");
                else if (simWon)
                    lifecycleEvent = DuelEventFactory.Completed(completedSimName, restorePartyMovement,
                        "yield", completedSimName, "player");
            }
            else if (wasActive && !_spectatorDuel && !string.IsNullOrWhiteSpace(reason))
            {
                string cancellationToken = string.IsNullOrWhiteSpace(_cancellationReasonToken)
                    ? DuelEventFactory.CancellationToken("Stop.Fallback", reason)
                    : _cancellationReasonToken;
                lifecycleEvent = DuelEventFactory.Cancelled(completedSimName, restorePartyMovement, cancellationToken, SafeLabel(reason));
            }

            string targetAfter = DescribeActor(GameData.PlayerControl == null ? null : GameData.PlayerControl.CurrentTarget);
            bool autoAttackAfter = ReadPlayerAutoattack();
            ClearSessionState();
            RunPostDuelAttackCleanup();
            Diagnostic("cleanup autoAttackBefore=" + autoAttackBefore + " autoAttackAfter=" + autoAttackAfter +
                " targetBefore=" + targetBefore + " targetAfter=" + targetAfter +
                " externalCombatPresent=" + externalCombatPresent + " virtualStateCleared=" + (!Active && _playerHp == 0 && _simHp == 0));

            // Exactly one terminal event is emitted by the Stop transition that owned active duel
            // state. Later cleanup calls see Active == false and cannot duplicate it.
            if (lifecycleEvent != null && lifecycleEvent.Type == "duel_completed")
                NotifyDuelEvent(lifecycleEvent, 75, false, 0.65);
            else if (lifecycleEvent != null)
                NotifyDuelEvent(lifecycleEvent, 45, false, 0.0);

            if (!string.IsNullOrWhiteSpace(reason)) Say("[Practice Duel] " + reason, "lightblue");
        }

        private static void BeginPostDuelAttackCleanup()
        {
            _postDuelPlayer = _player;
            _postDuelSim = _sim;
            _postDuelSimNpc = _simNpc;
            _postDuelFirstSimNpc = _firstSimNpc;
            SnapshotPostDuelPets();
            _postDuelStopLocalPlayer = !_spectatorDuel;
            _postDuelAttackCleanupFrames = 6;
            _postDuelAttackCleanupUntil = Time.unscaledTime + 2f;
        }

        private static void RunPostDuelAttackCleanup()
        {
            if (_postDuelAttackCleanupFrames <= 0 && Time.unscaledTime >= _postDuelAttackCleanupUntil) return;
            try
            {
                if (_postDuelStopLocalPlayer && GameData.PlayerControl != null)
                {
                    Character current = GameData.PlayerControl.CurrentTarget;
                    bool duelOwned = current == _postDuelPlayer || current == _postDuelSim;
                    if (DuelSafetyPolicy.ShouldSuppressPostDuelAutoAttack(duelOwned, current == null))
                    {
                        ForceStopPlayerAttack();
                        if (duelOwned) GameData.PlayerControl.CurrentTarget = null;
                    }
                    else
                    {
                        // A new unrelated target is authoritative evidence that native gameplay
                        // has moved on. Stop policing the player's attack loop immediately.
                        _postDuelStopLocalPlayer = false;
                    }
                }
            }
            catch { }
            try
            {
                if (_postDuelSimNpc != null)
                {
                    if (_postDuelSimNpc.CurrentAggroTarget == _postDuelPlayer || _postDuelSimNpc.CurrentAggroTarget == _postDuelSim)
                        _postDuelSimNpc.CurrentAggroTarget = null;
                    if (_postDuelSimNpc.PastAggroTarget == _postDuelPlayer || _postDuelSimNpc.PastAggroTarget == _postDuelSim)
                        _postDuelSimNpc.PastAggroTarget = null;
                    ResetNpcAttackAnimations(_postDuelSimNpc);
                }
                if (_postDuelFirstSimNpc != null)
                {
                    if (_postDuelFirstSimNpc.CurrentAggroTarget == _postDuelPlayer || _postDuelFirstSimNpc.CurrentAggroTarget == _postDuelSim)
                        _postDuelFirstSimNpc.CurrentAggroTarget = null;
                    if (_postDuelFirstSimNpc.PastAggroTarget == _postDuelPlayer || _postDuelFirstSimNpc.PastAggroTarget == _postDuelSim)
                        _postDuelFirstSimNpc.PastAggroTarget = null;
                    ResetNpcAttackAnimations(_postDuelFirstSimNpc);
                }
                foreach (NPC pet in PostDuelPetNpcs)
                    ClearDuelCombatReferences(pet);
            }
            catch { }
            _postDuelAttackCleanupFrames--;
            if (_postDuelAttackCleanupFrames > 0 || Time.unscaledTime < _postDuelAttackCleanupUntil) return;
            EndPostDuelAttackCleanup();
        }

        private static void EndPostDuelAttackCleanup()
        {
            _postDuelAttackCleanupFrames = 0;
            _postDuelAttackCleanupUntil = 0f;
            _postDuelPlayer = null;
            _postDuelSim = null;
            _postDuelSimNpc = null;
            _postDuelFirstSimNpc = null;
            _postDuelStopLocalPlayer = false;
            PostDuelPetNpcs.Clear();
            if (_state == DuelLifecycleState.Cleaning)
            {
                DuelLifecycleState next;
                if (DuelLifecyclePolicy.TryTransition(_state, DuelLifecycleTrigger.CleanupComplete, out next))
                    _state = next;
                else
                    _state = DuelLifecycleState.Idle;
                _stateStartedAt = 0f;
            }
        }

        private static void ResetNpcAttackAnimations(NPC npc)
        {
            if (npc == null || ResetNpcAttackAnimationsMethod == null) return;
            try { ResetNpcAttackAnimationsMethod.Invoke(npc, null); } catch { }
        }

        // Party movement/Guard state is temporary duel ownership. Keep this idempotent and call it
        // from both ordinary and emergency terminal paths before participant references are cleared.
        // Nearby non-party Sims are deliberately excluded: FreeFollow/AssignGuardSpot would create
        // persistent party-management state that did not exist before the duel.
        private static void RestorePartyMovementOwnership()
        {
            try
            {
                if (_simPlayer != null && _simWasParty)
                {
                    if (_previousGuardSpot) _simPlayer.AssignGuardSpot(_previousGuardPosition);
                    else _simPlayer.FreeFollow();
                }
            }
            catch { }
            try
            {
                if (_spectatorDuel && _firstSimPlayer != null && _firstSimWasParty)
                {
                    if (_previousFirstGuardSpot) _firstSimPlayer.AssignGuardSpot(_previousFirstGuardPosition);
                    else _firstSimPlayer.FreeFollow();
                }
            }
            catch { }
        }

        // ForceAttackOff() alone was observed to leave the player still auto-attacking after a
        // duel ends. PlayerCombat.Autoattack is the persistent toggle the game's own attack loop
        // checks each tick; clear it directly as well so a call that doesn't fully reset it (or a
        // re-trigger from residual input on the same frame) can't leave attacking stuck on.
        private static void ForceStopPlayerAttack()
        {
            try { if (GameData.PlayerCombat != null) GameData.PlayerCombat.ForceAttackOff(); } catch { }
            try
            {
                if (GameData.PlayerCombat != null && PlayerAutoattackField != null)
                    PlayerAutoattackField.SetValue(GameData.PlayerCombat, false);
            }
            catch { }
        }

        private static void SnapshotPostDuelPets()
        {
            PostDuelPetNpcs.Clear();
            try
            {
                foreach (Character pet in AllowedDuelPets)
                {
                    NPC npc = pet == null ? null : pet.MyNPC;
                    if (npc != null) PostDuelPetNpcs.Add(npc);
                }
                foreach (NPC npc in EngagedPets)
                    if (npc != null) PostDuelPetNpcs.Add(npc);
            }
            catch { }
        }

        // Combatants and AggroTable outlive CurrentAggroTarget in Erenshor's NPC AI. Leaving a
        // duelist there makes an owned pet continue to look engaged after the spar is over.
        private static void ClearDuelCombatReferences(NPC npc)
        {
            if (npc == null) return;
            try
            {
                Character first = _postDuelPlayer != null ? _postDuelPlayer : _player;
                Character second = _postDuelSim != null ? _postDuelSim : _sim;
                if (npc.CurrentAggroTarget == first || npc.CurrentAggroTarget == second)
                    npc.CurrentAggroTarget = null;
                if (npc.PastAggroTarget == first || npc.PastAggroTarget == second)
                    npc.PastAggroTarget = null;
                System.Collections.IList combatants = NpcCombatantsField == null ? null :
                    NpcCombatantsField.GetValue(npc) as System.Collections.IList;
                if (combatants != null)
                {
                    for (int i = combatants.Count - 1; i >= 0; i--)
                    {
                        Character actor = combatants[i] as Character;
                        if (actor == first || actor == second) combatants.RemoveAt(i);
                    }
                }
                if (npc.AggroTable != null)
                    npc.AggroTable.RemoveAll(slot => slot == null || slot.Player == first || slot.Player == second);
                ResetNpcAttackAnimations(npc);
            }
            catch { }
        }

        internal static void StartSpectator(SimPlayer first, SimPlayer second)
        {
            if (!CanStartNewDuel)
            {
                Say(_state == DuelLifecycleState.Cleaning
                    ? "[Practice Duel] Finishing cleanup from the previous duel. Try again in a moment."
                    : "[Practice Duel] Finish or stop the current duel before issuing another challenge.", "yellow");
                return;
            }
            if (first == null || second == null || first == second)
            {
                Say("[Practice Duel] Choose two different nearby Sims.", "yellow");
                return;
            }

            Character localPlayer = null;
            try { localPlayer = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself; } catch { }
            if (!IsAlive(localPlayer))
            {
                Say("[Practice Duel] You are not in a safe state to start a spectator duel.", "yellow");
                return;
            }

            Character firstCharacter;
            Character secondCharacter;
            NPC firstNpc;
            NPC secondNpc;
            bool firstParty;
            bool secondParty;
            DuelEligibilityDecision firstEligibility = EvaluateEligibility(first, localPlayer, out firstCharacter, out firstNpc, out firstParty);
            DuelEligibilityDecision secondEligibility = EvaluateEligibility(second, localPlayer, out secondCharacter, out secondNpc, out secondParty);
            if (firstEligibility != DuelEligibilityDecision.Eligible)
            {
                ReportEligibilityFailure(firstEligibility, first, localPlayer);
                return;
            }
            if (secondEligibility != DuelEligibilityDecision.Eligible)
            {
                ReportEligibilityFailure(secondEligibility, second, localPlayer);
                return;
            }
            if (!PlayerHealthAllowsDuel(firstCharacter) || !PlayerHealthAllowsDuel(secondCharacter))
            {
                Say("[Practice Duel] Both Sims need at least 35% real health before they spar.", "yellow");
                return;
            }
            if (Vector3.Distance(firstCharacter.transform.position, secondCharacter.transform.position) > MaximumDistance)
            {
                Say("[Practice Duel] Those Sims are too far apart to spar.", "yellow");
                return;
            }

            string firstKey = StableSimKey(first);
            string secondKey = StableSimKey(second);
            if (WasRecentlyAccepted(firstKey) || WasRecentlyAccepted(secondKey))
            {
                Say("[Practice Duel] One of those Sims needs a moment before another duel.", "lightblue");
                return;
            }

            _spectatorDuel = true;
            _player = firstCharacter;
            _firstSimPlayer = first;
            _firstSimNpc = firstNpc;
            _firstSimWasParty = firstParty;
            _firstSimName = ReadName(first);
            _firstSimStableKey = firstKey;
            _simPlayer = second;
            _sim = secondCharacter;
            _simNpc = secondNpc;
            _simWasParty = secondParty;
            _simName = ReadName(second);
            _simStableKey = secondKey;
            SnapshotAllowedDuelPets();
            SnapshotNearbyEnemyMembership();
            _playerMax = Math.Max(1, _player.MyStats.CurrentMaxHP);
            _simMax = Math.Max(1, _sim.MyStats.CurrentMaxHP);
            _playerRealHp = Math.Max(1, Math.Min(_playerMax, _player.MyStats.CurrentHP));
            _simRealHp = Math.Max(1, Math.Min(_simMax, _sim.MyStats.CurrentHP));
            _playerHp = _playerMax;
            _simHp = _simMax;
            SnapshotEffects(_player.MyStats, PlayerInitialEffects, PlayerInitialEffectState);
            SnapshotEffects(_sim.MyStats, SimInitialEffects, SimInitialEffectState);
            _playerInitialSpellShield = _player.MyStats.SpellShield;
            _simInitialSpellShield = _sim.MyStats.SpellShield;
            _playerInitialLastHitBy = _player.LastHitBy;
            _simInitialLastHitBy = _sim.LastHitBy;
            _playerInitialRecentDmg = _player.MyStats.RecentDmg;
            _simInitialRecentDmg = _sim.MyStats.RecentDmg;
            _playerInitialRecentDmgByPlayer = _player.MyStats.RecentDmgByPlayer;
            _simInitialRecentDmgByPlayer = _sim.MyStats.RecentDmgByPlayer;
            _previousFirstSimTarget = _firstSimNpc.CurrentAggroTarget;
            _previousSimTarget = _simNpc.CurrentAggroTarget;
            _previousPlayerTarget = GameData.PlayerControl.CurrentTarget;
            _previousFirstNpcProc = _firstSimNpc.NPCProcOnHit;
            _previousFirstNpcProcChance = _firstSimNpc.NPCProcOnHitChance;
            _previousNpcProc = _simNpc.NPCProcOnHit;
            _previousNpcProcChance = _simNpc.NPCProcOnHitChance;
            _previousFirstGuardSpot = first.GuardSpot;
            _previousFirstGuardPosition = first.GetGuardPos();
            _previousGuardSpot = second.GuardSpot;
            _previousGuardPosition = second.GetGuardPos();
            Scene activeZone = SceneManager.GetActiveScene();
            _scene = activeZone.name;
            _sceneHandle = activeZone.handle;
            if (!Transition(DuelLifecycleTrigger.ChallengeAccepted, "spectator challenge accepted"))
            {
                EmergencyCleanup("StartSpectator.StateTransition");
                return;
            }
            try
            {
                _firstSimNpc.NPCProcOnHit = null;
                _firstSimNpc.NPCProcOnHitChance = 0f;
                _simNpc.NPCProcOnHit = null;
                _simNpc.NPCProcOnHitChance = 0f;
            }
            catch
            {
                Cancel("StartSpectator.NpcProcGuard", null, null, null, "The spectator duel could not start safely.");
                return;
            }
            Say("[Practice Duel] " + _firstSimName + " challenges " + _simName + ".", "lightblue");
            Diagnostic("duel_start mode=spectator first=" + SafeLabel(_firstSimName) + " second=" + SafeLabel(_simName));
        }

        private static bool ReadPlayerAutoattack()
        {
            try
            {
                return GameData.PlayerCombat != null && PlayerAutoattackField != null &&
                       Convert.ToBoolean(PlayerAutoattackField.GetValue(GameData.PlayerCombat));
            }
            catch { return false; }
        }

        private static int YieldThreshold(int maximum)
        {
            return maximum <= 0 ? 0 : maximum * FinishPercent / 100;
        }

        private static void DiagnosticVirtual(string kind, string source, bool playerTarget, int nativeAmount,
            int virtualDelta, int before, int after, int maximum, int realBefore, int realAfter, string reason)
        {
            bool yields = after <= YieldThreshold(maximum);
            Diagnostic(kind + " target=" + (playerTarget ? "player" : SafeLabel(_simName)) +
                " source=" + SafeLabel(source) + " native=" + nativeAmount +
                " virtualBefore=" + before + "/" + maximum + " virtualDelta=" + virtualDelta +
                " virtualAfter=" + after + "/" + maximum + " realBefore=" + realBefore +
                " realAfter=" + realAfter + " yieldThreshold=" + YieldThreshold(maximum) +
                " yield=" + yields + " reason=" + SafeLabel(reason));
        }

        private static void MirrorVirtualHealth()
        {
            if (_state != DuelLifecycleState.Active) return;
            MirrorOne(_player, _playerHp, _playerMax);
            MirrorOne(_sim, _simHp, _simMax);
        }

        // Stats.CalcStats recomputes CurrentMaxHP whenever an effect is added or removed, so a buff
        // expiring mid-duel can leave the saved duel maximum above the live one. Clamp to both:
        // writing a CurrentHP above CurrentMaxHP leaves the health bar reading over 100%.
        private static void MirrorOne(Character character, int virtualHp, int duelMax)
        {
            try
            {
                if (character == null || character.MyStats == null) return;
                int ceiling = duelMax;
                int liveMax = character.MyStats.CurrentMaxHP;
                if (liveMax > 0 && liveMax < ceiling) ceiling = liveMax;
                character.MyStats.CurrentHP = Math.Max(1, Math.Min(ceiling, virtualHp));
            }
            catch { }
        }

        private static void RestoreRealHealthAndEffects()
        {
            try
            {
                if (_player != null && _player.MyStats != null)
                {
                    RemoveDuelEffects(_player.MyStats, PlayerInitialEffects, PlayerInitialEffectState);
                    _player.MyStats.SpellShield = _playerInitialSpellShield;
                    _player.LastHitBy = _playerInitialLastHitBy;
                    _player.MyStats.RecentDmg = _playerInitialRecentDmg;
                    _player.MyStats.RecentDmgByPlayer = _playerInitialRecentDmgByPlayer;
                    RestoreRealLedgerHp(_player, _playerRealHp);
                }
            }
            catch { }
            try
            {
                if (_sim != null && _sim.MyStats != null)
                {
                    RemoveDuelEffects(_sim.MyStats, SimInitialEffects, SimInitialEffectState);
                    _sim.MyStats.SpellShield = _simInitialSpellShield;
                    _sim.LastHitBy = _simInitialLastHitBy;
                    _sim.MyStats.RecentDmg = _simInitialRecentDmg;
                    _sim.MyStats.RecentDmgByPlayer = _simInitialRecentDmgByPlayer;
                    RestoreRealLedgerHp(_sim, _simRealHp);
                }
            }
            catch { }
        }

        private static int RealLedgerHp(Character target)
        {
            if (target == _player) return _playerRealHp;
            if (target == _sim) return _simRealHp;
            try { return target == null || target.MyStats == null ? 0 : target.MyStats.CurrentHP; }
            catch { return 0; }
        }

        private static void SetRealLedgerHp(Character target, int value)
        {
            int safe = Math.Max(0, value);
            if (target == _player) _playerRealHp = safe;
            else if (target == _sim) _simRealHp = safe;
        }

        private static void RestoreRealLedgerHp(Character target, int value)
        {
            if (target == null || target.MyStats == null) return;
            // A real hostile-world kill is authoritative. Never turn a native death into a duel
            // full-heal by restoring the pre-duel snapshot. If the actor still lives, restore the
            // updated real-world ledger that includes any hostile PvE damage taken mid-duel.
            if (value <= 0 && !IsAlive(target)) return;
            target.MyStats.CurrentHP = Math.Max(1, Math.Min(target.MyStats.CurrentMaxHP, value));
        }

        private static void AdoptCurrentRealDamageState(Character target)
        {
            if (target == null || target.MyStats == null) return;
            // Do not snapshot every status slot here: the participant may also be carrying
            // duel-only buffs/debuffs. Only native damage metadata/shield consumption is adopted
            // broadly. Hostile status effects use AdoptWorldStatusEffectSlots below.
            if (target == _player)
            {
                _playerInitialSpellShield = target.MyStats.SpellShield;
                _playerInitialLastHitBy = target.LastHitBy;
                _playerInitialRecentDmg = target.MyStats.RecentDmg;
                _playerInitialRecentDmgByPlayer = target.MyStats.RecentDmgByPlayer;
            }
            else if (target == _sim)
            {
                _simInitialSpellShield = target.MyStats.SpellShield;
                _simInitialLastHitBy = target.LastHitBy;
                _simInitialRecentDmg = target.MyStats.RecentDmg;
                _simInitialRecentDmgByPlayer = target.MyStats.RecentDmgByPlayer;
            }
        }

        private static EffectSlotSnapshot SnapshotEffectSlot(StatusEffect slot)
        {
            return new EffectSlotSnapshot
            {
                Effect = slot == null ? null : slot.Effect,
                Duration = slot == null ? 0f : slot.Duration,
                FromPlayer = slot != null && slot.fromPlayer,
                BonusDamage = slot == null ? 0 : slot.bonusDmg,
                CastedByPc = slot != null && slot.CastedByPC,
                Owner = slot == null ? null : slot.Owner,
                CreditDps = slot == null ? null : slot.CreditDPS
            };
        }

        private static void AdoptWorldStatusEffectSlots(Character target, Spell worldSpell)
        {
            if (target == null || target.MyStats == null || target.MyStats.StatusEffects == null || worldSpell == null) return;
            Dictionary<int, Spell> initial = target == _player ? PlayerInitialEffects : SimInitialEffects;
            Dictionary<int, EffectSlotSnapshot> state = target == _player ? PlayerInitialEffectState : SimInitialEffectState;
            Dictionary<int, Spell> tracked = target == _player ? PlayerWorldEffectSlots : SimWorldEffectSlots;
            StatusEffect[] effects = target.MyStats.StatusEffects;
            for (int i = 0; i < effects.Length; i++)
            {
                StatusEffect slot = effects[i];
                Spell current = slot == null ? null : slot.Effect;
                if (current != worldSpell) continue;
                initial[i] = current;
                state[i] = SnapshotEffectSlot(slot);
                tracked[i] = current;
            }
            AdoptCurrentRealDamageState(target);
        }

        private static void RefreshTrackedWorldEffects(Character target)
        {
            if (target == null || target.MyStats == null || target.MyStats.StatusEffects == null) return;
            Dictionary<int, Spell> initial = target == _player ? PlayerInitialEffects : SimInitialEffects;
            Dictionary<int, EffectSlotSnapshot> state = target == _player ? PlayerInitialEffectState : SimInitialEffectState;
            Dictionary<int, Spell> tracked = target == _player ? PlayerWorldEffectSlots : SimWorldEffectSlots;
            if (tracked.Count == 0) return;
            StatusEffect[] effects = target.MyStats.StatusEffects;
            List<int> remove = null;
            foreach (KeyValuePair<int, Spell> pair in tracked)
            {
                int index = pair.Key;
                if (index < 0 || index >= effects.Length) continue;
                StatusEffect slot = effects[index];
                Spell current = slot == null ? null : slot.Effect;
                if (current == pair.Value)
                {
                    initial[index] = current;
                    state[index] = SnapshotEffectSlot(slot);
                }
                else if (current == null)
                {
                    // The genuine world effect expired/was consumed naturally. Update the real
                    // baseline to empty so cleanup cannot resurrect it. A different non-null spell
                    // may be a temporary duel effect, so leave the world baseline untouched.
                    initial[index] = null;
                    state[index] = SnapshotEffectSlot(slot);
                    if (remove == null) remove = new List<int>();
                    remove.Add(index);
                }
            }
            if (remove != null) for (int i = 0; i < remove.Count; i++) tracked.Remove(remove[i]);
        }

        private static void SnapshotEffects(Stats stats, Dictionary<int, Spell> destination,
            Dictionary<int, EffectSlotSnapshot> stateDestination)
        {
            destination.Clear();
            stateDestination.Clear();
            if (stats == null || stats.StatusEffects == null) return;
            StatusEffect[] effects = stats.StatusEffects;
            for (int i = 0; i < effects.Length; i++)
            {
                StatusEffect slot = effects[i];
                destination[i] = slot == null ? null : slot.Effect;
                stateDestination[i] = SnapshotEffectSlot(slot);
            }
        }

        private static void RemoveDuelEffects(Stats stats, Dictionary<int, Spell> initial,
            Dictionary<int, EffectSlotSnapshot> initialState)
        {
            if (stats == null || stats.StatusEffects == null) return;
            StatusEffect[] effects = stats.StatusEffects;
            // Reverse-index loop is safe: slots are the same 30 pre-allocated objects for the
            // whole session and RemoveStatusEffect never reorders/resizes the array.
            for (int i = effects.Length - 1; i >= 0; i--)
            {
                StatusEffect slot = effects[i];
                Spell current = slot == null ? null : slot.Effect;
                if (current == null) continue;
                Spell recordedAtStart;
                initial.TryGetValue(i, out recordedAtStart);
                if (current != recordedAtStart)
                {
                    try { stats.RemoveStatusEffect(i); } catch { }
                }
            }
            // Native hit processing can break or consume an effect that existed before the duel.
            // Restore the complete slot state after removing duel-added effects so a practice match
            // cannot spend real shield charges, durations, or breakable buffs.
            for (int i = 0; i < effects.Length; i++)
            {
                StatusEffect slot = effects[i];
                EffectSlotSnapshot saved;
                if (slot == null || !initialState.TryGetValue(i, out saved)) continue;
                slot.Effect = saved.Effect;
                slot.Duration = saved.Duration;
                slot.fromPlayer = saved.FromPlayer;
                slot.bonusDmg = saved.BonusDamage;
                slot.CastedByPC = saved.CastedByPc;
                slot.Owner = saved.Owner;
                slot.CreditDPS = saved.CreditDps;
            }
            try { if (CountStatusEffectsMethod != null) CountStatusEffectsMethod.Invoke(stats, null); } catch { }
        }

        internal static void Cancel(string source, Character actor, Character victim, Character target, string reason)
        {
            if (!Active) return;
            _cancellationReasonToken = DuelEventFactory.CancellationToken(source, reason);
            LogCancellation(source, actor, victim, target, reason);
            Stop(reason);
        }

        // A scene event is still the earliest generic Unity lifecycle signal available to this
        // plugin. Cancel there rather than waiting for Tick(): the player Character can be destroyed
        // during zoning, and Unity fake-null would then make real-health restoration impossible.
        internal static void HandleSceneTransition()
        {
            if (!Active || PlayerStillInStartingScene()) return;
            Cancel("Scene.Transition", null, null, null, "Duel cancelled because the zone changed.");
        }

        private static void LogCancellation(string source, Character actor, Character victim, Character target, string reason)
        {
            _cancellationLogged = true;
            string message = "[Practice Duel] cancel source=" + SafeLabel(source) +
                " actor=" + DescribeActor(actor) +
                " victim=" + DescribeActor(victim) +
                " target=" + DescribeActor(target) +
                " reason=" + SafeLabel(reason);
            try { if (ErenshorDuelPlugin.Instance != null) ErenshorDuelPlugin.Instance.Diagnostic(message); } catch { }
        }

        internal static string Status()
        {
            if (_state == DuelLifecycleState.Cleaning) return "[Practice Duel] Cleanup is finishing; no duel damage is active.";
            if (!Active) return "[Practice Duel] No duel is active.";
            if (_state != DuelLifecycleState.Active) return _spectatorDuel
                ? "[Practice Duel] Preparing " + _firstSimName + " vs " + _simName + "."
                : "[Practice Duel] Preparing to duel " + _simName + ".";
            return "[Practice Duel] " + ParticipantLabel(_player) + ": " + Percent(_playerHp, _playerMax) + "% | " +
                   _simName + ": " + Percent(_simHp, _simMax) + "% virtual health.";
        }

        internal static string Diagnostics()
        {
            bool playerControlPresent = false;
            Character player = null;
            bool playerAlive = false;
            bool playerActive = false;
            Scene playerControlScene = default(Scene);
            Scene playerCharacterScene = default(Scene);
            Scene activeScene = default(Scene);
            try
            {
                playerControlPresent = GameData.PlayerControl != null;
                if (playerControlPresent)
                {
                    player = GameData.PlayerControl.Myself;
                    if (GameData.PlayerControl.gameObject != null) playerControlScene = GameData.PlayerControl.gameObject.scene;
                }
                activeScene = SceneManager.GetActiveScene();
                playerAlive = IsAlive(player);
                playerActive = player != null && player.gameObject != null && player.gameObject.activeInHierarchy;
                if (player != null && player.gameObject != null) playerCharacterScene = player.gameObject.scene;
            }
            catch { }

            int loadedSims = 0;
            int legacyActiveScenePass = 0;
            int playerLocalPass = 0;
            int coopExcluded = 0;
            try
            {
                SimPlayer[] sims = UnityEngine.Object.FindObjectsOfType<SimPlayer>();
                loadedSims = sims == null ? 0 : sims.Length;
                if (sims != null)
                {
                    for (int i = 0; i < sims.Length; i++)
                    {
                        SimPlayer sim = sims[i];
                        if (sim == null) continue;
                        Character actor = null;
                        try { actor = sim.MyStats == null ? null : sim.MyStats.Myself; } catch { }
                        try
                        {
                            if (sim.gameObject != null && actor != null && actor.gameObject != null &&
                                sim.gameObject.scene.handle == activeScene.handle && actor.gameObject.scene.handle == activeScene.handle)
                                legacyActiveScenePass++;
                        }
                        catch { }
                        if (IsUsableSim(sim)) playerLocalPass++;
                        if (CoopCompatibility.IsRemoteHuman(sim)) coopExcluded++;
                        LogNearbyCandidateDiagnostic(sim, player);
                    }
                }
            }
            catch { }

            DeepSimsCompatibility.CampStatus camp = DeepSimsCompatibility.GetCampStatus();
            return "[Practice Duel DIAG] build=" + DuelBuildInfo.Id +
                   " lifecycle=" + _state +
                   " active=" + Active +
                   " playerAlive=" + playerAlive +
                   " playerActiveInHierarchy=" + playerActive +
                   " PlayerControl=" + playerControlPresent +
                   " PlayerControlGOScene=" + SceneLabel(playerControlScene) +
                   " activeScene=" + SceneLabel(activeScene) +
                   " playerCharacterScene=" + SceneLabel(playerCharacterScene) +
                   " stableLocalPlayer=" + IsAlive(player) +
                   " localSimPlayers=" + loadedSims +
                   " legacyActiveScenePass=" + legacyActiveScenePass +
                   " playerLocalScenePass=" + playerLocalPass +
                   " coopExclusions=" + coopExcluded +
                   " campSource=" + (camp.Source ?? "none") +
                   " huntCamp=" + camp.HuntCampActive +
                   " relax=" + camp.RelaxActive +
                   " realLedger=" + _playerRealHp + "/" + _simRealHp +
                   " lastSpell=" + _lastSpellAdmission +
                   " lastDamage=" + _lastDamageDiagnostic +
                   " lastAoE=" + _lastAoeDiagnostic +
                   " coop=" + CoopCompatibility.Describe();
        }

        // /eduel diag is intentionally exhaustive for real SimPlayer components. This is the
        // evidence boundary for nearby non-party support: ordinary NPCs/pets do not enter this
        // list, while every actual SimPlayer gets its exact final policy token in the Lunaris log.
        private static void LogNearbyCandidateDiagnostic(SimPlayer sim, Character player)
        {
            if (sim == null) return;
            Character actor = null;
            NPC npc = null;
            bool party = IsPlayerPartySim(sim);
            bool remote = false;
            bool activePass = false;
            bool alive = false;
            float distance = float.MaxValue;
            DuelEligibilityDecision eligibility = DuelEligibilityDecision.NotSimPlayer;
            try
            {
                actor = sim.MyStats == null ? null : sim.MyStats.Myself;
                npc = actor == null ? null : actor.MyNPC;
                activePass = IsSimLocalToActiveZone(sim.gameObject, player) &&
                    actor != null && IsSimLocalToActiveZone(actor.gameObject, player);
                alive = IsAlive(actor);
                if (player != null && actor != null) distance = Vector3.Distance(player.transform.position, actor.transform.position);
                bool ignoredParty;
                eligibility = EvaluateEligibility(sim, player, out actor, out npc, out ignoredParty);
            }
            catch { }
            try { remote = CoopCompatibility.IsRemoteHuman(sim); } catch { }
            string willingness = "n/a";
            if (eligibility == DuelEligibilityDecision.Eligible)
                willingness = DuelChallengePolicy.Token(EvaluateWillingness(sim, player, actor, party, StableSimKey(sim)));
            Diagnostic("nearby_candidate name=" + SafeLabel(ReadName(sim)) +
                " distance=" + (distance == float.MaxValue ? "n/a" : distance.ToString("0.0")) +
                " party=" + party + " activeScenePass=" + activePass +
                " localAuthority=" + (!remote && npc != null && npc.ThisSim == sim) +
                " remoteHuman=" + remote + " " + CoopCompatibility.TargetFlags(sim) +
                " alive=" + alive + " identityValid=" + (!string.IsNullOrWhiteSpace(StableSimKey(sim))) +
                " willingness=" + willingness + " finalEligibility=" + DuelEligibilityPolicy.Token(eligibility) +
                " rejectionReason=" + (eligibility == DuelEligibilityDecision.Eligible ? "none" : DuelEligibilityPolicy.Token(eligibility)));
        }

        internal sealed class NativeDamageState
        {
            internal NativeDamageState Previous;
            internal Character Target;
            internal Character Attacker;
            internal int VirtualBefore;
            internal int RealBefore;
            internal int NativeBefore;
            internal int RawDamage;
            internal string Source;
            internal bool FromPlayer;
            internal bool WorldReal;
            internal Character.Faction OriginalFaction;
            internal bool FactionChanged;
            internal int OriginalLayer;
            internal bool LayerCaptured;
            internal int CapturedReduceHpDamage;
            internal bool CapturedReduceHp;
        }

        internal sealed class StandaloneWorldDamageState
        {
            internal Character Target;
            internal int RealBefore;
            internal string Source;
            internal bool NestedNativeWorldDamage;
            internal bool Completed;
        }

        private struct EffectSlotSnapshot
        {
            internal Spell Effect;
            internal float Duration;
            internal bool FromPlayer;
            internal int BonusDamage;
            internal bool CastedByPc;
            internal Character Owner;
            internal Character CreditDps;
        }

        // Erenshor gates duel-shaped hits on the victim's faction, and the installed IL shows three
        // separate rejections that a naive "pretend to be Faction.Player" swap walks straight into:
        //
        //   Character.DamageMe      : _dmgType != Physical && MyFaction == Player   -> returns -3
        //   Character.BleedDamageMe : _attacker != null     && MyFaction == Player   -> returns -3
        //   Character.MagicDamageMe : MyFaction == Player && isNPC                   -> returns -1
        //                             MyFaction == PC && attacker has SimPlayer      -> returns -1
        //                             attacker.isNPC && !Charmed &&
        //                               attacker.MyFaction == MyFaction              -> returns -1
        //
        // Faction.Player is literally 0, so swapping the victim to it disabled every non-physical
        // duel hit: all spell damage onto the Sim, all DoT ticks (Stats.TickEffects routes them
        // through DamageMe), and all bleeds on either duelist. Only plain physical melee survived,
        // which is why armor/resist behaviour looked like it was being skipped.
        //
        // The temporary faction therefore has to satisfy all three gates at once: not Player, not
        // PC, and not whatever the attacker currently is.
        private static Character.Faction NeutralHitFaction(Character attacker)
        {
            Character.Faction attackerFaction = Character.Faction.PC;
            try { if (attacker != null) attackerFaction = attacker.MyFaction; } catch { }
            if (attackerFaction != Character.Faction.DEBUG) return Character.Faction.DEBUG;
            return Character.Faction.Villager;
        }

        private static Character OwnedDuelPrincipal(Character actor)
        {
            if (actor == null) return null;
            if (actor == _player || actor == _sim) return actor;
            Character owner;
            try { owner = actor.Master; } catch { return null; }
            for (int depth = 0; owner != null && depth < 4; depth++)
            {
                if (owner == _player || owner == _sim) return owner;
                try { owner = owner.Master; } catch { return null; }
            }
            return null;
        }

        private static void SnapshotAllowedDuelPets()
        {
            AllowedDuelPets.Clear();
            try
            {
                foreach (NPC npc in UnityEngine.Object.FindObjectsOfType<NPC>())
                {
                    Character actor = NpcCharacter(npc);
                    if (actor == null || actor == _player || actor == _sim) continue;
                    if (!IsSimLocalToActiveZone(actor.gameObject, _player)) continue;
                    if (OwnedDuelPrincipal(actor) != null) AllowedDuelPets.Add(actor);
                }
            }
            catch { }
        }

        // Which duelist an actor is fighting for: the duelist itself, or a pet that was already
        // present at challenge time and is owned through Character.Master by a duelist. Ownership
        // alone is not enough: a newly created summon must not expand the 1v1 boundary.
        private static Character DuelPrincipal(Character actor)
        {
            if (actor == null) return null;
            if (actor == _player || actor == _sim) return actor;
            if (!AllowedDuelPets.Contains(actor)) return null;
            return OwnedDuelPrincipal(actor);
        }

        private static Character DuelOpponentOf(Character principal)
        {
            if (principal == _player) return _sim;
            if (principal == _sim) return _player;
            return null;
        }

        // True when the hit belongs inside the match: the victim is a duelist and the attacker is
        // fighting for the other side. Damage always lands on the victim's virtual ledger, whether
        // the swing came from the opposing duelist or from that duelist's pet.
        private static bool IsDuelHit(Character target, Character attacker)
        {
            if (target != _player && target != _sim) return false;
            Character principal = DuelPrincipal(attacker);
            return principal != null && principal != target;
        }

        // A duelist's pet is admitted as an extension of its owner, so it needs to be allowed to
        // acquire and act on the opposing duelist. It stays barred from every other actor, and
        // nothing can damage it: it is a conduit for its owner's damage, not a target.
        private static bool IsAdmittedPetEngagement(Character actor, Character target)
        {
            if (actor == null || target == null) return false;
            Character principal = DuelPrincipal(actor);
            return DuelSafetyPolicy.AllowPreExistingPetEngagement(
                _state == DuelLifecycleState.Active,
                actor == _player || actor == _sim,
                AllowedDuelPets.Contains(actor),
                principal != null,
                principal != null && target == DuelOpponentOf(principal));
        }

        private static void RememberEngagedPet(Character actor)
        {
            try
            {
                NPC npc = actor == null ? null : actor.MyNPC;
                if (npc != null) EngagedPets.Add(npc);
            }
            catch { }
        }

        private static void ReleaseEngagedPets()
        {
            try
            {
                foreach (NPC npc in EngagedPets)
                {
                    if (npc == null) continue;
                    ClearDuelCombatReferences(npc);
                }
            }
            catch { }
            EngagedPets.Clear();
        }

        private static PeriodicDamageAuthority ResolvePeriodicBleedAuthority(Character target)
        {
            if (target == null || target.MyStats == null || target.MyStats.StatusEffects == null)
                return PeriodicDamageAuthority.DuelVirtual;
            bool duelOwned = false;
            bool worldOwned = false;
            Dictionary<int, Spell> trackedWorld = target == _player ? PlayerWorldEffectSlots : SimWorldEffectSlots;
            StatusEffect[] effects = target.MyStats.StatusEffects;
            for (int i = 0; i < effects.Length; i++)
            {
                StatusEffect slot = effects[i];
                Spell effect = slot == null ? null : slot.Effect;
                if (effect == null) continue;
                bool bleedLike = false;
                try { bleedLike = effect.BleedDamagePercent > 0 || slot.bonusDmg > 0; } catch { }
                if (!bleedLike) continue;

                Spell tracked;
                if (trackedWorld.TryGetValue(i, out tracked) && tracked == effect) worldOwned = true;
                Character owner = null;
                try { owner = slot.Owner != null ? slot.Owner : slot.CreditDPS; } catch { }
                if (DuelPrincipal(owner) != null) duelOwned = true;
                else if (Classify(owner) == CombatActorClass.OutsideHostile) worldOwned = true;
            }
            if (worldOwned && duelOwned) return PeriodicDamageAuthority.Ambiguous;
            if (worldOwned) return PeriodicDamageAuthority.WorldReal;
            return PeriodicDamageAuthority.DuelVirtual;
        }

        private static bool IsUnattributedPeriodicBleed(Character target, Character attacker, string source)
        {
            return attacker == null && target != null && target == _effectTickOwner &&
                   (target == _player || target == _sim) && source != null &&
                   source.IndexOf("BleedDamageMe", StringComparison.Ordinal) >= 0;
        }

        // Let Erenshor calculate an admitted participant hit once so armor, resistances, active
        // buffs, crits and native modifiers remain authoritative. During that exact transaction,
        // the scoped Stats.ReduceHP Prefix captures the final effective reduction and suppresses
        // only the participant's real/mirrored HP write. FinishNativeDamage applies the captured
        // amount once to virtual Duel HP. No synthetic HP headroom enters native calculation.
        //
        // Hostile world -> duelist takes the opposite path: the participant is temporarily exposed
        // at its real-world HP ledger, the native hit is allowed unchanged, and that real result is
        // adopted into the ledger. It is never translated into virtual Duel HP.
        internal static bool PrepareNativeDamage(Character target, Character attacker, int rawDamage, bool fromPlayer,
            ref int result, ref NativeDamageState state, string source)
        {
            if (!Active) return true;
            bool duelHit = IsDuelHit(target, attacker);

            if (!duelHit && IsUnattributedPeriodicBleed(target, attacker, source))
            {
                PeriodicDamageAuthority periodicAuthority = ResolvePeriodicBleedAuthority(target);
                if (periodicAuthority == PeriodicDamageAuthority.Ambiguous)
                {
                    result = 0;
                    _lastDamageDiagnostic = "source=" + SafeLabel(source) +
                        " sourceRole=periodic_ambiguous targetRole=" + ParticipantRole(target) +
                        " authority=blocked virtualized=false reason=mixed_world_and_duel_periodic_sources";
                    DiagnosticRecord("damage_authority " + _lastDamageDiagnostic);
                    Cancel("Periodic.SourceAmbiguous", null, target, target,
                        "Duel cancelled because overlapping periodic effects could not be attributed safely.");
                    return false;
                }
                if (periodicAuthority == PeriodicDamageAuthority.WorldReal)
                {
                    int realHp = RealLedgerHp(target);
                    state = new NativeDamageState
                    {
                        Previous = _nativeDamageInFlight,
                        Target = target,
                        Attacker = null,
                        RealBefore = realHp,
                        NativeBefore = realHp,
                        RawDamage = rawDamage,
                        Source = source,
                        FromPlayer = fromPlayer,
                        WorldReal = true
                    };
                    _nativeDamageInFlight = state;
                    try { target.MyStats.CurrentHP = Math.Max(1, realHp); }
                    catch { _nativeDamageInFlight = state.Previous; state = null; }
                    return true;
                }
            }

            if (!duelHit)
            {
                // Periodic Duel-owned bleeds and explicit containment paths may already claim this
                // call. If not, classify exact source/target authority before deciding whether a
                // world-real transaction is needed.
                if (TryVirtualDamage(target, attacker, rawDamage, ref result, source)) return false;

                CombatActorClass attackerClass = Classify(attacker);
                CombatActorClass targetClass = Classify(target);
                if (IsDuelParticipantClass(targetClass) && attackerClass == CombatActorClass.OutsideHostile)
                {
                    int realHp = RealLedgerHp(target);
                    if (realHp <= 0) return true;
                    state = new NativeDamageState
                    {
                        Previous = _nativeDamageInFlight,
                        Target = target,
                        Attacker = attacker,
                        RealBefore = realHp,
                        NativeBefore = realHp,
                        RawDamage = rawDamage,
                        Source = source,
                        FromPlayer = fromPlayer,
                        WorldReal = true
                    };
                    _nativeDamageInFlight = state;
                    try { target.MyStats.CurrentHP = realHp; }
                    catch
                    {
                        _nativeDamageInFlight = state.Previous;
                        state = null;
                    }
                    return true;
                }
                return true;
            }

            if (attacker != _player && attacker != _sim) RememberEngagedPet(attacker);
            if (_state != DuelLifecycleState.Active)
            {
                result = 0;
                return false;
            }

            bool playerHit = target == _player;
            int virtualBefore = playerHit ? _playerHp : _simHp;
            int nativeBefore = virtualBefore;
            try { if (target.MyStats != null) nativeBefore = target.MyStats.CurrentHP; } catch { }

            state = new NativeDamageState
            {
                Previous = _nativeDamageInFlight,
                Target = target,
                Attacker = attacker,
                VirtualBefore = virtualBefore,
                RealBefore = RealLedgerHp(target),
                NativeBefore = nativeBefore,
                RawDamage = rawDamage,
                Source = source,
                FromPlayer = fromPlayer,
                WorldReal = false
            };
            _nativeDamageInFlight = state;
            try
            {
                state.OriginalLayer = target.gameObject.layer;
                state.LayerCaptured = true;
            }
            catch { }
            try
            {
                Character.Faction neutral = NeutralHitFaction(attacker);
                if (target.MyFaction != neutral)
                {
                    state.OriginalFaction = target.MyFaction;
                    target.MyFaction = neutral;
                    state.FactionChanged = true;
                }
            }
            catch { }
            return true;
        }

        internal static void FinishNativeDamage(NativeDamageState state, int nativeResult)
        {
            if (state == null || state.Target == null) return;
            int nativeAfter = state.NativeBefore;
            try { nativeAfter = state.Target.MyStats.CurrentHP; } catch { }

            if (state.WorldReal)
            {
                if (_standaloneWorldDamageInFlight != null && _standaloneWorldDamageInFlight.Target == state.Target)
                    _standaloneWorldDamageInFlight.NestedNativeWorldDamage = true;
                int before = state.RealBefore;
                int after = Math.Max(0, nativeAfter);
                SetRealLedgerHp(state.Target, after);
                AdoptCurrentRealDamageState(state.Target);
                int realDelta = Math.Max(0, before - after);
                _lastDamageDiagnostic = "source=" + SafeLabel(state.Source) +
                    " sourceRole=hostile_world targetRole=" + ParticipantRole(state.Target) +
                    " nativeAmount=" + realDelta + " authority=real_world virtualized=false" +
                    " realHpBefore=" + before + " realHpAfter=" + after +
                    " virtualScale=0.000 virtualDelta=0 worldDamagePreserved=true realEffectPreserved=true";
                Diagnostic("world_damage " + _lastDamageDiagnostic);
                PopNativeDamageState(state);

                // Native death is authoritative. Do not remirror virtual HP over a dead real actor;
                // normal participant-validity cleanup will end the practice session.
                if (IsAlive(state.Target) && DuelLifecyclePolicy.IsCombatActive(_state)) MirrorVirtualHealth();
                return;
            }

            // A nested callback can terminate the duel before the outer DamageMe postfix runs.
            // Never apply a stale virtual ledger after terminal cleanup has restored real state.
            if (!DuelLifecyclePolicy.IsCombatActive(_state) || (state.Target != _player && state.Target != _sim))
            {
                RestoreNativeHitState(state);
                return;
            }

            int captured = state.CapturedReduceHp ? Math.Max(0, state.CapturedReduceHpDamage) : 0;
            int effective = DuelCombatSemanticsPolicy.EffectiveCapturedDamage(
                state.CapturedReduceHp, captured, nativeResult);
            bool playerHit = state.Target == _player;
            if (playerHit) _playerHp = DuelSafetyPolicy.ApplyVirtualDamageOnce(_playerHp, effective);
            else if (state.Target == _sim) _simHp = DuelSafetyPolicy.ApplyVirtualDamageOnce(_simHp, effective);

            string factionLabel = state.FactionChanged ? state.OriginalFaction + "->duel" : "unchanged";
            RestoreNativeHitState(state);
            MirrorVirtualHealth();
            int hp = playerHit ? _playerHp : _simHp;
            int max = playerHit ? _playerMax : _simMax;
            int mirroredAfter = 0;
            try { mirroredAfter = state.Target.MyStats.CurrentHP; } catch { }
            int realAfter = RealLedgerHp(state.Target);
            _lastDamageDiagnostic = "source=" + SafeLabel(state.Source) +
                " nativeEntry=" + SafeLabel(state.Source) +
                " sourceRole=" + DamageSourceRole(state.Attacker) + " targetRole=" + ParticipantRole(state.Target) +
                " raw=" + state.RawDamage +
                " reduceHpCaptured=" + state.CapturedReduceHp + " capturedEffectiveDamage=" + captured +
                " nativeResult=" + nativeResult + " authority=virtual_duel virtualized=true" +
                " virtualScale=1.000 virtualBefore=" + state.VirtualBefore + " virtualDelta=" + effective +
                " virtualAfter=" + hp + " realHpBefore=" + state.RealBefore + " realHpAfter=" + realAfter +
                " mirroredHpAfter=" + mirroredAfter +
                " realEffectSuppressed=true worldDamagePreserved=false faction=" + factionLabel;
            DiagnosticVirtual("native_damage", state.Source, playerHit, state.RawDamage, effective,
                state.VirtualBefore, hp, max, state.RealBefore, realAfter,
                "reduceHpCaptured=" + state.CapturedReduceHp + " capturedEffectiveDamage=" + captured +
                " virtualScale=1.000 fromPlayer=" + state.FromPlayer + " faction=" + factionLabel);
            if (DuelSafetyPolicy.ReachedYieldThreshold(hp, max, FinishPercent))
            {
                try { Stop(ParticipantLabel(state.Target) + " yields. Friendly duel complete!"); } catch { }
            }
        }

        private static void PopNativeDamageState(NativeDamageState state)
        {
            if (state == null) return;
            if (_nativeDamageInFlight == state) _nativeDamageInFlight = state.Previous;
        }

        // Also reached from Harmony finalizers. It is safe to call twice and safe when the native
        // method threw partway through. Duel hits restore scoped faction/layer ownership; world
        // hits release the scoped real-HP exposure and remirror virtual HP if the actor lives.
        internal static void RestoreNativeHitState(NativeDamageState state)
        {
            if (state == null || state.Target == null) return;
            PopNativeDamageState(state);
            if (state.FactionChanged)
            {
                try { state.Target.MyFaction = state.OriginalFaction; } catch { }
                state.FactionChanged = false;
            }
            if (state.LayerCaptured)
            {
                try { if (state.Target.gameObject.layer != state.OriginalLayer) state.Target.gameObject.layer = state.OriginalLayer; } catch { }
                state.LayerCaptured = false;
            }
            if (DuelLifecyclePolicy.IsCombatActive(_state) && IsAlive(state.Target)) MirrorVirtualHealth();
        }

        // Capture/suppress only the final HP reduction belonging to the exact virtual Duel damage
        // transaction at the top of the thread-local transaction stack. World-real damage and every
        // ordinary Erenshor ReduceHP call pass through unchanged. This is the live-proven 0.4.1
        // semantic boundary, modernized with the current nested NativeDamageState stack.
        internal static bool CaptureNativeReduceHp(Stats stats, int damage, ref bool result)
        {
            NativeDamageState state = _nativeDamageInFlight;
            bool exactStats = state != null && stats != null && state.Target != null && state.Target.MyStats == stats;
            bool combatActive = DuelLifecyclePolicy.IsCombatActive(_state);
            bool exactDuelEdge = state != null && IsDuelHit(state.Target, state.Attacker);
            if (!DuelCombatSemanticsPolicy.ShouldCaptureReduceHp(
                state != null, state != null && state.WorldReal, exactStats, combatActive, exactDuelEdge))
                return true;

            state.CapturedReduceHpDamage = damage;
            state.CapturedReduceHp = true;
            result = false;
            return false;
        }

        internal static bool TryVirtualDamage(Character target, Character attacker, int damage, ref int result, string eventSource)
        {
            if (!Active || target == null || damage <= 0) return false;
            bool duelHit = IsDuelHit(target, attacker);
            if (duelHit)
            {
                if (_state != DuelLifecycleState.Active)
                {
                    result = 0;
                    return true;
                }
                ApplyVirtualDamage(target, damage, eventSource, "direct=true");
                result = damage;
                return true;
            }

            // Stats.TickEffects invokes BleedDamageMe with a null attacker. The active effect-tick
            // owner proves this is a periodic hit on the duelist, but Erenshor's API does not retain
            // the original caster on that bleed path. Keep a verified Duel-owned tick virtual.
            if (IsUnattributedPeriodicBleed(target, attacker, eventSource))
            {
                PeriodicDamageAuthority periodicAuthority = ResolvePeriodicBleedAuthority(target);
                if (periodicAuthority == PeriodicDamageAuthority.DuelVirtual)
                {
                    if (_state != DuelLifecycleState.Active) { result = 0; return true; }
                    ApplyVirtualDamage(target, damage, eventSource, "periodic_bleed_unattributed=true authority=duel_virtual");
                    result = damage;
                    return true;
                }
                // WorldReal is handled by PrepareNativeDamage so real HP can be exposed around the
                // native call. Ambiguous is cancelled/suppressed there before reaching this path.
                return false;
            }

            Character principal = DuelPrincipal(attacker);
            CombatActorClass attackerClass = Classify(attacker);
            CombatActorClass targetClass = Classify(target);
            bool targetIsDuelist = IsDuelParticipantClass(targetClass);
            bool targetIsWorldHostile = targetClass == CombatActorClass.OutsideHostile;
            bool sourceWorldHostile = attackerClass == CombatActorClass.OutsideHostile;
            bool sourceFriendlyOrProtected = IsFriendlyPartyClass(attackerClass) ||
                                             attackerClass == CombatActorClass.ProtectedNonParticipant;
            bool sourceUnknown = attackerClass == CombatActorClass.Unknown;
            DuelDamageAuthority authority = DuelCombatSemanticsPolicy.ResolveDamageAuthority(
                principal != null, targetIsDuelist, sourceWorldHostile, targetIsWorldHostile,
                sourceFriendlyOrProtected, sourceUnknown);

            if (authority == DuelDamageAuthority.VirtualDuel)
            {
                if (_state != DuelLifecycleState.Active) { result = 0; return true; }
                ApplyVirtualDamage(target, damage, eventSource, "authority=virtual_duel");
                result = damage;
                return true;
            }

            if (authority == DuelDamageAuthority.RealWorld)
            {
                _lastDamageDiagnostic = "source=" + SafeLabel(eventSource) +
                    " sourceRole=" + DamageSourceRole(attacker) + " targetRole=" + DamageTargetRole(target) +
                    " raw=" + damage + " authority=real_world virtualized=false realEffectPreserved=pending_native";
                DiagnosticRecord("damage_authority " + _lastDamageDiagnostic);
                return false;
            }

            if (authority == DuelDamageAuthority.Block)
            {
                result = 0;
                _lastDamageDiagnostic = "source=" + SafeLabel(eventSource) +
                    " sourceRole=" + DamageSourceRole(attacker) + " targetRole=" + DamageTargetRole(target) +
                    " raw=" + damage + " authority=blocked virtualized=false realEffectSuppressed=true";
                DiagnosticRecord("damage_authority " + _lastDamageDiagnostic);
                if (principal != null && !targetIsDuelist && !targetIsWorldHostile)
                    NotifyUnsafeAreaBystander();
                return true;
            }

            return false;
        }

        private static void ApplyVirtualDamage(Character target, int damage, string eventSource, string reason)
        {
            bool playerHit = target == _player;
            int hpBefore = playerHit ? _playerHp : _simHp;
            int realBefore = RealLedgerHp(target);
            int effective = Math.Max(0, damage);
            if (playerHit) _playerHp = DuelSafetyPolicy.ApplyVirtualDamageOnce(_playerHp, effective);
            else if (target == _sim) _simHp = DuelSafetyPolicy.ApplyVirtualDamageOnce(_simHp, effective);
            else return;
            MirrorVirtualHealth();
            int hp = playerHit ? _playerHp : _simHp;
            int max = playerHit ? _playerMax : _simMax;
            int realAfter = RealLedgerHp(target);
            _lastDamageDiagnostic = "source=" + SafeLabel(eventSource) +
                " nativeEntry=" + SafeLabel(eventSource) +
                " sourceRole=direct_contained targetRole=" + ParticipantRole(target) +
                " raw=" + damage + " reduceHpCaptured=false capturedEffectiveDamage=0" +
                " nativeAmount=" + effective + " authority=virtual_duel virtualized=true" +
                " virtualScale=1.000 virtualBefore=" + hpBefore + " virtualDelta=" + effective +
                " virtualAfter=" + hp + " realHpBefore=" + realBefore + " realHpAfter=" + realAfter +
                " realEffectSuppressed=true worldDamagePreserved=false reason=" + SafeLabel(reason);
            DiagnosticVirtual("virtual_damage", eventSource, playerHit, damage, effective, hpBefore, hp, max,
                realBefore, realAfter, reason);
            if (DuelSafetyPolicy.ReachedYieldThreshold(hp, max, FinishPercent))
            {
                try { Stop(ParticipantLabel(target) + " yields. Friendly duel complete!"); }
                catch { }
            }
        }

        internal static bool HandleSelfDamage(Character target, int amount, ref int result, string eventSource)
        {
            if (!Active || (target != _player && target != _sim)) return true;
            if (_state != DuelLifecycleState.Active) { result = 0; return false; }
            if (amount > 0)
            {
                ApplyVirtualDamage(target, amount, eventSource, "self_damage=true");
                result = amount;
                return false;
            }
            if (amount < 0)
            {
                int before = target == _player ? _playerHp : _simHp;
                int maximum = target == _player ? _playerMax : _simMax;
                int after = Math.Min(maximum, before - amount);
                if (target == _player) _playerHp = after; else _simHp = after;
                MirrorVirtualHealth();
                DiagnosticVirtual("virtual_heal", eventSource, target == _player, -amount, -amount, before, after,
                    maximum, before, after, "self_damage_negative_heal=true");
                result = amount;
                return false;
            }
            result = 0;
            return false;
        }

        internal static bool HandleEnvironmentalDamage(Character target)
        {
            if (!Active || (target != _player && target != _sim)) return true;
            // A hazard is external world damage, not part of the friendly match. Restore the real
            // state first, then let Erenshor apply the environmental hit normally.
            Cancel("Harmony.Character.EnvironmentalDamageMe", null, target, target,
                "Duel cancelled because a duelist took environmental damage.");
            return true;
        }

        internal static bool BeginDamageShield(Character target, int damage, Stats shieldOwner, ref StandaloneWorldDamageState state)
        {
            if (!Active || target == null || shieldOwner == null) return true;
            Character attacker = null;
            try { attacker = shieldOwner.Myself; } catch { }

            int ignored = 0;
            if (TryVirtualDamage(target, attacker, damage, ref ignored, "Harmony.Character.DamageShieldTaken"))
                return false;

            if (IsDuelParticipantClass(Classify(target)) && Classify(attacker) == CombatActorClass.OutsideHostile)
            {
                int realHp = RealLedgerHp(target);
                state = new StandaloneWorldDamageState
                {
                    Target = target,
                    RealBefore = realHp,
                    Source = "Harmony.Character.DamageShieldTaken"
                };
                _standaloneWorldDamageInFlight = state;
                try { if (target.MyStats != null) target.MyStats.CurrentHP = Math.Max(1, realHp); } catch { }
            }
            return true;
        }

        internal static void FinishDamageShield(StandaloneWorldDamageState state)
        {
            if (state == null || state.Completed || state.Target == null) return;
            state.Completed = true;
            if (_standaloneWorldDamageInFlight == state) _standaloneWorldDamageInFlight = null;

            if (!state.NestedNativeWorldDamage)
            {
                int after = state.RealBefore;
                try { if (state.Target.MyStats != null) after = Math.Max(0, state.Target.MyStats.CurrentHP); } catch { }
                SetRealLedgerHp(state.Target, after);
                AdoptCurrentRealDamageState(state.Target);
                _lastDamageDiagnostic = "source=" + SafeLabel(state.Source) +
                    " nativeEntry=" + SafeLabel(state.Source) +
                    " sourceRole=hostile_world targetRole=" + ParticipantRole(state.Target) +
                    " raw=unavailable nativeAmount=" + Math.Max(0, state.RealBefore - after) +
                    " reduceHpCaptured=false capturedEffectiveDamage=0" +
                    " authority=real_world virtualized=false realHpBefore=" + state.RealBefore +
                    " realHpAfter=" + after + " virtualScale=0.000 virtualDelta=0" +
                    " worldDamagePreserved=true realEffectPreserved=true standalone=true";
                DiagnosticRecord("world_damage " + _lastDamageDiagnostic);
            }
            if (DuelLifecyclePolicy.IsCombatActive(_state) && IsAlive(state.Target)) MirrorVirtualHealth();
        }

        internal static bool AllowAggro(NPC npc, Character target, string eventSource)
        {
            if (!Active || npc == null || target == null) return true;
            CombatActorClass actorClass = Classify(npc);
            CombatActorClass targetClass = Classify(target);

            if (actorClass == CombatActorClass.DuelParticipant)
            {
                Character opponent = DuelOpponentFor(npc);
                if (_state == DuelLifecycleState.Active && target == opponent)
                {
                    npc.CurrentAggroTarget = opponent;
                    return false; // Duel owns the exact participant<->participant target edge.
                }
                if (targetClass == CombatActorClass.OutsideHostile)
                {
                    DiagnosticRecord("world_aggro source=duelist target=hostile_world preserved=true");
                    return true;
                }
                return false;
            }

            if (IsFriendlyPartyClass(actorClass))
            {
                if (!IsDuelParticipantClass(targetClass)) return true;
                Character actor = NpcCharacter(npc);
                if (!IsAdmittedPetEngagement(actor, target)) return false;
                RememberEngagedPet(actor);
                return true;
            }

            if (IsDuelParticipantClass(targetClass))
            {
                if (actorClass == CombatActorClass.OutsideHostile)
                {
                    DiagnosticRecord("world_aggro source=hostile_world target=duelist preserved=true sourceHook=" + SafeLabel(eventSource));
                    return true;
                }
                // Friendly/protected/unknown nonparticipants may not join the duel through aggro.
                return false;
            }

            return true;
        }

        internal static bool AllowManageAggro(NPC npc, Character attacker, string eventSource)
        {
            if (!Active || npc == null || attacker == null) return true;
            CombatActorClass recipientClass = Classify(npc);
            CombatActorClass attackerClass = Classify(attacker);

            if (recipientClass == CombatActorClass.DuelParticipant)
            {
                if (attackerClass == CombatActorClass.OutsideHostile)
                {
                    DiagnosticRecord("world_threat source=hostile_world target=duelist preserved=true sourceHook=" + SafeLabel(eventSource));
                    return true;
                }
                if (IsFriendlyPartyClass(attackerClass) || attackerClass == CombatActorClass.ProtectedNonParticipant ||
                    attackerClass == CombatActorClass.Unknown) return false;
            }

            if (IsDuelParticipantClass(attackerClass))
            {
                if (recipientClass == CombatActorClass.OutsideHostile)
                {
                    DiagnosticRecord("world_threat source=duelist target=hostile_world preserved=true sourceHook=" + SafeLabel(eventSource));
                    return true;
                }
                return false;
            }

            return true;
        }

        internal static bool IsDuelingSim(SimPlayer sim) { return Active && sim != null && (sim == _simPlayer || sim == _firstSimPlayer); }
        internal static bool IsDuelingNpc(NPC npc) { return Active && npc != null && (npc == _simNpc || npc == _firstSimNpc); }
        internal static bool AllowCombatAction(NPC npc)
        {
            if (!Active || npc == null) return true;
            CombatActorClass actorClass = Classify(npc);
            if (actorClass == CombatActorClass.DuelParticipant)
            {
                if (_state != DuelLifecycleState.Active) return false;
                Character current = npc.CurrentAggroTarget;
                if (current == DuelOpponentFor(npc)) return true;
                // If a real hostile world NPC joined the fight, the dueling Sim's native response
                // remains normal PvE and is not forced through the virtual duel path.
                return Classify(current) == CombatActorClass.OutsideHostile;
            }
            if (!IsFriendlyPartyClass(actorClass)) return true;
            Character currentTarget = npc.CurrentAggroTarget;
            if (!IsDuelParticipantClass(Classify(currentTarget))) return true;
            // An admitted pet fights the opposing duelist with its own attack spells and skills;
            // its damage lands on that duelist's virtual ledger like any other duel hit.
            return IsAdmittedPetEngagement(NpcCharacter(npc), currentTarget);
        }

        internal sealed class SpellStartState
        {
            internal Stats CasterStats;
            internal int ManaBefore;
            internal bool Allowed;
            internal bool Completed;
            internal string Source;
        }

        internal static bool BeginSpellStart(CastSpell caster, Spell spell, Stats target, ref bool result,
            string eventSource, ref SpellStartState state)
        {
            bool allow = AllowSpellStart(caster, spell, target, ref result, eventSource);
            if (Active && caster != null && IsDuelParticipantClass(Classify(caster.MyChar)))
            {
                Stats stats = null;
                int mana = -1;
                try
                {
                    stats = caster.MyChar == null ? null : caster.MyChar.MyStats;
                    if (stats != null) mana = stats.CurrentMana;
                }
                catch { }
                state = new SpellStartState
                {
                    CasterStats = stats,
                    ManaBefore = mana,
                    Allowed = allow,
                    Source = eventSource
                };
            }
            return allow;
        }

        internal static void FinishSpellStart(SpellStartState state, bool nativeResult)
        {
            if (state == null || state.Completed) return;
            state.Completed = true;
            int manaAfter = -1;
            try { if (state.CasterStats != null) manaAfter = state.CasterStats.CurrentMana; } catch { }
            bool resourceCommitted = state.ManaBefore >= 0 && manaAfter >= 0 && manaAfter < state.ManaBefore;
            _lastSpellAdmission +=
                " startSpellEntered=" + state.Allowed +
                " nativeResult=" + nativeResult +
                " manaBefore=" + state.ManaBefore +
                " manaAfter=" + manaAfter +
                " resourceCommitted=" + resourceCommitted +
                " cooldownCommitted=unavailable_in_supplied_api";
            DiagnosticRecord("spell_commit " + _lastSpellAdmission);
        }

        internal static bool AllowSpellStart(CastSpell caster, Spell spell, Stats target, ref bool result, string eventSource)
        {
            if (!Active || caster == null) return true;
            CombatActorClass casterClass = Classify(caster.MyChar);
            CombatActorClass targetClass = target == null ? CombatActorClass.Unknown : Classify(target.Myself);

            if (IsDuelParticipantClass(casterClass))
            {
                if (_state != DuelLifecycleState.Active)
                    return RecordSpellAdmission(spell, casterClass, targetClass, false, false,
                        "blocked", "lifecycle_not_active", eventSource, BlockSpell(ref result));

                Character casterCharacter = caster.MyChar;
                Character targetCharacter = target == null ? null : target.Myself;
                bool declaresSelfApplication = DeclaresSelfApplication(spell);

                // Area shape must be decided before the self-target shortcut. Native PBAE/group
                // spells commonly pass the caster (or an unrelated selected Stats) even though the
                // eventual per-target effect set is larger. Admit a structurally containable area
                // cast only after bounded native-candidate preflight; every actual per-target
                // damage/heal/status edge is still independently authorized below.
                if (IsAreaSpell(spell))
                {
                    string areaReason;
                    bool areaAllowed = PreflightAreaSpell(casterCharacter, spell, targetCharacter, out areaReason);
                    if (!areaAllowed && areaReason.IndexOf("bystander", StringComparison.OrdinalIgnoreCase) >= 0)
                        NotifyUnsafeAreaBystander();
                    return RecordSpellAdmission(spell, casterClass, targetClass, declaresSelfApplication,
                        targetCharacter == casterCharacter, areaAllowed ? "allowed" : "blocked",
                        areaAllowed ? "area_per_target_containment" : areaReason, eventSource,
                        areaAllowed || BlockSpell(ref result));
                }

                // The Stats handed to StartSpell is NOT a statement of who the spell will affect.
                // Installed Assembly-CSharp Hotkeys::DoHotkeyTask can pass CurrentTarget for a
                // SelfOnly spell; native StartSpell later resolves SelfOnly/ApplyToCaster/
                // InflictOnSelf onto the caster. Preserve that declared self-application model.
                bool selfCast = DuelSpellAdmissionPolicy.IsSelfCast(targetCharacter == casterCharacter, declaresSelfApplication);
                if (selfCast)
                {
                    bool selfContained = IsSelfContainedDuelCast(spell);
                    return RecordSpellAdmission(spell, casterClass, targetClass, declaresSelfApplication, true,
                        selfContained ? "allowed" : "blocked",
                        selfContained ? "self_contained_self_cast" : "self_cast_not_containable",
                        eventSource, selfContained || BlockSpell(ref result));
                }

                if (IsDuelParticipantClass(targetClass))
                {
                    bool safeOffense = IsSafeDuelOffense(spell);
                    return RecordSpellAdmission(spell, casterClass, targetClass, declaresSelfApplication, false,
                        safeOffense ? "allowed" : "blocked",
                        safeOffense ? "duel_offense" : "not_safe_duel_offense",
                        eventSource, safeOffense || BlockSpell(ref result));
                }

                // The participant may attack a verified ordinary hostile NPC with its normal kit.
                // This is real Erenshor combat, not virtual duel damage. Protected/ambiguous third
                // actors remain fail-closed.
                if (targetClass == CombatActorClass.OutsideHostile && IsSafeDuelOffense(spell))
                    return RecordSpellAdmission(spell, casterClass, targetClass, declaresSelfApplication, false,
                        "allowed", "hostile_world_real_offense", eventSource, true);

                if ((targetClass == CombatActorClass.ProtectedNonParticipant || targetClass == CombatActorClass.Unknown) &&
                    IsKnownOffensivePayload(spell))
                    NotifyUnsafeAreaBystander();
                return RecordSpellAdmission(spell, casterClass, targetClass, declaresSelfApplication, false,
                    "blocked", "participant_cast_at_protected_or_unknown_actor", eventSource, BlockSpell(ref result));
            }

            if (IsFriendlyPartyClass(casterClass))
            {
                if (!IsDuelParticipantClass(targetClass)) return true;
                Character petCaster = caster.MyChar;
                Character petTarget = target == null ? null : target.Myself;
                if (IsAdmittedPetEngagement(petCaster, petTarget) && IsSafeDuelOffense(spell))
                {
                    RememberEngagedPet(petCaster);
                    return true;
                }
                return BlockSpell(ref result);
            }

            // Verified hostile-world casts are real native PvE even while a duel is active. Their
            // resulting damage/effects are kept in the separate real ledger and never virtualized.
            if (IsDuelParticipantClass(targetClass) && casterClass == CombatActorClass.OutsideHostile)
                return true;

            if (IsDuelParticipantClass(targetClass) &&
                (casterClass == CombatActorClass.ProtectedNonParticipant || casterClass == CombatActorClass.Unknown))
                return BlockSpell(ref result);

            return true;
        }

        private static bool BlockSpell(ref bool result)
        {
            result = false;
            return false;
        }

        private static bool BlockStatusEffect(ref int result)
        {
            result = 0;
            return false;
        }

        // Walks the spell -> StatusEffectToApply chain looking for shapes that reach past the single
        // target the duel is willing to expose: a group-wide effect, a pet summon, or a charm. Procs
        // are only tolerated on a duelist's own self-cast (a weapon proc buff is ordinary class kit);
        // handing one to the opponent would launch arbitrary follow-up spells from inside the match.
        private static bool StaysOnOneTarget(Spell spell, bool allowProc)
        {
            Spell current = spell;
            for (int depth = 0; current != null && depth < 8; depth++)
            {
                try
                {
                    if (!DuelSpellAdmissionPolicy.StaysOnOneTarget(current.GroupEffect, current.PetToSummon != null,
                            current.CharmTarget, current.AddProc != null, allowProc)) return false;
                    Spell next = current.StatusEffectToApply;
                    if (next == null || next == current) return true;
                    current = next;
                }
                catch { return false; }
            }
            return true;
        }

        private static bool IsAreaSpell(Spell spell)
        {
            if (spell == null) return false;
            try
            {
                return DuelSpellAdmissionPolicy.IsAreaShape(
                    spell.GroupEffect, spell.Type == Spell.SpellType.AE, spell.Type == Spell.SpellType.PBAE);
            }
            catch { return false; }
        }

        private static bool IsAreaStructurallyContainable(Spell spell)
        {
            Spell current = spell;
            for (int depth = 0; current != null && depth < 8; depth++)
            {
                try
                {
                    if (!DuelSpellAdmissionPolicy.IsAreaStructurallyContainable(
                        current.PetToSummon != null, current.CharmTarget, current.AddProc != null)) return false;
                    Spell next = current.StatusEffectToApply;
                    if (next == null || next == current) return true;
                    current = next;
                }
                catch { return false; }
            }
            return current == null;
        }

        private static bool IsKnownOffensivePayload(Spell spell)
        {
            Spell current = spell;
            for (int depth = 0; current != null && depth < 8; depth++)
            {
                try
                {
                    if (current.TargetDamage > 0 || current.BleedDamagePercent > 0 || current.Lifetap ||
                        current.Aggro > 0 || current.RootTarget || current.StunTarget || current.FearTarget ||
                        current.CrowdControlSpell || current.JoltSpell || current.TauntSpell ||
                        current.Type == Spell.SpellType.Damage || current.Type == Spell.SpellType.StatusEffect)
                        return true;
                    if ((current.Type == Spell.SpellType.AE || current.Type == Spell.SpellType.PBAE) &&
                        current.TargetHealing <= 0 && current.PercentManaRestoration <= 0) return true;
                    Spell next = current.StatusEffectToApply;
                    if (next == null || next == current) break;
                    current = next;
                }
                catch { return false; }
            }
            return false;
        }

        private static bool IsKnownBeneficialPayload(Spell spell)
        {
            Spell current = spell;
            for (int depth = 0; current != null && depth < 8; depth++)
            {
                try
                {
                    if (current.TargetHealing > 0 || current.PercentManaRestoration > 0 ||
                        current.Type == Spell.SpellType.Heal || current.Type == Spell.SpellType.Beneficial) return true;
                    Spell next = current.StatusEffectToApply;
                    if (next == null || next == current) break;
                    current = next;
                }
                catch { return false; }
            }
            return false;
        }

        private static bool PreflightAreaSpell(Character caster, Spell spell, Character passedTarget, out string rejectReason)
        {
            rejectReason = "area_unknown";
            bool offensive = IsKnownOffensivePayload(spell);
            bool beneficial = IsKnownBeneficialPayload(spell);
            bool structural = IsAreaStructurallyContainable(spell);
            bool allow = structural && DuelSpellAdmissionPolicy.CanAdmitArea(
                offensive, beneficial, false, false, false, true);

            HashSet<Character> candidates = new HashSet<Character>();
            if (passedTarget != null) candidates.Add(passedTarget);
            try
            {
                if (caster != null && offensive && caster.NearbyEnemies != null)
                    for (int i = 0; i < caster.NearbyEnemies.Count && candidates.Count < 48; i++)
                        if (caster.NearbyEnemies[i] != null) candidates.Add(caster.NearbyEnemies[i]);
                if (caster != null && beneficial && caster.NearbyFriends != null)
                    for (int i = 0; i < caster.NearbyFriends.Count && candidates.Count < 48; i++)
                        if (caster.NearbyFriends[i] != null) candidates.Add(caster.NearbyFriends[i]);
            }
            catch { }

            int participants = 0, hostileWorld = 0, protectedActors = 0, unknown = 0, friendly = 0;
            foreach (Character candidate in candidates)
            {
                CombatActorClass c = Classify(candidate);
                if (IsDuelParticipantClass(c)) participants++;
                else if (c == CombatActorClass.OutsideHostile) hostileWorld++;
                else if (c == CombatActorClass.ProtectedNonParticipant) protectedActors++;
                else if (c == CombatActorClass.GroupedLocalSim || c == CombatActorClass.GroupedSimOwnedPet) friendly++;
                else unknown++;
            }

            // For offensive areas, native NearbyEnemies is the best bounded candidate collection
            // available in the supplied current source. A protected/ambiguous actor in that native
            // enemy-candidate set is conservatively rejected before StartSpell. Friendly candidates
            // are not inferred to be blast targets merely from NearbyFriends; if native per-target
            // resolution nevertheless reaches them, the damage/effect hook blocks that exact edge.
            bool unsafeOffensiveCandidate = offensive && (protectedActors > 0 || unknown > 0);
            if (unsafeOffensiveCandidate) allow = false;

            string shape = "GroupEffect";
            try
            {
                if (spell.Type == Spell.SpellType.PBAE) shape = "PBAE";
                else if (spell.Type == Spell.SpellType.AE) shape = "AE";
                else if (!spell.GroupEffect) shape = spell.Type.ToString();
            }
            catch { }
            _lastAoeDiagnostic = "spell=" + SafeLabel(spell == null ? null : spell.SpellName) +
                " shape=" + shape + " radius=unavailable_in_supplied_api" +
                " offensive=" + offensive + " beneficial=" + beneficial + " structural=" + structural +
                " candidates=" + candidates.Count + " participants=" + participants +
                " hostileWorld=" + hostileWorld + " friendly=" + friendly +
                " protected=" + protectedActors + " unknown=" + unknown +
                " hostileWorldAllowed=true perTargetContainment=true admission=" + (allow ? "allowed" : "blocked");
            DiagnosticRecord("aoe_preflight " + _lastAoeDiagnostic);

            if (!structural) rejectReason = "area_uncontainable_pet_charm_or_proc";
            else if (!offensive && !beneficial) rejectReason = "area_payload_unknown";
            else if (unsafeOffensiveCandidate) rejectReason = "area_bystander_in_native_candidate_set";
            else rejectReason = allow ? "area_per_target_containment" : "area_not_admitted";
            return allow;
        }

        // Bounded, privacy-safe record of the most recent duel spell-admission decision. One line per
        // decision (not per frame), holding only spell shape and role tokens - never player identity,
        // save data, or paths. Surfaced through /eduel diag so a live "dead button" can be explained
        // without guessing at which stage it died.
        private static string _lastSpellAdmission = "none";
        private static string _lastDamageDiagnostic = "none";
        private static string _lastAoeDiagnostic = "none";
        private static float _lastBystanderMessageAt = -1000f;

        internal static string LastSpellAdmission { get { return _lastSpellAdmission; } }

        private static bool RecordSpellAdmission(Spell spell, CombatActorClass casterClass, CombatActorClass targetClass,
            bool declaresSelfApplication, bool computedSelfCast, string admission, string stage, string eventSource, bool allow)
        {
            try
            {
                string name = "unknown";
                string type = "unknown";
                bool selfOnly = false, applyToCaster = false, inflictOnSelf = false, groupEffect = false;
                int targetHealing = 0, targetDamage = 0;
                bool pet = false, charm = false;
                if (spell != null)
                {
                    try { name = string.IsNullOrEmpty(spell.SpellName) ? spell.name : spell.SpellName; } catch { }
                    try { type = spell.Type.ToString(); } catch { }
                    try { selfOnly = spell.SelfOnly; } catch { }
                    try { applyToCaster = spell.ApplyToCaster; } catch { }
                    try { inflictOnSelf = spell.InflictOnSelf; } catch { }
                    try { groupEffect = spell.GroupEffect; } catch { }
                    try { targetHealing = spell.TargetHealing; } catch { }
                    try { targetDamage = spell.TargetDamage; } catch { }
                    try { pet = spell.PetToSummon != null; } catch { }
                    try { charm = spell.CharmTarget; } catch { }
                }
                _lastSpellAdmission =
                    "spell=" + SafeLabel(name) +
                    " nativeType=" + type +
                    " caster=" + casterClass +
                    " targetArg=" + targetClass +
                    " selfOnly=" + selfOnly +
                    " applyToCaster=" + applyToCaster +
                    " inflictOnSelf=" + inflictOnSelf +
                    " declaresSelfApplication=" + declaresSelfApplication +
                    " computedSelfCast=" + computedSelfCast +
                    " targetHealing=" + targetHealing +
                    " targetDamage=" + targetDamage +
                    " groupEffect=" + groupEffect +
                    " petSummon=" + pet +
                    " charm=" + charm +
                    " admission=" + admission +
                    " stage=" + stage +
                    " startSpellAllowedToRun=" + allow +
                    " source=" + (eventSource ?? "unknown");
                DiagnosticRecord("spell_admission " + _lastSpellAdmission);
            }
            catch { }
            return allow;
        }

        // Does the spell itself declare that native resolution applies it to the caster? This is a
        // property of the spell asset, independent of whatever Stats the caller happened to pass -
        // see the DoHotkeyTask note in AllowSpellStart. Recognizing self-application is NOT the same
        // as admitting the cast: an admitted self-cast must still clear IsSelfContainedDuelCast, so
        // group effects, pet summons and charms stay blocked exactly as before.
        internal static bool DeclaresSelfApplication(Spell spell)
        {
            if (spell == null) return false;
            try { return DuelSpellAdmissionPolicy.DeclaresSelfApplication(spell.SelfOnly, spell.ApplyToCaster, spell.InflictOnSelf); }
            catch { return false; }
        }

        private static bool IsSelfContainedDuelCast(Spell spell)
        {
            return spell == null || StaysOnOneTarget(spell, true);
        }

        // A cast from one duelist at the other is admissible when it is confined to that one target
        // and is not a gift.
        //
        // The previous test was a whitelist of damage and crowd-control fields, so every *pure*
        // debuff failed it: resist debuffs (NPC.CheckResistDebuffs), snares (NPC.CheckSnareSpell),
        // and stat/attack-speed debuffs carry no TargetDamage and none of the CC booleans, so they
        // were refused at both StartSpell and AddStatusEffect. That left debuff-dependent classes
        // with no working kit in a duel. Spell.Type is the game's own classification, so key off it
        // and deny the shapes that escape the 1v1 or benefit the target.
        private static bool IsSafeDuelOffense(Spell spell)
        {
            if (spell == null) return false;
            if (IsAreaSpell(spell))
                return IsAreaStructurallyContainable(spell) && IsKnownOffensivePayload(spell);
            if (!StaysOnOneTarget(spell, false)) return false;
            try
            {
                if (spell.TargetHealing > 0) return false;
                switch (spell.Type)
                {
                    case Spell.SpellType.Damage:
                    case Spell.SpellType.StatusEffect:
                        // Direct damage, DoTs, roots, stuns, fears, snares, resist and stat debuffs.
                        return true;
                    case Spell.SpellType.Misc:
                        // Misc is the unclassified bucket. Admit only the shapes the old whitelist
                        // had already proven safe rather than opening it wholesale.
                        return spell.TargetDamage > 0 || spell.BleedDamagePercent > 0 || spell.Lifetap ||
                               spell.Aggro > 0 || spell.RootTarget || spell.StunTarget || spell.FearTarget ||
                               spell.CrowdControlSpell || spell.JoltSpell || spell.TauntSpell;
                    default:
                        // Beneficial, Heal, Pet, and the AE/PBAE area types.
                        return false;
                }
            }
            catch { return false; }
        }

        internal sealed class HealCapture
        {
            internal Stats Target;
            internal int Before;
            internal bool Track;
        }

        internal sealed class StatusEffectIngressState
        {
            internal Character Target;
            internal bool PreserveWorldReal;
            internal bool Completed;
            internal int RealBefore;
            internal Spell Spell;
        }

        internal sealed class HealEvaluationState
        {
            internal readonly List<Stats> Stats = new List<Stats>();
            internal readonly List<int> Health = new List<int>();
        }

        // Duel damage is mirrored into the real CurrentHP so the dueling Sim's own healer AI can see
        // it. The side effect is that every *other* healer in range sees two badly hurt party
        // members and starts casting at them. Their casts are refused at the StartSpell/HealMe
        // boundary, but the AI keeps re-selecting the duelists every tick, which is what shows up
        // in-game as bystanders spamming heals and buffs at the duel. Hide the relevant injuries for
        // the duration of the selection pass instead:
        //   - the dueling Sim sees only its own injury, so it can still self-heal;
        //   - everyone else sees both duelists at full health, so they never pick them at all.
        internal static void BeginDuelistHealEvaluation(NPC npc, ref HealEvaluationState state)
        {
            if (!Active || _state != DuelLifecycleState.Active || npc == null) return;
            state = new HealEvaluationState();
            if (!IsDuelingNpc(npc))
            {
                HideInjuryDuringAiEvaluation(_player == null ? null : _player.MyStats, state);
                HideInjuryDuringAiEvaluation(_sim == null ? null : _sim.MyStats, state);
                return;
            }

            Character self = NpcCharacter(npc);
            Character opponent = self == _player ? _sim : _player;
            HideInjuryDuringAiEvaluation(opponent == null ? null : opponent.MyStats, state);
            try
            {
                SimPlayerTracking[] members = GameData.GroupMembers;
                if (members == null) return;
                for (int i = 0; i < members.Length; i++)
                {
                    Stats stats = members[i] == null ? null : members[i].MyStats;
                    if (stats == null || stats.Myself == self) continue;
                    HideInjuryDuringAiEvaluation(stats, state);
                }
            }
            catch { }
        }

        private static void HideInjuryDuringAiEvaluation(Stats stats, HealEvaluationState state)
        {
            if (stats == null || state == null || state.Stats.Contains(stats)) return;
            state.Stats.Add(stats);
            state.Health.Add(stats.CurrentHP);
            stats.CurrentHP = Math.Max(1, stats.CurrentMaxHP);
        }

        internal static void FinishDuelistHealEvaluation(HealEvaluationState state)
        {
            if (state == null) return;
            for (int i = 0; i < state.Stats.Count && i < state.Health.Count; i++)
            {
                try { if (state.Stats[i] != null) state.Stats[i].CurrentHP = state.Health[i]; } catch { }
            }
            MirrorVirtualHealth();
        }

        internal sealed class BuffEvaluationState
        {
            internal Character Owner;
            internal List<Character> Saved;
        }

        // NPC.CheckBuffs picks buff targets by walking Character.NearbyFriends and casting at any
        // friend missing the buff (Spell.Type == Beneficial). Both duelists sit in every bystander's
        // NearbyFriends, so every buffing Sim in range re-selected them on every AI tick. The casts
        // were already refused at StartSpell, but the selection kept repeating -- which in game looks
        // exactly like bystanders buffing the duel. Take the duelists out of the candidate list for
        // the duration of the pass instead, so the AI never picks them and moves on to real targets.
        //
        // This also covers the dueling Sim itself: its own self-buff branch targets MyStats directly
        // and is unaffected, but its NearbyFriends walk would otherwise let it buff its opponent.
        internal static void BeginBuffEvaluation(NPC npc, ref BuffEvaluationState state)
        {
            if (!Active || npc == null) return;
            try
            {
                Character owner = NpcCharacter(npc);
                if (owner == null || owner.NearbyFriends == null) return;
                if (!owner.NearbyFriends.Contains(_player) && !owner.NearbyFriends.Contains(_sim)) return;
                state = new BuffEvaluationState { Owner = owner, Saved = new List<Character>(owner.NearbyFriends) };
                owner.NearbyFriends.RemoveAll(IsDuelist);
            }
            catch { state = null; }
        }

        internal static void FinishBuffEvaluation(BuffEvaluationState state)
        {
            if (state == null || state.Owner == null || state.Saved == null) return;
            try
            {
                // Restore by content rather than re-adding the duelists: the game may legitimately
                // have added or dropped other friends during the pass, so rebuild from the snapshot
                // only if the surviving entries still match it.
                List<Character> live = state.Owner.NearbyFriends;
                if (live == null) return;
                for (int i = 0; i < state.Saved.Count; i++)
                {
                    Character saved = state.Saved[i];
                    if (IsDuelist(saved) && !live.Contains(saved)) live.Insert(Math.Min(i, live.Count), saved);
                }
            }
            catch { }
        }

        private static bool IsDuelist(Character actor)
        {
            return actor != null && (actor == _player || actor == _sim);
        }

        internal static bool BeginSimpleHeal(Stats target, ref HealCapture state)
        {
            if (!Active || target == null) return true;
            CombatActorClass targetClass = Classify(target.Myself);
            CombatActorClass tickOwnerClass = Classify(_effectTickOwner);
            if (IsDuelParticipantClass(targetClass) && _effectTickOwner != null && _effectTickOwner != target.Myself)
            {
                Diagnostic("third_party_heal_blocked path=effect_tick source=" + DescribeActor(_effectTickOwner) +
                    " target=" + DescribeActor(target.Myself));
                return false;
            }
            if (IsDuelParticipantClass(tickOwnerClass) && !IsDuelParticipantClass(targetClass))
                return false;
            if (_state != DuelLifecycleState.Active || !IsDuelParticipantClass(targetClass)) return true;
            state = BeginHealCapture(target);
            return true;
        }

        internal static void BeginEffectTick(Stats stats, ref Character previousOwner)
        {
            previousOwner = _effectTickOwner;
            _effectTickOwner = stats == null ? null : stats.Myself;
        }

        internal static void FinishEffectTick(Character previousOwner)
        {
            Character completedOwner = _effectTickOwner;
            if (completedOwner == _player || completedOwner == _sim) RefreshTrackedWorldEffects(completedOwner);
            _effectTickOwner = previousOwner;
        }

        internal static bool BeginAttributedHeal(Stats target, Spell spell, Character source, bool isMana, ref int result, ref HealCapture state)
        {
            if (!Active || target == null) return true;
            source = ResolveSpellSource(spell, target, source);
            CombatActorClass targetClass = Classify(target.Myself);
            CombatActorClass sourceClass = Classify(source);

            // Mana/resource restoration is containment-sensitive too. The previous early return for
            // isMana let participant group-resource effects leak to unrelated actors. Self resource
            // effects stay native; cross/third-party resource assistance is refused per target.
            if (isMana)
            {
                if (IsDuelParticipantClass(targetClass))
                {
                    if (_state == DuelLifecycleState.Active && source == target.Myself && IsDuelParticipantClass(sourceClass))
                        return true;
                    if (sourceClass == CombatActorClass.OutsideHostile) return true;
                    result = 0;
                    DiagnosticRecord("resource_effect_blocked source=" + DamageSourceRole(source) +
                        " target=" + DamageTargetRole(target.Myself));
                    return false;
                }
                if (IsDuelParticipantClass(sourceClass))
                {
                    result = 0;
                    NotifyUnsafeAreaBystander();
                    return false;
                }
                return true;
            }

            if (IsDuelParticipantClass(targetClass))
            {
                if (_state == DuelLifecycleState.Active && source != null && source == target.Myself && IsDuelParticipantClass(sourceClass))
                {
                    state = BeginHealCapture(target);
                    return true;
                }
                // Hostile-world healing is bizarre but remains native rather than being confused
                // with friendly assistance. Ordinary friendly/protected/unknown help is blocked.
                if (sourceClass == CombatActorClass.OutsideHostile) return true;
                Diagnostic("third_party_heal_blocked path=attributed source=" + DescribeActor(source) +
                    " target=" + DescribeActor(target.Myself) + " spell=" + SafeLabel(spell == null ? null : spell.name));
                result = 0;
                return false;
            }

            if (IsDuelParticipantClass(sourceClass))
            {
                result = 0;
                NotifyUnsafeAreaBystander();
                return false;
            }
            return true;
        }

        private static HealCapture BeginHealCapture(Stats target)
        {
            int virtualHp = target.Myself == _player ? _playerHp : _simHp;
            target.CurrentHP = virtualHp;
            return new HealCapture { Target = target, Before = virtualHp, Track = true };
        }

        internal static void FinishHeal(HealCapture state)
        {
            if (!Active || _state != DuelLifecycleState.Active || state == null || !state.Track || state.Target == null) return;
            int gained = Math.Max(0, state.Target.CurrentHP - state.Before);
            if (state.Target.Myself == _player) _playerHp = Math.Max(1, Math.Min(_playerMax, _playerHp + gained));
            else if (state.Target.Myself == _sim) _simHp = Math.Max(1, Math.Min(_simMax, _simHp + gained));
            MirrorVirtualHealth();
            bool playerTarget = state.Target.Myself == _player;
            int after = playerTarget ? _playerHp : _simHp;
            int maximum = playerTarget ? _playerMax : _simMax;
            DiagnosticVirtual("virtual_heal", "HealMe", playerTarget, gained, gained, state.Before, after,
                maximum, state.Before, state.Target.CurrentHP, "selfOnly=true");
        }

        internal static bool BeginStatusEffect(Stats target, Spell spell, Character source, ref int result,
            string eventSource, ref StatusEffectIngressState state)
        {
            if (!Active || target == null) return true;
            source = ResolveSpellSource(spell, target, source);
            CombatActorClass targetClass = Classify(target.Myself);
            CombatActorClass sourceClass = Classify(source);

            if (IsDuelParticipantClass(sourceClass))
            {
                if (_state != DuelLifecycleState.Active) { result = 0; return false; }
                if (target.Myself == source)
                {
                    bool containedSelf = IsAreaSpell(spell)
                        ? IsAreaStructurallyContainable(spell) && IsKnownBeneficialPayload(spell)
                        : IsSelfContainedDuelCast(spell);
                    return containedSelf || BlockStatusEffect(ref result);
                }
                if (IsDuelParticipantClass(targetClass) && IsSafeDuelOffense(spell)) return true;
                if (targetClass == CombatActorClass.OutsideHostile && IsSafeDuelOffense(spell)) return true;
                result = 0;
                if (targetClass == CombatActorClass.ProtectedNonParticipant || targetClass == CombatActorClass.Unknown)
                    NotifyUnsafeAreaBystander();
                return false;
            }

            if (IsDuelParticipantClass(targetClass) && IsFriendlyPartyClass(sourceClass))
            {
                if (IsAdmittedPetEngagement(source, target.Myself) && IsSafeDuelOffense(spell)) return true;
                result = 0;
                return false;
            }

            if (IsDuelParticipantClass(targetClass) && sourceClass == CombatActorClass.OutsideHostile)
            {
                // Apply the hostile-world status effect against the actor's real HP/effect baseline,
                // then adopt the resulting native state before remirroring virtual duel health.
                int realHp = RealLedgerHp(target.Myself);
                state = new StatusEffectIngressState
                {
                    Target = target.Myself,
                    PreserveWorldReal = true,
                    RealBefore = realHp,
                    Spell = spell
                };
                try { target.CurrentHP = Math.Max(1, realHp); } catch { }
                _lastDamageDiagnostic = "source=" + SafeLabel(eventSource) +
                    " sourceRole=hostile_world targetRole=" + ParticipantRole(target.Myself) +
                    " authority=real_world effect=true virtualized=false realEffectPreserved=pending_native";
                return true;
            }

            if (IsDuelParticipantClass(targetClass) &&
                (sourceClass == CombatActorClass.ProtectedNonParticipant || sourceClass == CombatActorClass.Unknown))
            {
                result = 0;
                return false;
            }

            return true;
        }

        internal static void FinishStatusEffect(StatusEffectIngressState state)
        {
            if (state == null || state.Completed || !state.PreserveWorldReal || state.Target == null) return;
            state.Completed = true;
            int after = state.RealBefore;
            try { if (state.Target.MyStats != null) after = Math.Max(0, state.Target.MyStats.CurrentHP); } catch { }
            SetRealLedgerHp(state.Target, after);
            AdoptWorldStatusEffectSlots(state.Target, state.Spell);
            _lastDamageDiagnostic = "source=status_effect sourceRole=hostile_world targetRole=" + ParticipantRole(state.Target) +
                " authority=real_world effect=true virtualized=false realBefore=" + state.RealBefore +
                " realAfter=" + after + " realEffectPreserved=true";
            DiagnosticRecord("world_effect " + _lastDamageDiagnostic);
            if (DuelLifecyclePolicy.IsCombatActive(_state) && IsAlive(state.Target)) MirrorVirtualHealth();
        }

        private static Character ResolveSpellSource(Spell spell, Stats target, Character supplied)
        {
            if (supplied != null) return supplied;
            try
            {
                if (_player != null && _player.MySpells != null && _player.MySpells.GetCurrentCast() == spell) return _player;
                if (_sim != null && _sim.MySpells != null && _sim.MySpells.GetCurrentCast() == spell) return _sim;
            }
            catch { }
            if (spell != null && target != null && (spell.SelfOnly || spell.ApplyToCaster || spell.InflictOnSelf) &&
                IsDuelParticipantClass(Classify(target.Myself))) return target.Myself;
            return supplied;
        }

        // NPC.CheckAssist, CheckAssistRaid, ForceGroupOntoTarget, and ForceNewAggroTarget assign
        // NPC.CurrentAggroTarget with a direct field store instead of going through AggroOn /
        // ForceAggroOn / ManageAggro, so none of the aggro patches above can see them. That is how
        // party Sims (and a challenged non-party Sim's own independent group) end up assisting onto
        // a duelist mid-match. Snapshot the target around the routine and undo it if the routine
        // parked the NPC on a duelist, rather than suppressing assist behaviour wholesale -- a party
        // Sim assisting against a real mob elsewhere is legitimate and must keep working.
        internal static void BeginAssistRoutine(NPC npc, ref Character previousTarget)
        {
            previousTarget = null;
            if (!Active || npc == null || IsDuelingNpc(npc)) return;
            try { previousTarget = npc.CurrentAggroTarget; } catch { }
        }

        internal static void FinishAssistRoutine(NPC npc, Character previousTarget)
        {
            if (!Active || npc == null || IsDuelingNpc(npc)) return;
            try
            {
                Character acquired = npc.CurrentAggroTarget;
                if (acquired == previousTarget || !IsDuelParticipantClass(Classify(acquired))) return;
                // A duelist's own pet assisting onto the opposing duelist is the intended path for
                // routing pet damage into the match, not interference.
                Character actor = NpcCharacter(npc);
                if (IsAdmittedPetEngagement(actor, acquired)) { RememberEngagedPet(actor); return; }
                npc.CurrentAggroTarget = IsDuelParticipantClass(Classify(previousTarget)) ? null : previousTarget;
                ThrottledDiagnostic("assist", "interference=assist_target actor=" + DescribeActor(NpcCharacter(npc)) + " target=" + DescribeActor(acquired));
            }
            catch { }
        }

        // Character.DamageMe calls SimPlayerGrouping.GroupAttack(attacker) when the victim is a
        // grouped Sim, and SimPlayerIndependentGroup.CallForAssist(attacker) when the victim belongs
        // to an independent Sim group. Both run inside the native duel hit, so the duel's own melee
        // is what recruits bystanders against the player.
        internal static bool AllowGroupAssistCall(Character requestedTarget)
        {
            bool requestedTargetIsDuelist = requestedTarget != null && IsDuelParticipantClass(Classify(requestedTarget));
            if (DuelSafetyPolicy.AllowGroupAssistCall(Active, requestedTargetIsDuelist)) return true;
            ThrottledDiagnostic("group_assist", "interference=group_assist target=" + DescribeActor(requestedTarget));
            return false;
        }

        // The same DamageMe path also appends the attacker to every nearby group member's
        // Character.NearbyEnemies list. That list is not cleared when the duel ends, so without this
        // the player stays permanently enrolled as an enemy of their own party Sims.
        private static void SnapshotNearbyEnemyMembership()
        {
            InitialNearbyEnemyMembership.Clear();
            _playerInitiallyHadSimEnemy = false;
            try
            {
                if (_player != null && _player.NearbyEnemies != null && _sim != null)
                    _playerInitiallyHadSimEnemy = _player.NearbyEnemies.Contains(_sim);
            }
            catch { }

            // One bounded scene scan at accepted-duel setup, never in the combat update loop.
            try
            {
                foreach (SimPlayer loaded in UnityEngine.Object.FindObjectsOfType<SimPlayer>())
                {
                    if (loaded == null || CoopCompatibility.IsRemoteHuman(loaded) || !IsUsableSim(loaded)) continue;
                    Character actor = loaded.MyStats == null ? null : loaded.MyStats.Myself;
                    if (actor == null || actor.NearbyEnemies == null || InitialNearbyEnemyMembership.ContainsKey(actor)) continue;
                    byte flags = 0;
                    if (_player != null && actor.NearbyEnemies.Contains(_player)) flags |= 1;
                    if (_sim != null && actor.NearbyEnemies.Contains(_sim)) flags |= 2;
                    InitialNearbyEnemyMembership.Add(actor, flags);
                }
            }
            catch { }
        }

        private static void PurgeDuelistsFromNearbyEnemies()
        {
            try
            {
                // Iterate the one-time snapshot instead of finding scene objects every frame. This
                // includes the challenged non-party Sim's already-loaded independent-group peers.
                foreach (Character actor in InitialNearbyEnemyMembership.Keys)
                {
                    if (actor == null || actor.NearbyEnemies == null) continue;
                    if (_player != null && actor != _player) actor.NearbyEnemies.Remove(_player);
                    if (_sim != null && actor != _sim) actor.NearbyEnemies.Remove(_sim);
                }
            }
            catch { }
            try
            {
                if (_player != null && _player.NearbyEnemies != null && _sim != null) _player.NearbyEnemies.Remove(_sim);
                if (_sim != null && _sim.NearbyEnemies != null && _player != null) _sim.NearbyEnemies.Remove(_player);
            }
            catch { }
        }

        private static void RestoreInitialNearbyEnemyMembership()
        {
            try
            {
                foreach (KeyValuePair<Character, byte> pair in InitialNearbyEnemyMembership)
                {
                    Character actor = pair.Key;
                    if (actor == null || actor.NearbyEnemies == null) continue;
                    if (_player != null && actor != _player)
                    {
                        bool existed = (pair.Value & 1) != 0;
                        bool existsNow = actor.NearbyEnemies.Contains(_player);
                        if (DuelSafetyPolicy.ShouldRestoreInitialEnemyMembership(existed, existsNow))
                            actor.NearbyEnemies.Add(_player);
                    }
                    if (_sim != null && actor != _sim)
                    {
                        bool existed = (pair.Value & 2) != 0;
                        bool existsNow = actor.NearbyEnemies.Contains(_sim);
                        if (DuelSafetyPolicy.ShouldRestoreInitialEnemyMembership(existed, existsNow))
                            actor.NearbyEnemies.Add(_sim);
                    }
                }
            }
            catch { }
            try
            {
                if (_player != null && _player.NearbyEnemies != null && _sim != null)
                {
                    bool existsNow = _player.NearbyEnemies.Contains(_sim);
                    if (DuelSafetyPolicy.ShouldRestoreInitialEnemyMembership(_playerInitiallyHadSimEnemy, existsNow))
                        _player.NearbyEnemies.Add(_sim);
                }
            }
            catch { }
        }

        internal static bool AllowAggroShare(NPC npc)
        {
            if (!Active || npc == null) return true;
            CombatActorClass actorClass = Classify(npc);
            if (actorClass == CombatActorClass.DuelParticipant) return false;
            return !IsFriendlyPartyClass(actorClass) || !IsDuelParticipantClass(Classify(npc.CurrentAggroTarget));
        }

        internal static string NearbySummary()
        {
            Character player = null;
            try { player = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself; } catch { }
            // See FindSim: an aliveness check, not a scene-locality check against the player's
            // own (persistent) GameObject.
            if (!IsAlive(player))
                return "[Practice Duel] Nearby Sims unavailable while the player is not in a safe state.";
            if (!PlayerHealthAllowsDuel(player))
                return "[Practice Duel] Nearby Sims: you are currently too injured to start a duel.";

            List<string> rows = new List<string>();
            foreach (SimPlayer sim in UnityEngine.Object.FindObjectsOfType<SimPlayer>())
            {
                if (sim == null || sim.gameObject == null || !sim.gameObject.activeInHierarchy) continue;
                // A party Sim's locality/scope is proven by party membership, not the nearby-Sim
                // same-scene predicate -- otherwise party Sims silently vanish from /eduel nearby
                // whenever that predicate mis-evaluates. See DuelController.EvaluateEligibility.
                if (!IsPlayerPartySim(sim) && !IsSimLocalToActiveZone(sim.gameObject, player)) continue;

                Character simCharacter = null;
                try { if (sim.MyStats != null) simCharacter = sim.MyStats.Myself; } catch { }
                float distance = float.MaxValue;
                try
                {
                    Vector3 targetPosition = simCharacter != null ? simCharacter.transform.position : sim.transform.position;
                    distance = Vector3.Distance(player.transform.position, targetPosition);
                }
                catch { }
                if (distance > ChallengeDistance) continue;

                NPC simNpc;
                bool partySim;
                DuelEligibilityDecision eligibility = EvaluateEligibility(sim, player, out simCharacter, out simNpc, out partySim);
                string name = ReadName(sim);
                if (string.IsNullOrWhiteSpace(name)) name = "unnamed Sim";
                string status;
                if (eligibility == DuelEligibilityDecision.Eligible)
                {
                    string stableKey = StableSimKey(sim);
                    DuelSocialDecision decision = EvaluateWillingness(sim, player, simCharacter, partySim, stableKey);
                    status = "eligible decision=" + DuelChallengePolicy.Token(decision);
                }
                else status = "unavailable=" + DuelEligibilityPolicy.Token(eligibility);

                rows.Add(name + " (" + distance.ToString("0.0") + "m, " + (partySim ? "party" : "nearby") + ") " + status);
                if (rows.Count >= 12) break;
            }

            return rows.Count == 0
                ? "[Practice Duel] No same-scene SimPlayers are within 25m."
                : "[Practice Duel] Nearby Sims: " + string.Join(" | ", rows.ToArray());
        }

        internal static string[] EligibleNames()
        {
            Character player = null;
            try { player = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself; } catch { }
            if (!IsAlive(player) || !PlayerHealthAllowsDuel(player)) return new string[0];
            List<string> names = new List<string>();
            foreach (SimPlayer sim in UnityEngine.Object.FindObjectsOfType<SimPlayer>())
            {
                if (sim == null || sim.gameObject == null || !sim.gameObject.activeInHierarchy) continue;
                if (!IsPlayerPartySim(sim) && !IsSimLocalToActiveZone(sim.gameObject, player)) continue;
                Character simCharacter = null;
                try { if (sim.MyStats != null) simCharacter = sim.MyStats.Myself; } catch { }
                float distance = float.MaxValue;
                try { distance = Vector3.Distance(player.transform.position, (simCharacter != null ? simCharacter.transform : sim.transform).position); } catch { }
                if (distance > ChallengeDistance) continue;
                NPC simNpc; bool partySim;
                if (EvaluateEligibility(sim, player, out simCharacter, out simNpc, out partySim) != DuelEligibilityDecision.Eligible) continue;
                string name = ReadName(sim);
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
            }
            names.Sort(System.StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }

        // Unfiltered locality/eligibility dump: every local SimPlayer instance is reported with
        // the exact fields the locality and eligibility predicates evaluated, independent of the
        // 25m challenge distance and of any pass/fail short-circuit. Intended to make "wrong_scene"
        // and similar rejections provable in the log instead of only inferable from chat text.
        internal static string DiagSummary()
        {
            Character player = null;
            try { player = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself; } catch { }

            string activeZone = SafeSceneName(SceneManager.GetActiveScene());
            string playerScene = player == null || player.gameObject == null ? "none" : SafeSceneName(player.gameObject);
            bool playerStable = IsAlive(player);

            Diagnostic("diag=summary player_stable=" + playerStable + " player_scene=" + playerScene +
                " active_zone=" + activeZone);

            int total = 0;
            int local = 0;
            int eligible = 0;
            foreach (SimPlayer sim in UnityEngine.Object.FindObjectsOfType<SimPlayer>())
            {
                if (sim == null) continue;
                total++;
                if (total > 40) break; // Diagnostic only; guards against runaway logging on crowded scenes.

                string name = ReadName(sim);
                if (string.IsNullOrWhiteSpace(name)) name = "unnamed";

                bool loaded = sim.gameObject != null && sim.gameObject.activeInHierarchy;
                string simScene = SafeSceneName(sim.gameObject);

                float distance = float.MaxValue;
                try { if (player != null && player.transform != null && sim.transform != null) distance = Vector3.Distance(player.transform.position, sim.transform.position); }
                catch { }

                bool coopRemote = false;
                try { coopRemote = CoopCompatibility.IsRemoteHuman(sim); } catch { }

                bool isLocal = IsSimLocalToActiveZone(sim.gameObject, player);
                if (isLocal) local++;

                Character simCharacter;
                NPC simNpc;
                bool partySim;
                DuelEligibilityDecision decision = EvaluateEligibility(sim, player, out simCharacter, out simNpc, out partySim);
                bool isEligible = decision == DuelEligibilityDecision.Eligible;
                if (isEligible) eligible++;

                Diagnostic("candidate=" + SafeLabel(name) +
                    " scene=" + simScene +
                    " activeScene=" + activeZone +
                    " distance=" + (distance == float.MaxValue ? "n/a" : distance.ToString("0.0") + "m") +
                    " loaded=" + loaded +
                    " local=" + isLocal +
                    " coopRemote=" + coopRemote +
                    " eligible=" + isEligible +
                    (isEligible ? "" : " reason=" + DuelEligibilityPolicy.Token(decision)));
            }

            Diagnostic("diag=totals simPlayers=" + total + " local=" + local + " eligible=" + eligible);

            DiagnosticRecord("diag=last_spell_admission " + _lastSpellAdmission);
            DiagnosticRecord("diag=last_damage " + _lastDamageDiagnostic);
            DiagnosticRecord("diag=last_aoe " + _lastAoeDiagnostic);

            return "[Practice Duel] diag: " + total + " SimPlayer(s), " + local + " local, " + eligible +
                " eligible. active_zone=" + activeZone + " player_scene=" + playerScene +
                " player_stable=" + playerStable + " | lastSpell: " + _lastSpellAdmission +
                " | lastDamage: " + _lastDamageDiagnostic + " | lastAoE: " + _lastAoeDiagnostic +
                " (full per-candidate detail in the Lunaris log)";
        }

        private static DuelEligibilityDecision EvaluateEligibility(SimPlayer target, Character player,
            out Character simCharacter, out NPC simNpc, out bool partySim)
        {
            simCharacter = null;
            simNpc = null;
            partySim = IsPlayerPartySim(target);

            bool isSim = target != null;
            bool active = false;
            bool sameScene = false;
            bool alive = false;
            bool remote = false;
            bool components = false;
            float distance = float.MaxValue;

            try
            {
                active = target != null && target.gameObject != null && target.gameObject.activeInHierarchy;
                sameScene = active && IsSimLocalToActiveZone(target.gameObject, player);
                if (target != null && target.MyStats != null) simCharacter = target.MyStats.Myself;
                alive = IsAlive(simCharacter);
                if (alive) sameScene = sameScene && IsSimLocalToActiveZone(simCharacter.gameObject, player);
                simNpc = simCharacter == null ? null : simCharacter.MyNPC;
                components = target != null && target.MyStats != null && simCharacter != null &&
                             simCharacter.MyStats != null && simNpc != null && simNpc.ThisSim == target;
                if (player != null && simCharacter != null)
                    distance = Vector3.Distance(player.transform.position, simCharacter.transform.position);
            }
            catch { }
            try { remote = CoopCompatibility.IsRemoteHuman(target); } catch { }

            DuelEligibilityInput input = new DuelEligibilityInput
            {
                IsSimPlayer = isSim,
                ActiveInHierarchy = active,
                InLocalPlayerScene = sameScene,
                IsPartyMember = partySim,
                Alive = alive,
                RemoteCoop = remote,
                HasCombatComponents = components,
                CampConflict = IsCampActive(true),
                Distance = distance,
                MaximumDistance = ChallengeDistance,
                UnsafeRealCombat = components && HasUnsafeRealCombat(player, simCharacter, simNpc)
            };
            return DuelEligibilityPolicy.Evaluate(input);
        }

        private static void ReportEligibilityFailure(DuelEligibilityDecision decision, SimPlayer target, Character player)
        {
            string name = SafeLabel(ReadName(target));
            Character actor = null;
            try { actor = target == null || target.MyStats == null ? null : target.MyStats.Myself; } catch { }
            Scene candidateScene = default(Scene);
            Scene activeScene = SceneManager.GetActiveScene();
            Scene playerScene = default(Scene);
            float distance = float.MaxValue;
            try { if (target != null && target.gameObject != null) candidateScene = target.gameObject.scene; } catch { }
            try { if (player != null && player.gameObject != null) playerScene = player.gameObject.scene; } catch { }
            try { if (player != null && actor != null) distance = Vector3.Distance(player.transform.position, actor.transform.position); } catch { }
            bool partyMember = IsPlayerPartySim(target);
            bool remoteHuman = false;
            try { remoteHuman = CoopCompatibility.IsRemoteHuman(target); } catch { }
            Diagnostic("eligibility=" + DuelEligibilityPolicy.Token(decision) +
                " build=" + DuelBuildInfo.Id +
                " candidate=" + name +
                " partyMember=" + partyMember +
                " candidateScene=" + SceneLabel(candidateScene) +
                " activeScene=" + SceneLabel(activeScene) +
                " playerCharacterScene=" + SceneLabel(playerScene) +
                " candidateLoaded=" + (candidateScene.IsValid() && candidateScene.isLoaded) +
                " activeInHierarchy=" + (target != null && target.gameObject != null && target.gameObject.activeInHierarchy) +
                " distance=" + (distance == float.MaxValue ? "n/a" : distance.ToString("0.0")) +
                " remoteHuman=" + remoteHuman +
                " " + CoopCompatibility.TargetFlags(target) +
                " localSim=" + (target != null && actor != null && !remoteHuman) +
                " scenePredicateName=active_loaded_zone" +
                " scenePass=" + IsSimLocalToActiveZone(target == null ? null : target.gameObject, player) +
                " finalResult=" + DuelEligibilityPolicy.Token(decision));
            switch (decision)
            {
                case DuelEligibilityDecision.RemoteCoop:
                    Say("[Practice Duel] Remote COOP humans/proxies cannot be challenged.", "yellow");
                    break;
                case DuelEligibilityDecision.MissingCombatComponents:
                    Say("[Practice Duel] That Sim is missing required local combat components.", "yellow");
                    break;
                case DuelEligibilityDecision.CampConflict:
                    Say("[Practice Duel] End Hunt Camp before starting a duel. Relax does not block friendly duels.", "yellow");
                    break;
                case DuelEligibilityDecision.TooFar:
                    Say("[Practice Duel] Move closer before challenging that Sim.", "yellow");
                    break;
                case DuelEligibilityDecision.UnsafeRealCombat:
                    Say("[Practice Duel] That challenge is unsafe while real combat is active.", "yellow");
                    break;
                default:
                    Say("[Practice Duel] Choose a living local SimPlayer in the current scene.", "yellow");
                    break;
            }
        }

        private static bool PlayerHealthAllowsDuel(Character player)
        {
            try
            {
                if (player == null || player.MyStats == null) return false;
                int max = player.MyStats.CurrentMaxHP;
                if (max <= 0) return false;
                int percent = Mathf.Clamp(Mathf.RoundToInt(player.MyStats.CurrentHP * 100f / max), 0, 100);
                return percent >= MinimumPlayerHealthPercent;
            }
            catch { return false; }
        }

        private static bool HasUnsafeRealCombat(Character player, Character sim, NPC simNpc)
        {
            try
            {
                if (simNpc != null && IsAlive(simNpc.CurrentAggroTarget)) return true;
            }
            catch { }

            try
            {
                bool capabilityAvailable = GameData.PlayerCombat != null && PlayerAutoattackField != null;
                bool autoAttackActive = capabilityAvailable &&
                    Convert.ToBoolean(PlayerAutoattackField.GetValue(GameData.PlayerCombat));
                if (!DuelSafetyPolicy.CanStartWithPreExistingAutoAttack(capabilityAvailable, autoAttackActive)) return true;
            }
            catch { return true; }

            try
            {
                if (GameData.AttackingPlayer == null) return false;
                foreach (NPC npc in GameData.AttackingPlayer)
                {
                    if (npc == null || npc.CurrentAggroTarget == null) continue;
                    if (npc.CurrentAggroTarget == player || npc.CurrentAggroTarget == sim) return true;
                }
            }
            catch { }
            return false;
        }

        private static DuelSocialDecision EvaluateWillingness(SimPlayer sim, Character player, Character simCharacter, bool partySim, string stableKey)
        {
            int playerLevel;
            int simLevel;
            bool hasPlayerLevel = TryReadIntMember(player == null ? null : player.MyStats, "Level", out playerLevel);
            bool hasSimLevel = TryReadIntMember(simCharacter == null ? null : simCharacter.MyStats, "Level", out simLevel);

            int currentHp = 0;
            int maximumHp = 0;
            bool hasHealth = false;
            try
            {
                if (simCharacter != null && simCharacter.MyStats != null)
                {
                    currentHp = simCharacter.MyStats.CurrentHP;
                    maximumHp = simCharacter.MyStats.CurrentMaxHP;
                    hasHealth = maximumHp > 0;
                }
            }
            catch { }

            DuelWillingnessInput input = new DuelWillingnessInput
            {
                IsPartySim = partySim,
                Rival = ReadRival(sim),
                HasHealth = hasHealth,
                CurrentHealth = currentHp,
                MaximumHealth = maximumHp,
                HasLevel = hasPlayerLevel && hasSimLevel,
                PlayerLevel = playerLevel,
                SimLevel = simLevel,
                // The cooldown applies to party Sims too, not just non-party ones. See
                // DuelChallengePolicy.Evaluate: the party-Sim auto-accept bypass is checked after
                // RecentDuel, so it cannot skip the cooldown.
                RecentDuel = WasRecentlyAccepted(stableKey),
                StableKey = stableKey
            };
            return DuelChallengePolicy.Evaluate(input);
        }

        private static bool WasRecentlyAccepted(string key)
        {
            float now = Time.unscaledTime;
            PruneExpiredDuelCooldowns(now);
            float last;
            return !string.IsNullOrWhiteSpace(key) &&
                   LastAcceptedDuelBySim.TryGetValue(key, out last) &&
                   now >= last && now - last < RecentDuelCooldownSeconds;
        }

        private static void RememberAcceptedDuel(string key)
        {
            float now = Time.unscaledTime;
            PruneExpiredDuelCooldowns(now);
            if (!string.IsNullOrWhiteSpace(key)) LastAcceptedDuelBySim[key] = now;
        }

        private static void PruneExpiredDuelCooldowns(float now)
        {
            if (LastAcceptedDuelBySim.Count == 0) return;
            List<string> expired = null;
            foreach (KeyValuePair<string, float> pair in LastAcceptedDuelBySim)
            {
                if (now >= pair.Value && now - pair.Value < RecentDuelCooldownSeconds) continue;
                if (expired == null) expired = new List<string>();
                expired.Add(pair.Key);
            }
            if (expired == null) return;
            for (int i = 0; i < expired.Count; i++) LastAcceptedDuelBySim.Remove(expired[i]);
        }

        private static string StableSimKey(SimPlayer sim)
        {
            int simIndex;
            bool hasStableIndex = TryReadTrackingIndex(sim, out simIndex);
            return DuelIdentity.BuildKey(ReadName(sim), hasStableIndex, simIndex);
        }

        private static bool TryReadTrackingIndex(SimPlayer sim, out int simIndex)
        {
            simIndex = -1;
            if (sim == null) return false;

            // Current Erenshor ecosystem code verifies that SimPlayerTracking owns simIndex and
            // MyAvatar, and that GameData.SimMngr.Sims contains persistent tracking records. Prefer
            // that established mapping for a loaded avatar.
            try
            {
                if (GameData.SimMngr != null && GameData.SimMngr.Sims != null)
                {
                    foreach (SimPlayerTracking trackingRecord in GameData.SimMngr.Sims)
                    {
                        if (trackingRecord == null || trackingRecord.MyAvatar != sim || trackingRecord.simIndex < 0) continue;
                        simIndex = trackingRecord.simIndex;
                        return true;
                    }
                }
            }
            catch { }

            // Some builds may expose the tracking record directly on the runtime avatar. Probe only
            // the specifically known candidate shape and use it only when it really exists.
            object tracking = ReadObjectMember(sim, "MySimTracking");
            return tracking != null && TryReadIntMember(tracking, "simIndex", out simIndex) && simIndex >= 0;
        }

        private static object ReadObjectMember(object instance, string name)
        {
            if (instance == null || string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                Type type = instance.GetType();
                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(instance);
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return property != null && property.CanRead ? property.GetValue(instance, null) : null;
            }
            catch { return null; }
        }

        private static bool TryReadIntMember(object instance, string name, out int value)
        {
            value = 0;
            if (instance == null || string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                Type type = instance.GetType();
                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    value = Convert.ToInt32(field.GetValue(instance));
                    return true;
                }

                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null && property.CanRead)
                {
                    value = Convert.ToInt32(property.GetValue(instance, null));
                    return true;
                }
            }
            catch { }
            value = 0;
            return false;
        }

        private static bool ReadRival(SimPlayer sim)
        {
            if (sim == null) return false;
            try
            {
                Type type = sim.GetType();
                FieldInfo field = type.GetField("Rival", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return Convert.ToBoolean(field.GetValue(sim));
                PropertyInfo property = type.GetProperty("Rival", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return property != null && property.CanRead && Convert.ToBoolean(property.GetValue(sim, null));
            }
            catch { return false; }
        }

        // Interference is reported from AI routines that run every tick, so an unthrottled line per
        // occurrence buries the rest of the duel log.
        private static void ThrottledDiagnostic(string key, string message)
        {
            try
            {
                float last;
                float now = Time.unscaledTime;
                if (LastInterferenceLog.TryGetValue(key, out last) && now >= last && now - last < 2f) return;
                LastInterferenceLog[key] = now;
            }
            catch { }
            Diagnostic(message);
        }

        // Diagnostic() routes the whole message through SafeLabel, which caps at 120 characters -
        // correct for a single untrusted label, but it silently truncated the multi-field spell
        // admission record mid-field. This variant keeps the properties that actually matter for
        // safety (newlines stripped so a record can never forge extra log lines, and a hard length
        // ceiling) while leaving room for a complete record whose individual fields are already
        // sanitized and bounded at the point they are read.
        private static void DiagnosticRecord(string message)
        {
            try
            {
                if (ErenshorDuelPlugin.Instance == null) return;
                string clean = (message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (clean.Length == 0) return;
                if (clean.Length > 400) clean = clean.Substring(0, 400);
                ErenshorDuelPlugin.Instance.Diagnostic("[Practice Duel] " + clean);
            }
            catch { }
        }

        private static void Diagnostic(string message)
        {
            try
            {
                if (ErenshorDuelPlugin.Instance != null)
                    ErenshorDuelPlugin.Instance.Diagnostic("[Practice Duel] " + SafeLabel(message));
            }
            catch { }
        }

        private static bool ParticipantsAreValid()
        {
            Character localPlayer = null;
            try { localPlayer = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself; } catch { }
            if (!IsAlive(localPlayer)) return false;

            bool secondValid = IsParticipantRuntimeValid(_simPlayer, _sim, _simNpc, _simWasParty);
            if (!secondValid) return false;
            if (!_spectatorDuel) return _player == localPlayer && IsAlive(_player);
            return IsParticipantRuntimeValid(_firstSimPlayer, _player, _firstSimNpc, _firstSimWasParty);
        }

        private static bool IsParticipantRuntimeValid(SimPlayer sim, Character actor, NPC npc, bool wasPartyMember)
        {
            try
            {
                if (sim == null || actor == null || npc == null || sim.gameObject == null || !sim.gameObject.activeInHierarchy) return false;
                if (sim.MyStats == null || sim.MyStats.Myself != actor || !IsAlive(actor)) return false;
                if (actor.MyNPC != npc || npc.ThisSim != sim || CoopCompatibility.IsRemoteHuman(sim)) return false;
                // Nearby non-party support requires loaded-zone locality. Party Sims use the same
                // authoritative party-scope rule as start eligibility; the player's persistent
                // scene has previously made the generic same-scene predicate unreliable for them.
                if (!wasPartyMember && (!IsSimLocalToActiveZone(sim.gameObject, actor) ||
                    !IsSimLocalToActiveZone(actor.gameObject, actor))) return false;
                return true;
            }
            catch { return false; }
        }

        private static bool ParticipantScopesStillMatch()
        {
            bool secondPartyNow = IsPlayerPartySim(_simPlayer);
            if (!DuelSafetyPolicy.PartyScopeStillMatches(_simWasParty, secondPartyNow)) return false;
            if (!_spectatorDuel) return true;
            bool firstPartyNow = IsPlayerPartySim(_firstSimPlayer);
            return DuelSafetyPolicy.PartyScopeStillMatches(_firstSimWasParty, firstPartyNow);
        }

        private static bool IsCampActive(bool forceIntegrationRefresh)
        {
            try { if (GameData.PlayerControl != null && GameData.PlayerControl.Sitting) return true; } catch { }
            float now = Time.unscaledTime;
            if (!forceIntegrationRefresh && now < _nextIntegrationCampCheck) return _cachedIntegrationCampActive;
            _nextIntegrationCampCheck = now + 0.25f;
            _cachedIntegrationCampActive = DeepSimsCompatibility.IsCampActive();
            return _cachedIntegrationCampActive;
        }

        private static CombatActorClass Classify(NPC npc)
        {
            if (npc == null) return CombatActorClass.Unknown;
            if (npc == _simNpc || npc == _firstSimNpc) return CombatActorClass.DuelParticipant;
            try
            {
                SimPlayer sim = npc.ThisSim;
                if (sim != null)
                {
                    if (sim == _simPlayer || sim == _firstSimPlayer) return CombatActorClass.DuelParticipant;
                    if (CoopCompatibility.IsRemoteHuman(sim)) return CombatActorClass.Unknown;
                    return IsPlayerPartySim(sim) ? CombatActorClass.GroupedLocalSim : CombatActorClass.Unknown;
                }
            }
            catch { }
            Character actor = NpcCharacter(npc);
            if (actor != null) return Classify(actor);
            // Without a Character we cannot prove faction/vendor/resource semantics. Fail closed;
            // do not promote every anonymous NPC component to a hostile-world authority.
            return CombatActorClass.Unknown;
        }

        private static Character NpcCharacter(NPC npc)
        {
            try
            {
                if (npc == null) return null;
                Character actor = npc.GetComponent<Character>();
                return actor != null ? actor : npc.GetComponentInParent<Character>();
            }
            catch { return null; }
        }

        private static CombatActorClass Classify(Character actor)
        {
            if (actor == null) return CombatActorClass.Unknown;
            if (actor == _sim) return CombatActorClass.DuelParticipant;
            if (actor == _player) return _spectatorDuel ? CombatActorClass.DuelParticipant : CombatActorClass.LocalPlayer;

            NPC npc = null;
            try { npc = actor.MyNPC; } catch { }
            SimPlayer sim = null;
            try { sim = npc == null ? null : npc.ThisSim; } catch { }
            if (sim != null)
            {
                if (sim == _simPlayer || sim == _firstSimPlayer) return CombatActorClass.DuelParticipant;
                if (CoopCompatibility.IsRemoteHuman(sim)) return CombatActorClass.Unknown;
                return IsPlayerPartySim(sim) ? CombatActorClass.GroupedLocalSim : CombatActorClass.Unknown;
            }

            // Some party-capable/custom-framework actors do not expose NPC.ThisSim. A
            // matching authoritative GroupMembers entry is enough to identify a friendly
            // local party caster, but never enough to identify an arbitrary same-zone NPC.
            if (IsVerifiedPartyCharacter(actor)) return CombatActorClass.GroupedLocalSim;

            if (IsOwnedByFriendlyParty(actor)) return CombatActorClass.GroupedSimOwnedPet;

            // ResolveSpell in the installed game assigns Character.Master and sets
            // NPC.SummonedByPlayer for summoned pets. If a summoned actor has lost its Master,
            // ownership is ambiguous: keep it Unknown rather than inventing an owner.
            try { if (npc != null && npc.SummonedByPlayer) return CombatActorClass.Unknown; } catch { }
            if (npc != null && IsVerifiedHostileWorldActor(actor, npc)) return CombatActorClass.OutsideHostile;
            if (npc != null && IsKnownProtectedWorldActor(actor, npc)) return CombatActorClass.ProtectedNonParticipant;
            return CombatActorClass.Unknown;
        }

        // Same-snapshot native fields used by other current modules to distinguish ordinary hostile
        // world NPCs from Sims, vendors, villagers, resource objects and summons. Practice Duel uses
        // this only as an authority gate: ambiguous actors fail closed instead of being promoted to
        // "hostile" merely because they have an NPC component.
        private static bool IsVerifiedHostileWorldActor(Character actor, NPC npc)
        {
            if (actor == null || npc == null) return false;
            try
            {
                if (!actor.Alive || actor.Master != null || actor.Invulnerable || actor.isVendor) return false;
                if (npc.SimPlayer || npc.ThisSim != null || npc.NeverAggro || npc.MiningNode || npc.TreasureChest || npc.SummonedByPlayer) return false;
                if (actor.MyFaction == Character.Faction.Player || actor.MyFaction == Character.Faction.PC ||
                    actor.MyFaction == Character.Faction.Villager || actor.MyFaction == Character.Faction.DEBUG) return false;
                return true;
            }
            catch { return false; }
        }

        private static bool IsKnownProtectedWorldActor(Character actor, NPC npc)
        {
            if (actor == null || npc == null) return false;
            try
            {
                if (actor.isVendor || actor.Master != null || npc.SimPlayer || npc.ThisSim != null ||
                    npc.NeverAggro || npc.MiningNode || npc.TreasureChest || npc.SummonedByPlayer) return true;
                return actor.MyFaction == Character.Faction.Player || actor.MyFaction == Character.Faction.PC ||
                       actor.MyFaction == Character.Faction.Villager || actor.MyFaction == Character.Faction.DEBUG;
            }
            catch { return false; }
        }

        private static bool IsVerifiedPartyCharacter(Character actor)
        {
            if (actor == null) return false;
            string name = string.Empty;
            try { if (actor.MyStats != null) name = actor.MyStats.MyName; } catch { }
            if (string.IsNullOrWhiteSpace(name))
            {
                try { if (actor.MyNPC != null) name = actor.MyNPC.NPCName; } catch { }
            }
            if (string.IsNullOrWhiteSpace(name)) return false;
            try
            {
                SimPlayerTracking[] members = GameData.GroupMembers;
                if (members == null) return false;
                for (int i = 0; i < members.Length; i++)
                    if (members[i] != null && string.Equals(members[i].SimName, name.Trim(), StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { }
            return false;
        }

        private static bool IsOwnedByFriendlyParty(Character actor)
        {
            Character owner = null;
            try { owner = actor == null ? null : actor.Master; } catch { }
            for (int depth = 0; owner != null && depth < 4; depth++)
            {
                if (owner == _player || owner == _sim) return true;
                try
                {
                    NPC ownerNpc = owner.MyNPC;
                    SimPlayer ownerSim = ownerNpc == null ? null : ownerNpc.ThisSim;
                    if (ownerSim != null && !CoopCompatibility.IsRemoteHuman(ownerSim) && IsPlayerPartySim(ownerSim)) return true;
                    owner = owner.Master;
                }
                catch { return false; }
            }
            return false;
        }

        private static bool IsFriendlyPartyClass(CombatActorClass actorClass)
        {
            return actorClass == CombatActorClass.DuelParticipant ||
                   actorClass == CombatActorClass.LocalPlayer ||
                   actorClass == CombatActorClass.GroupedLocalSim ||
                   actorClass == CombatActorClass.GroupedSimOwnedPet;
        }

        private static bool IsDuelParticipantClass(CombatActorClass actorClass)
        {
            return actorClass == CombatActorClass.DuelParticipant || actorClass == CombatActorClass.LocalPlayer;
        }

        private static string ParticipantRole(Character actor)
        {
            if (actor == _player) return _spectatorDuel ? "duel_first_sim" : "duel_player";
            if (actor == _sim) return "duel_opponent";
            return DamageTargetRole(actor);
        }

        private static string DamageSourceRole(Character actor)
        {
            Character principal = DuelPrincipal(actor);
            if (principal != null) return actor == principal ? ParticipantRole(principal) : "duel_owned_pet";
            CombatActorClass c = Classify(actor);
            if (c == CombatActorClass.OutsideHostile) return "hostile_world";
            if (c == CombatActorClass.ProtectedNonParticipant) return "protected_nonparticipant";
            if (c == CombatActorClass.GroupedLocalSim || c == CombatActorClass.GroupedSimOwnedPet) return "friendly_nonparticipant";
            return c == CombatActorClass.Unknown ? "unknown" : c.ToString();
        }

        private static string DamageTargetRole(Character actor)
        {
            if (actor == _player || actor == _sim) return ParticipantRole(actor);
            CombatActorClass c = Classify(actor);
            if (c == CombatActorClass.OutsideHostile) return "hostile_world";
            if (c == CombatActorClass.ProtectedNonParticipant) return "protected_nonparticipant";
            if (c == CombatActorClass.GroupedLocalSim || c == CombatActorClass.GroupedSimOwnedPet) return "friendly_nonparticipant";
            return c == CombatActorClass.Unknown ? "unknown" : c.ToString();
        }

        private static void NotifyUnsafeAreaBystander()
        {
            float now = Time.unscaledTime;
            if (now - _lastBystanderMessageAt < 1.5f) return;
            _lastBystanderMessageAt = now;
            Say("[Practice Duel] Can't use that here — someone else is in the blast.", "yellow");
        }

        private static bool IsPlayerPartySim(SimPlayer sim)
        {
            try
            {
                return sim != null && sim.InGroup && GameData.SimPlayerGrouping != null &&
                       GameData.SimPlayerGrouping.IsSimInPlayerGroup(sim);
            }
            catch { return false; }
        }

        private static bool IsUsableSim(SimPlayer sim)
        {
            Character player = null;
            try { player = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself; } catch { }
            return sim != null && sim.gameObject != null && sim.gameObject.activeInHierarchy &&
                   IsSimLocalToActiveZone(sim.gameObject, player) && sim.MyStats != null && IsAlive(sim.MyStats.Myself) &&
                   IsSimLocalToActiveZone(sim.MyStats.Myself.gameObject, player);
        }

        // Single source of truth for "is this Sim GameObject a member of the current loaded
        // Erenshor zone". Player stability is aliveness only -- never the player's own
        // (persistent, DontDestroyOnLoad) scene. See DuelLocalityPolicy.
        private static bool IsSimLocalToActiveZone(GameObject gameObject, Character player)
        {
            DuelLocalityInput input = new DuelLocalityInput
            {
                PlayerStable = IsAlive(player),
                SimLoaded = gameObject != null && gameObject.activeInHierarchy,
                SimSceneName = SafeSceneName(gameObject),
                ActiveZoneSceneName = SafeSceneName(SceneManager.GetActiveScene())
            };
            return DuelLocalityPolicy.IsLocal(input);
        }

        private static string SafeSceneName(GameObject gameObject)
        {
            try { return gameObject == null ? string.Empty : gameObject.scene.name; }
            catch { return string.Empty; }
        }

        private static string SafeSceneName(Scene scene)
        {
            try { return scene.name; }
            catch { return string.Empty; }
        }

        private static bool PlayerStillInStartingScene()
        {
            Character localPlayer = null;
            try { localPlayer = GameData.PlayerControl == null ? null : GameData.PlayerControl.Myself; } catch { }
            // _sceneHandle comes from the active zone, never from the persistent Character scene.
            return IsAlive(localPlayer) && SceneManager.GetActiveScene().handle == _sceneHandle;
        }

        private static Character DuelOpponentFor(NPC npc)
        {
            if (npc == _simNpc) return _player;
            if (_spectatorDuel && npc == _firstSimNpc) return _sim;
            return null;
        }

        private static string ParticipantLabel(Character actor)
        {
            if (actor == _player) return _spectatorDuel ? SafeLabel(_firstSimName) : "You";
            if (actor == _sim) return SafeLabel(_simName);
            return "A duelist";
        }

        private static string SceneLabel(Scene scene)
        {
            try
            {
                return (scene.IsValid() ? scene.name : "invalid") + "#" + scene.handle +
                       " loaded=" + (scene.IsValid() && scene.isLoaded);
            }
            catch { return "unavailable"; }
        }

        private static bool IsAlive(Character character) { return character != null && character.gameObject != null && character.gameObject.activeInHierarchy && character.Alive; }
        private static int Percent(int value, int maximum) { return maximum <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(value * 100f / maximum), 0, 100); }

        private static string ReadName(SimPlayer sim)
        {
            if (sim == null) return string.Empty;
            foreach (string candidate in new[] { "PlayerName", "MyName", "CharacterName", "CharName", "SimName", "Name" })
            {
                try
                {
                    FieldInfo field = sim.GetType().GetField(candidate, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        string value = field.GetValue(sim) as string;
                        if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                    }
                    PropertyInfo property = sim.GetType().GetProperty(candidate, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (property != null && property.PropertyType == typeof(string))
                    {
                        string value = property.GetValue(sim, null) as string;
                        if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                    }
                }
                catch { }
            }
            return sim.gameObject == null ? string.Empty : sim.gameObject.name;
        }

        private static string DescribeActor(Character actor)
        {
            CombatActorClass actorClass = Classify(actor);
            if (actor == null) return actorClass + "(null)";
            string name = string.Empty;
            try { if (actor.MyStats != null) name = actor.MyStats.MyName; } catch { }
            try { if (string.IsNullOrWhiteSpace(name) && actor.MyNPC != null) name = actor.MyNPC.NPCName; } catch { }
            try { if (string.IsNullOrWhiteSpace(name) && actor.gameObject != null) name = actor.gameObject.name; } catch { }
            return actorClass + "(" + SafeLabel(name) + ")";
        }

        private static string SafeLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= 120 ? clean : clean.Substring(0, 120);
        }

        private static void EmergencyCleanup(string source)
        {
            try { Diagnostic("emergency_cleanup source=" + SafeLabel(source)); } catch { }

            if (DuelLifecyclePolicy.IsSessionActive(_state))
            {
                DuelLifecycleState next;
                if (DuelLifecyclePolicy.TryTransition(_state, DuelLifecycleTrigger.Terminal, out next)) _state = next;
                else _state = DuelLifecycleState.Cleaning;
            }
            else if (_state == DuelLifecycleState.Idle)
            {
                _state = DuelLifecycleState.Cleaning;
            }

            // Restore the narrowest irreversible/native state first. Every operation is isolated so
            // one damaged actor cannot prevent the other participant from being recovered.
            try { RestoreNativeHitState(_nativeDamageInFlight); } catch { }
            try { RestoreRealHealthAndEffects(); } catch { }
            try { ReleaseEngagedPets(); } catch { }
            try
            {
                if (_simNpc != null)
                {
                    _simNpc.NPCProcOnHit = _previousNpcProc;
                    _simNpc.NPCProcOnHitChance = _previousNpcProcChance;
                    if (_simNpc.CurrentAggroTarget == _player || _simNpc.CurrentAggroTarget == _sim)
                        _simNpc.CurrentAggroTarget = null;
                    if (_simNpc.PastAggroTarget == _player || _simNpc.PastAggroTarget == _sim)
                        _simNpc.PastAggroTarget = null;
                    ResetNpcAttackAnimations(_simNpc);
                }
            }
            catch { }
            try
            {
                if (_firstSimNpc != null)
                {
                    _firstSimNpc.NPCProcOnHit = _previousFirstNpcProc;
                    _firstSimNpc.NPCProcOnHitChance = _previousFirstNpcProcChance;
                    if (_firstSimNpc.CurrentAggroTarget == _player || _firstSimNpc.CurrentAggroTarget == _sim)
                        _firstSimNpc.CurrentAggroTarget = null;
                    if (_firstSimNpc.PastAggroTarget == _player || _firstSimNpc.PastAggroTarget == _sim)
                        _firstSimNpc.PastAggroTarget = null;
                    ResetNpcAttackAnimations(_firstSimNpc);
                }
            }
            catch { }
            try { RestoreInitialNearbyEnemyMembership(); } catch { }
            try { RestorePartyMovementOwnership(); } catch { }
            try { BeginPostDuelAttackCleanup(); } catch { }
            try { ClearSessionState(); } catch { }
            try { RunPostDuelAttackCleanup(); } catch { }
        }

        private static void ClearSessionState()
        {
            try { RestoreInitialNearbyEnemyMembership(); } catch { }
            try { RestoreNativeHitState(_nativeDamageInFlight); } catch { }
            _nativeDamageInFlight = null;
            _standaloneWorldDamageInFlight = null;
            _effectTickOwner = null;
            _player = null;
            _sim = null;
            _spectatorDuel = false;
            _firstSimPlayer = null;
            _firstSimNpc = null;
            _previousFirstSimTarget = null;
            _previousFirstNpcProc = null;
            _previousFirstNpcProcChance = 0f;
            _previousFirstGuardSpot = false;
            _previousFirstGuardPosition = Vector3.zero;
            _firstSimWasParty = false;
            _firstSimName = null;
            _firstSimStableKey = null;
            _simPlayer = null;
            _simNpc = null;
            _previousSimTarget = null;
            _previousPlayerTarget = null;
            _previousNpcProc = null;
            _previousNpcProcChance = 0f;
            _previousGuardSpot = false;
            _previousGuardPosition = Vector3.zero;
            _simWasParty = false;
            _simName = null;
            _simStableKey = null;
            _scene = null;
            _sceneHandle = 0;
            _playerHp = _simHp = _playerMax = _simMax = 0;
            _playerRealHp = _simRealHp = 0;
            LastInterferenceLog.Clear();
            AllowedDuelPets.Clear();
            EngagedPets.Clear();
            PlayerInitialEffects.Clear();
            SimInitialEffects.Clear();
            PlayerInitialEffectState.Clear();
            SimInitialEffectState.Clear();
            PlayerWorldEffectSlots.Clear();
            SimWorldEffectSlots.Clear();
            _playerInitialSpellShield = 0;
            _simInitialSpellShield = 0;
            _playerInitialLastHitBy = null;
            _simInitialLastHitBy = null;
            _playerInitialRecentDmg = _simInitialRecentDmg = 0f;
            _playerInitialRecentDmgByPlayer = _simInitialRecentDmgByPlayer = 0f;
            InitialNearbyEnemyMembership.Clear();
            _playerInitiallyHadSimEnemy = false;
            _lastCountdown = 0;
            _stateStartedAt = 0f;
            _cancellationLogged = false;
            _cancellationReasonToken = null;
            _cachedIntegrationCampActive = false;
            _nextIntegrationCampCheck = 0f;
        }

        private static void NotifyDuelEvent(DuelSemanticEvent value, int importance, bool importantMemory, double baseChance)
        {
            if (value == null) return;
            try { PracticeDuelEvents.Publish(value); } catch { }
            try { DeepSimsCompatibility.NotifyDuelEvent(value, importance, importantMemory, baseChance); } catch { }
        }

        private static void Say(string message, string color)
        {
            try { if (ErenshorDuelPlugin.Instance != null) ErenshorDuelPlugin.Instance.Chat(message, color); } catch { }
        }
    }

    [HarmonyPatch(typeof(Character), "DamageMe")]
    internal static class DuelPhysicalDamagePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance, int __0, ref bool __1, Character __3, ref int __result, ref DuelController.NativeDamageState __state)
        {
            return DuelController.PrepareNativeDamage(__instance, __3, __0, __1, ref __result, ref __state, "Harmony.Character.DamageMe");
        }
        [HarmonyPostfix]
        private static void Postfix(ref int __result, DuelController.NativeDamageState __state)
        {
            DuelController.FinishNativeDamage(__state, __result);
        }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DuelController.NativeDamageState __state)
        {
            DuelController.RestoreNativeHitState(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Character), "MagicDamageMe")]
    internal static class DuelMagicDamagePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance, int __0, ref bool __1, Character __3, ref int __result, ref DuelController.NativeDamageState __state)
        {
            return DuelController.PrepareNativeDamage(__instance, __3, __0, __1, ref __result, ref __state, "Harmony.Character.MagicDamageMe");
        }
        [HarmonyPostfix]
        private static void Postfix(ref int __result, DuelController.NativeDamageState __state)
        {
            DuelController.FinishNativeDamage(__state, __result);
        }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DuelController.NativeDamageState __state)
        {
            DuelController.RestoreNativeHitState(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Character), "BleedDamageMe")]
    internal static class DuelBleedDamagePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance, int __0, ref bool __1, Character __2, ref int __result, ref DuelController.NativeDamageState __state)
        {
            return DuelController.PrepareNativeDamage(__instance, __2, __0, __1, ref __result, ref __state, "Harmony.Character.BleedDamageMe");
        }
        [HarmonyPostfix]
        private static void Postfix(ref int __result, DuelController.NativeDamageState __state)
        {
            DuelController.FinishNativeDamage(__state, __result);
        }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DuelController.NativeDamageState __state)
        {
            DuelController.RestoreNativeHitState(__state);
            return __exception;
        }
    }

    // These four paths write Stats.CurrentHP directly through ReduceHP instead of passing through
    // DamageMe/MagicDamageMe/BleedDamageMe. Leaving any of them native would let a practice duel
    // alter or kill a participant's real health.
    [HarmonyPatch(typeof(Character), "SelfDamageMe")]
    internal static class DuelPercentSelfDamagePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance, float __0, ref int __result)
        {
            int amount = 0;
            try { amount = Mathf.RoundToInt(__instance.MyStats.CurrentMaxHP * __0 / 100f); } catch { }
            return DuelController.HandleSelfDamage(__instance, amount, ref __result, "Harmony.Character.SelfDamageMe");
        }
    }

    [HarmonyPatch(typeof(Character), "SelfDamageMeFlat")]
    internal static class DuelFlatSelfDamagePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance, int __0, ref int __result)
        {
            return DuelController.HandleSelfDamage(__instance, __0, ref __result, "Harmony.Character.SelfDamageMeFlat");
        }
    }

    [HarmonyPatch(typeof(Character), "EnvironmentalDamageMe")]
    internal static class DuelEnvironmentalDamagePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance)
        {
            return DuelController.HandleEnvironmentalDamage(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "DamageShieldTaken")]
    internal static class DuelDamageShieldPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __instance, int __0, Stats __1, ref DuelController.StandaloneWorldDamageState __state)
        {
            return DuelController.BeginDamageShield(__instance, __0, __1, ref __state);
        }
        [HarmonyPostfix]
        private static void Postfix(DuelController.StandaloneWorldDamageState __state)
        {
            DuelController.FinishDamageShield(__state);
        }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DuelController.StandaloneWorldDamageState __state)
        {
            DuelController.FinishDamageShield(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Stats), "ReduceHP")]
    internal static class DuelNativeReduceHpCapturePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, int __0, ref bool __result)
        {
            return DuelController.CaptureNativeReduceHp(__instance, __0, ref __result);
        }
    }

    [HarmonyPatch(typeof(NPC), "AggroOn")]
    internal static class DuelAggroOnPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance, Character __0) { return DuelController.AllowAggro(__instance, __0, "Harmony.NPC.AggroOn"); }
    }

    [HarmonyPatch(typeof(NPC), "ForceAggroOn")]
    internal static class DuelForceAggroPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance, Character __0) { return DuelController.AllowAggro(__instance, __0, "Harmony.NPC.ForceAggroOn"); }
    }

    [HarmonyPatch(typeof(NPC), "ManageAggro")]
    internal static class DuelManageAggroPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance, Character __1) { return DuelController.AllowManageAggro(__instance, __1, "Harmony.NPC.ManageAggro"); }
    }

    [HarmonyPatch(typeof(SimPlayer), "AvoidTargetingPlayer")]
    internal static class DuelAvoidPlayerPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(SimPlayer __instance) { return !DuelController.IsDuelingSim(__instance); }
    }

    [HarmonyPatch(typeof(NPC), "ShareAggroTargetWithMyGroup")]
    internal static class DuelShareAggroPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance)
        {
            return DuelController.AllowAggroShare(__instance);
        }
    }

    // These four assign NPC.CurrentAggroTarget directly rather than routing through AggroOn /
    // ForceAggroOn / ManageAggro, so the aggro patches never see them. See
    // DuelController.BeginAssistRoutine.
    [HarmonyPatch(typeof(NPC), "CheckAssist")]
    internal static class DuelAssistPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance, ref Character __state) { DuelController.BeginAssistRoutine(__instance, ref __state); }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, NPC __instance, Character __state)
        {
            DuelController.FinishAssistRoutine(__instance, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NPC), "CheckAssistRaid")]
    internal static class DuelAssistRaidPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance, ref Character __state) { DuelController.BeginAssistRoutine(__instance, ref __state); }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, NPC __instance, Character __state)
        {
            DuelController.FinishAssistRoutine(__instance, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NPC), "ForceGroupOntoTarget")]
    internal static class DuelForceGroupTargetPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance, ref Character __state) { DuelController.BeginAssistRoutine(__instance, ref __state); }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, NPC __instance, Character __state)
        {
            DuelController.FinishAssistRoutine(__instance, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NPC), "ForceNewAggroTarget")]
    internal static class DuelForceNewAggroPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance, ref Character __state) { DuelController.BeginAssistRoutine(__instance, ref __state); }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, NPC __instance, Character __state)
        {
            DuelController.FinishAssistRoutine(__instance, __state);
            return __exception;
        }
    }

    // Both are called from inside Character.DamageMe against the attacker, so an ordinary duel swing
    // is what recruits bystanders. Suppress only the calls that name a duelist.
    [HarmonyPatch(typeof(SimPlayerGrouping), "GroupAttack", new Type[] { typeof(Character) })]
    internal static class DuelGroupAttackPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __0) { return DuelController.AllowGroupAssistCall(__0); }
    }

    [HarmonyPatch(typeof(SimPlayerIndependentGroup), "CallForAssist", new Type[] { typeof(Character) })]
    internal static class DuelCallForAssistPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Character __0) { return DuelController.AllowGroupAssistCall(__0); }
    }

    // Same masking as CheckHeals: the raid heal pass and the buff pass both re-select duelists from
    // the mirrored duel health every tick.
    [HarmonyPatch(typeof(NPC), "CheckHealsRaid")]
    internal static class DuelHealerRaidChoicePatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance, ref DuelController.HealEvaluationState __state)
        {
            DuelController.BeginDuelistHealEvaluation(__instance, ref __state);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DuelController.HealEvaluationState __state)
        {
            DuelController.FinishDuelistHealEvaluation(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NPC), "CheckBuffs")]
    internal static class DuelBuffChoicePatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance, ref DuelController.BuffEvaluationState __state)
        {
            DuelController.BeginBuffEvaluation(__instance, ref __state);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DuelController.BuffEvaluationState __state)
        {
            DuelController.FinishBuffEvaluation(__state);
            return __exception;
        }
    }

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

    [HarmonyPatch(typeof(CastSpell), "StartSpell", new Type[] { typeof(Spell), typeof(Stats) })]
    internal static class DuelPartySpellStartPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CastSpell __instance, Spell __0, Stats __1, ref bool __result, ref DuelController.SpellStartState __state)
        {
            return DuelController.BeginSpellStart(__instance, __0, __1, ref __result, "Harmony.CastSpell.StartSpell/2", ref __state);
        }

        [HarmonyPostfix]
        private static void Postfix(bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CastSpell), "StartSpell", new Type[] { typeof(Spell), typeof(Stats), typeof(float) })]
    internal static class DuelSpellStartTimedPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CastSpell __instance, Spell __0, Stats __1, ref bool __result, ref DuelController.SpellStartState __state)
        {
            return DuelController.BeginSpellStart(__instance, __0, __1, ref __result, "Harmony.CastSpell.StartSpell/3", ref __state);
        }

        [HarmonyPostfix]
        private static void Postfix(bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CastSpell), "StartSpell", new Type[] { typeof(Spell), typeof(Stats), typeof(float), typeof(bool) })]
    internal static class DuelSpellStartResonatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CastSpell __instance, Spell __0, Stats __1, ref bool __result, ref DuelController.SpellStartState __state)
        {
            return DuelController.BeginSpellStart(__instance, __0, __1, ref __result, "Harmony.CastSpell.StartSpell/4", ref __state);
        }

        [HarmonyPostfix]
        private static void Postfix(bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CastSpell), "StartSpell", new Type[] { typeof(Spell), typeof(Stats), typeof(float), typeof(bool), typeof(float) })]
    internal static class DuelSpellStartScaledPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CastSpell __instance, Spell __0, Stats __1, ref bool __result, ref DuelController.SpellStartState __state)
        {
            return DuelController.BeginSpellStart(__instance, __0, __1, ref __result, "Harmony.CastSpell.StartSpell/5", ref __state);
        }

        [HarmonyPostfix]
        private static void Postfix(bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CastSpell), "StartSpellFromProc", new Type[] { typeof(Spell), typeof(Stats), typeof(float), typeof(bool), typeof(float) })]
    internal static class DuelSpellProcStartPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CastSpell __instance, Spell __0, Stats __1, ref bool __result, ref DuelController.SpellStartState __state)
        {
            return DuelController.BeginSpellStart(__instance, __0, __1, ref __result, "Harmony.CastSpell.StartSpellFromProc", ref __state);
        }

        [HarmonyPostfix]
        private static void Postfix(bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CastSpell), "StartSpellNoAnim", new Type[] { typeof(Spell), typeof(Stats), typeof(float) })]
    internal static class DuelSpellNoAnimPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CastSpell __instance, Spell __0, Stats __1, ref bool __result, ref DuelController.SpellStartState __state)
        {
            return DuelController.BeginSpellStart(__instance, __0, __1, ref __result, "Harmony.CastSpell.StartSpellNoAnim", ref __state);
        }

        [HarmonyPostfix]
        private static void Postfix(bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, bool __result, DuelController.SpellStartState __state)
        {
            DuelController.FinishSpellStart(__state, __result);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NPC), "CheckHeals")]
    internal static class DuelHealerChoicePatch
    {
        [HarmonyPrefix]
        private static void Prefix(NPC __instance, ref DuelController.HealEvaluationState __state)
        {
            DuelController.BeginDuelistHealEvaluation(__instance, ref __state);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DuelController.HealEvaluationState __state)
        {
            DuelController.FinishDuelistHealEvaluation(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Stats), "HealMe", new Type[] { typeof(int) })]
    internal static class DuelSimpleHealPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, ref DuelController.HealCapture __state)
        {
            return DuelController.BeginSimpleHeal(__instance, ref __state);
        }

        [HarmonyPostfix]
        private static void Postfix(DuelController.HealCapture __state) { DuelController.FinishHeal(__state); }
    }

    [HarmonyPatch(typeof(Stats), "TickEffects")]
    internal static class DuelEffectTickContextPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Stats __instance, ref Character __state)
        {
            DuelController.BeginEffectTick(__instance, ref __state);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, Character __state)
        {
            DuelController.FinishEffectTick(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Stats), "HealMe", new Type[] { typeof(Spell), typeof(int), typeof(bool), typeof(bool), typeof(Character) })]
    internal static class DuelAttributedHealPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, Spell __0, bool __3, Character __4, ref int __result, ref DuelController.HealCapture __state)
        {
            return DuelController.BeginAttributedHeal(__instance, __0, __4, __3, ref __result, ref __state);
        }

        [HarmonyPostfix]
        private static void Postfix(DuelController.HealCapture __state) { DuelController.FinishHeal(__state); }
    }

    // The 3-arg overload (no explicit Character caster) is also live game API. Source resolution
    // falls back to the current cast/self-application rules. All overloads retain a small state so a
    // hostile-world effect can update the real-world baseline after native application.
    [HarmonyPatch(typeof(Stats), "AddStatusEffect", new Type[] { typeof(Spell), typeof(bool), typeof(int) })]
    internal static class DuelBasicStatusEffectPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, Spell __0, ref int __result, ref DuelController.StatusEffectIngressState __state)
        {
            return DuelController.BeginStatusEffect(__instance, __0, null, ref __result, "Harmony.Stats.AddStatusEffect/3", ref __state);
        }
        [HarmonyPostfix]
        private static void Postfix(DuelController.StatusEffectIngressState __state) { DuelController.FinishStatusEffect(__state); }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DuelController.StatusEffectIngressState __state)
        {
            DuelController.FinishStatusEffect(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Stats), "AddStatusEffect", new Type[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character) })]
    internal static class DuelStatusEffectPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, Spell __0, Character __3, ref int __result, ref DuelController.StatusEffectIngressState __state)
        {
            return DuelController.BeginStatusEffect(__instance, __0, __3, ref __result, "Harmony.Stats.AddStatusEffect/4", ref __state);
        }
        [HarmonyPostfix]
        private static void Postfix(DuelController.StatusEffectIngressState __state) { DuelController.FinishStatusEffect(__state); }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DuelController.StatusEffectIngressState __state)
        {
            DuelController.FinishStatusEffect(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Stats), "AddStatusEffect", new Type[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character), typeof(float) })]
    internal static class DuelTimedStatusEffectPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, Spell __0, Character __3, ref int __result, ref DuelController.StatusEffectIngressState __state)
        {
            return DuelController.BeginStatusEffect(__instance, __0, __3, ref __result, "Harmony.Stats.AddStatusEffect/5", ref __state);
        }
        [HarmonyPostfix]
        private static void Postfix(DuelController.StatusEffectIngressState __state) { DuelController.FinishStatusEffect(__state); }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DuelController.StatusEffectIngressState __state)
        {
            DuelController.FinishStatusEffect(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Stats), "AddStatusEffectNoChecks", new Type[] { typeof(Spell), typeof(bool), typeof(int), typeof(Character) })]
    internal static class DuelUncheckedStatusEffectPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stats __instance, Spell __0, Character __3, ref DuelController.StatusEffectIngressState __state)
        {
            int ignoredResult = 0;
            return DuelController.BeginStatusEffect(__instance, __0, __3, ref ignoredResult, "Harmony.Stats.AddStatusEffectNoChecks", ref __state);
        }
        [HarmonyPostfix]
        private static void Postfix(DuelController.StatusEffectIngressState __state) { DuelController.FinishStatusEffect(__state); }
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, DuelController.StatusEffectIngressState __state)
        {
            DuelController.FinishStatusEffect(__state);
            return __exception;
        }
    }

}
