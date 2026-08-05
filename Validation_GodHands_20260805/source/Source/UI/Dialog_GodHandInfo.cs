using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    public class Dialog_GodHandInfo : Window
    {
        private readonly Texture2D icon;
        private readonly string title;
        private readonly string rawDescription;
        private readonly string videoUrl;

        // 解析后的数据
        private string introText;
        private List<Section> sections = new List<Section>();
        private Vector2 scrollPosition = Vector2.zero;

        private struct Section
        {
            public string title;
            public string content;
        }

        public override Vector2 InitialSize => new Vector2(950f, 700f);

        public Dialog_GodHandInfo(Texture2D icon, string title, string description, string videoUrl = null)
        {
            this.icon = icon;
            this.title = title;
            this.rawDescription = description;
            this.videoUrl = videoUrl;

            this.closeOnClickedOutside = true;
            this.doCloseX = true;
            this.doCloseButton = true;
            this.forcePause = true;

            ParseDescription();
        }

        private void ParseDescription()
        {
            if (string.IsNullOrEmpty(rawDescription))
            {
                introText = "";
                return;
            }

            // 提取简介：兼容中英文括号
            int idxCN = rawDescription.IndexOf("【");
            int idxEN = rawDescription.IndexOf("[");
            int firstBracket = -1;

            if (idxCN != -1 && idxEN != -1) firstBracket = Mathf.Min(idxCN, idxEN);
            else if (idxCN != -1) firstBracket = idxCN;
            else if (idxEN != -1) firstBracket = idxEN;

            if (firstBracket == -1)
            {
                introText = rawDescription;
                return;
            }

            introText = rawDescription.Substring(0, firstBracket).Trim();

            // 提取段落：兼容中英文括号
            // 模式：[标题] 内容 ($ | \n\n[下一标题])
            string pattern = @"[【\[](.*?)[】\]]\s*([\s\S]*?)(?=\n\n[【\[]|$)";
            MatchCollection matches = Regex.Matches(rawDescription, pattern);

            foreach (Match match in matches)
            {
                sections.Add(new Section
                {
                    title = match.Groups[1].Value,
                    content = match.Groups[2].Value.Trim()
                });
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 颜色定义
            Color headerBgColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
            Color sectionBgColor = new Color(0.2f, 0.2f, 0.2f, 0.6f);
            Color iconBgColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            Color outlineColor = new Color(0.4f, 0.4f, 0.4f);

            float padding = 15f;

            // === 顶部区域 (Top) ===
            float topHeight = 140f;

            // 图标区域
            float iconRectWidth = 140f;
            Rect iconRect = new Rect(0, 0, iconRectWidth, topHeight);
            Widgets.DrawBoxSolidWithOutline(iconRect, iconBgColor, outlineColor);

            float iconSize = 100f;
            Rect iconTexRect = new Rect(
                iconRect.x + (iconRectWidth - iconSize) / 2,
                iconRect.y + (topHeight - iconSize) / 2,
                iconSize, iconSize);
            GUI.DrawTexture(iconTexRect, icon);

            // 顶部标题和简介区
            Rect headerRect = new Rect(iconRect.width + padding, 0, inRect.width - iconRect.width - padding, topHeight);
            Widgets.DrawBoxSolidWithOutline(headerRect, headerBgColor, outlineColor);

            // 标题
            Rect titleRect = new Rect(headerRect.x + 20f, headerRect.y + 15f, headerRect.width - 40f, 40f);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(titleRect, title);
            Text.Anchor = TextAnchor.UpperLeft;

            // 简介
            Rect introRect = new Rect(headerRect.x + 20f, headerRect.y + 55f, headerRect.width - 40f, headerRect.height - 65f);
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.9f, 0.9f, 0.9f);
            Widgets.Label(introRect, introText);
            GUI.color = Color.white;

            // === 底部功能卡片区 ===
            float contentY = topHeight + padding;
            float contentHeight = inRect.height - contentY - 55f;
            Rect outRect = new Rect(0, contentY, inRect.width, contentHeight);

            float cardHeight = 180f;
            int cols = 3;
            int rows = Mathf.CeilToInt((float)sections.Count / cols);
            float viewHeight = Mathf.Max(contentHeight, rows * (cardHeight + padding));

            Rect viewRect = new Rect(0, 0, inRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            if (sections.Count > 0)
            {
                float colWidth = (viewRect.width - (cols - 1) * padding) / cols;

                for (int i = 0; i < sections.Count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;

                    Rect cardRect = new Rect(col * (colWidth + padding), row * (cardHeight + padding), colWidth, cardHeight);
                    DrawSectionCard(cardRect, sections[i], sectionBgColor, outlineColor);
                }
            }
            else
            {
                Widgets.Label(viewRect, rawDescription);
            }

            Widgets.EndScrollView();
        }

        private void DrawSectionCard(Rect rect, Section section, Color bgColor, Color outlineColor)
        {
            Widgets.DrawBoxSolidWithOutline(rect, bgColor, outlineColor);

            // 标题栏
            Rect titleRect = new Rect(rect.x, rect.y, rect.width, 32f);
            Widgets.DrawBoxSolid(titleRect, new Color(0f, 0f, 0f, 0.3f));
            Widgets.DrawLineHorizontal(rect.x, rect.y + 32f, rect.width);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 0.8f, 0.2f); // 金色标题
            Widgets.Label(titleRect, section.title);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            // 内容
            Rect contentRect = new Rect(rect.x + 10f, rect.y + 40f, rect.width - 20f, rect.height - 50f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(contentRect, section.content);
            Text.Font = GameFont.Small;
        }
    }
}
