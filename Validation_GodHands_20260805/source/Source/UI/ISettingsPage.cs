using Verse;

namespace GodHandMod
{
    // 设置页面接口
    public interface ISettingsPage
    {
        // 页面在导航栏显示的标签
        string Label { get; }

        // 绘制页面内容
        void Draw(Listing_Standard listing, GodHandSettings settings);

        // 视图高度
        // 视图高度
        float GetViewHeight(GodHandSettings settings);

        // 重置当前页面的设置
        void Reset(GodHandSettings settings);

        // 页面排序优先级 (越小越靠前)
        int Order { get; }
    }
}
