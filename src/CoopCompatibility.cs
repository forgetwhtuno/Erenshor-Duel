using System;
using System.Reflection;

namespace ErenshorDuel
{
    internal static class CoopCompatibility
    {
        private static Type _networkedPlayer;
        private static Type _legacyNetworkedPlayer;
        private static Type _networkedSim;
        private static bool _resolved;
        private static int _resolvedAssemblyCount;

        internal static bool IsRemoteHuman(SimPlayer sim)
        {
            if (sim == null || sim.gameObject == null) return false;
            Resolve();
            try
            {
                // A Sim owned by another Coop client is not a locally-driven duel candidate either:
                // NetworkedSim.Update() unconditionally overwrites transform.position and CurrentHP
                // every frame, which fights the duel's virtual-HP mirroring.
                return (_networkedPlayer != null && sim.gameObject.GetComponent(_networkedPlayer) != null) ||
                       (_legacyNetworkedPlayer != null && sim.gameObject.GetComponent(_legacyNetworkedPlayer) != null) ||
                       (_networkedSim != null && sim.gameObject.GetComponent(_networkedSim) != null);
            }
            catch { return false; }
        }

        internal static void Refresh()
        {
            Reset();
            Resolve();
        }

        internal static void Reset()
        {
            _networkedPlayer = null;
            _legacyNetworkedPlayer = null;
            _networkedSim = null;
            _resolved = false;
            _resolvedAssemblyCount = 0;
        }

        internal static string Describe()
        {
            Resolve();
            return "networkedPlayer=" + (_networkedPlayer != null) +
                   " legacyNetworkedPlayer=" + (_legacyNetworkedPlayer != null) +
                   " networkedSim=" + (_networkedSim != null);
        }

        internal static string TargetFlags(SimPlayer sim)
        {
            Resolve();
            try
            {
                return "networkedPlayer=" + (_networkedPlayer != null && sim != null && sim.gameObject.GetComponent(_networkedPlayer) != null) +
                       " networkedSim=" + (_networkedSim != null && sim != null && sim.gameObject.GetComponent(_networkedSim) != null);
            }
            catch { return "networkedPlayer=unknown networkedSim=unknown"; }
        }

        private static void Resolve()
        {
            Assembly[] assemblies;
            try { assemblies = AppDomain.CurrentDomain.GetAssemblies(); }
            catch { return; }

            // Optional loaders can bring COOP in after Practice Duels. Cache normally, but rescan on
            // assembly-count change so load order cannot permanently disable the remote-human guard.
            if (_resolved && _resolvedAssemblyCount == assemblies.Length) return;
            _resolved = true;
            _resolvedAssemblyCount = assemblies.Length;

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null) continue;
                try
                {
                    if (_networkedPlayer == null) _networkedPlayer = assembly.GetType("ErenshorCoop.NetworkedPlayer", false);
                    if (_legacyNetworkedPlayer == null) _legacyNetworkedPlayer = assembly.GetType("ErenshorCoop.Client.NetworkedPlayer", false);
                    if (_networkedSim == null) _networkedSim = assembly.GetType("ErenshorCoop.NetworkedSim", false);
                }
                catch { }
            }
        }
    }
}
