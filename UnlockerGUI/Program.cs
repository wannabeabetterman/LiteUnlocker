// UnlockerGUI —— SimpleUnlocker 图形启动器（深色现代面板版）
//
// 视觉风格：参考 Discord/Spotify/Linear 的深色面板，青绿强调色 #14B8A6
// 业务逻辑：完全保留（P-Invoke 调 Launcher.dll、config.ini 读写、自动保存）
//
// 编译：dotnet publish -c Release
// 运行前需 Launcher.dll 和 Plugins\SimpleUnlocker.dll 在本 exe 同目录

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace UnlockerGUI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    // ===================== 主题配色 =====================
    private static readonly Color Bg          = Color.FromArgb(0x0F, 0x14, 0x19); // 窗口背景
    private static readonly Color CardBg      = Color.FromArgb(0x1A, 0x1F, 0x26); // 卡片背景
    private static readonly Color CardBorder  = Color.FromArgb(0x2A, 0x30, 0x38); // 卡片边框
    private static readonly Color Accent      = Color.FromArgb(0x14, 0xB8, 0xA6); // 青绿强调
    private static readonly Color AccentHover = Color.FromArgb(0x2D, 0xDD, 0xC8); // 强调悬停
    private static readonly Color TextMain    = Color.FromArgb(0xE6, 0xED, 0xF3); // 主文字
    private static readonly Color TextDim     = Color.FromArgb(0x8B, 0x94, 0x9E); // 次要文字
    private static readonly Color TrackBg     = Color.FromArgb(0x30, 0x36, 0x3D); // 滑块槽

    // ===================== P/Invoke =====================
    private const string LauncherDll = "Launcher.dll";

    [DllImport(LauncherDll, CallingConvention = CallingConvention.Cdecl,
               CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int LaunchGameAndInject(
        string gamePath, string dllPath, string commandLineArgs,
        StringBuilder errorMessage, int errorMessageSize);

    // ===================== 状态 =====================
    private readonly TextBox _gamePathBox = new();
    private readonly NumericUpDown _fpsBox = new();
    private readonly NumericUpDown _fovBox = new();
    private readonly TrackBar _fovSpeedBar = new();
    private readonly Label _fovSpeedVal = new();
    private readonly ModernCheckBox _enableFps = new();
    private readonly ModernCheckBox _enableFov = new();
    private readonly ModernCheckBox _enableVSync = new();
    private readonly ModernCheckBox _enableRemoveTeamAnim = new();
    private readonly AccentButton _startBtn = new();
    private readonly Label _statusLabel = new();
    private readonly LinkLabel _gradientToggle = new();
    private bool _configReady;

    public MainForm()
    {
        Text = "LiteUnlocker";
        Width = 560;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Bg;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9F);

        BuildUi();
        LoadConfig();
        WireAutoSave();
        _configReady = true;
    }

    // ===================== UI 构建 =====================
    private void BuildUi()
    {
        int y = 70; // 顶部留空给标题

        // --- 标题 ---
        var title = new Label
        {
            Text = "LiteUnlocker",
            Font = new Font("Segoe UI Semibold", 18F),
            ForeColor = TextMain,
            BackColor = Color.Transparent,
            Left = 28, Top = 22, Width = 300, Height = 34
        };
        var subtitle = new Label
        {
            Text = "FPS Unlock  ·  FOV Control",
            Font = new Font("Segoe UI", 9F),
            ForeColor = TextDim,
            BackColor = Color.Transparent,
            Left = 30, Top = 52, Width = 300
        };
        Controls.Add(title);
        Controls.Add(subtitle);

        // --- 右上角：渐变效果开关 ---
        _gradientToggle.Text = "◐ Gradient";
        _gradientToggle.Font = new Font("Segoe UI", 8.5F);
        _gradientToggle.ForeColor = TextDim;
        _gradientToggle.LinkColor = Accent;
        _gradientToggle.VisitedLinkColor = Accent;
        _gradientToggle.Left = 430; _gradientToggle.Top = 30;
        _gradientToggle.Width = 100; _gradientToggle.Height = 22;
        _gradientToggle.BackColor = Color.Transparent;
        _gradientToggle.LinkArea = new LinkArea(0, _gradientToggle.Text.Length);
        _gradientToggle.LinkClicked += (_, _) =>
        {
            CardPanel.UseGradient = !CardPanel.UseGradient;
            // 刷新所有卡片
            foreach (Control c in Controls)
                if (c is CardPanel cp) cp.Invalidate();
            UpdateGradientToggleText();
        };
        UpdateGradientToggleText();
        Controls.Add(_gradientToggle);

        // --- 游戏路径（无卡片，顶部输入）---
        var lblGame = MakeLabel("GAME PATH");
        lblGame.Top = y; lblGame.Left = 28;
        Controls.Add(lblGame);
        y += 24;

        StyleTextBox(_gamePathBox);
        _gamePathBox.Left = 28; _gamePathBox.Top = y; _gamePathBox.Width = 420;
        var browseBtn = new AccentButton { Text = "Browse", Left = 456, Top = y - 2, Width = 70, Height = 30 };
        browseBtn.Click += OnBrowse;
        Controls.Add(_gamePathBox);
        Controls.Add(browseBtn);
        y += 48;

        // --- Graphics 卡片 ---
        var card1 = new CardPanel { Left = 24, Top = y, Width = 504, Height = 116 };
        int cy = 14;
        InitCheckBox(_enableFps, "Unlock FPS", card1, 22, ref cy);
        var lblFps = MakeLabel("Target");
        lblFps.Top = cy + 4; lblFps.Left = 300;
        StyleNumeric(_fpsBox); _fpsBox.Left = 350; _fpsBox.Top = cy; _fpsBox.Width = 70;
        var lblFpsU = MakeLabel("FPS"); lblFpsU.Top = cy + 4; lblFpsU.Left = 428;
        cy += 38;
        InitCheckBox(_enableVSync, "Disable VSync  (recommended when unlocking)", card1, 22, ref cy);
        card1.Height = cy + 14;
        Controls.Add(card1);
        y += card1.Height + 16;

        // --- Camera 卡片 ---
        var card2 = new CardPanel { Left = 24, Top = y, Width = 504, Height = 160 };
        cy = 14;
        InitCheckBox(_enableFov, "Modify FOV", card2, 22, ref cy);
        var lblFov = MakeLabel("Target");
        lblFov.Top = cy + 4; lblFov.Left = 300;
        StyleNumeric(_fovBox); _fovBox.Left = 350; _fovBox.Top = cy; _fovBox.Width = 70;
        var lblFovU = MakeLabel("°"); lblFovU.Top = cy + 4; lblFovU.Left = 428;
        cy += 40;

        var lblSpeed = MakeLabel("Transition");
        lblSpeed.Top = cy + 6; lblSpeed.Left = 22;
        StyleTrackBar(_fovSpeedBar); _fovSpeedBar.Left = 130; _fovSpeedBar.Top = cy; _fovSpeedBar.Width = 240;
        StyleLabel(_fovSpeedVal); _fovSpeedVal.Top = cy + 6; _fovSpeedVal.Left = 380;
        _fovSpeedVal.Text = "0.05";
        _fovSpeedBar.Scroll += (_, _) =>
            _fovSpeedVal.Text = (_fovSpeedBar.Value / 100.0).ToString("0.00");
        cy += 42;
        var lblHint = MakeLabel("0 = instant   0.05 = smooth (recommended)   1 = immediate");
        lblHint.Top = cy; lblHint.Left = 22; lblHint.Width = 460;
        lblHint.ForeColor = TextDim;
        card2.Height = cy + 24;
        card2.Controls.Add(lblSpeed);
        Controls.Add(card2);
        y += card2.Height + 16;

        // --- UI 卡片 ---
        var card3 = new CardPanel { Left = 24, Top = y, Width = 504, Height = 56 };
        cy = 14;
        InitCheckBox(_enableRemoveTeamAnim, "Remove team-switch animation  (skip character showcase)",
                     card3, 22, ref cy);
        card3.Height = cy + 14;
        Controls.Add(card3);
        y += card3.Height + 24;

        // --- 启动按钮 ---
        _startBtn.Text = "▶  LAUNCH GAME";
        _startBtn.Left = 24; _startBtn.Top = y; _startBtn.Width = 504; _startBtn.Height = 48;
        _startBtn.Font = new Font("Segoe UI Semibold", 11F);
        _startBtn.Click += OnStart;
        Controls.Add(_startBtn);
        y += 60;

        // --- 状态栏 ---
        _statusLabel.Left = 28; _statusLabel.Top = y; _statusLabel.Width = 504; _statusLabel.Height = 40;
        _statusLabel.Font = new Font("Segoe UI", 9F);
        _statusLabel.ForeColor = TextDim;
        _statusLabel.BackColor = Color.Transparent;
        _statusLabel.Text = "Ready.";
        Controls.Add(_statusLabel);
    }

    // ===================== 控件样式助手 =====================
    private Label MakeLabel(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 9F),
        ForeColor = TextDim,
        BackColor = Color.Transparent,
        AutoSize = false, Height = 20
    };

    private void StyleLabel(Label l)
    {
        l.Font = new Font("Segoe UI", 9F);
        l.ForeColor = Accent;
        l.BackColor = Color.Transparent;
        l.Width = 50; l.Height = 20;
    }

    private void StyleTextBox(TextBox tb)
    {
        tb.BackColor = CardBg;
        tb.ForeColor = TextMain;
        tb.BorderStyle = BorderStyle.FixedSingle;
        tb.Height = 30;
        tb.Font = new Font("Segoe UI", 9F);
    }

    private void StyleNumeric(NumericUpDown n)
    {
        n.BackColor = CardBg;
        n.ForeColor = TextMain;
        n.BorderStyle = BorderStyle.FixedSingle;
        n.Height = 26;
        n.Font = new Font("Segoe UI", 9F);
    }

    private void StyleTrackBar(TrackBar t)
    {
        t.BackColor = CardBg;
        t.TickStyle = TickStyle.None;
        t.Height = 28;
    }

    private void InitCheckBox(ModernCheckBox cb, string text, Control parent, int left, ref int y)
    {
        cb.Text = text;
        cb.Checked = true;
        cb.Left = left; cb.Top = y + 2;
        cb.Width = 280;
        cb.Parent = parent;
        y += 36;
    }

    // ===================== 事件 =====================
    private void WireAutoSave()
    {
        _enableFps.CheckedChanged += (_, _) => TrySaveConfig();
        _fpsBox.ValueChanged += (_, _) => TrySaveConfig();
        _enableFov.CheckedChanged += (_, _) => TrySaveConfig();
        _fovBox.ValueChanged += (_, _) => TrySaveConfig();
        _fovSpeedBar.ValueChanged += (_, _) => TrySaveConfig();
        _enableVSync.CheckedChanged += (_, _) => TrySaveConfig();
        _enableRemoveTeamAnim.CheckedChanged += (_, _) => TrySaveConfig();
        _gamePathBox.Leave += (_, _) => TrySaveConfig();
        FormClosing += (_, _) => TrySaveConfig();
    }

    private void OnBrowse(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select game executable",
            Filter = "Game executable (*.exe)|*.exe|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _gamePathBox.Text = dlg.FileName;
            TrySaveConfig();
        }
    }

    private void OnStart(object? s, EventArgs e)
    {
        string gamePath = _gamePathBox.Text.Trim();
        if (string.IsNullOrEmpty(gamePath) || !File.Exists(gamePath))
        {
            SetStatus("✗ Please select a valid game .exe path", error: true);
            return;
        }

        try { WriteConfig(); }
        catch (Exception ex)
        {
            SetStatus($"✗ Failed to write config: {ex.Message}", error: true);
            return;
        }

        SetStatus("Launching and injecting…");
        _startBtn.Enabled = false;
        try
        {
            var errorMsg = new StringBuilder(256);
            int ret = LaunchGameAndInject(gamePath, "", "", errorMsg, 256);
            if (ret == 0)
                SetStatus("✓ Launched successfully. Plugin injected.");
            else
                SetStatus($"✗ Launch failed (code {ret}): {errorMsg}", error: true);
        }
        catch (DllNotFoundException)
        {
            SetStatus("✗ Launcher.dll not found. Ensure it is in the same folder.", error: true);
        }
        catch (Exception ex)
        {
            SetStatus($"✗ Error: {ex.Message}", error: true);
        }
        finally { _startBtn.Enabled = true; }
    }

    // ===================== 配置读写（逻辑完全保留）=====================
    private void WriteConfig()
    {
        string pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        Directory.CreateDirectory(pluginsDir);
        string cfgPath = Path.Combine(pluginsDir, "config.ini");

        var sb = new StringBuilder();
        sb.AppendLine("; Auto-generated by UnlockerGUI");
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
        sb.AppendLine($"Value={(_fovSpeedBar.Value / 100.0).ToString("F2", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("[FovLimitCheck]");
        sb.AppendLine("Value=1");
        sb.AppendLine();
        sb.AppendLine("[RemoveTeamAnim]");
        sb.AppendLine($"Value={(_enableRemoveTeamAnim.Checked ? 1 : 0)}");

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

            _enableRemoveTeamAnim.Checked = GetBool(values, "RemoveTeamAnim", _enableRemoveTeamAnim.Checked);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to load config: {ex.Message}", error: true);
        }
    }

    private void TrySaveConfig()
    {
        if (!_configReady) return;
        try { WriteConfig(); }
        catch (Exception ex) { SetStatus($"Save failed: {ex.Message}", error: true); }
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
            { section = line[1..^1].Trim(); continue; }
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals("Value", StringComparison.OrdinalIgnoreCase))
                values[section] = line[(eq + 1)..].Trim();
        }
        return values;
    }

    private static string GetValue(Dictionary<string, string> v, string s, string f) =>
        v.TryGetValue(s, out string? val) ? val : f;
    private static bool GetBool(Dictionary<string, string> v, string s, bool f) =>
        int.TryParse(GetValue(v, s, f ? "1" : "0"), out int val) ? val != 0 : f;
    private static decimal GetDecimal(Dictionary<string, string> v, string s, decimal f) =>
        decimal.TryParse(GetValue(v, s, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal val) ? val : f;
    private static void SetNumericValue(NumericUpDown c, decimal v) =>
        c.Value = Math.Clamp(v, c.Minimum, c.Maximum);

    private void SetStatus(string text, bool error = false)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = error ? Color.FromArgb(0xF8, 0x71, 0x71) : Accent;
    }

    // 根据开关状态更新渐变按钮的显示文字和颜色
    private void UpdateGradientToggleText()
    {
        if (CardPanel.UseGradient)
        {
            _gradientToggle.Text = "◐ Gradient";
            _gradientToggle.ForeColor = Accent;
        }
        else
        {
            _gradientToggle.Text = "◑ Flat";
            _gradientToggle.ForeColor = TextDim;
        }
        // 文字长度变化后更新可点击区域，保证整段文字都能点
        _gradientToggle.LinkArea = new LinkArea(0, _gradientToggle.Text.Length);
    }
}

