using UnityEngine;
using Verse;
using RimWorld;
using System;
using System.Reflection;

namespace GodHandMod
{
    // 超精调整对话框
    public class Dialog_PrecisionAdjust : Window
    {
        private Thing targetThing;
        private GraphicTransformManager.TransformData transform;
        private ThingComponentTree componentTree;
        private ThingComponentTree.ComponentNode currentComponent;

        // 调整参数
        private Vector2 offset;
        private float rotation;
        private Vector3 scale;
        private int drawLayer;
        private int? overrideRot;

        // 原始值（用于重置）
        private Vector2 originalOffset;
        private float originalRotation;
        private Vector3 originalScale;
        private int originalDrawLayer;
        private int? originalOverrideRot;

        // 文本输入缓冲区
        private string offsetXBuffer = "";
        private string offsetYBuffer = "";
        private string rotationBuffer = "";
        private string scaleXBuffer = "";
        private string scaleYBuffer = "";
        private string scaleZBuffer = "";
        private string layerBuffer = "";
        private string turretRotBuffer = "";

        // 滚动位置
        private Vector2 rightScrollPos = Vector2.zero;

        private static readonly FloatRange OffsetRange = new FloatRange(-10f, 10f);
        private static readonly FloatRange ScaleRange = new FloatRange(0.01f, 10f);
        private static readonly FloatRange AngleRange = new FloatRange(-180f, 180f);
        private static readonly FloatRange LayerRange = new FloatRange(-100f, 100f);

        public override Vector2 InitialSize => new Vector2(700f, 650f);

        protected override float Margin => 10f;

        public Dialog_PrecisionAdjust(Thing thing)
        {
            this.targetThing = thing;
            this.forcePause = false;
            this.doCloseX = true;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = false;
            this.draggable = true;

            GodHandModMain.DebugLog($"[超精调整] 目标: {thing.LabelCap}, 类型: {thing.GetType().Name}");

            // 构建组件树
            componentTree = new ThingComponentTree(thing);
            currentComponent = componentTree.root; // 默认选中根组件

            // 使用全局管理器
            transform = currentComponent.transform;

            // 加载当前值
            LoadCurrentComponentValues();

            GodHandModMain.DebugLog($"[超精调整] 当前值: offset={offset}, rotation={rotation}, scale={scale}, layer={drawLayer}, overrideRot={overrideRot}");

            // 保存原始值
            originalOffset = offset;
            originalRotation = rotation;
            originalScale = scale;
            originalDrawLayer = drawLayer;
            originalOverrideRot = overrideRot;

            // 初始化缓冲区
            UpdateBuffers();
        }

        private void LoadCurrentComponentValues()
        {
            transform = currentComponent.transform;
            offset = transform.offset;
            rotation = transform.rotation;
            scale = transform.scale;
            drawLayer = transform.drawLayer;
            overrideRot = transform.overrideRot;

            UpdateBuffers();
        }

