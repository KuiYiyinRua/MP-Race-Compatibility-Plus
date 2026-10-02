using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    /// <summary>
    /// Ariandel's map music player runs from MapComponentTick, but whether it
    /// switches songs depends on each peer's local audio state and current map.
    /// MusicManagerPlay.Stop/ForcePlaySong use Verse Rand. Keep those draws out
    /// of the shared map stream without changing the component's audio behavior.
    /// </summary>
    internal static class AriandelMusicRandom
    {
        internal static void Apply(Harmony harmony)
        {
            var component = Bootstrap.Type("AriandelLibrary.CustomMapComponent_PlayMusic");
            var extension = Bootstrap.Type("AriandelLibrary.AL_ModExtension_MapCompPlayMusic");
            var songs = typeof(List<SongDef>);
            var play = Bootstrap.Method(component, "PlayNextSong",
                typeof(MusicManagerPlay), extension, songs);
            var release = Bootstrap.Method(component, "ReleaseMusicControlIfNeeded",
                extension, songs, typeof(bool));
            var prefix = new HarmonyMethod(typeof(AriandelMusicRandom), nameof(Before));
            var finalizer = new HarmonyMethod(typeof(AriandelMusicRandom), nameof(After));
            harmony.Patch(play, prefix: prefix, finalizer: finalizer);
            harmony.Patch(release, prefix: prefix, finalizer: finalizer);
        }

        private static void Before(out bool __state)
        {
            __state = Multiplayer.API.MP.IsInMultiplayer;
            if (__state) Rand.PushState();
        }

        private static void After(bool __state)
        {
            if (__state) Rand.PopState();
        }
    }
}
