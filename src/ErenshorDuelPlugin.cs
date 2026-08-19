using System;
using Lunaris;
using Lunaris.Config;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using ForgottenRoads.StandaloneUi;

namespace ErenshorDuel
{
    [LunarisPlugin(PluginGuid, PluginVersion, "forgetwhtuno",
        "Friendly, non-lethal simulated sparring between the player and a local Sim, or between two local Sims while the player watches, using virtualized health inside a bounded duel.")]
    [LunarisPermission(LunarisPermission.Reflection | LunarisPermission.Harmony)]
    public sealed class ErenshorDuelPlugin : LunarisPlugin
    {
        internal const string PluginGuid = "forgetwhtuno.erenshor.practice-duels";
        internal const string PluginVersion = "0.4.17";
        internal static ErenshorDuelPlugin Instance;
        private Harmony _harmony;
        private DuelSettings _settings;
        internal static bool VerboseDiagnostics { get; private set; }
        private bool _runtimeHooksReady;
        private string _runtimeHookFailure = string.Empty;
        private static bool _sceneHooksInstalled;
        private string _pendingControlChallenge;
        private bool _pendingControlStop;
        private DuelSuiteAuraProvider _auraProvider;

        // Launcher/control-surface readiness must match the rest of the Forgotten Roads suite.
        // Merely having PlayerControl/Myself is too early: those persistent objects can exist while
        // the destination scene and Sim systems are still initializing during character entry.
        private const float UiStableReadySeconds = 1.0f;
        private static float _uiRawReadySince = -1f;
        private static int _uiReadySceneHandle = int.MinValue;
        private static bool _uiCanMoveLatched;
        private static bool _uiReadyAcquired;

        private void Awake()
        {
            Instance = this;
            _settings = new DuelSettings();
            Config.Register(ref _settings);
            VerboseDiagnostics = _settings.DiagnosticsVerbose;
            _harmony = new Harmony(PluginGuid);
            try
            {
                _harmony.PatchAll();
                _runtimeHooksReady = true;
                _runtimeHookFailure = string.Empty;
            }
            catch (Exception ex)
            {
                _runtimeHooksReady = false;
                _runtimeHookFailure = ex.GetType().Name;
                try { _harmony.UnpatchSelf(); } catch { }
                Logging.LogError("Practice Duels runtime hooks unavailable (" + _runtimeHookFailure + "). Duel gameplay is disabled, but the standalone status UI will remain available.");
            }
            DeepSimsCompatibility.Initialize();
            CoopCompatibility.Refresh();
            if (_runtimeHooksReady && !_sceneHooksInstalled)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                _sceneHooksInstalled = true;
            }

            // Optional Suite Hub transport adapter. Never assumed present; registration failure
            // must never block normal standalone duel commands.
            try
            {
                _auraProvider = new DuelSuiteAuraProvider();
                _auraProvider.Register(this);
            }
            catch (Exception ex) { Logging.LogError("Duel Suite Aura provider setup failed: " + ex); }

