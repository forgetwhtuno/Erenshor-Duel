using System;
using System.Text;
using Lunaris;
using Lunaris.IPC;

namespace ErenshorDuel
{
    // Thin, optional Lunaris Aura transport adapter over the authoritative DuelControlApi.
    // Erenshor-Three-Audit-Integration-Handoff/CONTRACT_RECONCILIATION.md: Hub speaks Aura only
    // and never reflects into private mod state; this class owns nothing beyond
    // formatting/parsing the bounded wire payloads and forwarding to DuelControlApi. No
    // compile-time reference to ErenshorSuiteHub.dll, no duel gameplay/eligibility logic
    // duplicated here. Duel has no dedicated panel and no settings, per SUITE_UI_MOD_CONTRACT.md.
    internal sealed class DuelSuiteAuraProvider
    {
        private const string Prefix = "forgetwhtuno.erenshor.suite." + DuelControlApi.ModuleId + ".v1.";

        private IAuraProvider<string> _describe;
        private IAuraProvider<string, string, string> _action;
        private string _version = "0.0.0";
        private ILog _log;

        internal bool Registered { get; private set; }

        internal void Register(LunarisPlugin owner)
        {
            if (owner == null) return;
            _log = owner.Logging;
            try
            {
                LunarisPluginAttribute attr = Attribute.GetCustomAttribute(owner.GetType(), typeof(LunarisPluginAttribute)) as LunarisPluginAttribute;
                if (attr != null && !string.IsNullOrEmpty(attr.Version)) _version = attr.Version;

                _describe = owner.IPCAuraProvider<string>(Prefix + "describe");
                _describe.RegisterFunc(Describe);

                // action  Func<string actionId, string argument, string result>
                // challenge takes the verified local Sim name as its argument; stop takes none.
                _action = owner.IPCAuraProvider<string, string, string>(Prefix + "action");
                _action.RegisterFunc(InvokeAction);

                Registered = true;
            }
            catch (Exception ex)
            {
                Registered = false;
                if (_log != null) { try { _log.LogError("[Erenshor Duel] Suite Aura provider registration failed: " + ex.GetType().Name); } catch { } }
                Unregister();
            }
        }

        // Provider lifecycle contract: explicitly unregister every Aura handler on OnDestroy so
        // Hub sees this module disappear immediately rather than calling into a torn-down plugin.
        internal void Unregister()
        {
            SafeUnregister(_describe); _describe = null;
            SafeUnregister(_action); _action = null;
            Registered = false;
        }

        private static void SafeUnregister(IAuraProvider provider)
        {
            if (provider == null) return;
            try { provider.UnregisterFunc(); } catch { }
        }

        private string Describe()
        {
            try
            {
                DuelControlState s = DuelControlApi.GetBasicState();
                StringBuilder sb = new StringBuilder(256);
                AppendField(sb, "protocol", "1");
                AppendField(sb, "module", DuelControlApi.ModuleId);
                AppendField(sb, "display", "Erenshor Duel");
                AppendField(sb, "version", _version);
                AppendField(sb, "summary", s.Active ? "Duel in progress" : "No active duel");
                AppendField(sb, "status", DuelControlApi.GetStatus());
                AppendField(sb, "eligibleCount", s.EligibleNames == null ? "0" : s.EligibleNames.Length.ToString());
                AppendField(sb, "actions", "challenge,stop");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "protocol=1&module=" + DuelControlApi.ModuleId + "&display=Erenshor+Duel&version=" +
                    Uri.EscapeDataString(_version) + "&warning=" + Uri.EscapeDataString(ex.GetType().Name);
            }
        }

        // Every mutating call is revalidated by DuelControlApi/DuelController (eligibility,
        // locality, distance, active-state); Hub is not authorization. "ok" means accepted, not
        // necessarily synchronously completed - the challenge is consumed on the plugin's Update
        // path, not inside this call.
        private string InvokeAction(string actionId, string argument)
        {
            try
            {
                if (string.Equals(actionId, "challenge", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(argument)) return "missing argument";
                    return DuelControlApi.TryChallenge(argument) ? "ok" : "rejected";
                }
                if (string.Equals(actionId, "stop", StringComparison.Ordinal))
                    return DuelControlApi.TryStop() ? "ok" : "rejected";
                return "unknown action";
            }
            catch (Exception ex) { return "error:" + ex.GetType().Name; }
        }

        private static void AppendField(StringBuilder sb, string key, string value)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(key).Append('=').Append(Uri.EscapeDataString(value ?? string.Empty));
        }
    }
}
