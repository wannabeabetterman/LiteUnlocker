// UnlockerGUI —— SimpleUnlocker 的简单图形启动器
//
// 功能：
//   1. 选择游戏 exe 路径
//   2. 填写目标 FPS / FOV 值
//   3. 点"启动" → 把值写入 Plugins\config.ini → 调用 Launcher.dll::LaunchGameAndInject
//
// 编译：dotnet build  或  msbuild  (Release 配置)
// 运行前需确保 Launcher.dll 和 Plugins\SimpleUnlocker.dll 在本 exe 同目录

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UnlockerGUI;

internal static class Program
{
    internal static readonly string RuntimeDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteUnlocker", "runtime");
    internal static readonly string PluginsDir = Path.Combine(RuntimeDir, "Plugins");
    internal static readonly string LauncherPath = Path.Combine(RuntimeDir, "Launcher.dll");

    [STAThread]
    private static void Main()
    {
        try
        {
            EnsureBundledRuntime();
            NativeLibrary.SetDllImportResolver(
                Assembly.GetExecutingAssembly(),
                (libraryName, assembly, searchPath) =>
                    string.Equals(libraryName, "Launcher.dll", StringComparison.OrdinalIgnoreCase)
                        ? NativeLibrary.Load(LauncherPath)
                        : IntPtr.Zero);

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "UnlockerGUI-error.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n", new UTF8Encoding(false));
            }
            catch { /* best effort */ }

            MessageBox.Show(ex.ToString(), "UnlockerGUI 启动异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void EnsureBundledRuntime()
    {
        Directory.CreateDirectory(RuntimeDir);
        Directory.CreateDirectory(PluginsDir);

        ExtractResource("Bundled.Launcher.dll", LauncherPath);
        ExtractResource("Bundled.SimpleUnlocker.dll", Path.Combine(PluginsDir, "SimpleUnlocker.dll"));
    }

    private static void ExtractResource(string resourceName, string targetPath)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        using Stream? input = asm.GetManifestResourceStream(resourceName);
        if (input == null)
            throw new InvalidOperationException($"缺少内嵌资源：{resourceName}");

        using var ms = new MemoryStream();
        input.CopyTo(ms);
        byte[] bundledBytes = ms.ToArray();

        bool needWrite = true;
        if (File.Exists(targetPath))
        {
            byte[] existingBytes = File.ReadAllBytes(targetPath);
            needWrite = !existingBytes.SequenceEqual(bundledBytes);
        }

        if (!needWrite) return;

        string tempPath = targetPath + ".tmp";
        File.WriteAllBytes(tempPath, bundledBytes);

        if (File.Exists(targetPath))
            File.Delete(targetPath);
        File.Move(tempPath, targetPath);
    }
}

internal enum LauncherState
{
    Idle,
    Starting,
    Running
}

internal sealed class MainForm : Form
{
    // ===== P/Invoke：调用 Launcher.dll 导出的注入函数 =====
    // 注意 CallingConvention.Cdecl：Launcher.h 用 extern "C"，C++ 默认 cdecl
    private const string LauncherDll = "Launcher.dll";

