using System;
using Lunaris;
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
        internal const string PluginVersion = "0.4.6";
        internal static ErenshorDuelPlugin Instance;
        private Harmony _harmony;
        private bool _runtimeHooksReady;
        private string _runtimeHookFailure = string.Empty;
        private static bool _sceneHooksInstalled;
        private string _pendingControlChallenge;
        private bool _pendingControlStop;
        private DuelSuiteAuraProvider _auraProvider;

        private void Awake()
        {
            Instance = this;
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
                "Select a Sim for the full Sim Actions surface, or challenge the first eligible nearby Sim here.", 160f,
                DuelControlApi.GetStatus,
                new FallbackAction("Challenge Nearby", ChallengeFirstEligible, delegate { return DuelControlApi.GetBasicState().CanStart && (DuelControlApi.GetBasicState().EligibleNames ?? new string[0]).Length > 0; }),
                new FallbackAction("Stop Duel", DuelControlApi.TryStop, delegate { return DuelControlApi.GetBasicState().Active; }));
        }
        private static bool ChallengeFirstEligible()
        { string[] names = DuelControlApi.GetBasicState().EligibleNames ?? new string[0]; return names.Length > 0 && DuelControlApi.TryChallenge(names[0]); }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // A queued Hub challenge is actor-name based and must never survive a zone boundary.
            _pendingControlChallenge = null;
            DuelController.HandleSceneTransition();
            DeepSimsCompatibility.Refresh();
            CoopCompatibility.Refresh();
        }

        private void OnSceneUnloaded(Scene scene)
        {
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
            try
            {
                if (_pendingControlStop) { _pendingControlStop = false; DuelController.Stop("Practice duel stopped from Suite Hub."); }
                if (!string.IsNullOrWhiteSpace(_pendingControlChallenge))
                {
                    string requested = _pendingControlChallenge; _pendingControlChallenge = null;
                    bool ambiguous; SimPlayer sim = DuelController.FindSim(requested, out ambiguous);
                    if (!ambiguous && sim != null && DuelController.CanStartNewDuel) DuelController.Start(sim);
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
            if (Instance == this) Instance = null;
        }

        private static bool DuelUiReady()
        {
            try { return !GameData.InCharSelect && !GameData.Zoning && GameData.PlayerControl != null && GameData.PlayerControl.Myself != null; }
            catch { return false; }
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
            // Duel lifecycle diagnostics are expected observability, not warning conditions.
            // Exceptions and actual patch failures continue to use error logging at their call sites.
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
                DuelController.StartSpectator(first, second);
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
            DuelController.Start(sim);
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
