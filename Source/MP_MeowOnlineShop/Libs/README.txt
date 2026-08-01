0Harmony.dll 用于编译时引用，运行时由 RimWorld 或 Harmony 前置 Mod 提供。

若本目录下已有 0Harmony.dll，可直接编译。若编译报错“未能找到引用的组件 0Harmony”，请任选其一：

方式一（推荐）：在项目目录执行还原后再编译
  dotnet restore MP_MeowOnlineShop.csproj

方式二：手动放置 DLL
  1. 从 NuGet 获取 Lib.Harmony 2.4.2：
     https://www.nuget.org/api/v2/package/Lib.Harmony/2.4.2
  2. 将下载的 .nupkg 改名为 .zip 并解压
  3. 将 lib\net472\0Harmony.dll 复制到本 Libs 文件夹（与本 README 同级）
