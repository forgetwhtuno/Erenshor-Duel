using System;
using Lunaris;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorDuel
{
    [LunarisPlugin("forgetwhtuno.erenshor.practice-duels", "0.4.0", "forgetwhtuno",
        "Friendly, non-lethal simulated sparring between the player and a local Sim, or between two local Sims while the player watches, using virtualized health inside a bounded duel.")]
    [LunarisPermission(LunarisPermission.Reflection | LunarisPermission.Harmony)]
    public sealed class ErenshorDuelPlugin : LunarisPlugin
    {
        internal static ErenshorDuelPlugin Instance;
        private Harmony _harmony;
        private static bool _sceneHooksInstalled;

        private void Awake()
        {
            Instance = this;
            _harmony = new Harmony("forgetwhtuno.erenshor.practice-duels");
            _harmony.PatchAll();
            DeepSimsCompatibility.Initialize();
            CoopCompatibility.Refresh();
            if (!_sceneHooksInstalled)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                _sceneHooksInstalled = true;
            }
            Logging.LogInfo("Practice Duels loaded. Use /eduel <SimName>, /eduel <Sim A> vs <Sim B>, /eduel nearby, /eduel status, /eduel diag, /eduel selftest, or /eduel stop.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            DuelController.HandleSceneTransition();
            DeepSimsCompatibility.Refresh();
            CoopCompatibility.Refresh();
        }

        private void OnSceneUnloaded(Scene scene) { DuelController.HandleSceneTransition(); }

        private void OnApplicationQuit()
        {
            // Real CurrentHP is mirrored to virtual health while fighting. Restore it before the
            // application can run ordinary disconnect/quit save paths.
            DuelController.Stop(null);
        }

        private void Update()
        {
            try { DuelController.Tick(); }
            catch (Exception ex)
            {
                Logging.LogError("Practice duel update failed: " + ex);
                DuelController.Cancel("Update.Exception", null, null, null,
                    "Duel cancelled after an internal error: " + ex.GetType().Name + ".");
            }
        }

        private void OnDestroy()
        {
            DuelController.Shutdown();
            try { if (_harmony != null) _harmony.UnpatchSelf(); } catch { }
            if (_sceneHooksInstalled)
            {
                try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
                try { SceneManager.sceneUnloaded -= OnSceneUnloaded; } catch { }
                _sceneHooksInstalled = false;
            }
            DeepSimsCompatibility.Reset();
            CoopCompatibility.Reset();
            if (Instance == this) Instance = null;
        }

        internal void Chat(string message, string color)
        {
            try { UpdateSocialLog.LogAdd(message, color); }
            catch { try { UpdateSocialLog.LogAdd(message); } catch { } }
        }

        internal void Diagnostic(string message)
        {
            Logging.LogWarning(message);
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
