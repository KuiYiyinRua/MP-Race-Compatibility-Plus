using UnityEngine;
using Verse;
using System.Collections.Generic;

namespace GodHandMod
{
    // 变换渲染组件 - 已简化
    // 补丁处理渲染
    public class MapComponentGraphicTransform : MapComponent
    {
        public MapComponentGraphicTransform(Map map) : base(map)
        {
        }

        // 防止双重渲染
        public override void MapComponentOnGUI()
        {
        }
    }
}