            Logging.LogInfo("Practice Duels " + PluginVersion + " loaded. Use /eduel <SimName>, /eduel <Sim A> vs <Sim B>, /eduel nearby, /eduel status, /eduel diag, /eduel selftest, or /eduel stop.");
            StandaloneFallbackUi.Initialize(this, "duel", "PRACTICE DUEL",
                "Select a Sim for the full Sim Actions surface, or challenge the first eligible nearby Sim here.\n" +
                "Sim vs Sim: /eduel <Sim A> vs <Sim B>",
                StandaloneLauncherColumnPolicy.DefaultX(),
                StandaloneLauncherColumnPolicy.DefaultY(StandaloneLauncherColumnPolicy.SlotIndex),
                DuelControlApi.GetStatus,
                new FallbackAction("Challenge Nearby", ChallengeFirstEligible, delegate { return DuelControlApi.GetBasicState().CanStart && (DuelControlApi.GetBasicState().EligibleNames ?? new string[0]).Length > 0; }),
                new FallbackAction("Stop Duel", DuelControlApi.TryStop, delegate { return DuelControlApi.GetBasicState().Active; }));
            // Compact workspace tuning: the guide text alone is two lines, and status can grow to
            // list eligible Sim names, so this keeps more headroom than Follow's. Default panel
            // position sits in the shared right-side workspace below the launcher column - above
            // the combat/chat log, not overlapping it - instead of the old lower-center default.
            StandaloneFallbackUi.ConfigureWorkspaceDefaults(68f,
                StandaloneLauncherColumnPolicy.DefaultPanelRightNormalized(),
                StandaloneLauncherColumnPolicy.DefaultPanelTopNormalized(),
                StandaloneLauncherColumnPolicy.SlotIndex);
        }
        private static bool ChallengeFirstEligible()
        { string[] names = DuelControlApi.GetBasicState().EligibleNames ?? new string[0]; return names.Length > 0 && DuelControlApi.TryChallenge(names[0]); }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ResetDuelUiReadiness();
            // A queued Hub challenge is actor-name based and must never survive a zone boundary.
            _pendingControlChallenge = null;
            DuelController.HandleSceneTransition();
            DeepSimsCompatibility.Refresh();
            CoopCompatibility.Refresh();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            ResetDuelUiReadiness();
            _pendingControlChallenge = null;
            DuelController.HandleSceneTransition();
        }

        private void OnApplicationQuit()
        {
            // Real CurrentHP is mirrored to virtual health while fighting. Restore it before the
            // application can run ordinary disconnect/quit save paths.
            DuelController.Stop(null);
        }

        private void Update()
        {
            StandaloneFallbackUi.Tick(DuelUiReady());
            if (!_runtimeHooksReady)
            {
                _pendingControlChallenge = null;
                _pendingControlStop = false;
                return;
            }
            try { DuelSimActionsFallback.Tick(); } catch { }
            try
            {
                if (_pendingControlStop) { _pendingControlStop = false; DuelController.Stop("Practice duel stopped from Suite Hub."); }
                if (!string.IsNullOrWhiteSpace(_pendingControlChallenge))
                {
                    string requested = _pendingControlChallenge; _pendingControlChallenge = null;
                    bool ambiguous; SimPlayer sim = DuelController.FindSim(requested, out ambiguous);
                    if (!ambiguous && sim != null && DuelController.CanStartNewDuel) DuelController.Start(sim, DuelRequestOrigin.ExplicitPlayer);
                }
                DuelController.Tick();
            }
            catch (Exception ex)
            {
                Logging.LogError("Practice duel update failed: " + ex);
                DuelController.Cancel("Update.Exception", null, null, null,
                    "Duel cancelled after an internal error: " + ex.GetType().Name + ".");
            }
        }

        private void OnDestroy()
        {
            StandaloneFallbackUi.Dispose();
            try { DuelSimActionsFallback.Shutdown(); } catch { }
            _pendingControlChallenge = null; _pendingControlStop = false;
            DuelController.Shutdown();
            try { if (_harmony != null) _harmony.UnpatchSelf(); } catch { }
            if (_sceneHooksInstalled)
            {
                try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
                try { SceneManager.sceneUnloaded -= OnSceneUnloaded; } catch { }
                _sceneHooksInstalled = false;
            }
            try { if (_auraProvider != null) _auraProvider.Unregister(); } catch { }
            _auraProvider = null;
            DeepSimsCompatibility.Reset();
            CoopCompatibility.Reset();
            DuelFollowCompatibility.Reset();
            ResetDuelUiReadiness();
            VerboseDiagnostics = false;
            _settings = null;
            if (Instance == this) Instance = null;
        }

        // internal (not private): DuelSimActionsFallback shares the exact same stable-world gate as
        // the standalone launcher. PlayerControl/Myself alone is not sufficient during character
        // entry because those persistent objects can exist before the active zone/Sim systems are
        // actually usable. Match the suite's canonical readiness acquisition semantics: prove the
        // world graph, observe CanMove at least once, then remain stable for one second.
        internal static bool DuelUiReady()
        {
            if (!RawDuelUiReady())
            {
                ResetDuelUiReadiness();
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (_uiReadySceneHandle != scene.handle)
            {
                _uiReadySceneHandle = scene.handle;
                _uiRawReadySince = Time.unscaledTime;
                _uiCanMoveLatched = false;
                _uiReadyAcquired = false;
            }
            if (_uiRawReadySince < 0f) _uiRawReadySince = Time.unscaledTime;

            if (_uiReadyAcquired) return true;

            try
            {
                if (GameData.PlayerControl != null && GameData.PlayerControl.CanMove)
                    _uiCanMoveLatched = true;
            }
            catch { }

            if (!_uiCanMoveLatched || Time.unscaledTime - _uiRawReadySince < UiStableReadySeconds)
                return false;

            _uiReadyAcquired = true;
            return true;
        }

        private static bool RawDuelUiReady()
        {
            try
            {
                if (GameData.InCharSelect || GameData.Zoning) return false;
                if (GameData.PlayerControl == null || GameData.PlayerControl.Myself == null) return false;

                Character player = GameData.PlayerControl.Myself;
                if (player.MyStats == null || player.gameObject == null || !player.gameObject.activeInHierarchy)
                    return false;

                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded) return false;

                if (GameData.SimMngr == null || GameData.SimPlayerGrouping == null || GameData.GroupMembers == null)
                    return false;

                return true;
            }
            catch { return false; }
        }

        private static void ResetDuelUiReadiness()
        {
            _uiRawReadySince = -1f;
            _uiReadySceneHandle = int.MinValue;
            _uiCanMoveLatched = false;
            _uiReadyAcquired = false;
        }

        internal void Chat(string message, string color)
        {
            try { UpdateSocialLog.LogAdd(message, color); }
            catch { try { UpdateSocialLog.LogAdd(message); } catch { } }
        }

        internal bool RuntimeHooksReady { get { return _runtimeHooksReady; } }
        internal string RuntimeHookFailure { get { return _runtimeHookFailure; } }

        internal bool RequestControlChallenge(string simName)
        {
            if (!_runtimeHooksReady || string.IsNullOrWhiteSpace(simName) || !DuelController.CanStartNewDuel) return false;
            _pendingControlChallenge = simName.Trim(); _pendingControlStop = false; return true;
        }
        internal bool RequestControlStop()
        {
            if (!_runtimeHooksReady) return !DuelController.Active;
            _pendingControlStop = true; _pendingControlChallenge = null; return true;
        }

        internal void Diagnostic(string message)
        {
            // Forensic duel diagnostics can fire once per hit/spell/effect. Keep that developer
            // observability available, but do not synchronously format/write it during normal play.
            if (!VerboseDiagnostics) return;
            Logging.LogDebug(message);
        }

        internal void LifecycleDiagnostic(string message)
        {
            // Low-frequency state transitions remain visible even with verbose diagnostics off, so a
            // live report can still prove Preparing/Countdown/Active/Cleaning/Idle and terminal cleanup.
            Logging.LogDebug(message);
        }

        internal bool Handle(TypeText typeText, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string command = raw.Trim();
            if (!command.StartsWith("/eduel", StringComparison.OrdinalIgnoreCase) ||
                (command.Length > 6 && !char.IsWhiteSpace(command[6]))) return false;
            string argument = command.Length == 6 ? string.Empty : command.Substring(6).Trim();
            try { typeText.typed.text = string.Empty; } catch { }

            if (argument.Equals("stop", StringComparison.OrdinalIgnoreCase) || argument.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                DuelController.Stop("Practice duel stopped.");
                return true;
            }
            if (argument.Length == 0)
            {
                Chat("[Practice Duel] Usage: /eduel <SimName>, /eduel <Sim A> vs <Sim B>, /eduel nearby, /eduel status, /eduel diag, /eduel selftest, or /eduel stop", "yellow");
                return true;
            }
            if (argument.Equals("selftest", StringComparison.OrdinalIgnoreCase))
            {
                string result = DuelSelfTests.RunAll();
                Chat("[Practice Duel] " + result, "lightblue");
                Diagnostic("[Practice Duel] selftest=" + result);
                return true;
            }
            if (argument.Equals("nearby", StringComparison.OrdinalIgnoreCase))
            {
                Chat(DuelController.NearbySummary(), "lightblue");
                return true;
            }
            if (argument.Equals("diag", StringComparison.OrdinalIgnoreCase))
            {
                Chat(DuelController.DiagSummary(), "lightblue");
                return true;
            }
            if (argument.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                Chat(DuelController.Status(), "lightblue");
                return true;
            }
            if (argument.Equals("diag", StringComparison.OrdinalIgnoreCase) || argument.Equals("diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                string result = DuelController.Diagnostics();
                Chat(result, "lightblue");
                Diagnostic(result);
                return true;
            }

            bool explicitWatch = argument.StartsWith("watch ", StringComparison.OrdinalIgnoreCase);
            string pairing = explicitWatch ? argument.Substring(6).Trim() : argument;
            int versus = pairing.IndexOf(" vs ", StringComparison.OrdinalIgnoreCase);
            if (explicitWatch || versus >= 0)
            {
                if (versus <= 0 || versus + 4 >= pairing.Length)
                {
                    Chat("[Practice Duel] Usage: /eduel <Sim A> vs <Sim B>", "yellow");
                    return true;
                }
                string firstName = pairing.Substring(0, versus).Trim();
                string secondName = pairing.Substring(versus + 4).Trim();
                bool firstAmbiguous;
                bool secondAmbiguous;
                SimPlayer first = DuelController.FindSim(firstName, out firstAmbiguous);
                SimPlayer second = DuelController.FindSim(secondName, out secondAmbiguous);
                if (first == null || second == null)
                {
                    Chat(firstAmbiguous || secondAmbiguous
                        ? "[Practice Duel] A Sim name is ambiguous. Type longer names."
                        : "[Practice Duel] Both Sims must be living, local, in this scene, and within 25m of you.", "yellow");
                    return true;
                }
                DuelController.StartSpectator(first, second, DuelRequestOrigin.ExplicitPlayer);
                return true;
            }

            bool ambiguous;
            SimPlayer sim = DuelController.FindSim(argument, out ambiguous);
            if (sim == null)
            {
                Chat(ambiguous
                    ? "[Practice Duel] More than one nearby Sim matches that name. Type a longer name."
                    : "[Practice Duel] No living same-scene local SimPlayer matched within 25m. Use /eduel nearby or /eduel diag for status.", "yellow");
                return true;
            }
            DuelController.Start(sim, DuelRequestOrigin.ExplicitPlayer);
            return true;
        }
    }

    [HarmonyPatch(typeof(TypeText), "CheckCommands")]
    internal static class DuelChatPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(TypeText __instance)
        {
            try
            {
                return ErenshorDuelPlugin.Instance == null || __instance == null || __instance.typed == null ||
                       !ErenshorDuelPlugin.Instance.Handle(__instance, __instance.typed.text);
            }
            catch { return true; }
        }
    }
}