    [DllImport(LauncherDll, CallingConvention = CallingConvention.Cdecl,
               CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int LaunchGameAndInject(
        string gamePath,
        string dllPath,
        string commandLineArgs,
        StringBuilder errorMessage,
        int errorMessageSize);

    [DllImport(LauncherDll, CallingConvention = CallingConvention.Cdecl,
               CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool ValidateGamePath(string gamePath);

    // ===== 控件 =====
    private readonly TextBox _gamePathBox = new();
    private readonly NumericUpDown _fpsBox = new();
    private readonly NumericUpDown _fovBox = new();
    private readonly TrackBar _fovSpeedBar = new();
    private readonly Label _fovSpeedVal = new();
    private readonly CheckBox _enableFps = new();
    private readonly CheckBox _enableFov = new();
    private readonly CheckBox _enableVSync = new();
    private readonly CheckBox _enableRemoveTeamAnim = new();
    private readonly CheckBox _enableHideUid = new();
    private readonly CheckBox _enableDisableFog = new();
    private readonly CheckBox _enableDisableCharFade = new();
    private readonly CheckBox _enableRedirectCraft = new();
    private readonly ComboBox _craftKeyCombo = new();
    private readonly Button _startBtn = new();
    private readonly Button _diagBtn = new();
    private readonly Label _statusLabel = new();
    private readonly System.Windows.Forms.Timer _gameMonitorTimer = new();
    private Process? _gameProcess;
    private LauncherState _launcherState = LauncherState.Idle;
    private bool _configReady;

    public MainForm()
    {
        Text = "LiteUnlocker 启动器";
        Width = 560;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(241, 250, 246);
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi();
        LoadConfig();
        WireAutoSave();
        _gameMonitorTimer.Interval = 1500;
        _gameMonitorTimer.Tick += (_, _) => RefreshGameState();
        _configReady = true;
        DetectExistingGame();
    }

    private void BuildUi()
    {
        var header = new Panel
        {
            Left = 0,
            Top = 0,
            Width = ClientSize.Width,
            Height = 76,
            BackColor = Color.FromArgb(20, 83, 72)
        };
        header.Controls.Add(new Label
        {
            Text = "LiteUnlocker",
            Left = 20,
            Top = 13,
            Width = 230,
            Height = 30,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent
        });
        header.Controls.Add(new Label
        {
            Text = "帧率解锁 · FOV 调整 · 体验优化",
            Left = 22,
            Top = 43,
            Width = 360,
            Height = 22,
            ForeColor = Color.FromArgb(204, 251, 241),
            BackColor = Color.Transparent
        });
        Controls.Add(header);

        // --- 游戏路径 ---
        var pathGroup = MakeGroup("游戏路径", 18, 88, 508, 70);
        var lblGame = new Label { Text = "路径：", Left = 16, Top = 30, Width = 52 };
        _gamePathBox.Left = 68; _gamePathBox.Top = 27; _gamePathBox.Width = 338;
        var browseBtn = new Button { Text = "浏览…", Left = 416, Top = 25, Width = 72, Height = 28 };
        StyleSecondaryButton(browseBtn);
        browseBtn.Click += OnBrowse;
        pathGroup.Controls.AddRange(new Control[] { lblGame, _gamePathBox, browseBtn });
        Controls.Add(pathGroup);

        // --- FPS ---
        var fpsGroup = MakeGroup("帧率设置", 18, 166, 508, 72);
        _enableFps.Text = "解锁 FPS";
        _enableFps.Checked = true;
        _enableFps.Left = 16; _enableFps.Top = 31; _enableFps.Width = 100;
        var lblFps = new Label { Text = "目标：", Left = 145, Top = 33, Width = 44 };
        _fpsBox.Left = 190; _fpsBox.Top = 28; _fpsBox.Width = 76;
        _fpsBox.Minimum = 30; _fpsBox.Maximum = 360; _fpsBox.Value = 120; _fpsBox.Increment = 5;
        var lblFpsUnit = new Label { Text = "FPS", Left = 273, Top = 33, Width = 40 };
        _enableVSync.Text = "关闭垂直同步";
        _enableVSync.Checked = true;
        _enableVSync.Left = 340; _enableVSync.Top = 31; _enableVSync.Width = 140;
        fpsGroup.Controls.AddRange(new Control[] { _enableFps, lblFps, _fpsBox, lblFpsUnit, _enableVSync });
        Controls.Add(fpsGroup);

        // --- FOV ---
        var fovGroup = MakeGroup("视角设置", 18, 246, 508, 112);
        _enableFov.Text = "修改 FOV";
        _enableFov.Checked = true;
        _enableFov.Left = 16; _enableFov.Top = 29; _enableFov.Width = 100;
        var lblFov = new Label { Text = "目标：", Left = 145, Top = 31, Width = 44 };
        _fovBox.Left = 190; _fovBox.Top = 26; _fovBox.Width = 76;
        _fovBox.Minimum = 20; _fovBox.Maximum = 120; _fovBox.Value = 60; _fovBox.DecimalPlaces = 1; _fovBox.Increment = 5;
        var lblFovUnit = new Label { Text = "°", Left = 273, Top = 31, Width = 20 };

        // --- FOV 过渡速度滑块 ---
        var lblSpeed = new Label { Text = "过渡速度：", Left = 16, Top = 67, Width = 80 };
        _fovSpeedBar.Left = 96; _fovSpeedBar.Top = 61; _fovSpeedBar.Width = 250;
        _fovSpeedBar.Minimum = 0; _fovSpeedBar.Maximum = 100; _fovSpeedBar.Value = 5;   // 默认 0.05
        _fovSpeedBar.TickFrequency = 10;
        _fovSpeedVal.Left = 356; _fovSpeedVal.Top = 67; _fovSpeedVal.Width = 54;
        _fovSpeedVal.Text = "0.05";
        // 滑块滚动时实时显示数值（value/100 = 实际比例）
        _fovSpeedBar.Scroll += (_, _) =>
            _fovSpeedVal.Text = (_fovSpeedBar.Value / 100.0).ToString("0.00");
        var lblSpeedHint = new Label
        {
            Text = "0=瞬切  0.05=丝滑(推荐)  1=立即",
            Left = 96, Top = 89, Width = 380, ForeColor = Color.Gray
        };
        fovGroup.Controls.AddRange(new Control[] {
            _enableFov, lblFov, _fovBox, lblFovUnit,
            lblSpeed, _fovSpeedBar, _fovSpeedVal, lblSpeedHint
        });
        Controls.Add(fovGroup);

        // --- 体验优化 ---
        var extraGroup = MakeGroup("体验优化", 18, 366, 508, 62);
        _enableRemoveTeamAnim.Text = "移除队伍切换动画";
        _enableRemoveTeamAnim.Left = 16; _enableRemoveTeamAnim.Top = 28; _enableRemoveTeamAnim.Width = 170;
        _enableHideUid.Text = "隐藏 UID 水印";
        _enableHideUid.Left = 220; _enableHideUid.Top = 28; _enableHideUid.Width = 160;
        extraGroup.Controls.AddRange(new Control[] { _enableRemoveTeamAnim, _enableHideUid });
        Controls.Add(extraGroup);

        // --- 视觉效果 ---
        var visualGroup = MakeGroup("视觉效果", 18, 440, 508, 62);
        _enableDisableFog.Text = "关闭场景雾效";
        _enableDisableFog.Left = 16; _enableDisableFog.Top = 28; _enableDisableFog.Width = 170;
        _enableDisableCharFade.Text = "关闭角色半透明";
        _enableDisableCharFade.Left = 220; _enableDisableCharFade.Top = 28; _enableDisableCharFade.Width = 170;
        visualGroup.Controls.AddRange(new Control[] { _enableDisableFog, _enableDisableCharFade });
        Controls.Add(visualGroup);

        // --- 便利功能（随身合成台）---
        var craftGroup = MakeGroup("便利功能", 18, 514, 508, 62);
        _enableRedirectCraft.Text = "随身合成台";
        _enableRedirectCraft.Left = 16; _enableRedirectCraft.Top = 28; _enableRedirectCraft.Width = 130;
        var lblCraftKey = new Label { Text = "热键", Left = 170, Top = 32, Width = 36, Height = 20 };
        _craftKeyCombo.Left = 210; _craftKeyCombo.Top = 28; _craftKeyCombo.Width = 100;
        _craftKeyCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _craftKeyCombo.Font = new Font("Microsoft YaHei UI", 9F);
        _craftKeyCombo.Items.AddRange(new object[] { "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12" });
        _craftKeyCombo.SelectedIndex = 11;  // 默认 F12
        var lblCraftHint = new Label { Text = "（涉及游戏交互，风险较高）", Left = 320, Top = 32, Width = 180, Height = 20, ForeColor = Color.Gray };
        craftGroup.Controls.AddRange(new Control[] { _enableRedirectCraft, lblCraftKey, _craftKeyCombo, lblCraftHint });
        Controls.Add(craftGroup);

        // --- 启动按钮 ---
        _startBtn.Text = "▶  启动游戏";
        _startBtn.Left = 18; _startBtn.Top = 588; _startBtn.Width = 508; _startBtn.Height = 44;
        _startBtn.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        StylePrimaryButton();
        _startBtn.Click += OnStart;

        // --- 状态栏 ---
        _statusLabel.Left = 20; _statusLabel.Top = 640; _statusLabel.Width = 410; _statusLabel.Height = 22;
        _statusLabel.Text = "就绪。";
        _statusLabel.ForeColor = Color.FromArgb(21, 128, 61);

        // --- 诊断按钮 ---
        _diagBtn.Text = "特征码诊断";
        _diagBtn.Left = 438; _diagBtn.Top = 638; _diagBtn.Width = 90; _diagBtn.Height = 26;
        _diagBtn.Font = new Font("Microsoft YaHei UI", 8.5F);
        StyleSecondaryButton(_diagBtn);
        _diagBtn.Click += OnDiagnose;

        Controls.AddRange(new Control[] {
            _startBtn,
            _diagBtn,
            _statusLabel
        });
    }

    private GroupBox MakeGroup(string text, int left, int top, int width, int height)
    {
        return new GroupBox
        {
            Text = text,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(30, 92, 76),
            Font = new Font("Microsoft YaHei UI", 9F)
        };
    }

    private void StyleSecondaryButton(Button button)
    {
        button.BackColor = Color.FromArgb(220, 252, 231);
        button.ForeColor = Color.FromArgb(20, 83, 45);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(134, 239, 172);
        button.UseVisualStyleBackColor = false;
    }

    private void StylePrimaryButton()
    {
        _startBtn.BackColor = Color.FromArgb(16, 185, 129);
        _startBtn.ForeColor = Color.White;
        _startBtn.FlatStyle = FlatStyle.Flat;
        _startBtn.FlatAppearance.BorderSize = 0;
        _startBtn.UseVisualStyleBackColor = false;
    }

    private void StyleDangerButton()
    {
        _startBtn.BackColor = Color.FromArgb(220, 38, 38);
        _startBtn.ForeColor = Color.White;
        _startBtn.FlatStyle = FlatStyle.Flat;
        _startBtn.FlatAppearance.BorderSize = 0;
        _startBtn.UseVisualStyleBackColor = false;
    }

    private void WireAutoSave()
    {
        _enableFps.CheckedChanged += (_, _) => TrySaveConfig();
        _fpsBox.ValueChanged += (_, _) => TrySaveConfig();
        _enableFov.CheckedChanged += (_, _) => TrySaveConfig();
        _fovBox.ValueChanged += (_, _) => TrySaveConfig();
        _fovSpeedBar.ValueChanged += (_, _) => TrySaveConfig();
        _enableVSync.CheckedChanged += (_, _) => TrySaveConfig();
        _enableRemoveTeamAnim.CheckedChanged += (_, _) => TrySaveConfig();
        _enableHideUid.CheckedChanged += (_, _) => TrySaveConfig();
        _enableDisableFog.CheckedChanged += (_, _) => TrySaveConfig();
        _enableDisableCharFade.CheckedChanged += (_, _) => TrySaveConfig();
        _enableRedirectCraft.CheckedChanged += (_, _) => TrySaveConfig();
        _craftKeyCombo.SelectedIndexChanged += (_, _) => TrySaveConfig();
        _gamePathBox.Leave += (_, _) => TrySaveConfig();
        FormClosing += (_, _) => TrySaveConfig();
    }

    // ---- 特征码诊断：读取 Plugins\diag.json 并弹窗显示 ----
    private void OnDiagnose(object? s, EventArgs e)
    {
        string diagPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "diag.json");
        if (!File.Exists(diagPath))
        {
            MessageBox.Show(
                "未找到诊断文件。\n\n请先点「启动游戏」注入插件，插件会在 Plugins\\diag.json 生成诊断结果。",
                "特征码诊断", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            string json = File.ReadAllText(diagPath, Encoding.UTF8);
            // 简易正则解析（diag.json 格式固定：name/feature/ok/note 字段）
            var rows = new List<(string name, string feature, bool ok, string note)>();
            var match = System.Text.RegularExpressions.Regex.Match(json,
                @"""name"":""([^""]*)""[^}]*?""feature"":""([^""]*)""[^}]*?""ok"":(true|false)[^}]*?""note"":""([^""]*)""");
            while (match.Success)
            {
                rows.Add((match.Groups[1].Value, match.Groups[2].Value,
                          match.Groups[3].Value == "true", match.Groups[4].Value));
                match = match.NextMatch();
            }

            if (rows.Count == 0)
            {
                MessageBox.Show("诊断文件为空或格式异常。", "特征码诊断",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int okCount = rows.Count(r => r.ok);
            int failCount = rows.Count - okCount;

            // 用 ListView 显示表格结果
            using var dlg = new Form
            {
                Text = $"特征码诊断  ({okCount} 成功 / {failCount} 失败)",
                Width = 560, Height = 440,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.White
            };
            var lv = new ListView
            {
                Left = 12, Top = 12, Width = 520, Height = 340,
                View = View.Details, FullRowSelect = true, GridLines = true,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            lv.Columns.Add("状态", 56);
            lv.Columns.Add("特征码", 150);
            lv.Columns.Add("所属功能", 130);
            lv.Columns.Add("类型", 150);
            foreach (var r in rows)
            {
                var item = new ListViewItem(r.ok ? "✓ OK" : "✗ 失败");
                item.ForeColor = r.ok ? Color.FromArgb(21, 128, 61) : Color.FromArgb(185, 28, 28);
                item.SubItems.Add(r.name);
                item.SubItems.Add(r.feature);
                item.SubItems.Add(r.note);
                lv.Items.Add(item);
            }
            var hint = new Label
            {
                Left = 12, Top = 360, Width = 520, Height = 32,
                Text = failCount == 0
                    ? "全部特征码生效，所有功能可用。"
                    : $"有 {failCount} 个特征码失效，对应功能可能无法使用（通常因游戏版本更新）。需重新逆向定位。",
                ForeColor = failCount == 0 ? Color.FromArgb(21, 128, 61) : Color.FromArgb(185, 28, 28)
            };
            dlg.Controls.AddRange(new Control[] { lv, hint });
            dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"读取诊断文件失败：{ex.Message}", "特征码诊断",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---- 浏览选游戏 exe ----
    private void OnBrowse(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择游戏主程序",
            Filter = "游戏程序 (*.exe)|*.exe|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _gamePathBox.Text = dlg.FileName;
            TrySaveConfig();
        }
    }

    // ---- 启动 ----
    private async void OnStart(object? s, EventArgs e)
    {
        if (_launcherState == LauncherState.Running)
        {
            StopGame();
            return;
        }

        if (_launcherState == LauncherState.Starting)
        {
            return;
        }

        await StartGameAsync();
    }

    private async Task StartGameAsync()
    {
        string gamePath = _gamePathBox.Text.Trim();
        if (string.IsNullOrEmpty(gamePath) || !File.Exists(gamePath))
        {
            SetStatus("✗ 请先选择有效的游戏 exe 路径", error: true);
            return;
        }

        Process? existingProcess = FindGameProcess(gamePath, DateTime.MinValue);
        if (existingProcess != null)
        {
            _gameProcess = existingProcess;
            _gameMonitorTimer.Start();
            SetLauncherState(LauncherState.Running, $"检测到游戏已在运行（进程 ID：{existingProcess.Id}），已切换为关闭模式。");
            return;
        }

        // 1. 把用户设置写入 Plugins\config.ini
        try
        {
            WriteConfig();
        }
        catch (Exception ex)
        {
            SetStatus($"✗ 写入配置失败：{ex.Message}", error: true);
            return;
        }

        // 2. 调用 Launcher.dll 启动并注入
        SetLauncherState(LauncherState.Starting, "正在启动游戏…");
        DateTime launchTime = DateTime.Now.AddSeconds(-2);

        int ret;
        string errorText;
        try
        {
            var result = await Task.Run(() =>
            {
                var errorMsg = new StringBuilder(512);
                // dllPath 参数保留未用，传空串；commandLineArgs 传空
                int code = LaunchGameAndInject(gamePath, "", "", errorMsg, errorMsg.Capacity);
                return (code, errorMsg.ToString());
            });
            ret = result.code;
            errorText = result.Item2;
        }
        catch (DllNotFoundException)
        {
            SetStatus("✗ 找不到 Launcher.dll，请确保它与本程序在同一目录。", error: true);
            SetLauncherState(LauncherState.Idle);
            return;
        }
        catch (Exception ex)
        {
            SetStatus($"✗ 异常：{ex.Message}", error: true);
            SetLauncherState(LauncherState.Idle);
            return;
        }

        if (ret == 0)
        {
            _gameProcess = FindGameProcess(gamePath, launchTime);
            _gameMonitorTimer.Start();
            SetLauncherState(LauncherState.Running,
                _gameProcess == null
                    ? "✓ 启动成功。未能自动绑定游戏进程，关闭按钮会再次查找。"
                    : $"✓ 启动成功。进程 ID：{_gameProcess.Id}");
        }
        else
        {
            SetStatus($"✗ 启动失败 (代码 {ret})：{errorText}", error: true);
            SetLauncherState(LauncherState.Idle);
        }
    }

    private void StopGame()
    {
        string gamePath = _gamePathBox.Text.Trim();
        Process? process = GetLiveGameProcess();
        process ??= !string.IsNullOrEmpty(gamePath) ? FindGameProcess(gamePath, DateTime.MinValue) : null;

        if (process == null)
        {
            SetStatus("游戏进程已结束。");
            SetLauncherState(LauncherState.Idle);
            return;
        }

        try
        {
            SetStatus("正在关闭游戏…");
            if (!process.CloseMainWindow() || !process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }

            SetStatus("✓ 游戏已关闭。");
            SetLauncherState(LauncherState.Idle);
        }
        catch (Exception ex)
        {
            SetStatus($"✗ 关闭游戏失败：{ex.Message}", error: true);
        }
    }

    private void RefreshGameState()
    {
        if (_launcherState != LauncherState.Running) return;

        Process? process = GetLiveGameProcess();
        if (process != null) return;

        _gameMonitorTimer.Stop();
        _gameProcess = null;
        SetLauncherState(LauncherState.Idle, "游戏已退出。");
    }

    private void DetectExistingGame()
    {
        string gamePath = _gamePathBox.Text.Trim();
        if (string.IsNullOrEmpty(gamePath) || !File.Exists(gamePath)) return;

        Process? existingProcess = FindGameProcess(gamePath, DateTime.MinValue);
        if (existingProcess == null) return;

        _gameProcess = existingProcess;
        _gameMonitorTimer.Start();
        SetLauncherState(LauncherState.Running, $"检测到游戏已在运行（进程 ID：{existingProcess.Id}）。");
    }

    private Process? GetLiveGameProcess()
    {
        try
        {
            if (_gameProcess != null && !_gameProcess.HasExited) return _gameProcess;
        }
        catch { /* process may have exited */ }

        _gameProcess = null;
        return null;
    }

    private Process? FindGameProcess(string gamePath, DateTime notBefore)
    {
        string exeName = Path.GetFileNameWithoutExtension(gamePath);
        var candidates = Process.GetProcessesByName(exeName)
            .Where(p =>
            {
                try { return !p.HasExited && p.StartTime >= notBefore; }
                catch { return false; }
            })
            .OrderByDescending(p =>
            {
                try { return p.StartTime; }
                catch { return DateTime.MinValue; }
            })
            .ToList();

        foreach (Process p in candidates)
        {
            try
            {
                string? modulePath = p.MainModule?.FileName;
                if (string.Equals(modulePath, gamePath, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            catch
            {
                // 某些进程模块路径读取可能失败；下面会用启动时间兜底。
            }
        }

        return candidates.FirstOrDefault();
    }

    private void SetLauncherState(LauncherState state, string? status = null)
    {
        _launcherState = state;
        switch (state)
        {
            case LauncherState.Idle:
                _startBtn.Enabled = true;
                _startBtn.Text = "▶  启动游戏";
                StylePrimaryButton();
                _gameMonitorTimer.Stop();
                _gameProcess = null;
                break;
            case LauncherState.Starting:
                _startBtn.Enabled = false;
                _startBtn.Text = "启动中…";
                _startBtn.BackColor = Color.FromArgb(94, 151, 132);
                break;
            case LauncherState.Running:
                _startBtn.Enabled = true;
                _startBtn.Text = "■  关闭游戏";
                StyleDangerButton();
                break;
        }

        if (!string.IsNullOrEmpty(status))
            SetStatus(status);
    }

    // ---- 写 Plugins\config.ini（格式与 Plugin\Config.cpp 读取逻辑一致）----
    private void WriteConfig()
    {
        // config.ini 必须和 SimpleUnlocker.dll 同目录。
        // 单 exe 分发时，DLL 会自动解包到 LocalAppData\LiteUnlocker\runtime\Plugins。
        string pluginsDir = Program.PluginsDir;
        Directory.CreateDirectory(pluginsDir);
        string cfgPath = Path.Combine(pluginsDir, "config.ini");

        // Windows INI 格式：[Section] + Value=...
        var sb = new StringBuilder();
        sb.AppendLine("; 由 UnlockerGUI 自动生成");
        sb.AppendLine();
        sb.AppendLine("[GamePath]");
        sb.AppendLine($"Value={_gamePathBox.Text.Trim()}");
        sb.AppendLine();
        sb.AppendLine("[DebugConsole]");
        sb.AppendLine("Value=0");
        sb.AppendLine();
        sb.AppendLine("[FpsUnlock]");
        sb.AppendLine($"Value={(_enableFps.Checked ? 1 : 0)}");
        sb.AppendLine();
        sb.AppendLine("[TargetFps]");
        sb.AppendLine($"Value={(int)_fpsBox.Value}");
        sb.AppendLine();
        sb.AppendLine("[VSync]");
        sb.AppendLine($"Value={(_enableVSync.Checked ? 1 : 0)}");
        sb.AppendLine();
        sb.AppendLine("[FovUnlock]");
        sb.AppendLine($"Value={(_enableFov.Checked ? 1 : 0)}");
        sb.AppendLine();
        sb.AppendLine("[FovValue]");
        sb.AppendLine($"Value={_fovBox.Value.ToString("F1", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("[FovTransitionSpeed]");
        // 滑块 0~100 映射到 0.00~1.00
        sb.AppendLine($"Value={(_fovSpeedBar.Value / 100.0).ToString("F2", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("[FovLimitCheck]");
        sb.AppendLine("Value=1");
        sb.AppendLine();
        sb.AppendLine("[RemoveTeamAnim]");
        sb.AppendLine($"Value={(_enableRemoveTeamAnim.Checked ? 1 : 0)}");
        sb.AppendLine();
        sb.AppendLine("[HideUID]");
        sb.AppendLine($"Value={(_enableHideUid.Checked ? 1 : 0)}");
        sb.AppendLine();
        sb.AppendLine("[DisableFog]");
        sb.AppendLine($"Value={(_enableDisableFog.Checked ? 1 : 0)}");
        sb.AppendLine();
        sb.AppendLine("[DisableCharFade]");
        sb.AppendLine($"Value={(_enableDisableCharFade.Checked ? 1 : 0)}");
        sb.AppendLine();
        sb.AppendLine("[RedirectCraft]");
        sb.AppendLine($"Value={(_enableRedirectCraft.Checked ? 1 : 0)}");
        sb.AppendLine();
        sb.AppendLine("[CraftKey]");
        // F1=112 ... F12=123；未选中视为 0
        int craftKey = _craftKeyCombo.SelectedIndex >= 0 ? 112 + _craftKeyCombo.SelectedIndex : 0;
        sb.AppendLine($"Value={craftKey}");

        File.WriteAllText(cfgPath, sb.ToString(), new UTF8Encoding(false));
    }

    private void LoadConfig()
    {
        string cfgPath = Path.Combine(Program.PluginsDir, "config.ini");
        if (!File.Exists(cfgPath)) return;

        try
        {
            var values = ReadIniValues(cfgPath);
            _gamePathBox.Text = GetValue(values, "GamePath", "");
            _enableFps.Checked = GetBool(values, "FpsUnlock", _enableFps.Checked);
            SetNumericValue(_fpsBox, GetDecimal(values, "TargetFps", _fpsBox.Value));
            _enableVSync.Checked = GetBool(values, "VSync", _enableVSync.Checked);
            _enableFov.Checked = GetBool(values, "FovUnlock", _enableFov.Checked);
            SetNumericValue(_fovBox, GetDecimal(values, "FovValue", _fovBox.Value));

            decimal speed = GetDecimal(values, "FovTransitionSpeed", _fovSpeedBar.Value / 100m);
            int sliderValue = (int)Math.Round(speed * 100m);
            _fovSpeedBar.Value = Math.Clamp(sliderValue, _fovSpeedBar.Minimum, _fovSpeedBar.Maximum);
            _fovSpeedVal.Text = (_fovSpeedBar.Value / 100.0).ToString("0.00");
            _enableRemoveTeamAnim.Checked = GetBool(values, "RemoveTeamAnim", _enableRemoveTeamAnim.Checked);
            _enableHideUid.Checked = GetBool(values, "HideUID", _enableHideUid.Checked);
            _enableDisableFog.Checked = GetBool(values, "DisableFog", _enableDisableFog.Checked);
            _enableDisableCharFade.Checked = GetBool(values, "DisableCharFade", _enableDisableCharFade.Checked);
            _enableRedirectCraft.Checked = GetBool(values, "RedirectCraft", _enableRedirectCraft.Checked);
            int craftKeyVal = (int)GetDecimal(values, "CraftKey", 123);
            _craftKeyCombo.SelectedIndex = (craftKeyVal >= 112 && craftKeyVal <= 123) ? craftKeyVal - 112 : 11;
        }
        catch (Exception ex)
        {
            SetStatus($"读取上次配置失败：{ex.Message}", error: true);
        }
    }

    private void TrySaveConfig()
    {
        if (!_configReady) return;
        try
        {
            WriteConfig();
        }
        catch (Exception ex)
        {
            SetStatus($"保存配置失败：{ex.Message}", error: true);
        }
    }

    private static Dictionary<string, string> ReadIniValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string section = "";
        foreach (string rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals > 0 && line[..equals].Trim().Equals("Value", StringComparison.OrdinalIgnoreCase))
                values[section] = line[(equals + 1)..].Trim();
        }
        return values;
    }

    private static string GetValue(Dictionary<string, string> values, string section, string fallback) =>
        values.TryGetValue(section, out string? value) ? value : fallback;

    private static bool GetBool(Dictionary<string, string> values, string section, bool fallback) =>
        int.TryParse(GetValue(values, section, fallback ? "1" : "0"), out int value) ? value != 0 : fallback;

    private static decimal GetDecimal(Dictionary<string, string> values, string section, decimal fallback) =>
        decimal.TryParse(GetValue(values, section, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)
            ? value : fallback;

    private static void SetNumericValue(NumericUpDown control, decimal value) =>
        control.Value = Math.Clamp(value, control.Minimum, control.Maximum);

    private void SetStatus(string text, bool error = false)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = error ? System.Drawing.Color.FromArgb(200, 40, 40)
                                       : Color.FromArgb(21, 128, 61);
    }
}
