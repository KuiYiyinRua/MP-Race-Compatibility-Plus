using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    // Facial parameter gathering reaches shared health caches and EBF's global protocol flag.
    // Worker jobs may enqueue visual updates; only the game thread may perform the query.
    public sealed class FacialHealthBoundary : GameComponent
    {
        static readonly ConcurrentDictionary<Pawn, byte> Pending = new ConcurrentDictionary<Pawn, byte>();
        static MethodInfo update;
        public FacialHealthBoundary(Game game) { Pending.Clear(); }
        internal static void Apply(Harmony harmony)
        {
            var type=AccessTools.TypeByName("YaOpt.OtherMod.FacialAnimation.Helpers.ParallelUpdateHelper");
            if(type==null)return;
            update=AccessTools.DeclaredMethod(type,"UpdateFacialAnimation",new[]{typeof(Pawn)});
            if(update==null)throw new System.MissingMethodException(type.FullName,"UpdateFacialAnimation(Pawn)");
            harmony.Patch(update,prefix:new HarmonyMethod(typeof(FacialHealthBoundary),nameof(Enqueue)));
            Log.Message("[RaceTrioCompat] YaOpt facial health queries deferred from workers to game thread.");
        }
        static bool Enqueue(Pawn __0)
        {
            if(UnityData.IsInMainThread||!MP.IsInMultiplayer)return true;
            if(__0!=null)Pending.TryAdd(__0,0);
            return false;
        }
        public override void GameComponentUpdate()
        {
            if(update==null)return;
            foreach(var pawn in Pending.Keys.OrderBy(p=>p.thingIDNumber).ToArray())
                if(Pending.TryRemove(pawn,out _)&&pawn!=null&&!pawn.Destroyed)
                    update.Invoke(null,new object[]{pawn});
        }
    }
}