// ===================== 自定义控件 =====================

// 深色卡片面板：圆角 + 边框 + 标题区域
internal sealed class CardPanel : Panel
{
    // 是否启用渐变层次感（可由主窗口开关控制，所有卡片联动）
    public static bool UseGradient = true;

    public CardPanel()
    {
        BackColor = Color.Transparent;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var path = RoundedRect(rect, 10);

        if (UseGradient)
        {
            // 垂直渐变：顶部稍亮 → 底部稍深，营造卡片浮起的层次感
            using var gradBrush = new LinearGradientBrush(
                rect,
                Color.FromArgb(0x1F, 0x25, 0x2D),  // 顶部（稍亮）
                Color.FromArgb(0x15, 0x1A, 0x20),  // 底部（稍深）
                LinearGradientMode.Vertical);
            g.FillPath(gradBrush, path);
        }
        else
        {
            // 关闭渐变：纯色填充（扁平风格）
            using var bgBrush = new SolidBrush(Color.FromArgb(0x1A, 0x1F, 0x26));
            g.FillPath(bgBrush, path);
        }

        // 边框
        using var borderPen = new Pen(Color.FromArgb(0x2A, 0x30, 0x38));
        g.DrawPath(borderPen, path);
    }

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// 自绘复选框：圆形勾选 + 青绿色 + 文字
internal sealed class ModernCheckBox : Control
{
    public bool Checked { get; set; }
    private static readonly Color Accent = Color.FromArgb(0x14, 0xB8, 0xA6);
    private bool _hover;

