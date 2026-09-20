using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.ParallelRenderCompatibility
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private const string Owner="meow.multiplayer.parallelrender";
        static Bootstrap()
        {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("performance")) return;
            if(MP.enabled)LongEventHandler.ExecuteWhenFinished(Install);
        }
        private static HarmonyMethod CopyMetadata(MethodInfo method,Patch original) =>
            new HarmonyMethod(method){priority=original.priority,before=original.before,after=original.after};
        private static void Install()
        {
            var draw=AccessTools.Method(typeof(PawnTweener),nameof(PawnTweener.PreDrawPosCalculation));
            var rate=AccessTools.PropertyGetter(typeof(TickManager),nameof(TickManager.TickRateMultiplier));
            var marker=AccessTools.TypeByName("Multiplayer.Client.AsyncTime.PreDrawCalcMarker");
            var oldEnter=AccessTools.Method(marker,"Prefix");
            var oldExit=AccessTools.Method(marker,"Finalizer");
            var oldRate=AccessTools.Method("Multiplayer.Client.AsyncTime.TickRateMultiplierPatch:Postfix");
            var enter=AccessTools.Method(typeof(RenderContext),nameof(RenderContext.Enter));
            var exit=AccessTools.Method(typeof(RenderContext),nameof(RenderContext.Exit));
            var read=AccessTools.Method(typeof(RenderContext),nameof(RenderContext.Rate));
            Patch p=null,f=null,r=null;
            var h=new Harmony(Owner);
            try
            {
                var field=AccessTools.Field(marker,"calculating");
                if(AccessTools.Field(typeof(PawnTweener),"pawn")?.FieldType!=typeof(Pawn))
                    throw new MissingFieldException("PawnTweener.pawn changed");
                if(field==null || field.FieldType!=typeof(Pawn) || !field.IsStatic ||
                    field.IsDefined(typeof(ThreadStaticAttribute),false))
                    throw new InvalidOperationException("MP render marker shape changed; review upstream implementation");
                if(PatchProcessor.GetOriginalInstructions(oldRate).Count(i=>i.opcode==OpCodes.Ldsfld && Equals(i.operand,field))!=2)
                    throw new InvalidOperationException("MP rate reader changed");
                var drawPatches=Harmony.GetPatchInfo(draw);
                p=drawPatches?.Prefixes.SingleOrDefault(x=>x.PatchMethod==oldEnter);
                f=drawPatches?.Finalizers.SingleOrDefault(x=>x.PatchMethod==oldExit);
                r=Harmony.GetPatchInfo(rate)?.Postfixes.SingleOrDefault(x=>x.PatchMethod==oldRate);
                if(p==null || f==null || r==null)throw new MissingMethodException("Required MP render hooks are not installed");
                // Replace only the three verified MP hooks, preserving their ordering
                // constraints. Every unrelated drawing/rate patch remains installed.
                h.Patch(draw,prefix:CopyMetadata(enter,p),finalizer:CopyMetadata(exit,f));
                h.Patch(rate,postfix:CopyMetadata(read,r));
                h.Unpatch(draw,oldEnter);h.Unpatch(draw,oldExit);h.Unpatch(rate,oldRate);
                Log.Message("[Meow.ParallelRender] Thread-local MP drawing context installed; nested scopes restored; simulation unchanged.");
            }
            catch(Exception e)
            {
                h.Unpatch(draw,enter);h.Unpatch(draw,exit);h.Unpatch(rate,read);
                if(p!=null && !Harmony.GetPatchInfo(draw).Prefixes.Any(x=>x.PatchMethod==oldEnter))new Harmony(p.owner).Patch(draw,prefix:CopyMetadata(oldEnter,p));
                if(f!=null && !Harmony.GetPatchInfo(draw).Finalizers.Any(x=>x.PatchMethod==oldExit))new Harmony(f.owner).Patch(draw,finalizer:CopyMetadata(oldExit,f));
                if(r!=null && !Harmony.GetPatchInfo(rate).Postfixes.Any(x=>x.PatchMethod==oldRate))new Harmony(r.owner).Patch(rate,postfix:CopyMetadata(oldRate,r));
                Log.Error("[Meow.ParallelRender] REQUIRED_TARGET_FAILURE "+e);
            }
        }
    }
}
