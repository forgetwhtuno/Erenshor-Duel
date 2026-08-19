using Lunaris.Config;

namespace ErenshorDuel
{
    internal sealed class DuelSettings
    {
        [Config("Verbose", "Diagnostics",
            "Enable forensic per-hit/per-spell Practice Duel logging. Off by default; lifecycle transitions and real errors remain visible.")]
        public bool DiagnosticsVerbose = false;
    }
}
