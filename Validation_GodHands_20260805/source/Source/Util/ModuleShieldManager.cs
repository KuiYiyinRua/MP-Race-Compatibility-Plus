using Verse;
using System.Collections.Generic;
using RimWorld.Planet;

namespace GodHandMod
{
    // 护盾模块管理器
    // 护盾模块管理器
    public class ModuleShieldManager : WorldComponent
    {
        public ModuleShieldManager(World world) : base(world) { }

        public static ModuleShieldManager Instance => Find.World?.GetComponent<ModuleShieldManager>();

        // 护盾数据
        public class ShieldData : IExposable
        {
            public TurretSlot slot;
            public float radius = 2.5f;  // 护盾半径
            public int lastInterceptTick = -9999;
            public float lastInterceptAngle;

            public void ExposeData()
            {
                // 需要实现接口
                // 假设已实现
                // 暂注防报错
                // Scribe_References
                Scribe_Values.Look(ref radius, "radius", 2.5f);
                Scribe_Values.Look(ref lastInterceptTick, "lastInterceptTick", -9999);
                Scribe_Values.Look(ref lastInterceptAngle, "lastInterceptAngle", 0f);
            }
        }

        private Dictionary<Pawn, ShieldData> shieldedPawns = new Dictionary<Pawn, ShieldData>();
        private List<Pawn> pawnKeysWorkingList;
        private List<ShieldData> shieldValuesWorkingList;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref shieldedPawns, "shieldedPawns", LookMode.Reference, LookMode.Deep, ref pawnKeysWorkingList, ref shieldValuesWorkingList);
            if (shieldedPawns == null) shieldedPawns = new Dictionary<Pawn, ShieldData>();
        }

        public void RegisterShield(Pawn pawn, TurretSlot slot, float radius = 2.5f)
        {
            if (pawn != null && slot != null)
            {
                shieldedPawns[pawn] = new ShieldData { slot = slot, radius = radius };
            }
        }

        public void UnregisterShield(Pawn pawn)
        {
            if (pawn != null)
                shieldedPawns.Remove(pawn);
        }

        public bool HasShield(Pawn pawn)
        {
            return pawn != null && shieldedPawns.ContainsKey(pawn);
        }

        public ShieldData GetShieldData(Pawn pawn)
        {
            if (pawn != null && shieldedPawns.TryGetValue(pawn, out var data))
                return data;
            return null;
        }

        public IEnumerable<KeyValuePair<Pawn, ShieldData>> GetAllShields()
        {
            return shieldedPawns;
        }
    }
}
