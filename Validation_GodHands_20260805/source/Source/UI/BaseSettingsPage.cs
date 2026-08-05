using Verse;
using UnityEngine;
using RimWorld;

namespace GodHandMod.SettingsPages
{
    // 自动发现的设置页面基类
    // 新增设置页面从此类继承即可自动注册
    public abstract class BaseSettingsPage : ISettingsPage
    {
        public abstract string Label { get; }

        public virtual float GetViewHeight(GodHandSettings settings)
        {
            // 默认高度
            return 800f;
        }

        public virtual void Reset(GodHandSettings settings)
        {
            // 默认无动作
        }

        public virtual int Order => 100;

        public void Draw(Listing_Standard listing, GodHandSettings settings)
        {
            DrawContent(listing, settings);
            DrawResetButton(listing, settings);
        }

        protected abstract void DrawContent(Listing_Standard listing, GodHandSettings settings);

        protected virtual void DrawResetButton(Listing_Standard listing, GodHandSettings settings)
        {
            listing.Gap(20f);
            if (listing.ButtonText("GodHand.Settings.ResetThisPage".Translate()))
            {
                Reset(settings);
                Messages.Message("Settings Reset", MessageTypeDefOf.NeutralEvent, false);
            }
        }
    }
}
