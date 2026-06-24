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
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace UnlockerGUI;

internal static class Program
{
    [STAThread]
    private static void Main() =>
        Application.Run(new MainForm());
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
    private readonly Button _startBtn = new();
    private readonly Label _statusLabel = new();
    private bool _configReady;

    public MainForm()
    {
        Text = "SimpleUnlocker 启动器";
        Width = 520;
        Height = 400;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        BuildUi();
        LoadConfig();
        WireAutoSave();
        _configReady = true;
    }

    private void BuildUi()
    {
        int y = 15;

        // --- 游戏路径 ---
        var lblGame = new Label { Text = "游戏路径：", Left = 15, Top = y, Width = 80 };
        _gamePathBox.Left = 100; _gamePathBox.Top = y - 3; _gamePathBox.Width = 320;
        var browseBtn = new Button { Text = "浏览…", Left = 425, Top = y - 4, Width = 65 };
        browseBtn.Click += OnBrowse;
        y += 35;

        // --- FPS ---
        _enableFps.Text = "解锁 FPS";
        _enableFps.Checked = true;
        _enableFps.Left = 100; _enableFps.Top = y; _enableFps.Width = 90;
        var lblFps = new Label { Text = "目标：", Left = 200, Top = y, Width = 40 };
        _fpsBox.Left = 240; _fpsBox.Top = y - 3; _fpsBox.Width = 70;
        _fpsBox.Minimum = 30; _fpsBox.Maximum = 360; _fpsBox.Value = 120; _fpsBox.Increment = 5;
        var lblFpsUnit = new Label { Text = "FPS", Left = 315, Top = y, Width = 40 };
        y += 30;

        // --- FOV ---
        _enableFov.Text = "修改 FOV";
        _enableFov.Checked = true;
        _enableFov.Left = 100; _enableFov.Top = y; _enableFov.Width = 90;
        var lblFov = new Label { Text = "目标：", Left = 200, Top = y, Width = 40 };
        _fovBox.Left = 240; _fovBox.Top = y - 3; _fovBox.Width = 70;
        _fovBox.Minimum = 20; _fovBox.Maximum = 120; _fovBox.Value = 60; _fovBox.DecimalPlaces = 1; _fovBox.Increment = 5;
        var lblFovUnit = new Label { Text = "°", Left = 315, Top = y, Width = 20 };
        y += 30;

        // --- FOV 过渡速度滑块 ---
        var lblSpeed = new Label { Text = "过渡速度：", Left = 100, Top = y + 4, Width = 75 };
        _fovSpeedBar.Left = 180; _fovSpeedBar.Top = y; _fovSpeedBar.Width = 200;
        _fovSpeedBar.Minimum = 0; _fovSpeedBar.Maximum = 100; _fovSpeedBar.Value = 5;   // 默认 0.05
        _fovSpeedBar.TickFrequency = 10;
        _fovSpeedVal.Left = 385; _fovSpeedVal.Top = y + 4; _fovSpeedVal.Width = 60;
        _fovSpeedVal.Text = "0.05";
        // 滑块滚动时实时显示数值（value/100 = 实际比例）
        _fovSpeedBar.Scroll += (_, _) =>
            _fovSpeedVal.Text = (_fovSpeedBar.Value / 100.0).ToString("0.00");
        var lblSpeedHint = new Label
        {
            Text = "0=瞬切  0.05=丝滑(推荐)  1=立即",
            Left = 100, Top = y + 26, Width = 380, ForeColor = System.Drawing.Color.Gray
        };
        y += 50;

        // --- VSync ---
        _enableVSync.Text = "关闭垂直同步 (解锁帧率时建议勾选)";
        _enableVSync.Checked = true;
        _enableVSync.Left = 100; _enableVSync.Top = y; _enableVSync.Width = 380;
        y += 40;

        // --- 启动按钮 ---
        _startBtn.Text = "▶  启动游戏";
        _startBtn.Left = 100; _startBtn.Top = y; _startBtn.Width = 390; _startBtn.Height = 42;
        _startBtn.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold);
        _startBtn.BackColor = System.Drawing.Color.FromArgb(70, 130, 180);
        _startBtn.ForeColor = System.Drawing.Color.White;
        _startBtn.FlatStyle = FlatStyle.Flat;
        _startBtn.Click += OnStart;
        y += 55;

        // --- 状态栏 ---
        _statusLabel.Left = 15; _statusLabel.Top = y; _statusLabel.Width = 480; _statusLabel.Height = 40;
        _statusLabel.Text = "就绪。";

        Controls.AddRange(new Control[] {
            lblGame, _gamePathBox, browseBtn,
            _enableFps, lblFps, _fpsBox, lblFpsUnit,
            _enableFov, lblFov, _fovBox, lblFovUnit,
            lblSpeed, _fovSpeedBar, _fovSpeedVal, lblSpeedHint,
            _enableVSync,
            _startBtn,
            _statusLabel
        });
    }

    private void WireAutoSave()
    {
        _enableFps.CheckedChanged += (_, _) => TrySaveConfig();
        _fpsBox.ValueChanged += (_, _) => TrySaveConfig();
        _enableFov.CheckedChanged += (_, _) => TrySaveConfig();
        _fovBox.ValueChanged += (_, _) => TrySaveConfig();
        _fovSpeedBar.ValueChanged += (_, _) => TrySaveConfig();
        _enableVSync.CheckedChanged += (_, _) => TrySaveConfig();
        _gamePathBox.Leave += (_, _) => TrySaveConfig();
        FormClosing += (_, _) => TrySaveConfig();
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
    private void OnStart(object? s, EventArgs e)
    {
        string gamePath = _gamePathBox.Text.Trim();
        if (string.IsNullOrEmpty(gamePath) || !File.Exists(gamePath))
        {
            SetStatus("✗ 请先选择有效的游戏 exe 路径", error: true);
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
        SetStatus("正在启动并注入…");
        _startBtn.Enabled = false;
        try
        {
            var errorMsg = new StringBuilder(256);
            // dllPath 参数保留未用，传空串；commandLineArgs 传空
            int ret = LaunchGameAndInject(gamePath, "", "", errorMsg, 256);
            if (ret == 0)
            {
                SetStatus("✓ 启动成功，插件已注入。可在游戏中查看效果。");
            }
            else
            {
                SetStatus($"✗ 启动失败 (代码 {ret})：{errorMsg}", error: true);
            }
        }
        catch (DllNotFoundException)
        {
            SetStatus("✗ 找不到 Launcher.dll，请确保它与本程序在同一目录。", error: true);
        }
        catch (Exception ex)
        {
            SetStatus($"✗ 异常：{ex.Message}", error: true);
        }
        finally
        {
            _startBtn.Enabled = true;
        }
    }

    // ---- 写 Plugins\config.ini（格式与 Plugin\Config.cpp 读取逻辑一致）----
    private void WriteConfig()
    {
        // config.ini 必须和 SimpleUnlocker.dll 同目录，即 exe 同级下的 Plugins\
        string exeDir = AppContext.BaseDirectory;
        string pluginsDir = Path.Combine(exeDir, "Plugins");
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

        File.WriteAllText(cfgPath, sb.ToString(), new UTF8Encoding(false));
    }

    private void LoadConfig()
    {
        string cfgPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "config.ini");
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
                                       : System.Drawing.Color.FromArgb(40, 120, 40);
    }
}