        private void UpdateBuffers()
        {
            offsetXBuffer = offset.x.ToString("F3");
            offsetYBuffer = offset.y.ToString("F3");
            rotationBuffer = rotation.ToString("F1");
            scaleXBuffer = scale.x.ToString("F2");
            scaleYBuffer = scale.y.ToString("F2");
            scaleZBuffer = scale.z.ToString("F2");
            layerBuffer = drawLayer.ToString();

            // 炮塔旋转缓冲
            if (targetThing is Building_Turret turret)
            {
                object turretTop = GetTurretTop(turret);
                if (turretTop != null)
                {
                    turretRotBuffer = GetTurretRotation(turretTop).ToString("F1");
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            LeftRect(inRect.LeftHalf());
            RightRect(inRect.RightHalf());
        }

        private void LeftRect(Rect inRect)
        {
            Widgets.DrawMenuSection(inRect);
            inRect = inRect.ContractedBy(Margin / 2f);

            // 标题区域
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, Text.LineHeight);
            Widgets.DrawLightHighlight(titleRect);
            using (new TextBlock(TextAnchor.MiddleCenter))
            {
                Widgets.Label(titleRect, targetThing.LabelCap.Truncate(inRect.width));
            }

            inRect.yMin += Text.LineHeight + Margin * 2;

            // 组件树显示
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Tiny;
            listing.Label("GodHand.Wrench.PrecisionAdjustTitle".Translate(targetThing.LabelCap));
            Text.Font = GameFont.Small;

            listing.Gap(Margin);

            // 显示所有组件
            foreach (var node in componentTree.GetAllNodes())
            {
                Rect nodeRect = listing.GetRect(25f);
                bool isSelected = (node == currentComponent);

                if (isSelected)
                {
                    Widgets.DrawHighlight(nodeRect);
                }

                Widgets.DrawHighlightIfMouseover(nodeRect);

                // 缩进（子组件）
                float indent = (node == componentTree.root) ? 0f : 15f;
                Rect labelRect = new Rect(nodeRect.x + indent, nodeRect.y, nodeRect.width - indent, nodeRect.height);

                Widgets.Label(labelRect, node.displayName);

                if (Widgets.ButtonInvisible(nodeRect))
                {
                    currentComponent = node;
                    LoadCurrentComponentValues();
                }
            }

            listing.Gap(Margin * 2);

            // 移除整容刀入口

            listing.End();
        }

        private void RightRect(Rect inRect)
        {
            Widgets.DrawMenuSection(inRect);
            inRect = inRect.ContractedBy(Margin / 2f);

            // 顶部标题
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, Text.LineHeight);
            Widgets.DrawLightHighlight(titleRect);
            using (new TextBlock(TextAnchor.MiddleCenter))
            {
                Widgets.Label(titleRect, "调整参数");
            }

            // 底部按钮区域
            Rect bottomRect = new Rect(inRect.x, inRect.yMax - 30f, inRect.width, 30f);
            Rect resetBtnRect = new Rect(bottomRect.x, bottomRect.y, bottomRect.width / 2f - 2f, 30f);
            Rect closeBtnRect = new Rect(resetBtnRect.xMax + 4f, bottomRect.y, bottomRect.width / 2f - 2f, 30f);

            if (Widgets.ButtonText(resetBtnRect, "GodHand.Wrench.ResetAll".Translate()))
            {
                ResetToOriginal();
            }

            if (Widgets.ButtonText(closeBtnRect, "CloseButton".Translate()))
            {
                Close();
            }

            // 滚动内容区域
            Rect scrollOutRect = new Rect(inRect.x, titleRect.yMax + Margin, inRect.width, inRect.height - titleRect.height - bottomRect.height - Margin * 2f);
            Rect scrollViewRect = new Rect(0f, 0f, scrollOutRect.width - 16f, 1000f);

            Widgets.BeginScrollView(scrollOutRect, ref rightScrollPos, scrollViewRect);
            Widgets.BeginGroup(scrollViewRect);

            Rect controlRect = new Rect(Margin, 0f, scrollViewRect.width - Margin * 2f, 30f);

            // === 位置偏移 ===
            DrawSectionHeader(ref controlRect, "GodHand.Wrench.Offset".Translate(), scrollViewRect.width);

            // X偏移
            DrawTextField(ref controlRect, "X (左右):", ref offsetXBuffer, ref offset.x, 0.001f, () => ApplyChanges());
            DrawSlider(ref controlRect, ref offset.x, OffsetRange, "X (左右): {0:F3}", 0.05f, () =>
            {
                offsetXBuffer = offset.x.ToString("F3");
                ApplyChanges();
            });

            // Z偏移 (原Y)
            DrawTextField(ref controlRect, "Z (上下):", ref offsetYBuffer, ref offset.y, 0.001f, () => ApplyChanges());
            DrawSlider(ref controlRect, ref offset.y, OffsetRange, "Z (上下): {0:F3}", 0.05f, () =>
            {
                offsetYBuffer = offset.y.ToString("F3");
                ApplyChanges();
            });

            controlRect.y += Margin;

            // === 旋转 ===
            DrawSectionHeader(ref controlRect, "GodHand.Wrench.Rotation".Translate(), inRect.width);

            DrawTextField(ref controlRect, "角度:", ref rotationBuffer, ref rotation, 0.1f, () =>
            {
                while (rotation > 180f) rotation -= 360f;
                while (rotation < -180f) rotation += 360f;
                ApplyChanges();
            });

            DrawSlider(ref controlRect, ref rotation, AngleRange, "{0:F1}°", 1f, () =>
            {
                rotationBuffer = rotation.ToString("F1");
                ApplyChanges();
            });

            // 快捷旋转按钮
            float btnWidth = (inRect.width - Margin * 2f) / 4f;
            if (Widgets.ButtonText(new Rect(controlRect.x, controlRect.y, btnWidth - 2f, 30f), "0°"))
            {
                rotation = 0f;
                rotationBuffer = "0.0";
                ApplyChanges();
            }
            if (Widgets.ButtonText(new Rect(controlRect.x + btnWidth, controlRect.y, btnWidth - 2f, 30f), "90°"))
            {
                rotation = 90f;
                rotationBuffer = "90.0";
                ApplyChanges();
            }
            if (Widgets.ButtonText(new Rect(controlRect.x + btnWidth * 2, controlRect.y, btnWidth - 2f, 30f), "180°"))
            {
                rotation = 180f;
                rotationBuffer = "180.0";
                ApplyChanges();
            }
            if (Widgets.ButtonText(new Rect(controlRect.x + btnWidth * 3, controlRect.y, btnWidth - 2f, 30f), "270°"))
            {
                rotation = 270f;
                rotationBuffer = "270.0";
                ApplyChanges();
            }
            controlRect.y += 30f + Margin;

            // === 贴图朝向 ===
            DrawSectionHeader(ref controlRect, "GodHand.Wrench.TextureOrientation".Translate(), inRect.width);

            float orientBtnW = (inRect.width - Margin * 2f) / 5f;
            if (Widgets.ButtonText(new Rect(controlRect.x, controlRect.y, orientBtnW - 2f, 30f), "自动")) { overrideRot = null; ApplyChanges(); }
            if (Widgets.ButtonText(new Rect(controlRect.x + orientBtnW, controlRect.y, orientBtnW - 2f, 30f), "N")) { overrideRot = 0; ApplyChanges(); }
            if (Widgets.ButtonText(new Rect(controlRect.x + orientBtnW * 2, controlRect.y, orientBtnW - 2f, 30f), "E")) { overrideRot = 1; ApplyChanges(); }
            if (Widgets.ButtonText(new Rect(controlRect.x + orientBtnW * 3, controlRect.y, orientBtnW - 2f, 30f), "S")) { overrideRot = 2; ApplyChanges(); }
            if (Widgets.ButtonText(new Rect(controlRect.x + orientBtnW * 4, controlRect.y, orientBtnW - 2f, 30f), "W")) { overrideRot = 3; ApplyChanges(); }
            controlRect.y += 30f + Margin;

            // === 缩放 ===
            DrawSectionHeader(ref controlRect, "GodHand.Wrench.Scale".Translate(), inRect.width);

            // X缩放
            DrawTextField(ref controlRect, "X (左右):", ref scaleXBuffer, ref scale.x, 0.01f, () => ApplyChanges());
            DrawSlider(ref controlRect, ref scale.x, ScaleRange, "X (左右): {0:F2}", 0.01f, () =>
            {
                scaleXBuffer = scale.x.ToString("F2");
                ApplyChanges();
            });

            // Z缩放 (原Z)
            DrawTextField(ref controlRect, "Z (上下):", ref scaleZBuffer, ref scale.z, 0.01f, () => ApplyChanges());
            DrawSlider(ref controlRect, ref scale.z, ScaleRange, "Z (上下): {0:F2}", 0.01f, () =>
            {
                scaleZBuffer = scale.z.ToString("F2");
                ApplyChanges();
            });

            // Y层级高低
            DrawTextField(ref controlRect, "Y (层级):", ref scaleYBuffer, ref scale.y, 0.01f, () => ApplyChanges());
            DrawSlider(ref controlRect, ref scale.y, ScaleRange, "Y (层级): {0:F2}", 0.01f, () =>
            {
                scaleYBuffer = scale.y.ToString("F2");
                ApplyChanges();
            });

            controlRect.y += Margin;

            // === 渲染层 ===
            DrawSectionHeader(ref controlRect, "GodHand.Wrench.DrawLayer".Translate(), inRect.width);

            DrawTextField(ref controlRect, "层级:", ref layerBuffer, ref drawLayer, 1, () => ApplyChanges());

            Rect layerSliderRect = new Rect(controlRect.x, controlRect.y, controlRect.width, 30f);
            float layerFloat = drawLayer;
            layerFloat = Widgets.HorizontalSlider(layerSliderRect, layerFloat, -10f, 10f, false, "层级: " + drawLayer, null, null, 1f);
            int newLayer = (int)layerFloat;
            if (newLayer != drawLayer)
            {
                drawLayer = newLayer;
                layerBuffer = drawLayer.ToString();
                ApplyChanges();
            }
            controlRect.y += 30f + Margin;

            // === 炮塔特殊调整 ===
            if (targetThing is Building_Turret turret)
            {
                DrawSectionHeader(ref controlRect, "GodHand.Wrench.TurretSettings".Translate(), inRect.width);

                object turretTop = GetTurretTop(turret);
                if (turretTop != null)
                {
                    float curTurretRot = GetTurretRotation(turretTop);

                    // 炮筒旋转输入框
                    DrawTextField(ref controlRect, "炮筒:", ref turretRotBuffer, ref curTurretRot, 0.1f, () =>
                    {
                        SetTurretRotation(turretTop, curTurretRot);
                    });

                    // 炮筒旋转滑块
                    Rect turretSliderRect = new Rect(controlRect.x, controlRect.y, controlRect.width, 30f);
                    float newTurretRot = Widgets.HorizontalSlider(turretSliderRect, curTurretRot, 0f, 360f, false, $"炮筒: {curTurretRot:F1}°", null, null, 1f);
                    if (Mathf.Abs(newTurretRot - curTurretRot) > 0.1f)
                    {
                        SetTurretRotation(turretTop, newTurretRot);
                        turretRotBuffer = newTurretRot.ToString("F1");
                    }
                    controlRect.y += 30f;

                    // 快捷旋转
                    float turretBtnW = (inRect.width - Margin * 2f) / 4f;
                    if (Widgets.ButtonText(new Rect(controlRect.x, controlRect.y, turretBtnW - 2f, 30f), "0°"))
                    {
                        SetTurretRotation(turretTop, 0f);
                        turretRotBuffer = "0.0";
                    }
                    if (Widgets.ButtonText(new Rect(controlRect.x + turretBtnW, controlRect.y, turretBtnW - 2f, 30f), "90°"))
                    {
                        SetTurretRotation(turretTop, 90f);
                        turretRotBuffer = "90.0";
                    }
                    if (Widgets.ButtonText(new Rect(controlRect.x + turretBtnW * 2, controlRect.y, turretBtnW - 2f, 30f), "180°"))
                    {
                        SetTurretRotation(turretTop, 180f);
                        turretRotBuffer = "180.0";
                    }
                    if (Widgets.ButtonText(new Rect(controlRect.x + turretBtnW * 3, controlRect.y, turretBtnW - 2f, 30f), "270°"))
                    {
                        SetTurretRotation(turretTop, 270f);
                        turretRotBuffer = "270.0";
                    }
                    controlRect.y += 30f + Margin;
                }
            }

            Widgets.EndGroup();
            Widgets.EndScrollView();
        }

