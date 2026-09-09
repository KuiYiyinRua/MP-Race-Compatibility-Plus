using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class Determinism
    {
        internal static void Monolyn(Harmony harmony)
        {
            foreach (string name in new[]{"ASEL.CompLightConduit", "ASEL.CompLightReceiver"})
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName(name), "GenerateCode"), prefix:new HarmonyMethod(typeof(Determinism), nameof(Code)));
        }
        static bool Code(ThingComp __instance)
        {
            if (!MP.IsInMultiplayer) return true;
            bool conduit=__instance.GetType().Name=="CompLightConduit";
            var field=AccessTools.Field(__instance.GetType(), conduit?"customLabel":"Code");
            if (field.GetValue(__instance)==null)
            {
                var random=new Random(__instance.parent.thingIDNumber);
                string code=((char)random.Next(65,91)).ToString()+random.Next(100,1000);
                field.SetValue(__instance,conduit?__instance.parent.def.label+" "+code:code);
            }
            return false;
        }
        internal static void Voiceroid(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("VoiceroidAsAnimal.Verb_NineTailShoot+<>c"),"<ChangeProjectile>b__2_0"),prefix:new HarmonyMethod(typeof(Determinism),nameof(GuidKey)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("VoiceroidAsAnimal.VAA_ChangeBodyGraphic+<>c"),"<AddSkillParms>b__7_0"),prefix:new HarmonyMethod(typeof(Determinism),nameof(GuidKey)));
        }
        static bool GuidKey(ref Guid __result)
        {
            if (!MP.IsInMultiplayer) return true;
            var bytes=new byte[16];
            for(int i=0;i<4;i++) Array.Copy(BitConverter.GetBytes(Rand.Int),0,bytes,i*4,4);
            __result=new Guid(bytes);
            return false;
        }
    }
}
