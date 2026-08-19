using System;
using System.Reflection;

namespace ErenshorDuel
{
    // Detects whether Erenshor Follow's Sim Actions system is present and healthy, so Duel's own
    // standalone Sim-click fallback (DuelSimActionsFallback) can stand down rather than compete for
    // the same pointer event. No compile-time reference to Follow: resolved by reflection over the
    // loaded AppDomain, mirroring the exact caching convention DeepSimsCompatibility already uses for
    // its own optional integrations (resolve once, re-resolve only when the assembly count changes).
    internal static class DuelFollowCompatibility
    {
        private const string FollowControlApiTypeName = "ErenshorFollow.FollowControlApi";

        private static bool _resolved;
        private static int _resolvedAssemblyCount;
        private static Type _controlApiType;
        private static MethodInfo _getStatus;
        private static FieldInfo _apiVersionField;

        internal static void Reset()
        {
            _resolved = false;
            _resolvedAssemblyCount = 0;
            _controlApiType = null;
            _getStatus = null;
            _apiVersionField = null;
        }

        internal static void Refresh()
        {
            Reset();
            Resolve();
        }

        // True only when Follow's own public control surface reports itself loaded and functional.
        // Follow has no dedicated IsAvailable boolean the way DuelControlApi does, so this reads the
        // live GetStatus() string and classifies it with the same pure logic FollowControlApi's own
        // author used to distinguish "not loaded" from "loaded but broken": both count as not-healthy
        // for ownership purposes, matching this task's "absent/unhealthy/unavailable" grouping.
        //
        // Type/method resolution is cached (see Resolve()), but the status string itself is re-read on
        // every call, so a load-order change or Follow becoming healthy/unhealthy after Duel is already
        // running is always reflected on the very next check -- no stale positive or negative result.
        internal static bool IsFollowSimActionsHealthy()
        {
            Resolve();
            if (_controlApiType == null || _getStatus == null) return false;
            try
            {
                if (_apiVersionField != null)
                {
                    object version = _apiVersionField.GetValue(null);
                    if (!(version is int) || (int)version != 1) return false;
                }
                object result = _getStatus.Invoke(null, null);
                return DuelFollowCompatibilityPolicy.ClassifyStatus(result as string) ==
                    FollowHealthClassification.Healthy;
            }
            catch { return false; }
        }

        private static void Resolve()
        {
            Assembly[] assemblies;
            try { assemblies = AppDomain.CurrentDomain.GetAssemblies(); }
            catch { return; }

            // Optional integrations may load before or after Practice Duels, and in either order.
            // Avoid a per-frame scan, but retry automatically whenever the AppDomain assembly count
            // changes -- the same bounded staleness window DeepSimsCompatibility already relies on.
            if (_resolved && _resolvedAssemblyCount == assemblies.Length) return;
            _resolved = true;
            _resolvedAssemblyCount = assemblies.Length;
            _controlApiType = null;
            _getStatus = null;
            _apiVersionField = null;

            try
            {
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Assembly assembly = assemblies[i];
                    if (assembly == null) continue;
                    Type api = null;
                    try { api = assembly.GetType(FollowControlApiTypeName, false); } catch { }
                    if (api == null) continue;

                    const BindingFlags staticPublic = BindingFlags.Public | BindingFlags.Static;
                    MethodInfo getStatus = api.GetMethod("GetStatus", staticPublic, null, Type.EmptyTypes, null);
                    if (getStatus == null || getStatus.ReturnType != typeof(string)) continue;

                    _controlApiType = api;
                    _getStatus = getStatus;
                    _apiVersionField = api.GetField("ApiVersion", staticPublic);
                    return;
                }
            }
            catch { }
        }
    }
}
