using System;
using System.Reflection;

namespace ErenshorDuel
{
    internal static class DeepSimsCompatibility
    {
        private const string CampmasterApiTypeName = "ErenshorCampmaster.CampmasterApi";
        private const int SupportedCampmasterSchemaVersion = 3;

        private static bool _resolved;
        private static int _resolvedAssemblyCount;
        private static Type _pluginType;
        private static FieldInfo _instanceField;
        private static FieldInfo _directorField;
        private static MethodInfo _notifyObservedGameEvent;
        private static MethodInfo _notifyDuelEvent;
        private static Type _directorType;
        private static MethodInfo _describeCamp;
        private static Type _campmasterApiType;
        private static PropertyInfo _huntCampActiveProperty;
        private static PropertyInfo _relaxActiveProperty;

        internal struct CampStatus
        {
            internal bool HuntCampActive;
            internal bool RelaxActive;
            internal bool CampmasterApiAuthoritative;
            internal string Source;
        }

        internal static void Initialize() { Resolve(); }

        internal static void Refresh()
        {
            Reset();
            Resolve();
        }

        internal static void Reset()
        {
            _resolved = false;
            _resolvedAssemblyCount = 0;
            _pluginType = null;
            _instanceField = null;
            _directorField = null;
            _notifyObservedGameEvent = null;
            _notifyDuelEvent = null;
            _directorType = null;
            _describeCamp = null;
            _campmasterApiType = null;
            _huntCampActiveProperty = null;
            _relaxActiveProperty = null;
        }

        internal static bool IsCampActive()
        {
            return GetCampStatus().HuntCampActive;
        }

        internal static CampStatus GetCampStatus()
        {
            Resolve();

            bool hunt;
            bool relax;
            if (TryReadCampmasterApi(out hunt, out relax))
            {
                // The public Campmaster API is authoritative whenever the Hunt Camp property can be
                // read. In particular, do not fall through to human-readable Deep Sims text when the
                // API says Hunt Camp is false: Relax is a separate mode and must not block a duel.
                return new CampStatus
                {
                    HuntCampActive = hunt,
                    RelaxActive = relax,
                    CampmasterApiAuthoritative = true,
                    Source = "campmaster_api"
                };
            }

            string legacy = ReadLegacyCampDescription();
            return new CampStatus
            {
                HuntCampActive = LegacyTextMeansHuntCamp(legacy),
                RelaxActive = false,
                CampmasterApiAuthoritative = false,
                Source = string.IsNullOrWhiteSpace(legacy) ? "none" : "deep_sims_legacy"
            };
        }

        internal static string DescribeCampStatus()
        {
            CampStatus status = GetCampStatus();
            return "source=" + (status.Source ?? "none") +
                   " huntCamp=" + status.HuntCampActive +
                   " relax=" + status.RelaxActive +
                   " authoritative=" + status.CampmasterApiAuthoritative;
        }

        internal static void NotifyDuelEvent(DuelSemanticEvent value, int importance, bool importantMemory, double baseChance)
        {
            if (value == null) return;
            Resolve();

            bool structuredSucceeded = false;
            if (_notifyDuelEvent != null)
            {
                try
                {
                    _notifyDuelEvent.Invoke(null, new object[]
                    {
                        value.Type,
                        value.OpponentName,
                        value.OpponentScope,
                        value.Decision,
                        value.Outcome,
                        value.Winner,
                        value.Yielded,
                        value.ReasonToken,
                        value.Reason
                    });
                    structuredSucceeded = true;
                }
                catch { }
            }

            // Exactly one transport path per event. The generic path is compatibility fallback only
            // when the structured bridge is absent or its invocation failed.
            if (!ShouldUseGenericFallback(_notifyDuelEvent != null, structuredSucceeded)) return;
            if (_instanceField == null || _notifyObservedGameEvent == null) return;
            try
            {
                object instance = _instanceField.GetValue(null);
                if (instance == null) return;
                _notifyObservedGameEvent.Invoke(instance, new object[]
                {
                    value.Type,
                    value.ToObservedGameEventDescription(),
                    importance,
                    importantMemory,
                    baseChance
                });
            }
            catch { }
        }

        internal static string RunSelfTests()
        {
            if (!LegacyTextMeansHuntCamp("Camp mode is active in Hidden Hills."))
                return "FAIL compatibility: legacy Hunt Camp text";
            if (!LegacyTextMeansHuntCamp("Hunt Camp social context is active."))
                return "FAIL compatibility: current Hunt Camp text fallback";
            if (LegacyTextMeansHuntCamp("Relax social context is active."))
                return "FAIL compatibility: Relax must not be Hunt Camp";
            if (ResolveCampConflictForTest(true, false, "Camp mode is active") != false)
                return "FAIL compatibility: authoritative API false must outrank stale text";
            if (!ResolveCampConflictForTest(true, true, "Relax social context is active"))
                return "FAIL compatibility: authoritative Hunt Camp true";
            if (ResolveCampConflictForTest(false, false, "Relax social context is active"))
                return "FAIL compatibility: Relax fallback must remain non-blocking";
            if (!CampmasterApiShapeIsSupported(typeof(CampmasterApiFixture)))
                return "FAIL compatibility: Campmaster API property detection";
            if (CampmasterApiShapeIsSupported(typeof(BadCampmasterApiFixture)))
                return "FAIL compatibility: unsupported Campmaster API shape admitted";
            if (CampmasterApiShapeIsSupported(typeof(FutureCampmasterApiFixture)))
                return "FAIL compatibility: unknown Campmaster schema admitted";
            if (ShouldUseGenericFallback(true, true))
                return "FAIL compatibility: structured event duplicated to generic fallback";
            if (!ShouldUseGenericFallback(true, false) || !ShouldUseGenericFallback(false, false))
                return "FAIL compatibility: generic fallback availability";
            return "PASS compatibility";
        }

