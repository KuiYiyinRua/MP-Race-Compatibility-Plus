using UnityEngine;
using Verse;

namespace GodHandMod
{
    // 炮管数据（兼容残阳 TopGun_cy）
    // 用于存储和渲染多炮管炮塔的每个炮管
    public class TopGunData : IExposable
    {
        #region 字段

        public Vector3 offsetGun = Vector3.zero;       // 炮管偏移
        public GraphicData graphicDataGun;             // 炮管图形
        public GraphicData graphicDataGunEd;           // 通电时的炮管图形
        public Vector3 retarderV3 = Vector3.zero;      // 制退器当前偏移（后坐力）
        public int retarderTick = 0;                   // 制退器动画计时
        public int angle = 0;                          // 角度偏移
        public int retarderRecoverPauseTick = 0;       // 后坐力恢复暂停
        public int retarderRecoverTick = 0;            // 后坐力恢复计时

        // 缓存的材质
        [Unsaved] private Material cachedMat;
        [Unsaved] private Material cachedMatEd;

        #endregion

        #region 序列化

        public void ExposeData()
        {
            Scribe_Values.Look(ref offsetGun, "offsetGun", Vector3.zero);
            Scribe_Values.Look(ref angle, "angle", 0);
            // 序列化不保存
        }

        #endregion

        #region 材质获取

        // 获取炮管材质
        public Material GetMaterial()
        {
            if (cachedMat == null && graphicDataGun != null)
            {
                try
                {
                    cachedMat = graphicDataGun.Graphic?.MatSingle;
                }
                catch { }
            }
            return cachedMat;
        }

        // 获取通电时的炮管材质
        public Material GetMaterialEd()
        {
            if (cachedMatEd == null && graphicDataGunEd != null)
            {
                try
                {
                    cachedMatEd = graphicDataGunEd.Graphic?.MatSingle;
                }
                catch { }
            }
            return cachedMatEd;
        }

        #endregion

        #region 后坐力动画

        // 应用后坐力效果
        public void ApplyRecoil(float recoilAmount = 0.15f)
        {
            retarderV3 = new Vector3(0, 0, -recoilAmount);
            retarderTick = 10;
            retarderRecoverPauseTick = 5;
        }

        // Tick 更新后坐力恢复
        public void TickRecoil()
        {
            if (retarderTick > 0)
            {
                retarderTick--;
                return;
            }

            if (retarderRecoverPauseTick > 0)
            {
                retarderRecoverPauseTick--;
                return;
            }

            if (retarderV3.z < 0)
            {
                retarderV3.z = Mathf.Min(0, retarderV3.z + 0.02f);
            }
        }

        #endregion
    }
}
