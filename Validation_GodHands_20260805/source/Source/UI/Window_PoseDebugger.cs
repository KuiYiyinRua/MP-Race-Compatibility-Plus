using UnityEngine;
using Verse;
using RimWorld;
using System.Collections.Generic;

namespace GodHandMod
{
    public class PoseData
    {
        public float AngleOffset = 0f;
        public Vector3 PosOffset = new Vector3(0, 0, -0.15f);
        public Vector3 HeadPosOffset = Vector3.zero;
        public float AltitudeOffset = 4f;
    }

    public static class PoseDebugData
    {
        public static bool ForceCrawling = true;

        // 存储 N/E/S/W 四个方向的数据
        public static Dictionary<int, PoseData> RotData = new Dictionary<int, PoseData>();

        static PoseDebugData()
        {
            // 初始化默认值
            for (int i = 0; i < 4; i++)
            {
                RotData[i] = new PoseData();
            }
        }
    }

    public class Window_PoseDebugger : Window
    {
        public override Vector2 InitialSize => new Vector2(400, 700);

        // 当前编辑方向
        private int currentEditRot = 0;

        public Window_PoseDebugger()
        {
            this.draggable = true;
            this.resizeable = true;

            // 尝试自动定位到选中的建筑朝向
            var sel = Find.Selector.SingleSelectedThing;
            if (sel != null && sel is Building_EndRodGenerator)
            {
                currentEditRot = sel.Rotation.AsInt;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("--- 姿态调试器 (多方向独立) ---");
            list.CheckboxLabeled("全局强制 Crawling", ref PoseDebugData.ForceCrawling);

            list.GapLine();

            // 方向选择栏
            float w = inRect.width / 4f - 2f;
            Rect btnRect = list.GetRect(30f);

            for (int i = 0; i < 4; i++)
            {
                Rot4 r = new Rot4(i);
                Rect rBtn = new Rect(btnRect.x + i * w, btnRect.y, w, 30f);
                string label = r.ToStringHuman(); // 方向名

                // 高亮当前选中的
                Color color = (i == currentEditRot) ? Color.green : Color.white;
                GUI.color = color;

                if (Widgets.ButtonText(rBtn, label))
                {
                    currentEditRot = i;
                }
            }
            GUI.color = Color.white;
            list.Gap();

            // 获取当前方向的数据
            PoseData data = PoseDebugData.RotData[currentEditRot];

            list.Label($"当前编辑: {new Rot4(currentEditRot).ToStringHuman()}");

            // 调整角度
            list.Gap();
            list.Label($"角度修正: {data.AngleOffset:F1}");
            data.AngleOffset = list.Slider(data.AngleOffset, -180f, 180f);
            if (list.ButtonText("重置角度 (-90)")) data.AngleOffset = -90f;

            // 调整身体位移
            list.Gap();
            list.Label($"X 偏移: {data.PosOffset.x:F3}");
            data.PosOffset.x = list.Slider(data.PosOffset.x, -2f, 2f);

            list.Label($"Y 偏移 (高度): {data.PosOffset.y:F3}");
            data.PosOffset.y = list.Slider(data.PosOffset.y, -2f, 2f);

            list.Label($"Z 偏移: {data.PosOffset.z:F3}");
            data.PosOffset.z = list.Slider(data.PosOffset.z, -2f, 2f);

            if (list.ButtonText("重置位移 (0, 0, -0.15)")) data.PosOffset = new Vector3(0, 0, -0.15f);

            // 头部微调
            list.Gap();
            list.Label("--- 头部额外微调 ---");
            list.Label($"头 X: {data.HeadPosOffset.x:F3}");
            data.HeadPosOffset.x = list.Slider(data.HeadPosOffset.x, -1f, 1f);
            list.Label($"头 Y: {data.HeadPosOffset.y:F3}");
            data.HeadPosOffset.y = list.Slider(data.HeadPosOffset.y, -1f, 1f);
            list.Label($"头 Z: {data.HeadPosOffset.z:F3}");
            data.HeadPosOffset.z = list.Slider(data.HeadPosOffset.z, -1f, 1f);
            if (list.ButtonText("重置头部偏移")) data.HeadPosOffset = Vector3.zero;

            // 渲染层级
            list.Gap();
            list.Label($"渲染层级偏移: {data.AltitudeOffset:F3}");
            data.AltitudeOffset = list.Slider(data.AltitudeOffset, -1f, 1f);

            // 导出配置
            list.GapLine();
            if (list.ButtonText("导出所有方向配置 (至控制台)"))
            {
                string msg = "[PoseDebugData Export]\n";
                for (int i = 0; i < 4; i++)
                {
                    var d = PoseDebugData.RotData[i];
                    string rotName = new Rot4(i).ToString();
                    msg += $"// {rotName}\n"; // 包含方向名
                    msg += $"RotData[{i}].AngleOffset = {d.AngleOffset}f;\n";
                    msg += $"RotData[{i}].PosOffset = new Vector3({d.PosOffset.x}f, {d.PosOffset.y}f, {d.PosOffset.z}f);\n";
                    msg += $"RotData[{i}].HeadPosOffset = new Vector3({d.HeadPosOffset.x}f, {d.HeadPosOffset.y}f, {d.HeadPosOffset.z}f);\n";
                    msg += $"RotData[{i}].AltitudeOffset = {d.AltitudeOffset}f;\n\n";
                }
                Log.Message(msg);
                Messages.Message("已导出配置至日志窗口", MessageTypeDefOf.TaskCompletion, false);
            }

            list.End();
        }
    }
}