        // === 辅助UI绘制方法 ===
        private void DrawSectionHeader(ref Rect controlRect, string label, float width)
        {
            Rect headerRect = new Rect(0f, controlRect.y, width, Text.LineHeight + 4f);
            Widgets.DrawLightHighlight(headerRect);
            using (new TextBlock(TextAnchor.MiddleCenter))
            {
                Widgets.Label(headerRect, label);
            }
            controlRect.y += headerRect.height + Margin;
        }

        private void DrawTextField(ref Rect controlRect, string label, ref string buffer, ref float value, float threshold, Action onChange)
        {
            Rect textRect = new Rect(controlRect.x, controlRect.y, controlRect.width * 0.4f, 30f);
            Widgets.Label(new Rect(textRect.x, textRect.y + 5f, 50f, 20f), label);

            Rect inputRect = new Rect(textRect.x + 50f, textRect.y + 5f, textRect.width - 50f, 20f);
            string newBuffer = Widgets.TextField(inputRect, buffer);

            if (newBuffer != buffer)
            {
                buffer = newBuffer;
                if (float.TryParse(buffer, out float newValue) && Mathf.Abs(newValue - value) > threshold)
                {
                    value = newValue;
                    onChange?.Invoke();
                }
            }

            controlRect.y += 30f;
        }

