using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;

namespace Meow.RaceTrioCompatibility
{
    internal static class VoiceroidUi
    {
        internal static void Apply(Harmony harmony)
        {
            MP.RegisterSyncField(AccessTools.TypeByName("VoiceroidAsAnimal_Stuffed.Building_VAAStuffed"),"mendPoint");
            var command=AccessTools.TypeByName("VoiceroidAsAnimal_Stuffed.Command_VAASetMendPoint");
            harmony.Patch(AccessTools.Method(command,"<ProcessInput>b__0_1"),prefix:new HarmonyMethod(typeof(VoiceroidUi),nameof(Begin)),finalizer:new HarmonyMethod(typeof(VoiceroidUi),nameof(End)));
        }
        static void Begin(object __instance,out bool __state)
        {
            __state=MP.IsInMultiplayer&&MP.InInterface;
            if(!__state)return;
            MP.WatchBegin();
            foreach(var stuffed in (IEnumerable)AccessTools.Field(__instance.GetType(),"stuffeds").GetValue(__instance))MP.Watch(stuffed,"mendPoint");
        }
        static Exception End(Exception __exception,bool __state){if(__state)MP.WatchEnd();return __exception;}
    }
}
