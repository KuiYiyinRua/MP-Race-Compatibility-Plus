using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using System.Linq;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynUi
    {
        internal static void Apply(Harmony harmony)
        {
            var consumer=AccessTools.TypeByName("ASEL.MonolynConsumer");
            MP.RegisterSyncField(consumer,"sliderValue");
            var slider=AccessTools.TypeByName("ASEL.Building_GravityPillar+<>c");
            harmony.Patch(AccessTools.Method(slider,"<GetGizmos>b__12_2"),prefix:new HarmonyMethod(typeof(MonolynUi),nameof(SliderPrefix)),finalizer:new HarmonyMethod(typeof(MonolynUi),nameof(Finish)));
            var menu=AccessTools.TypeByName("ASEL.ITab_MonolynRecipeList+<>c__DisplayClass27_0");
            harmony.Patch(AccessTools.Method(menu,"<FillTab>b__2"),prefix:new HarmonyMethod(typeof(MonolynUi),nameof(MenuPrefix)),finalizer:new HarmonyMethod(typeof(MonolynUi),nameof(Finish)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("ASEL.ITab_ManageConduitNetwork"),"FillTab"),prefix:new HarmonyMethod(typeof(MonolynUi),nameof(Conduits)),finalizer:new HarmonyMethod(typeof(MonolynUi),nameof(Finish)));
        }
        static void Conduits(out bool __state)
        {
            __state=MP.IsInMultiplayer&&MP.InInterface;
            if(!__state)return;
            MP.WatchBegin();
            foreach(var map in Find.Maps)
                foreach(var thing in map.listerThings.AllThings.OfType<ThingWithComps>())
                    foreach(var comp in thing.AllComps.Where(c=>c.GetType().FullName=="ASEL.CompLightConduit"))
                        MP.Watch(comp,"connectedConduit");
        }
        static void SliderPrefix(object __0,out bool __state)
        {
            __state=MP.IsInMultiplayer&&MP.InInterface;
            if(__state){MP.WatchBegin();MP.Watch(__0,"sliderValue");}
        }
        static void MenuPrefix(object __instance,out bool __state)
        {
            __state=MP.IsInMultiplayer&&MP.InInterface;
            if(!__state)return;
            var tab=AccessTools.Field(__instance.GetType(),"<>4__this").GetValue(__instance);
            var fabricator=AccessTools.Property(tab.GetType(),"fabricator").GetValue(tab);
            MP.WatchBegin();MP.Watch(fabricator,"selectedOption");
        }
        static Exception Finish(Exception __exception,bool __state){if(__state)MP.WatchEnd();return __exception;}
    }
}
