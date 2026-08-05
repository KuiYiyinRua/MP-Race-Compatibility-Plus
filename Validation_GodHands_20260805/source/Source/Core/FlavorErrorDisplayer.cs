using Verse;

namespace GodHandMod
{
    // 风味错误显示器
    [StaticConstructorOnStartup]
    public static class FlavorErrorDisplayer
    {
        static FlavorErrorDisplayer()
        {
            // 初始化显示
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                if (GodHandModMain.Settings.showFlavorError)
                {
                    ShowFlavorError();
                }
            });
        }

        private static void ShowFlavorError()
        {
            Log.Error("GodHand.FlavorError.Message".Translate());
        }
    }
}
