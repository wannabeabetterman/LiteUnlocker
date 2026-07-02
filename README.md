# SimpleUnlocker

从 [FufuLauncher.UnlockerIsland](https://github.com/FufuLauncher/FufuLauncher.UnlockerIsland) 剥离出的**精简版**，专注体验优化：

- **FPS 解锁**（解除游戏帧率上限，目标可调）
- **FOV 修改**（修改摄像机视场角，含**平滑过渡**——切场景/队伍时丝滑拉开）
- **移除队伍切换动画**（跳过角色展示，直接显示队伍页）
- **隐藏 UID 水印**（隐藏屏幕右上角 UID，直播/截图隐私保护；仅本地显示，不影响联机）
- **关闭场景雾效**（远景更清晰，画面更通透）
- **关闭角色半透明**（摄像机贴脸时角色不再变半透明）
- **随身合成台**（热键随时打开合成界面 + 走到合成台秒开；⚠️ 涉及游戏交互，风险较高）
- **深色现代 GUI**（青绿强调色，高 DPI 支持，卡片渐变层次）

删掉了原仓库的 FreeCam、派蒙、隐藏UI、伤害数字、F12合成台、联网授权验证等所有无关或高风险逻辑。

---

## ⚠️ 风险声明（必读）

1. **违反游戏服务条款**：注入、修改客户端属违规行为，**有封号风险**。
2. **反作弊检测**：本程序仅做了"帧计数钳回 60/45/30"的基础规避，**不保证不被检测**。
3. **仅供学习研究**：理解 Hook / 注入 / 特征码扫描技术之用，请勿用于线上正式账号。

---

## 目录结构

```
D:\zhj\
├── SimpleUnlocker.sln              ← 解决方案（用 VS 打开这个）
├── Plugin\                         ← 功能插件（被注入到游戏进程）
│   ├── SimpleUnlocker.vcxproj      ← 插件工程
│   ├── dllmain.cpp                 ← DLL 入口（去掉了原版的联网授权验证）
│   ├── Hooks.cpp / .h              ← FPS/FOV Hook + 反检测 + 队伍动画
│   ├── Scanner.cpp / .h            ← 特征码扫描器
│   ├── Config.cpp / .h             ← 配置读取
│   ├── config.ini                  ← 配置示例
│   └── MinHook\                    ← Hook 库（从原仓库拷贝）
│       ├── MinHook.h
│       └── libMinHook.x64.lib
├── Launcher\                       ← 注入器
│   ├── Launcher.vcxproj
│   ├── Launcher.cpp                ← CreateProcess挂起 + LoadLibrary注入
│   └── Launcher.h
└── UnlockerGUI\                    ← 图形启动器（C# WinForms，深色主题）
    ├── UnlockerGUI.csproj
    ├── Program.cs                  ← UI + 业务逻辑（自绘卡片/按钮/复选框）
    └── app.manifest                ← 高 DPI 感知声明（保证高分屏不模糊）
```

## 编译

### 环境要求
- Visual Studio 2022（Community 免费版即可）
- 安装时勾选 **"使用 C++ 的桌面开发"**（含 MSVC v143 + Windows 10/11 SDK）

### 方式一：Visual Studio 图形界面
1. 双击打开 `SimpleUnlocker.sln`
2. 顶部配置选 **Release | x64**
3. 菜单 **生成 → 生成解决方案**（Ctrl+Shift+B）
4. 产物在 `D:\zhj\build\` 下：`Launcher.dll` 和 `SimpleUnlocker.dll`

### 方式二：命令行 MSBuild
打开 **"x64 Native Tools Command Prompt for VS 2022"**：
```cmd
cd /d D:\zhj
msbuild SimpleUnlocker.sln /p:Configuration=Release /p:Platform=x64
```

## 部署

编译完成后，按以下结构摆放文件（关键：`config.ini` 必须和 `SimpleUnlocker.dll` 同目录）：

```
任意文件夹\
├── Launcher.dll              ← 来自 build\
├── Plugins\
│   ├── SimpleUnlocker.dll    ← 来自 build\
│   └── config.ini            ← 来自 Plugin\（按需修改里面的值）
└── Launcher.exe              ← 你自己写的简单启动器（见下）
```

> 注：当前仓库**未包含 Launcher.exe**。`Launcher.dll` 只是个 dll，需要一个宿主程序调用它导出的 `LaunchGameAndInject`。你可以：
> - 用 C# 写一个带按钮的简单窗口（P/Invoke 调用）
> - 或用任意语言 LoadLibrary 后 GetProcAddress 调用

## 调用方式（路线A：复用 Launcher.dll）

Launcher.dll 导出函数签名（见 `Launcher.h`）：
```cpp
int LaunchGameAndInject(
    const wchar_t* gamePath,         // 游戏 exe 完整路径
    const wchar_t* dllPath,          // 保留参数，传 nullptr 或空串
    const wchar_t* commandLineArgs,  // 游戏启动参数，可空
    wchar_t* errorMessage,           // 接收错误信息（256 宽字符缓冲）
    int errorMessageSize);           // 返回 0 成功
```

### C# 启动器示例
```csharp
using System.Runtime.InteropServices;

class LauncherInterop
{
    [DllImport("Launcher.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern int LaunchGameAndInject(
        string gamePath, string dllPath, string args,
        System.Text.StringBuilder errorMessage, int errorMessageSize);

    public static void Launch(string gameExe)
    {
        var err = new System.Text.StringBuilder(256);
        int ret = LaunchGameAndInject(gameExe, "", "", err, 256);
        if (ret != 0)
            System.Windows.Forms.MessageBox.Show($"启动失败({ret}): {err}");
    }
}
```
UI 上放一个"选择游戏路径"按钮 + "启动"按钮即可。

## 图形启动器 UnlockerGUI（已内置）

仓库已自带一个 C# WinForms 启动器 `UnlockerGUI\`，采用**深色现代面板**设计（青绿强调色 #14B8A6，自绘圆角卡片/按钮/复选框，高 DPI 支持）。

功能：选游戏路径 → 勾选/调节各项（FPS/FOV/过渡速度/移除队伍动画）→ 点 "LAUNCH GAME" 自动写 config.ini 并调用 `LaunchGameAndInject`。

界面特性：
- **分组卡片**：Graphics / Camera / UI 三个功能区，自绘圆角 + 渐变层次
- **自动保存**：改动任何控件即时写盘，下次启动自动恢复上次配置
- **渐变开关**：右上角 "◐ Gradient / ◑ Flat" 链接，切换卡片的层次感渐变效果
- **高 DPI**：配合 `app.manifest`（PerMonitorV2 声明），2K/4K 屏不模糊

### 编译（需 .NET 8 SDK）

```cmd
cd /d D:\zhj\UnlockerGUI
dotnet build -c Release
```
产物：`UnlockerGUI\bin\Release\net8.0-windows\win-x64\UnlockerGUI.exe`

### 发布成单文件 exe（推荐，便于分发）

在仓库根目录运行一键发布脚本，它会先重新编译两个原生 DLL，再生成单文件，
可避免把旧版 DLL 误打进发布包：

```powershell
.\build-release.ps1 -Version 1.0.0
```
产物：`dist\LiteUnlocker-v1.0.0-win-x64.exe`（单文件、自包含，目标机无需装 .NET）

> 如果没装 .NET 8 SDK，也可用 Visual Studio 2022 打开 `SimpleUnlocker.sln` 直接编译全部三个工程（需勾选 ".NET 桌面开发" 工作负载）。
>
> `app.manifest` 是 csproj 引用的高 DPI 感知声明文件，保证深色面板在 2K/4K 高分屏上不模糊。它会被嵌入 exe，无需单独分发。

### 最终部署目录

把三个工程的产物按下表摆放，`UnlockerGUI.exe` 即可直接运行：

```
任意文件夹\
├── UnlockerGUI.exe        ← GUI 启动器（来自 publish\）
├── Launcher.dll           ← 注入器（来自 build\）
└── Plugins\
    ├── SimpleUnlocker.dll ← 功能插件（来自 build\）
    └── config.ini         ← 由 GUI 启动时自动生成，无需手动放
```

GUI 点 "LAUNCH GAME" 时会自动把界面上的所有设置写入 `Plugins\config.ini`，所以无需手动编辑配置。

## 配置说明（config.ini）

| Section | 含义 | 示例值 |
|---------|------|--------|
| `[FpsUnlock]` | 是否解锁帧率 | 1 |
| `[TargetFps]` | 目标帧率 | 120 |
| `[VSync]` | 关闭垂直同步 | 1 |
| `[FovUnlock]` | 是否改 FOV | 1 |
| `[FovValue]` | 目标 FOV | 60.0 |
| `[FovTransitionSpeed]` | FOV 平滑过渡速度（0=瞬切，0.05=丝滑） | 0.05 |
| `[FovLimitCheck]` | FOV>30 才覆盖（保护过场/队伍特写） | 1 |
| `[RemoveTeamAnim]` | 移除队伍切换动画 | 1 |
| `[HideUID]` | 隐藏 UID 水印（隐私保护） | 1 |
| `[DisableFog]` | 关闭场景雾效 | 0 |
| `[DisableCharFade]` | 关闭角色穿模半透明 | 0 |
| `[RedirectCraft]` | 随身合成台（风险较高） | 0 |
| `[CraftKey]` | 随身合成热键（123=F12） | 123 |
| `[DebugConsole]` | 弹控制台看日志 | 0 |

修改 config.ini **无需重启游戏**，插件会自动热重载。

## 技术原理速览

1. **注入**：`Launcher.dll` 以挂起态启动游戏 → 扫描 `Plugins\` → `CreateRemoteThread + LoadLibraryW` 注入所有 dll
2. **反检测**：`hk_GetFrameCount` 把游戏自报的帧计数钳回 60/45/30，骗过锁帧检测
3. **FPS 解锁**：`hk_ChangeFov` 每帧调用 `SetFrameCount` 把目标帧率设成用户值
4. **FOV 修改**：`hk_ChangeFov` 拦截 ChangeFOV 调用，把 value 参数改写成用户值后传给原函数
5. **FOV 平滑过渡**：用 `g_CurrentFov` 全局变量每帧做线性插值（指数衰减）逼近目标值，切场景时丝滑拉开而非瞬切
6. **特殊镜头保护**：FOV 修改只在 `value > 30` 时生效——队伍特写/过场动画时游戏会传入窄 FOV（<30），此时不覆盖，避免镜头穿帮
7. **移除队伍动画**：`hk_OpenTeam` 拦截打开队伍的调用，改为直接调 `OpenTeamPage(false)` 跳过角色展示动画
8. **隐藏 UID 水印**：主循环每 2 秒通过 `FindString + FindGameObject` 找到屏幕 UID 水印的 UI 对象，调 `SetActive(obj, false)` 隐藏。仅改本地显示，服务器侧真实 UID 不变
9. **关闭场景雾效**：`hk_DisplayFog` 拦截雾效函数，把雾参数复制到对齐安全缓冲后将"雾开关"标志位置 0，再用改造参数调原函数
10. **关闭角色半透明**：`hk_PlayerPerspective` 拦截角色透视函数，将透明度参数强制设为 1.0（完全不透明）
11. **随身合成台**：双路径设计。路径A——热键线程设置 `g_RequestCraft` 标志，主循环检测到后在游戏主线程调用 `CraftPartner` 打开合成界面（跨线程标志转发，避免 UI 跨线程崩溃）；路径B——`hk_CraftEntry` 拦截游戏原本的合成台入口，绕过位置检查直接打开界面

## 已知限制

- **特征码会随游戏版本失效**：`Patterns` 里的字节序列对应特定版本，游戏更新后可能需要重新逆向
- 原仓库 README 称"7.0 之前版本可用"

## 致谢

基于 [FufuLauncher.UnlockerIsland](https://github.com/FufuLauncher/FufuLauncher.UnlockerIsland) 与
[Snap.Hutao.Remastered.UnlockerIsland](https://github.com/SnapHutaoRemasteringProject/Snap.Hutao.Remastered.UnlockerIsland)。