        private sealed class CampmasterApiFixture
        {
            public const int SchemaVersion = SupportedCampmasterSchemaVersion;
            public static bool IsHuntCampActive { get { return true; } }
            public static bool IsRelaxActive { get { return false; } }
        }

        private sealed class BadCampmasterApiFixture
        {
            public const int SchemaVersion = SupportedCampmasterSchemaVersion;
            public static string IsHuntCampActive { get { return "yes"; } }
        }

        private sealed class FutureCampmasterApiFixture
        {
            public const int SchemaVersion = SupportedCampmasterSchemaVersion + 1;
            public static bool IsHuntCampActive { get { return true; } }
            public static bool IsRelaxActive { get { return false; } }
        }

        private static bool CampmasterApiShapeIsSupported(Type api)
        {
            if (api == null) return false;
            const BindingFlags staticPublic = BindingFlags.Public | BindingFlags.Static;
            FieldInfo schema = api.GetField("SchemaVersion", staticPublic);
            PropertyInfo hunt = api.GetProperty("IsHuntCampActive", staticPublic);
            PropertyInfo relax = api.GetProperty("IsRelaxActive", staticPublic);
            if (schema == null || schema.FieldType != typeof(int)) return false;
            int schemaVersion;
            try { schemaVersion = (int)schema.GetValue(null); } catch { return false; }
            return schemaVersion == SupportedCampmasterSchemaVersion &&
                   hunt != null && hunt.PropertyType == typeof(bool) &&
                   (relax == null || relax.PropertyType == typeof(bool));
        }

        private static bool TryReadCampmasterApi(out bool hunt, out bool relax)
        {
            hunt = false;
            relax = false;
            if (_campmasterApiType == null || _huntCampActiveProperty == null ||
                _huntCampActiveProperty.PropertyType != typeof(bool)) return false;
            try
            {
                hunt = (bool)_huntCampActiveProperty.GetValue(null, null);
                if (_relaxActiveProperty != null && _relaxActiveProperty.PropertyType == typeof(bool))
                {
                    try { relax = (bool)_relaxActiveProperty.GetValue(null, null); }
                    catch { relax = false; }
                }
                return true;
            }
            catch { return false; }
        }

        private static string ReadLegacyCampDescription()
        {
            if (_instanceField == null || _directorField == null) return string.Empty;
            try
            {
                object instance = _instanceField.GetValue(null);
                object director = instance == null ? null : _directorField.GetValue(instance);
                if (director == null) return string.Empty;
                Type currentType = director.GetType();
                if (_directorType != currentType)
                {
                    _directorType = currentType;
                    _describeCamp = currentType.GetMethod("DescribeCamp",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                }
                return _describeCamp == null ? string.Empty : (_describeCamp.Invoke(director, null) as string ?? string.Empty);
            }
            catch { return string.Empty; }
        }

        internal static bool LegacyTextMeansHuntCamp(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return false;
            string value = state.Trim();
            return value.StartsWith("Camp mode is active", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("Hunt Camp social context is active", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ResolveCampConflictForTest(bool authoritativeApiAvailable, bool huntCampActive, string legacyText)
        {
            return authoritativeApiAvailable ? huntCampActive : LegacyTextMeansHuntCamp(legacyText);
        }

        private static bool ShouldUseGenericFallback(bool structuredAvailable, bool structuredSucceeded)
        {
            return !structuredAvailable || !structuredSucceeded;
        }

        private static void Resolve()
        {
            Assembly[] assemblies;
            try { assemblies = AppDomain.CurrentDomain.GetAssemblies(); }
            catch { return; }

            // Optional integrations may load after Practice Duels. Avoid a per-frame scan, but retry
            // automatically whenever the AppDomain assembly count changes.
            if (_resolved && _resolvedAssemblyCount == assemblies.Length) return;
            _resolved = true;
            _resolvedAssemblyCount = assemblies.Length;

            try
            {
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Assembly assembly = assemblies[i];
                    if (assembly == null) continue;

                    if (_campmasterApiType == null)
                    {
                        Type api = null;
                        try { api = assembly.GetType(CampmasterApiTypeName, false); } catch { }
                        if (api != null && CampmasterApiShapeIsSupported(api))
                        {
                            _campmasterApiType = api;
                            const BindingFlags staticPublic = BindingFlags.Public | BindingFlags.Static;
                            _huntCampActiveProperty = api.GetProperty("IsHuntCampActive", staticPublic);
                            _relaxActiveProperty = api.GetProperty("IsRelaxActive", staticPublic);
                        }
                    }

                    Type bridgeType = null;
                    try { bridgeType = assembly.GetType("ErenshorDeepSims.DuelEventBridge", false); } catch { }
                    if (bridgeType != null && _notifyDuelEvent == null)
                    {
                        _notifyDuelEvent = bridgeType.GetMethod("NotifyDuelEvent",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new Type[]
                            {
                                typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                                typeof(string), typeof(string), typeof(string), typeof(string)
                            },
                            null);
                    }

                    Type candidate = null;
                    try { candidate = assembly.GetType("ErenshorDeepSims.DeepSimsPlugin", false); } catch { }
                    if (candidate == null || _pluginType != null) continue;
                    _pluginType = candidate;
                    _instanceField = candidate.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    _directorField = candidate.GetField("_director", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    _notifyObservedGameEvent = candidate.GetMethod("NotifyObservedGameEvent",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(string), typeof(string), typeof(int), typeof(bool), typeof(double) },
                        null);
                }
            }
            catch { }
        }
    }
}
