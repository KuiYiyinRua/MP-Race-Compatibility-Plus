using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 手术知识问答窗口
    public class Window_GodHandQuiz : Window
    {
        private string question;
        private List<string> options;
        private int correctIndex;
        private Action onCorrect;
        private Action onIncorrect;

        public override Vector2 InitialSize => new Vector2(500f, 400f);

        public Window_GodHandQuiz(Action onCorrect, Action onIncorrect)
        {
            this.onCorrect = onCorrect;
            this.onIncorrect = onIncorrect;
            this.doCloseX = false;
            this.closeOnClickedOutside = false;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;

            // 随机选取题目
            var quiz = DefDatabase<GodHandQuizDef>.AllDefs.RandomElementWithFallback();
            if (quiz != null)
            {
                this.question = quiz.question;
                this.options = quiz.options;
                this.correctIndex = quiz.correctIndex;
            }
            else
            {
                // 保底逻辑
                this.question = "（题库加载失败）原版医疗技能叫什么？";
                this.options = new List<string> { "射击", "医疗", "格斗", "烹饪" };
                this.correctIndex = 1;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Text.Font = GameFont.Medium;
            listing.Label("GodHand.Quiz.Title".Translate());
            Text.Font = GameFont.Small;
            listing.Gap();

            listing.Label("GodHand.Quiz.Description".Translate());
            listing.GapLine();

            listing.Label("GodHand.Quiz.QuestionLabel".Translate(question));
            listing.Gap();

            for (int i = 0; i < options.Count; i++)
            {
                if (listing.ButtonText(options[i]))
                {
                    if (i == correctIndex)
                    {
                        Messages.Message("GodHand.Quiz.Correct".Translate(), MessageTypeDefOf.PositiveEvent);
                        onCorrect?.Invoke();
                        Close();
                    }
                    else
                    {
                        Messages.Message("GodHand.Quiz.Incorrect".Translate(), MessageTypeDefOf.RejectInput);
                        onIncorrect?.Invoke();
                        Close();
                    }
                }
                listing.Gap(4f);
            }

            listing.End();
        }
    }
}
