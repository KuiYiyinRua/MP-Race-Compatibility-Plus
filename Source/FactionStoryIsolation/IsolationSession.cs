using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Meow.FactionStoryIsolation
{
    // Only the host boundary reads local preferences. Loading/joining always reads
    // the serialized value; constructors must never import client preferences.
    public sealed class IsolationSession : GameComponent
    {
        public bool RoutingEnabled;
        public IsolationSession(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref RoutingEnabled, "meowFactionStoryRoutingEnabled", false);
        }

        internal static PropertyInfo SettingsProperty;
        internal static FieldInfo PreferenceField;

        internal static void CaptureHostPreferences(bool fromReplay)
        {
            // HostServer creates a fresh shared snapshot, including when rehosting
            // a loaded replay. Ordinary replay playback never calls this boundary.
            var state = Current.Game?.GetComponent<IsolationSession>();
            if (state == null) throw new InvalidOperationException("Faction isolation session component missing before hosting.");
            var settings = SettingsProperty.GetValue(null, null);
            if (settings == null) throw new InvalidOperationException("Faction isolation mod settings unavailable.");
            state.RoutingEnabled = (bool)PreferenceField.GetValue(settings);
            Log.Message("[Meow.FactionStoryIsolation] Host snapshot routing=" + state.RoutingEnabled + "; fromReplay=" + fromReplay + "; coverage=stage1-source-routing");
        }
    }
}