        private void DrawTextField(ref Rect controlRect, string label, ref string buffer, ref int value, int threshold, Action onChange)
        {
            Rect textRect = new Rect(controlRect.x, controlRect.y, controlRect.width * 0.4f, 30f);
            Widgets.Label(new Rect(textRect.x, textRect.y + 5f, 50f, 20f), label);

            Rect inputRect = new Rect(textRect.x + 50f, textRect.y + 5f, textRect.width - 50f, 20f);
            string newBuffer = Widgets.TextField(inputRect, buffer);

            if (newBuffer != buffer)
            {
                buffer = newBuffer;
                if (int.TryParse(buffer, out int newValue) && Mathf.Abs(newValue - value) >= threshold)
                {
                    value = newValue;
                    onChange?.Invoke();
                }
            }

            controlRect.y += 30f;
        }

        private void DrawSlider(ref Rect controlRect, ref float value, FloatRange range, string labelFormat, float increment, Action onChange)
        {
            Rect sliderRect = new Rect(controlRect.x, controlRect.y, controlRect.width, 30f);
            float oldValue = value;
            value = Widgets.HorizontalSlider(sliderRect, value, range.min, range.max, false, string.Format(labelFormat, value), null, null, increment);

            if (Mathf.Abs(value - oldValue) > 0.001f)
            {
                onChange?.Invoke();
            }

            controlRect.y += 30f;
        }

