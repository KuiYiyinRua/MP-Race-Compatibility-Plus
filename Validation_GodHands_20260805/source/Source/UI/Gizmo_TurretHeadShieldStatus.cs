using RimWorld;
using UnityEngine;
using Verse;

namespace GodHandMod
{
    // 炮塔头护盾状态条Gizmo
    [StaticConstructorOnStartup]
    public class Gizmo_TurretHeadShieldStatus : Gizmo
    {
        public GodHandTurretHead turretHead;

        private static readonly Texture2D FullBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.2f, 0.24f));
        private static readonly Texture2D EmptyBarTex = SolidColorMaterials.NewSolidColorTexture(Color.clear);
        private static readonly Texture2D ShieldBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.35f, 0.35f, 0.8f));

        private const float Width = 140f;
        private const int InRectPadding = 6;

        public Gizmo_TurretHeadShieldStatus()
        {
            Order = -99f; // 显示在靠前的位置
        }

        public override float GetWidth(float maxWidth)
        {
            return Width;
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect innerRect = rect.ContractedBy(InRectPadding);

            Widgets.DrawWindowBackground(rect);

            // 计算护盾百分比
            float fillPercent = turretHead.ShieldMaxHP > 0
                ? (float)turretHead.ShieldCurrentHP / turretHead.ShieldMaxHP
                : 0f;

            // 标题
            string label = "护盾能量";

            // 数值文本
            string valueText = $"{turretHead.ShieldCurrentHP} / {turretHead.ShieldMaxHP}";

            // 绘制标题
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Rect labelRect = new Rect(innerRect.x, innerRect.y - 2f, innerRect.width, innerRect.height / 2f);
            Widgets.Label(labelRect, label);

            // 绘制进度条
            Rect barRect = new Rect(innerRect.x, labelRect.yMax, innerRect.width, innerRect.height / 2f);

            // 根据护盾状态选择颜色
            Texture2D barTex = fillPercent < 0.2f
                ? SolidColorMaterials.NewSolidColorTexture(new Color(0.8f, 0.2f, 0.2f))
                : ShieldBarTex;

            Widgets.FillableBar(barRect, fillPercent, barTex, EmptyBarTex, false);

            // 在进度条上显示数值
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, valueText);

            Text.Anchor = TextAnchor.UpperLeft;

            // 工具提示
            TooltipHandler.TipRegion(rect, "护盾可以拦截敌方弹丸，保护穿戴者免受伤害。\n护盾会随时间缓慢恢复。");

            return new GizmoResult(GizmoState.Clear);
        }
    }
}
