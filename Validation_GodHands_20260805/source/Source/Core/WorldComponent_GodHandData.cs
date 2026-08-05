using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace GodHandMod
{
    // 神之手数据持久化组件
    public class WorldComponent_GodHandData : WorldComponent
    {
        // 存储艺术描述数据
        private Dictionary<int, string> artDescriptions = new Dictionary<int, string>();

        private List<int> tmpKeys;
        private List<string> tmpValues;

        public WorldComponent_GodHandData(World world) : base(world)
        {
        }

        public void RegisterDescription(int thingID, string desc)
        {
            if (artDescriptions.ContainsKey(thingID))
            {
                artDescriptions[thingID] = desc;
            }
            else
            {
                artDescriptions.Add(thingID, desc);
            }
        }

        public bool TryGetDescription(int thingID, out string desc)
        {
            return artDescriptions.TryGetValue(thingID, out desc);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref artDescriptions, "godHandArtDescriptions", LookMode.Value, LookMode.Value, ref tmpKeys, ref tmpValues);
        }
    }
}