        private void ApplyChanges()
        {
            if (transform != null && currentComponent != null)
            {
                GodHandModMain.DebugLog($"[超精调整] 应用变换到组件 '{currentComponent.displayName}': offset={offset}, rotation={rotation}, scale={scale}, layer={drawLayer}, overrideRot={overrideRot}");

                // 更新当前组件的transform
                transform.offset = offset;
                transform.rotation = rotation;
                transform.scale = scale;
                transform.drawLayer = drawLayer;
                transform.overrideRot = overrideRot;

                if (targetThing != null && targetThing.Spawned && targetThing.Map != null)
                {
                    targetThing.Map.mapDrawer.MapMeshDirty(targetThing.Position, RimWorld.MapMeshFlagDefOf.Things);
                    targetThing.Map.mapDrawer.MapMeshDirty(targetThing.Position, RimWorld.MapMeshFlagDefOf.Buildings);
                    GodHandModMain.DebugLog($"[超精调整] 已标记重绘: {targetThing.Position}");
                }
            }
        }

        private void ResetToOriginal()
        {
            if (currentComponent != null)
            {
                // 重置当前组件到初始值
                offset = Vector2.zero;
                rotation = 0f;
                scale = Vector3.one;
                drawLayer = 0;
                overrideRot = null;

                UpdateBuffers();
                ApplyChanges();

                GodHandModMain.DebugLog($"[超精调整] 已重置组件 '{currentComponent.displayName}' 到初始值");
            }
        }

        // === 炮塔反射方法 ===
        private static FieldInfo turretTopField;
        private static PropertyInfo curRotationProperty;

        private object GetTurretTop(Building_Turret turret)
        {
            try
            {
                if (turretTopField == null)
                {
                    turretTopField = typeof(Building_Turret).GetField("top", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                return turretTopField?.GetValue(turret);
            }
            catch
            {
                return null;
            }
        }

        private float GetTurretRotation(object turretTop)
        {
            try
            {
                if (curRotationProperty == null)
                {
                    curRotationProperty = turretTop.GetType().GetProperty("CurRotation");
                }
                return (float)(curRotationProperty?.GetValue(turretTop) ?? 0f);
            }
            catch
            {
                return 0f;
            }
        }

        private void SetTurretRotation(object turretTop, float rotation)
        {
            try
            {
                if (curRotationProperty == null)
                {
                    curRotationProperty = turretTop.GetType().GetProperty("CurRotation");
                }
                curRotationProperty?.SetValue(turretTop, rotation);
            }
            catch (Exception ex)
            {
                GodHandModMain.DebugLog($"[超精调整] 设置炮塔旋转失败: {ex.Message}");
            }
        }

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 initialSize = InitialSize;
            windowRect = new Rect(5f, 5f, initialSize.x, initialSize.y).Rounded();
        }
    }

    // 数值输入对话框
    public class Dialog_NumericInput : Window
    {
        private string buffer;
        private Action<float> callback;
        private string paramName;

        public override Vector2 InitialSize => new Vector2(280f, 150f);

        public Dialog_NumericInput(float initial, Action<float> callback, string paramName = "")
        {
            this.buffer = initial.ToString("F3");
            this.callback = callback;
            this.paramName = paramName;
            this.forcePause = false;
            this.closeOnClickedOutside = true;
            this.doCloseButton = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            string prompt = string.IsNullOrEmpty(paramName)
                ? "GodHand.Wrench.EnterValueUnlimited".Translate().ToString()
                : string.Format("GodHand.Wrench.EnterValueFor".Translate().ToString(), paramName);
            listing.Label(prompt);
            buffer = listing.TextEntry(buffer);

            Rect btnRect = listing.GetRect(30f);
            Rect okRect = new Rect(btnRect.x, btnRect.y, btnRect.width / 2 - 5f, 30f);
            Rect cancelRect = new Rect(btnRect.x + btnRect.width / 2 + 5f, btnRect.y, btnRect.width / 2 - 5f, 30f);

            if (Widgets.ButtonText(okRect, "OK".Translate()))
            {
                if (float.TryParse(buffer, out float value))
                {
                    callback?.Invoke(value);
                    Close();
                }
            }

            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()))
            {
                Close();
            }

            listing.End();
        }
    }
}
