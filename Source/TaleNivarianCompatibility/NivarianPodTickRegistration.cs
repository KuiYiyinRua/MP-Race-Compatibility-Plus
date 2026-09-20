using System;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using Verse;
namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianPodTickRegistration
    {
        static Type pod;
        static Func<bool> normalizerEnabled;
        static readonly AccessTools.FieldRef<TickList,List<Thing>> Pending = AccessTools.FieldRefAccess<TickList,List<Thing>>("thingsToRegister");
        static readonly AccessTools.FieldRef<TickList,List<Thing>> Removed = AccessTools.FieldRefAccess<TickList,List<Thing>>("thingsToDeregister");
        internal static void Apply(Harmony harmony)
        {
            var normalizer=AccessTools.TypeByName("MP_MeowOnlineShop.Patch_TickListOrderNormalizer");
            if(normalizer==null)return;
            pod=AccessTools.TypeByName("Nivarian_Race.Code.NivarianThing.NivarianLotusDropPodLanded")??throw new TypeLoadException("NivarianLotusDropPodLanded");
            var enabled=AccessTools.PropertyGetter(normalizer,"EnabledAtStartup")??throw new MissingMethodException(normalizer.FullName,"EnabledAtStartup");
            normalizerEnabled=(Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>),enabled);
            var target=AccessTools.DeclaredMethod(normalizer,"TickPrefix",new[]{typeof(TickList)})??throw new MissingMethodException(normalizer.FullName,"TickPrefix");
            harmony.Patch(target,prefix:new HarmonyMethod(typeof(NivarianPodTickRegistration),nameof(BeforeMerge)));
            Log.Message("[TaleNivarianCompat] relocated Lotus pod tick registration guard installed.");
        }
        static void BeforeMerge(TickList __0)
        {
            if(!MP.IsInMultiplayer||!normalizerEnabled())return;
            var pending=Pending(__0);var removed=Removed(__0);
            if(pending.Count==0||removed.Count==0)return;
            // The normalizer removes all old occurrences before inserting once.
            // A same-list despawn/respawn must therefore cancel its queued removal.
            for(int i=removed.Count-1;i>=0;i--)
            {
                var t=removed[i];
                if(t!=null&&t.Spawned&&!t.Destroyed&&pod.IsInstanceOfType(t)&&pending.Contains(t))removed.RemoveAt(i);
            }
        }
    }
}