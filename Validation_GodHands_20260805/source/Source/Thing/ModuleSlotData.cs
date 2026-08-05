using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace GodHandMod
{
    // 模块槽位数据（兼容残阳科技的模块系统）
    // 用于正确计算模块尺寸和位置
    public class ModuleSlotData : IExposable
    {
        #region 字段
        
        public int slot;                                      // 槽位ID
        public int width = 1;                                 // 槽位宽度
        public List<Vector3> moduleOffsets = new List<Vector3>();  // 每个位置的偏移
        public List<float> moduleSizes = new List<float>();        // 每个位置的尺寸系数
        
        #endregion
        
        #region 构造函数
        
        public ModuleSlotData()
        {
            slot = 0;
            width = 1;
        }
        
        public ModuleSlotData(int slot)
        {
            this.slot = slot;
            width = 1;
        }
        
        #endregion
        
        #region 公共方法
        
        // 获取指定索引的尺寸系数
        public float GetSizeMultiplier(int index)
        {
            if (index < 0 || index >= moduleSizes.Count)
                return 1f;
            return moduleSizes[index];
        }
        
        // 获取指定索引的偏移
        public Vector3 GetOffset(int index)
        {
            if (index < 0 || index >= moduleOffsets.Count)
                return Vector3.zero;
            return moduleOffsets[index];
        }
        
        #endregion
        
        #region 序列化
        
        public void ExposeData()
        {
            Scribe_Values.Look(ref slot, "slot", 0);
            Scribe_Values.Look(ref width, "width", 1);
            Scribe_Collections.Look(ref moduleOffsets, "moduleOffsets", LookMode.Value);
            Scribe_Collections.Look(ref moduleSizes, "moduleSizes", LookMode.Value);
            
            if (moduleOffsets == null) moduleOffsets = new List<Vector3>();
            if (moduleSizes == null) moduleSizes = new List<float>();
        }
        
        #endregion
    }
}