    public ModernCheckBox()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        ForeColor = Color.FromArgb(0xE6, 0xED, 0xF3);
        Cursor = Cursors.Hand;
        Size = new Size(280, 26);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CheckedChanged;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // 复选框圆角方框
        var box = new Rectangle(0, 2, 20, 20);
        var path = CardPanel.RoundedRect(box, 5);
        if (Checked)
        {
            using var fill = new SolidBrush(Accent);
            g.FillPath(fill, path);
            // 画对勾
            using var p = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(p, 4, 11, 8, 15);
            g.DrawLine(p, 8, 15, 16, 6);
        }
        else
        {
            using var border = new Pen(_hover ? Color.FromArgb(0x4A, 0x55, 0x60) : Color.FromArgb(0x33, 0x3A, 0x42), 2f);
            g.DrawPath(border, path);
        }
        // 文字
        TextRenderer.DrawText(g, Text, Font, new Rectangle(28, 0, Width - 28, Height),
            ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}

// 自绘强调按钮：圆角 + 青绿 + 悬停/按下效果
internal sealed class AccentButton : Button
{
    private static readonly Color Accent = Color.FromArgb(0x14, 0xB8, 0xA6);
    private static readonly Color Hover = Color.FromArgb(0x2D, 0xDD, 0xC8);
    private bool _hover, _pressed;

    public AccentButton()
    {
        DoubleBuffered = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var path = CardPanel.RoundedRect(rect, 8);

        Color c = _pressed ? Color.FromArgb(0x0D, 0x9A, 0x8A) : (_hover ? Hover : Accent);
        using var brush = new SolidBrush(c);
        g.FillPath(brush, path);

        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(0, 0, Width, Height), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
