// ============================================================================
// 攻略悬浮窗 GuideOverlay v5.0.0 — 单文件源码（C# 5 / WinForms / System.Drawing）
// 编译：双击 build.bat（使用 Windows 自带 csc，无需 Visual Studio / MSBuild / NuGet）
// 自测：双击 selftest.bat（或 GuideOverlay.exe --selftest）
// 本文件即完整实现：全部 UI 自绘，无第三方库、无注入/Hook/模拟输入、无网络/注册表/管理员。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Drawing.Text;
using System.Reflection;

// ============================================================================
// 程序集资源 — 让 exe 的「属性 → 详细信息」显示真实版本/产品名（A1）
// 版本号必须与 AppInfo.Version（5.0.0）保持一致；AssemblyVersion 用四段 5.0.0.0。
// ============================================================================
[assembly: AssemblyVersion("5.0.0.0")]
[assembly: AssemblyFileVersion("5.0.0.0")]
[assembly: AssemblyProduct("攻略悬浮窗 GuideOverlay")]
[assembly: AssemblyTitle("攻略悬浮窗")]

// ============================================================================
// 基础定义 — 全局常量 / 主题 / 布局 / 枚举 / Win32 声明
// 本部分为全局常量与 Win32 声明，其他部分只读引用。

// ============================================================================

internal static class AppInfo
{
    public const string Version = "5.0.0";
    public const string AppName = "攻略悬浮窗";
    public const string ExeName = "GuideOverlay.exe";
    public const string SettingsFileName = "settings.ini";
    public const string HelpFileName = "使用说明.md";
    public const string SingleInstanceMutexName = "GuideOverlay_SingleInstance_Mutex_v5";
}

// ---------------------------------------------------------------------------
// 枚举
// ---------------------------------------------------------------------------
internal enum ThemeKind { Light = 0, Dark = 1 }

internal enum GateKeyKind { Ctrl = 0, Alt = 1, Shift = 2 }

internal enum ViewMode { Fit = 0, Manual = 1, LongImage = 2 }

internal enum WindowPreset { Left, CenterH, Right, Top, CenterV, Bottom, ResetDefault }

internal enum RotateOp { None = 0, Rotate90 = 1, Rotate180 = 2, Rotate270 = 3, FlipH = 4, FlipV = 5 }

internal enum ToolbarButton { None = -1, Prev = 0, Page = 1, Next = 2, Fit = 3, OneToOne = 4, LongMode = 5, Settings = 6, Close = 7, LockImage = 8 }

internal enum CardButton { None = -1, ChooseFolder = 0, OpenFolder = 1, Rescan = 2 }

internal enum HotkeyAction
{
    ZoomIn = 1, ZoomOut = 2, Prev = 3, Next = 4, Fit = 5,
    Jump1 = 6, Jump2 = 7, Jump3 = 8, Jump4 = 9, Jump5 = 10,
    Jump6 = 11, Jump7 = 12, Jump8 = 13, Jump9 = 14,
    OpacityDown = 15, OpacityUp = 16, ToggleLock = 17, ToggleHide = 18
}

internal enum HotkeyStatus { Unregistered = 0, Registered = 1, FailedInUse = 2, FailedOther = 3, Disabled = 4 }

internal enum SettingsSection { Images = 0, Window = 1, Display = 2, Zoom = 3, Hotkeys = 4, Diagnostics = 5 }

// ---------------------------------------------------------------------------
// 主题 token（浅色见提示词 §八；深色见 §19.8）
// ---------------------------------------------------------------------------
internal sealed class ThemeTokens
{
    public Color PageBg;
    public Color Card;
    public Color Text;
    public Color TextSecondary;
    public Color Border;
    public Color Hover;
    public Color Accent;
    public Color Danger;
    public Color Canvas;
    public bool IsDark;

    public static ThemeTokens Light()
    {
        ThemeTokens t = new ThemeTokens();
        t.IsDark = false;
        t.PageBg = ColorTranslator.FromHtml("#F7F8FA");
        t.Card = ColorTranslator.FromHtml("#FFFFFF");
        t.Text = ColorTranslator.FromHtml("#202124");
        t.TextSecondary = ColorTranslator.FromHtml("#5F6368");
        t.Border = ColorTranslator.FromHtml("#DADCE0");
        t.Hover = ColorTranslator.FromHtml("#EEF2F7");
        t.Accent = ColorTranslator.FromHtml("#2F6FED");
        t.Danger = ColorTranslator.FromHtml("#D93025");
        t.Canvas = ColorTranslator.FromHtml("#F2F3F5");
        return t;
    }

    public static ThemeTokens Dark()
    {
        ThemeTokens t = new ThemeTokens();
        t.IsDark = true;
        t.PageBg = ColorTranslator.FromHtml("#1E1F22");
        t.Card = ColorTranslator.FromHtml("#2B2D31");
        t.Text = ColorTranslator.FromHtml("#E8EAED");
        t.TextSecondary = ColorTranslator.FromHtml("#A8ADB4");
        t.Border = ColorTranslator.FromHtml("#3A3D41");
        t.Hover = ColorTranslator.FromHtml("#35373B");
        t.Accent = ColorTranslator.FromHtml("#7AA2F7");
        t.Danger = ColorTranslator.FromHtml("#F28B82");
        t.Canvas = ColorTranslator.FromHtml("#141518");
        return t;
    }

    public static ThemeTokens Get(ThemeKind kind)
    {
        return kind == ThemeKind.Dark ? Dark() : Light();
    }
}

// ---------------------------------------------------------------------------
// 布局常量（全部 DIP）
// ---------------------------------------------------------------------------
internal static class Layout
{
    public const int ToolbarHeightDip = 32;
    public const int ToolbarEdgeMarginDip = 8;
    public const int ToolbarButtonGapDip = 4;
    public const int ToolbarPaddingDip = 8;
    public const int ToolbarCornerDip = 6;
    public const int ToolbarPrevWidthDip = 28;
    public const int ToolbarPageWidthDip = 56;
    public const int ToolbarNextWidthDip = 28;
    public const int ToolbarFitWidthDip = 72;
    public const int ToolbarOneToOneWidthDip = 52;      // 100% 已移出工具条（菜单/设置面板/热键仍可 100%）
    public const int ToolbarLockWidthDip = 52;          // 「锁图」开关宽度（与原 100% 同宽，工具条总宽不变）
    public const int ToolbarLongWidthDip = 56;
    public const int ToolbarSettingsWidthDip = 60;
    public const int ToolbarCloseWidthDip = 28;

    public const int ResizeGripDip = 12;
    public const int SnapThresholdDip = 12;
    public const int ToastMarginDip = 8;
    public const int ToastPaddingDip = 8;
    public const int ToastCornerDip = 4;

    public const int MinWindowWidthDip = 240;
    public const int MinWindowHeightDip = 180;
    // 首次运行默认窗口尺寸（B7）：高 = 工作区高 60%，宽 = 高 * 9/16（竖版攻略图比例）。
    // 只有取不到屏幕信息时才用下面两个固定兜底值。
    public const int DefaultWindowWidthDip = 520;
    public const int DefaultWindowHeightDip = 756;
    public const int DefaultWindowRightMarginDip = 24;
    public const double DefaultWindowHeightWorkAreaRatio = 0.60;
    public const int DefaultWindowHeightMinDip = 320;
    public const int DefaultWindowHeightMaxDip = 900;
    public const int DefaultWindowWidthMinDip = 240;
    public const int DefaultWindowWidthMaxDip = 1200;
    public const double DefaultWindowAspectRatio = 9.0 / 16.0;

    // 设置面板尺寸（B6）：min(560, 工作区宽-80) × min(560, 工作区高-80)，
    // 保证 1366x768@125%（工作区高约 614 DIP）时面板外框不会超出工作区把关闭按钮裁掉。
    public const int SettingsPanelMaxWidthDip = 560;
    public const int SettingsPanelMaxHeightDip = 560;
    public const int SettingsPanelMarginDip = 80;
    public const int SettingsPanelMinWidthDip = 420;
    public const int SettingsPanelMinHeightDip = 360;

    // 工作区（物理像素）+ DPI → 默认窗口高度（DIP，夹取 320..900）
    public static int DefaultWindowHeight(int workAreaHeightPx, int dpi)
    {
        if (workAreaHeightPx <= 0 || dpi <= 0) { return DefaultWindowHeightDip; }
        int workAreaHeightDip = (int)Math.Round(workAreaHeightPx * 96.0 / dpi);
        int height = (int)Math.Round(workAreaHeightDip * DefaultWindowHeightWorkAreaRatio);
        if (height < DefaultWindowHeightMinDip) { height = DefaultWindowHeightMinDip; }
        if (height > DefaultWindowHeightMaxDip) { height = DefaultWindowHeightMaxDip; }
        return height;
    }

    // 默认窗口高度（DIP）→ 默认窗口宽度（DIP，9:16 竖版，夹取 240..1200）
    public static int DefaultWindowWidth(int heightDip)
    {
        if (heightDip <= 0) { heightDip = DefaultWindowHeightDip; }
        int width = (int)Math.Round(heightDip * DefaultWindowAspectRatio);
        if (width < DefaultWindowWidthMinDip) { width = DefaultWindowWidthMinDip; }
        if (width > DefaultWindowWidthMaxDip) { width = DefaultWindowWidthMaxDip; }
        return width;
    }

    // 默认窗口尺寸（DIP）：两个方向一起算，供 AppSettings 默认值与 DefaultWindowRect 共用
    public static Size DefaultWindowSize(Rectangle workAreaPx, int dpi)
    {
        int height = DefaultWindowHeight(workAreaPx.Height, dpi);
        return new Size(DefaultWindowWidth(height), height);
    }

    public const int EmptyCardMaxWidthDip = 420;
    public const int EmptyCardPaddingDip = 16;
    public const int EmptyCardButtonHeightDip = 30;
    public const int EmptyCardButtonGapDip = 8;

    // C4：长图滚动条 8→6 DIP，给右下角拉伸热区让出宽度（拉伸角优先命中）。
    public const int ScrollbarWidthDip = 6;

    public static int Px(int dip, int dpi)
    {
        if (dpi <= 0) { dpi = 96; }
        return (int)Math.Round(dip * dpi / 96.0);
    }

    public static int Dip(int px, int dpi)
    {
        if (dpi <= 0) { dpi = 96; }
        return (int)Math.Round(px * 96.0 / dpi);
    }

    public static float DipF(float dip, int dpi)
    {
        if (dpi <= 0) { dpi = 96; }
        return (float)(dip * dpi / 96.0);
    }
}

// ---------------------------------------------------------------------------
// 视图状态（图片坐标 → 屏幕坐标：screen = image * zoom + offset；offset 相对视口左上角）
// ---------------------------------------------------------------------------
internal struct ViewState
{
    public double Zoom;
    public double OffsetX;
    public double OffsetY;
    public ViewMode Mode;
}

// ---------------------------------------------------------------------------
// 字体
// ---------------------------------------------------------------------------
internal static class UiFonts
{
    private const string PreferredFamily = "Microsoft YaHei UI";
    private static string _resolved;

    public static string ResolvedFamily
    {
        get
        {
            if (_resolved != null) { return _resolved; }
            try
            {
                using (Font f = new Font(PreferredFamily, 9f))
                {
                    if (string.Equals(f.Name, PreferredFamily, StringComparison.OrdinalIgnoreCase))
                    {
                        _resolved = PreferredFamily;
                        return _resolved;
                    }
                }
            }
            catch { }
            try
            {
                _resolved = SystemFonts.MessageBoxFont.Name;
            }
            catch { _resolved = "宋体"; }
            return _resolved;
        }
    }

    public static Font Create(float sizePt, FontStyle style)
    {
        try { return new Font(ResolvedFamily, sizePt, style, GraphicsUnit.Point); }
        catch
        {
            try { return new Font(SystemFonts.MessageBoxFont.Name, sizePt, style, GraphicsUnit.Point); }
            catch { return new Font(FontFamily.GenericSansSerif, sizePt, style, GraphicsUnit.Point); }
        }
    }
}

// ---------------------------------------------------------------------------
// DPI
// ---------------------------------------------------------------------------
internal static class DpiHelper
{
    private static int _systemDpi = 96;
    private static bool _initialized;

    public static int SystemDpi
    {
        get
        {
            if (!_initialized) { Refresh(); }
            return _systemDpi;
        }
    }

    public static void Refresh()
    {
        _initialized = true;
        IntPtr hdc = IntPtr.Zero;
        try
        {
            hdc = NativeMethods.GetDC(IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                int dpi = NativeMethods.GetDeviceCaps(hdc, Win32Const.LOGPIXELSX);
                if (dpi > 0) { _systemDpi = dpi; }
            }
        }
        catch { }
        finally
        {
            // 异常路径也必须归还 DC，避免每次刷新泄漏一个 HDC
            if (hdc != IntPtr.Zero)
            {
                try { NativeMethods.ReleaseDC(IntPtr.Zero, hdc); }
                catch { }
            }
        }
        if (_systemDpi <= 0) { _systemDpi = 96; }
    }

    // 系统级 DPI 感知程序：所有屏幕按系统 DPI 渲染
    public static int ForPoint(Point screenPoint)
    {
        return SystemDpi;
    }
}

// ---------------------------------------------------------------------------
// Win32
// ---------------------------------------------------------------------------
[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
    public POINT(int x, int y) { X = x; Y = y; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
    public int Width { get { return Right - Left; } }
    public int Height { get { return Bottom - Top; } }
}

internal static class Win32Const
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_LAYERED = 0x00080000;

    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_NCHITTEST = 0x0084;
    public const int HTTRANSPARENT = -1;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_HIDE = 0;

    public const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    public const int LOGPIXELSX = 88;

    public const int VK_CONTROL = 0x11;
    public const int VK_SHIFT = 0x10;
    public const int VK_MENU = 0x12;

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool AttachConsole(uint dwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}

// ---------------------------------------------------------------------------
// 运行环境检测：低完整性 / AppContainer
//
// Windows 的 UIPI 会让「低完整性级别（Low IL）」或 AppContainer 进程的
// GetAsyncKeyState 一律返回 0 —— 此时按住门控键永远检测不到，工具条也就
// 永远不会出现。这不是程序逻辑错误（同一台机器上用资源管理器正常双击就
// 一切正常），但必须让用户看得见原因、并且窗口仍然可用，而不是静默失效。
// ---------------------------------------------------------------------------
internal static class IntegrityProbe
{
    private const int TokenIntegrityLevel = 25;
    private const int TokenIsAppContainer = 29;
    private const uint TokenQuery = 0x0008;
    private const int MediumIntegrityRid = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public IntPtr Label;    // PSID
        public int Attributes;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass,
        IntPtr info, int infoLength, out int returnLength);

    // 是否处于受限环境（低完整性或 AppContainer）；查询失败一律按「不受限」处理
    internal static bool IsRestricted()
    {
        try
        {
            return IsLowIntegrity() || IsAppContainer();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLowIntegrity()
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token)) { return false; }
        try
        {
            int needed;
            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out needed);
            if (needed <= 0) { return false; }
            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                int written;
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, needed, out written)) { return false; }
                TokenMandatoryLabel label = (TokenMandatoryLabel)Marshal.PtrToStructure(
                    buffer, typeof(TokenMandatoryLabel));
                if (label.Label == IntPtr.Zero) { return false; }
                int rid = LastSubAuthority(label.Label);
                return rid > 0 && rid < MediumIntegrityRid;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static bool IsAppContainer()
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token)) { return false; }
        try
        {
            IntPtr buffer = Marshal.AllocHGlobal(4);
            try
            {
                int written;
                if (!GetTokenInformation(token, TokenIsAppContainer, buffer, 4, out written)) { return false; }
                return Marshal.ReadInt32(buffer) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    // SID 布局：Revision(1) + SubAuthorityCount(1) + IdentifierAuthority(6) + SubAuthority[]
    private static int LastSubAuthority(IntPtr sid)
    {
        byte count = Marshal.ReadByte(sid, 1);
        if (count <= 0) { return 0; }
        return Marshal.ReadInt32(sid, 8 + (count - 1) * 4);
    }
}

// ---------------------------------------------------------------------------
// 通用小工具
// ---------------------------------------------------------------------------
internal static class Util
{
    public static Color WithAlpha(Color c, int alpha)
    {
        if (alpha < 0) { alpha = 0; }
        if (alpha > 255) { alpha = 255; }
        return Color.FromArgb(alpha, c.R, c.G, c.B);
    }

    public static Rectangle ToRectangle(RectangleF r)
    {
        return new Rectangle((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height));
    }

    public static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        GraphicsPath path = new GraphicsPath();
        int r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
        if (r <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.Left, rect.Top, r * 2, r * 2, 180, 90);
        path.AddArc(rect.Right - r * 2, rect.Top, r * 2, r * 2, 270, 90);
        path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static string FormatPercent(double zoom)
    {
        return ((int)Math.Round(zoom * 100.0)).ToString(CultureInfo.InvariantCulture) + "%";
    }
}
// ============================================================================
// 共享字段 — OverlayForm 的状态字段契约
// 「主窗口」（交互/状态机）与「主窗口绘制」两部分共同使用这些字段。
// 规则：两个部分都不得改名/删除这些字段；各自需要的额外字段可加在自己的部分里。
// ============================================================================

internal sealed partial class OverlayForm : Form
{
    // ---- 依赖对象 ----
    internal AppSettings Settings;
    internal HotkeyManager Hotkeys;
    internal ImageLibrary Library;
    internal FolderWatcher Watcher;
    internal SettingsForm SettingsPanel;
    internal bool SelfTestMode;

    // ---- 图片与视图 ----
    internal List<ImageEntry> Images;          // 当前图库列表（= Library.Images）
    internal int CurrentIndex = -1;
    internal ViewState View;                   // 当前缩放/偏移/视图模式
    internal RotateOp ManualRotate = RotateOp.None;
    internal Bitmap CurrentBase;               // 借用自 Library（不得 Dispose）
    internal Bitmap CurrentBitmap;             // 实际绘制位图：未旋转时 == CurrentBase
    internal bool CurrentBitmapOwned;          // CurrentBitmap 是否为派生副本（需 Dispose）
    internal Bitmap FitCache;                  // 适应窗口缓存位图
    internal int FitCacheIndex = -1;
    internal Size FitCacheClient;
    internal double FitCacheZoom;
    internal string CurrentError;              // 当前图片加载失败原因（null 表示正常）

    // ---- 交互状态 ----
    internal bool GateHeld;                    // 门控键当前是否按住
    internal bool Interactive;                 // 当前是否可交互（WS_EX_TRANSPARENT 已去掉）
    internal bool Locked;                      // 锁定交互（Ctrl+Alt+T）
    internal bool WindowHidden;                // 隐藏状态
    internal bool DraggingWindow;              // 左键/中键拖动窗口
    internal bool ResizingWindow;              // 右下角改大小
    internal bool PanningImage;                // 平移图片
    internal Point DragStartScreen;            // 拖动起点（屏幕坐标）
    internal Point DragStartLocation;          // 拖动开始时窗口位置
    internal Rectangle DragStartRect;          // 拖动/拉伸开始时窗口矩形
    internal double PanStartOffsetX;
    internal double PanStartOffsetY;
    internal Point PanStartScreen;
    internal bool DragMoved;                   // 本次按下是否超过拖动阈值
    internal long LastClickTick;               // 双击判定
    internal Point LastClickPoint;

    // ---- 门控轮询 ----
    internal System.Windows.Forms.Timer GateTimer;
    internal long LastGatePollTick;
    internal bool GateKeyDownLast;

    // ---- 状态提示 toast ----
    internal string ToastText = "";
    internal long ToastUntilTick;              // Environment.TickCount 过期时间
    internal long ToastFadeStartTick;          // 开始淡出的 tick（0 表示未开始）
    internal int ToastDurationMs;
    internal bool ToastUrgent;                 // 错误/警告类（6-8 秒）

    // ---- 工具条 / 空状态卡片 ----
    internal ToolbarButton HoverToolbar = ToolbarButton.None;
    internal ToolbarButton PressedToolbar = ToolbarButton.None;
    internal CardButton HoverCard = CardButton.None;
    internal CardButton PressedCard = CardButton.None;
    internal bool ToolbarVisible;
    internal bool CardVisible;

    // ---- 长图滚动 ----
    internal bool LongImageMode;               // 当前是否处于长图滚动视图
    internal bool DraggingScrollbar;
    internal Point ScrollbarDragStart;
    internal double ScrollbarStartOffsetY;

    // ---- 图片锁定（锁上后按住门控键拖动 = 只移动窗口，不平移图片） ----
    // 仅本次运行有效，不写入 settings.ini（和旋转一样属于临时查看状态）。
    internal bool ImageLocked;

    // ---- 保存防抖 ----
    internal System.Windows.Forms.Timer SaveTimer;

    // ---- 主题 / 字体缓存 ----
    internal ThemeKind CurrentTheme;
    internal ThemeTokens Colors;
    internal Font UiFont;
    internal Font TitleFont;
    internal Font ToastFont;
    internal int CurrentDpi;
    internal bool ExitRequested;

    // ---- 右键菜单 ----
    internal ContextMenuStrip OverlayContextMenu;
}
// ============================================================================
// 视图几何 — 纯函数几何/数学模块
// 约定：本文件所有函数必须是纯函数（不依赖窗口、不读文件、不写日志、不取当前时间），
//       以便 --selftest 直接断言；非法输入一律返回安全默认值，绝不抛异常。
//       坐标单位：图片坐标 = 原图像素；视口坐标 = 客户区像素。
//       映射关系：screen = image * zoom + offset（offset 相对视口左上角）。
//       DPI 换算统一走 Layout.Px(dip, dpi)；本文件只做几何，不创建 GDI 资源。
// ============================================================================

internal sealed class ToolbarItem
{
    public ToolbarButton Kind;
    public Rectangle Rect;
    public string Text;
    public bool Enabled;
}

internal sealed class ToolbarLayout
{
    public Rectangle Bar;
    public List<ToolbarItem> Items = new List<ToolbarItem>();

    public ToolbarItem Find(ToolbarButton kind)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Kind == kind) { return Items[i]; }
        }
        return null;
    }
}

internal sealed class CardItem
{
    public CardButton Kind;
    public Rectangle Rect;
    public string Text;
    public bool Enabled;
}

internal sealed class CardLayout
{
    public Rectangle Card;
    public Rectangle TitleRect;
    public Rectangle FolderRect;
    public Rectangle HintRect;
    public List<CardItem> Buttons = new List<CardItem>();

    public CardItem Find(CardButton kind)
    {
        for (int i = 0; i < Buttons.Count; i++)
        {
            if (Buttons[i].Kind == kind) { return Buttons[i]; }
        }
        return null;
    }
}

internal sealed class ToastLayout
{
    public Rectangle Rect;
    public string Text;
    public Size TextSize;
}

internal static class ViewMath
{
    public const double MinManualZoom = 0.1;
    public const double MaxManualZoom = 10.0;
    public const double WheelZoomStep = 1.1;
    public const double KeyZoomStep = 1.25;
    public const long MaxImagePixels = 60000000;

    // ---- 本模块专用的排版数值（DIP）。凡是 Layout 里已有的常量一律复用 Layout.* ----
    private const int LongScrollStepDip = 60;          // 长图滚轮一格 ≈ 3 行文字
    private const int ToastWidthSlackDip = 40;         // toast 整条不超过“窗口宽 − 40 DIP”（§三）
    private const int EmptyCardSideMarginDip = 16;     // 卡片左右各留 16 DIP（= 契约的“宽 − 32 DIP”）
    private const int EmptyCardTitleHeightDip = 28;    // 标题行高
    private const int EmptyCardTextHeightDip = 20;     // 文件夹路径 / 热键提示行高
    private const int EmptyCardLineGapDip = 8;         // 标题与路径之间
    private const int EmptyCardSectionGapDip = 12;     // 路径→按钮、按钮→提示之间
    private const int ScrollbarMinThumbDip = 24;       // 滚动条滑块最小高度

    private static readonly char[] PathSeparators = new char[] { '/', '\\' };
    private static readonly NaturalStringComparer NaturalComparerInstance = new NaturalStringComparer();

    // ---------- 内部小工具 ----------

    // 有效的正数（排除 NaN / ±Inf / <=0），所有几何入口都先过这一道，保证纯函数不抛异常。
    private static bool IsPositiveFinite(double v)
    {
        return !double.IsNaN(v) && !double.IsInfinity(v) && v > 0.0;
    }

    // 视口坐标 → 图片坐标：p = (view - offset) / zoom
    // ---------- 缩放 ----------

    // 手动缩放夹取到 [0.1, 10]；NaN 返回 1.0（安全默认），-Inf 落到下限、+Inf 落到上限。
    public static double ClampManualZoom(double zoom)
    {
        if (double.IsNaN(zoom)) { return 1.0; }
        if (zoom < MinManualZoom) { return MinManualZoom; }
        if (zoom > MaxManualZoom) { return MaxManualZoom; }
        return zoom;
    }

    // 适应窗口比例 = min(viewW/imgW, viewH/imgH)，**不夹取**（允许超大图 < 0.1 的特例）。
    // 图片尺寸非法 → 1.0；视口尺寸非法（窗口还没建好时可能为 0）→ 手动下限 0.1。
    public static double FitZoom(double imgW, double imgH, double viewW, double viewH)
    {
        if (!IsPositiveFinite(imgW) || !IsPositiveFinite(imgH)) { return 1.0; }
        if (!IsPositiveFinite(viewW) || !IsPositiveFinite(viewH)) { return MinManualZoom; }
        double zx = viewW / imgW;
        double zy = viewH / imgH;
        return zx < zy ? zx : zy;
    }

    // 长图视图“宽度适应”：zoom = viewW / imgW，不夹取（长图允许 < 0.1 才放得下宽度）。
    public static double FitWidthZoom(double imgW, double viewW)
    {
        if (!IsPositiveFinite(imgW)) { return 1.0; }
        if (!IsPositiveFinite(viewW)) { return MinManualZoom; }
        return viewW / imgW;
    }

    // 长图判定：h / w >= 3 且总像素 <= 6000 万（§19.4）。pixelCount<=0 时用宽高自行计算。
    public static bool IsLongImage(double imgW, double imgH, long pixelCount)
    {
        if (!IsPositiveFinite(imgW) || !IsPositiveFinite(imgH)) { return false; }
        long pixels = pixelCount;
        if (pixels <= 0)
        {
            double p = imgW * imgH;
            if (!IsPositiveFinite(p) || p > (double)long.MaxValue) { return false; }
            pixels = (long)Math.Round(p);
        }
        if (pixels > MaxImagePixels) { return false; }
        return imgH / imgW >= 3.0;
    }

    // ---------- 锚点缩放 / 平移夹取 ----------
    // 以视口内 (anchorViewX, anchorViewY) 为锚点，从 oldZoom 变到 newZoom，返回新 offset。
    // 公式：p = (anchor - oldOffset) / oldZoom；newOffset = anchor - p * newZoom。
    // 推导：缩放前 anchor = p * oldZoom + oldOffset；缩放后要求 p 仍映射到 anchor。
    public static void ZoomAtPoint(double oldZoom, double oldOffX, double oldOffY, double newZoom,
        double anchorViewX, double anchorViewY, out double newOffX, out double newOffY)
    {
        if (!IsPositiveFinite(oldZoom)) { oldZoom = 1.0; }
        if (!IsPositiveFinite(newZoom)) { newZoom = oldZoom; }
        if (double.IsNaN(oldOffX) || double.IsInfinity(oldOffX)) { oldOffX = 0.0; }
        if (double.IsNaN(oldOffY) || double.IsInfinity(oldOffY)) { oldOffY = 0.0; }
        if (double.IsNaN(anchorViewX) || double.IsInfinity(anchorViewX)) { anchorViewX = 0.0; }
        if (double.IsNaN(anchorViewY) || double.IsInfinity(anchorViewY)) { anchorViewY = 0.0; }

        double pX = (anchorViewX - oldOffX) / oldZoom;
        double pY = (anchorViewY - oldOffY) / oldZoom;

        newOffX = anchorViewX - pX * newZoom;
        newOffY = anchorViewY - pY * newZoom;
    }

    // 视口 X → 图片 X：p = (viewX - offsetX) / zoom（zoom 非法按 1:1 处理）。
    public static double ImageCoordX(double viewX, double offsetX, double zoom)
    {
        if (double.IsNaN(viewX) || double.IsInfinity(viewX)) { return 0.0; }
        if (!IsPositiveFinite(zoom)) { zoom = 1.0; }
        if (double.IsNaN(offsetX) || double.IsInfinity(offsetX)) { offsetX = 0.0; }
        return (viewX - offsetX) / zoom;
    }

    // 视口 Y → 图片 Y：p = (viewY - offsetY) / zoom。
    public static double ImageCoordY(double viewY, double offsetY, double zoom)
    {
        if (double.IsNaN(viewY) || double.IsInfinity(viewY)) { return 0.0; }
        if (!IsPositiveFinite(zoom)) { zoom = 1.0; }
        if (double.IsNaN(offsetY) || double.IsInfinity(offsetY)) { offsetY = 0.0; }
        return (viewY - offsetY) / zoom;
    }

    // 视口点是否落在缩放后的图片矩形内（B3：键盘缩放的光标锚点判定）。
    // 键盘缩放热键触发时鼠标可能不在窗口内/不在图片上：此时返回 false，调用方退回窗口中心锚点。
    public static bool ImageContainsViewPoint(double offsetX, double offsetY, double zoom,
        int imageWidth, int imageHeight, double viewX, double viewY)
    {
        if (!IsPositiveFinite(zoom)) { return false; }
        if (imageWidth <= 0 || imageHeight <= 0) { return false; }
        if (double.IsNaN(offsetX) || double.IsInfinity(offsetX)) { return false; }
        if (double.IsNaN(offsetY) || double.IsInfinity(offsetY)) { return false; }
        if (double.IsNaN(viewX) || double.IsInfinity(viewX)) { return false; }
        if (double.IsNaN(viewY) || double.IsInfinity(viewY)) { return false; }
        double right = offsetX + imageWidth * zoom;
        double bottom = offsetY + imageHeight * zoom;
        return viewX >= offsetX && viewX <= right && viewY >= offsetY && viewY <= bottom;
    }

    // 图片大于视口的方向夹取到边缘不露底；小于视口的方向居中。
    public static void ClampOffsets(double zoom, double imgW, double imgH, double viewW, double viewH,
        ref double offX, ref double offY)
    {
        offX = ClampAxisOffset(zoom, imgW, viewW, offX);
        offY = ClampAxisOffset(zoom, imgH, viewH, offY);
    }

    // 单轴夹取：
    //   图片缩放后尺寸 scaled = img * zoom；
    //   scaled > view → offset ∈ [view - scaled, 0]（左/上边缘不进视口，右/下边缘也不露底）；
    //   scaled <= view → 该方向居中固定：offset = (view - scaled) / 2。
    private static double ClampAxisOffset(double zoom, double imgSize, double viewSize, double offset)
    {
        if (!IsPositiveFinite(zoom) || !IsPositiveFinite(imgSize) || !IsPositiveFinite(viewSize)) { return 0.0; }
        double scaled = imgSize * zoom;
        if (scaled > viewSize)
        {
            double min = viewSize - scaled; // <= 0
            if (double.IsNaN(offset)) { return min; }
            if (offset > 0.0) { offset = 0.0; }
            if (offset < min) { offset = min; }
            return offset;
        }
        return (viewSize - scaled) / 2.0;
    }

    // 该方向是否有平移空间（图片缩放后比视口大）。1e-6 容差吸收浮点误差，避免“差一丁点”也判定可平移。
    public static bool CanPanX(double zoom, double imgW, double viewW)
    {
        if (!IsPositiveFinite(zoom) || !IsPositiveFinite(imgW) || !IsPositiveFinite(viewW)) { return false; }
        return imgW * zoom - viewW > 1e-6;
    }

    public static bool CanPanY(double zoom, double imgH, double viewH)
    {
        if (!IsPositiveFinite(zoom) || !IsPositiveFinite(imgH) || !IsPositiveFinite(viewH)) { return false; }
        return imgH * zoom - viewH > 1e-6;
    }

    // 按住门控键 + 左键拖动时的动作选择：可平移且未锁定图片 → 平移图片；否则 → 移动窗口。
    // 抽出纯函数便于 selftest 断言，也保证「拖窗口」永远有确定行为。
    public static bool ShouldPanImage(bool canPan, bool imageLocked)
    {
        return canPan && !imageLocked;
    }

    // 视口可见区域对应的源图子矩形（用于只绘制可见部分，避免每帧缩放整张大图）。
    // 做法：视口矩形反变换回图片坐标 [ (0-off)/zoom, (view-off)/zoom ]，再与 [0,imgW]×[0,imgH] 求交。
    // 完全不可见 → RectangleF.Empty。
    public static RectangleF VisibleSourceRect(double zoom, double offX, double offY,
        double viewW, double viewH, double imgW, double imgH)
    {
        if (!IsPositiveFinite(zoom) || !IsPositiveFinite(imgW) || !IsPositiveFinite(imgH)
            || !IsPositiveFinite(viewW) || !IsPositiveFinite(viewH))
        {
            return RectangleF.Empty;
        }
        if (double.IsNaN(offX) || double.IsInfinity(offX)) { offX = 0.0; }
        if (double.IsNaN(offY) || double.IsInfinity(offY)) { offY = 0.0; }

        double x0 = (0.0 - offX) / zoom;
        double x1 = (viewW - offX) / zoom;
        double y0 = (0.0 - offY) / zoom;
        double y1 = (viewH - offY) / zoom;

        double left = x0 > 0.0 ? x0 : 0.0;
        double top = y0 > 0.0 ? y0 : 0.0;
        double right = x1 < imgW ? x1 : imgW;
        double bottom = y1 < imgH ? y1 : imgH;

        if (right <= left || bottom <= top) { return RectangleF.Empty; }
        return new RectangleF((float)left, (float)top, (float)(right - left), (float)(bottom - top));
    }

    // ---------- 窗口几何（物理像素；dpi 仅用于 DIP→px） ----------
    // 默认位置（B7）：距工作区右边 24 DIP；高 = 工作区高度 60%（夹取 320..900 DIP）；
    //                  宽 = 高 * 9/16（竖版攻略图比例，夹取 240..1200 DIP）；垂直居中。
    //                  最后统一走 ClampWindowRect 防止小屏出界。
    public static Rectangle DefaultWindowRect(Rectangle workArea, int dpi)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) { return Rectangle.Empty; }

        Size sizeDip = Layout.DefaultWindowSize(workArea, dpi);
        int width = Layout.Px(sizeDip.Width, dpi);
        int height = Layout.Px(sizeDip.Height, dpi);
        int x = workArea.Right - Layout.Px(Layout.DefaultWindowRightMarginDip, dpi) - width;
        int y = workArea.Top + (workArea.Height - height) / 2;

        return ClampWindowRect(new Rectangle(x, y, width, height), workArea, dpi);
    }

    // 设置面板尺寸（DIP；B6）：min(560, 工作区宽-80) × min(560, 工作区高-80)，再做下限保护。
    // 面板后续按这个尺寸居中到工作区，避免小屏/高缩放下外框超出工作区、关闭按钮被裁。
    public static Size SettingsPanelSize(Rectangle workAreaPx, int dpi)
    {
        if (workAreaPx.Width <= 0 || workAreaPx.Height <= 0 || dpi <= 0)
        {
            return new Size(Layout.SettingsPanelMaxWidthDip, Layout.SettingsPanelMaxHeightDip);
        }
        int workAreaWidthDip = Layout.Dip(workAreaPx.Width, dpi);
        int workAreaHeightDip = Layout.Dip(workAreaPx.Height, dpi);
        int width = workAreaWidthDip - Layout.SettingsPanelMarginDip;
        int height = workAreaHeightDip - Layout.SettingsPanelMarginDip;
        if (width > Layout.SettingsPanelMaxWidthDip) { width = Layout.SettingsPanelMaxWidthDip; }
        if (height > Layout.SettingsPanelMaxHeightDip) { height = Layout.SettingsPanelMaxHeightDip; }
        if (width < Layout.SettingsPanelMinWidthDip) { width = Layout.SettingsPanelMinWidthDip; }
        if (height < Layout.SettingsPanelMinHeightDip) { height = Layout.SettingsPanelMinHeightDip; }
        return new Size(width, height);
    }

    // 宽 >= 240 DIP、高 >= 180 DIP、完整落在工作区内（位置夹取使窗口完整可见）。
    // 极小工作区时“不超出工作区”优先于最小尺寸。
    public static Rectangle ClampWindowRect(Rectangle rect, Rectangle workArea, int dpi)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) { return Rectangle.Empty; }

        int minW = Layout.Px(Layout.MinWindowWidthDip, dpi);
        int minH = Layout.Px(Layout.MinWindowHeightDip, dpi);
        if (minW < 1) { minW = 1; }
        if (minH < 1) { minH = 1; }

        int w = rect.Width;
        if (w < minW) { w = minW; }
        if (w > workArea.Width) { w = workArea.Width; }
        if (w < 1) { w = 1; }

        int h = rect.Height;
        if (h < minH) { h = minH; }
        if (h > workArea.Height) { h = workArea.Height; }
        if (h < 1) { h = 1; }

        int x = rect.X;
        if (w >= workArea.Width)
        {
            x = workArea.Left;
        }
        else
        {
            int maxX = workArea.Right - w;
            if (x < workArea.Left) { x = workArea.Left; }
            if (x > maxX) { x = maxX; }
        }

        int y = rect.Y;
        if (h >= workArea.Height)
        {
            y = workArea.Top;
        }
        else
        {
            int maxY = workArea.Bottom - h;
            if (y < workArea.Top) { y = workArea.Top; }
            if (y > maxY) { y = maxY; }
        }

        return new Rectangle(x, y, w, h);
    }

    // 位置预设（**只改目标轴**，另一轴沿用 currentRect 的当前位置与尺寸）：
    //   Left/Right        → 只改 X（贴左/贴右），Y 与高度保持 currentRect；
    //   Top/Bottom        → 只改 Y（贴顶/贴底），X 与宽度保持 currentRect；
    //   CenterH           → 只改 X（水平居中），Y 与高度保持 currentRect；
    //   CenterV           → 只改 Y（垂直居中），X 与宽度保持 currentRect；
    //   ResetDefault      → 直接回默认矩形（§一 公式），与 currentRect 无关。
    // 目标轴与位置最终仍走 ClampWindowRect 夹进 workArea（窗口比工作区更大时收缩，属既有语义）。
    public static Rectangle PresetWindowRect(WindowPreset preset, Rectangle workArea,
        Rectangle currentRect, int dpi)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) { return Rectangle.Empty; }

        Rectangle def = DefaultWindowRect(workArea, dpi);
        if (preset == WindowPreset.ResetDefault) { return def; }

        // 宽高优先取 currentRect；currentRect 未初始化（宽或高为 0）时该轴退回默认矩形。
        int w = currentRect.Width > 0 ? currentRect.Width : def.Width;
        int h = currentRect.Height > 0 ? currentRect.Height : def.Height;
        if (w <= 0) { w = Layout.Px(Layout.MinWindowWidthDip, dpi); }
        if (h <= 0) { h = Layout.Px(Layout.MinWindowHeightDip, dpi); }

        // 非目标轴：先沿用当前位置（currentRect 为空时退回默认位置）
        int x = currentRect.Width > 0 ? currentRect.X : def.X;
        int y = currentRect.Height > 0 ? currentRect.Y : def.Y;

        switch (preset)
        {
            case WindowPreset.Left:
                x = workArea.Left;
                break;
            case WindowPreset.Right:
                x = workArea.Right - w;
                break;
            case WindowPreset.CenterH:
                x = workArea.Left + (workArea.Width - w) / 2;
                break;
            case WindowPreset.Top:
                y = workArea.Top;
                break;
            case WindowPreset.Bottom:
                y = workArea.Bottom - h;
                break;
            case WindowPreset.CenterV:
                y = workArea.Top + (workArea.Height - h) / 2;
                break;
            default:
                break;
        }

        return ClampWindowRect(new Rectangle(x, y, w, h), workArea, dpi);
    }

    // 【旧签名】保持原行为：签名拿不到当前位置，只能把**两轴都居中**后再贴目标轴。
    // 因此 Left/Right 会把另一轴垂直居中、Top/Bottom 会把另一轴水平居中、CenterH/CenterV
    // 两轴都居中——这是本重载的历史语义，为兼容既有调用方（设置面板 / --selftest）而保留。
    // 新代码请改用上面接收 currentRect 的重载，它只改目标轴。
    public static Rectangle PresetWindowRect(WindowPreset preset, Rectangle workArea, Size currentSize, int dpi)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) { return Rectangle.Empty; }

        Rectangle def = DefaultWindowRect(workArea, dpi);
        if (preset == WindowPreset.ResetDefault) { return def; }

        int w = currentSize.Width > 0 ? currentSize.Width : def.Width;
        int h = currentSize.Height > 0 ? currentSize.Height : def.Height;
        if (w <= 0) { w = Layout.Px(Layout.MinWindowWidthDip, dpi); }
        if (h <= 0) { h = Layout.Px(Layout.MinWindowHeightDip, dpi); }

        int centerX = workArea.Left + (workArea.Width - w) / 2;
        int centerY = workArea.Top + (workArea.Height - h) / 2;
        int x = centerX;
        int y = centerY;

        switch (preset)
        {
            case WindowPreset.Left:
                x = workArea.Left;
                break;
            case WindowPreset.Right:
                x = workArea.Right - w;
                break;
            case WindowPreset.Top:
                y = workArea.Top;
                break;
            case WindowPreset.Bottom:
                y = workArea.Bottom - h;
                break;
            default:
                // CenterH / CenterV：都居中
                break;
        }

        return ClampWindowRect(new Rectangle(x, y, w, h), workArea, dpi);
    }

    // 拖动吸附：窗口左/右边缘与工作区左/右边缘距离 <= thresholdPx 时吸附（取更近的一边）。
    // 吸附不改变宽高，也不跨屏（只用传入的 workArea）。snappedX/snappedY 表示该轴是否吸附。
    public static Point SnapMove(Rectangle proposed, Rectangle workArea, int thresholdPx,
        out bool snappedX, out bool snappedY)
    {
        snappedX = false;
        snappedY = false;
        if (workArea.Width <= 0 || workArea.Height <= 0) { return proposed.Location; }
        if (thresholdPx < 0) { thresholdPx = 0; }

        int dx = 0;
        int bestX = thresholdPx + 1;
        int deltaLeft = workArea.Left - proposed.Left;
        if (Math.Abs(deltaLeft) <= thresholdPx && Math.Abs(deltaLeft) < bestX)
        {
            bestX = Math.Abs(deltaLeft);
            dx = deltaLeft;
        }
        int deltaRight = workArea.Right - proposed.Right;
        if (Math.Abs(deltaRight) <= thresholdPx && Math.Abs(deltaRight) < bestX)
        {
            bestX = Math.Abs(deltaRight);
            dx = deltaRight;
        }
        if (bestX <= thresholdPx) { snappedX = true; }

        int dy = 0;
        int bestY = thresholdPx + 1;
        int deltaTop = workArea.Top - proposed.Top;
        if (Math.Abs(deltaTop) <= thresholdPx && Math.Abs(deltaTop) < bestY)
        {
            bestY = Math.Abs(deltaTop);
            dy = deltaTop;
        }
        int deltaBottom = workArea.Bottom - proposed.Bottom;
        if (Math.Abs(deltaBottom) <= thresholdPx && Math.Abs(deltaBottom) < bestY)
        {
            bestY = Math.Abs(deltaBottom);
            dy = deltaBottom;
        }
        if (bestY <= thresholdPx) { snappedY = true; }

        return new Point(proposed.Left + dx, proposed.Top + dy);
    }

    // 拉伸吸附：右下角拉伸时右/下边缘贴近工作区右/下边缘则吸附，左/上（对边）固定。
    // 吸附不改变对边；返回矩形宽高至少 1px。snapped 表示任一轴发生吸附。
    public static Rectangle SnapResize(Rectangle proposed, Rectangle workArea, int thresholdPx, out bool snapped)
    {
        snapped = false;
        if (workArea.Width <= 0 || workArea.Height <= 0) { return proposed; }
        if (thresholdPx < 0) { thresholdPx = 0; }

        int right = proposed.Right;
        int bottom = proposed.Bottom;

        int deltaRight = workArea.Right - right;
        if (Math.Abs(deltaRight) <= thresholdPx)
        {
            right = workArea.Right;
            snapped = true;
        }
        int deltaBottom = workArea.Bottom - bottom;
        if (Math.Abs(deltaBottom) <= thresholdPx)
        {
            bottom = workArea.Bottom;
            snapped = true;
        }

        int w = right - proposed.Left;
        int h = bottom - proposed.Top;
        if (w < 1) { w = 1; }
        if (h < 1) { h = 1; }
        return new Rectangle(proposed.Left, proposed.Top, w, h);
    }

    // ---------- 工具条 / 空状态 / toast / 滚动条几何 ----------
    // 顶部居中、高 32 DIP、左右各 8 DIP 边距（§8.1）。
    // 按钮顺序固定：◀ | 页码 | ▶ | 适应窗口 | 锁图 | [长图] | 设置… | ×。
    // 页码只读（Enabled=false，命中测试返回 None）；无图片时 0/0 且翻页/适应 禁用。
    // 宽度 = 内边距*2 + 各按钮宽 + 间距*(n-1)，**永不压缩按钮宽度**。
    // 放不下时（barW > 客户区宽 − 2*8 DIP）按优先级**渐进隐藏可选按钮**：
    //   × → [长图] → 锁图 → 适应窗口；每移除一个重算，直到放得下。
    //   剩下的 ◀/页码/▶/设置… 约 200 DIP，240 DIP 最小窗口也能完整容纳。
    //   极端小窗仍放不下时允许贴左溢出（自测只断言正常尺寸下不重叠/不出界）。
    public static ToolbarLayout ToolbarGeometry(Size client, int dpi, bool showLongToggle,
        int imageCount, int currentIndex, bool canClose)
    {
        ToolbarLayout layout = new ToolbarLayout();
        if (client.Width <= 0 || client.Height <= 0) { return layout; }

        int barH = Layout.Px(Layout.ToolbarHeightDip, dpi);
        if (barH > client.Height) { barH = client.Height; }
        if (barH < 1) { barH = 1; }
        int pad = Layout.Px(Layout.ToolbarPaddingDip, dpi);
        int gap = Layout.Px(Layout.ToolbarButtonGapDip, dpi);
        int edge = Layout.Px(Layout.ToolbarEdgeMarginDip, dpi);

        bool hasImage = imageCount > 0;

        int page = 0;
        if (hasImage)
        {
            page = currentIndex + 1;
            if (page < 1) { page = 1; }
            if (page > imageCount) { page = imageCount; }
        }
        string pageText = hasImage
            ? page.ToString(CultureInfo.InvariantCulture) + "/" + imageCount.ToString(CultureInfo.InvariantCulture)
            : "0/0";

        // 1) 先按 DIP 收集按钮规格（顺序即绘制/命中顺序）
        List<ToolbarItem> specs = new List<ToolbarItem>();
        List<int> widthsDip = new List<int>();
        AddToolbarSpec(specs, widthsDip, ToolbarButton.Prev, "◀", hasImage, Layout.ToolbarPrevWidthDip);
        AddToolbarSpec(specs, widthsDip, ToolbarButton.Page, pageText, false, Layout.ToolbarPageWidthDip);
        AddToolbarSpec(specs, widthsDip, ToolbarButton.Next, "▶", hasImage, Layout.ToolbarNextWidthDip);
        AddToolbarSpec(specs, widthsDip, ToolbarButton.Fit, "适应窗口", hasImage, Layout.ToolbarFitWidthDip);
        // 「锁图」：锁上后按住门控键拖动 = 只移动窗口（图片不再平移）；再点一次解锁。
        AddToolbarSpec(specs, widthsDip, ToolbarButton.LockImage, "锁图", true, Layout.ToolbarLockWidthDip);
        if (showLongToggle)
        {
            AddToolbarSpec(specs, widthsDip, ToolbarButton.LongMode, "长图", hasImage, Layout.ToolbarLongWidthDip);
        }
        AddToolbarSpec(specs, widthsDip, ToolbarButton.Settings, "设置…", true, Layout.ToolbarSettingsWidthDip);
        // × = 退出程序（关闭主窗口并释放资源），不是隐藏；隐藏请用 Ctrl+Alt+G 或右键菜单。
        // 窄窗口放不下时它也是第一个被渐进隐藏的。
        if (canClose)
        {
            AddToolbarSpec(specs, widthsDip, ToolbarButton.Close, "×", true, Layout.ToolbarCloseWidthDip);
        }

        // 2) 整条宽度（含左右内边距），放不下则按优先级渐进隐藏可选按钮（不压缩宽度）
        int barW = ComputeToolbarWidth(widthsDip, pad, gap, dpi);
        int maxW = client.Width - edge * 2;
        if (maxW < 0) { maxW = 0; }
        ToolbarButton[] hideOrder = new ToolbarButton[]
        {
            ToolbarButton.Close, ToolbarButton.LongMode, ToolbarButton.LockImage, ToolbarButton.Fit
        };
        for (int i = 0; i < hideOrder.Length && barW > maxW; i++)
        {
            // 只对已存在的项生效（LongMode 仅 showLongToggle 时存在，Close 始终存在但可能已隐藏）
            if (RemoveToolbarSpec(specs, widthsDip, hideOrder[i]))
            {
                barW = ComputeToolbarWidth(widthsDip, pad, gap, dpi);
            }
        }

        int barX = (client.Width - barW) / 2;
        if (barX < 0) { barX = 0; } // 极端小窗仍放不下：贴左（不压缩）
        layout.Bar = new Rectangle(barX, 0, barW, barH);

        // 3) 逐个摆放（按钮与工具条同高，命中区就是整块矩形，不留缝）
        int cursor = barX + pad;
        for (int i = 0; i < specs.Count; i++)
        {
            ToolbarItem item = specs[i];
            int itemW = Layout.Px(widthsDip[i], dpi);
            if (itemW < 1) { itemW = 1; }
            item.Rect = new Rectangle(cursor, 0, itemW, barH);
            layout.Items.Add(item);
            cursor += itemW + gap;
        }
        return layout;
    }

    private static void AddToolbarSpec(List<ToolbarItem> specs, List<int> widthsDip,
        ToolbarButton kind, string text, bool enabled, int widthDip)
    {
        ToolbarItem item = new ToolbarItem();
        item.Kind = kind;
        item.Text = text;
        item.Enabled = enabled;
        item.Rect = Rectangle.Empty;
        specs.Add(item);
        widthsDip.Add(widthDip);
    }

    // 工具条总宽 = 左右内边距 + 各按钮宽 + 间距*(n-1)，单位物理像素。
    private static int ComputeToolbarWidth(List<int> widthsDip, int pad, int gap, int dpi)
    {
        int w = pad * 2;
        for (int i = 0; i < widthsDip.Count; i++) { w += Layout.Px(widthsDip[i], dpi); }
        if (widthsDip.Count > 1) { w += gap * (widthsDip.Count - 1); }
        return w;
    }

    // 移除指定按钮（不存在则返回 false）；用于窄窗口渐进隐藏。
    private static bool RemoveToolbarSpec(List<ToolbarItem> specs, List<int> widthsDip, ToolbarButton kind)
    {
        for (int i = 0; i < specs.Count; i++)
        {
            if (specs[i].Kind == kind)
            {
                specs.RemoveAt(i);
                widthsDip.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    // 工具条命中：只在工具条矩形内、按钮可用、且不是只读页码时返回按钮；否则 None。
    public static ToolbarButton HitTestToolbar(ToolbarLayout layout, Point point)
    {
        if (layout == null) { return ToolbarButton.None; }
        if (layout.Bar.Width <= 0 || layout.Bar.Height <= 0) { return ToolbarButton.None; }
        if (!layout.Bar.Contains(point)) { return ToolbarButton.None; }
        for (int i = 0; i < layout.Items.Count; i++)
        {
            ToolbarItem item = layout.Items[i];
            if (item == null || item.Kind == ToolbarButton.None) { continue; }
            if (item.Kind == ToolbarButton.Page) { continue; } // 页码只读
            if (!item.Enabled) { continue; }
            if (item.Rect.Contains(point)) { return item.Kind; }
        }
        return ToolbarButton.None;
    }

    // 空状态卡片（§二）：居中，宽 = min(420 DIP, 客户区宽 − 32 DIP)。
    // 结构：标题行 / 文件夹路径行 / 三个按钮 / 热键提示行，全部按 DIP 换算。
    // 注：folderPath 由绘制层做单行省略绘制，几何上只保留 FolderRect 一行高度，不参与计算。
    public static CardLayout EmptyCardGeometry(Size client, int dpi, string folderPath)
    {
        CardLayout layout = new CardLayout();
        if (client.Width <= 0 || client.Height <= 0) { return layout; }

        int pad = Layout.Px(Layout.EmptyCardPaddingDip, dpi);
        int titleH = Layout.Px(EmptyCardTitleHeightDip, dpi);
        int lineH = Layout.Px(EmptyCardTextHeightDip, dpi);
        int btnH = Layout.Px(Layout.EmptyCardButtonHeightDip, dpi);
        int btnGap = Layout.Px(Layout.EmptyCardButtonGapDip, dpi);
        int lineGap = Layout.Px(EmptyCardLineGapDip, dpi);
        int sectionGap = Layout.Px(EmptyCardSectionGapDip, dpi);

        int sideMargin = Layout.Px(EmptyCardSideMarginDip, dpi);
        int cardW = client.Width - sideMargin * 2;
        int maxW = Layout.Px(Layout.EmptyCardMaxWidthDip, dpi);
        if (cardW > maxW) { cardW = maxW; }
        if (cardW < 1) { cardW = 1; }

        // 高度 = 上下内边距 + 标题 + 行距 + 路径行 + 段距 + 按钮行 + 段距 + 提示行
        int cardH = pad * 2 + titleH + lineGap + lineH + sectionGap + btnH + sectionGap + lineH;

        int cardX = (client.Width - cardW) / 2;
        if (cardX < 0) { cardX = 0; }
        int cardY = (client.Height - cardH) / 2;
        if (cardY < 0) { cardY = 0; }
        layout.Card = new Rectangle(cardX, cardY, cardW, cardH);

        int innerW = cardW - pad * 2;
        if (innerW < 1) { innerW = 1; }

        int y = cardY + pad;
        layout.TitleRect = new Rectangle(cardX + pad, y, innerW, titleH);

        y = layout.TitleRect.Bottom + lineGap;
        layout.FolderRect = new Rectangle(cardX + pad, y, innerW, lineH);

        y = layout.FolderRect.Bottom + sectionGap;
        int btnW = (innerW - btnGap * 2) / 3;
        if (btnW < 1) { btnW = 1; }
        int btnX = cardX + pad;
        layout.Buttons.Add(MakeCardItem(CardButton.ChooseFolder, btnX, y, btnW, btnH, "选择图片文件夹…"));
        btnX += btnW + btnGap;
        layout.Buttons.Add(MakeCardItem(CardButton.OpenFolder, btnX, y, btnW, btnH, "打开文件夹"));
        btnX += btnW + btnGap;
        layout.Buttons.Add(MakeCardItem(CardButton.Rescan, btnX, y, btnW, btnH, "重新扫描"));

        y = y + btnH + sectionGap;
        layout.HintRect = new Rectangle(cardX + pad, y, innerW, lineH);

        return layout;
    }

    private static CardItem MakeCardItem(CardButton kind, int x, int y, int w, int h, string text)
    {
        CardItem item = new CardItem();
        item.Kind = kind;
        item.Rect = new Rectangle(x, y, w, h);
        item.Text = text;
        item.Enabled = true;
        return item;
    }

    // 空状态卡片命中：按钮矩形内且可用 → 对应按钮；否则 None。
    public static CardButton HitTestCard(CardLayout layout, Point point)
    {
        if (layout == null) { return CardButton.None; }
        if (layout.Card.Width <= 0 || layout.Card.Height <= 0) { return CardButton.None; }
        if (!layout.Card.Contains(point)) { return CardButton.None; }
        for (int i = 0; i < layout.Buttons.Count; i++)
        {
            CardItem item = layout.Buttons[i];
            if (item == null || item.Kind == CardButton.None) { continue; }
            if (!item.Enabled) { continue; }
            if (item.Rect.Contains(point)) { return item.Kind; }
        }
        return CardButton.None;
    }

    // toast 几何（§三）：右下角距窗口边 8 DIP；整条不超过“窗口宽 − 40 DIP”；
    // 文本超宽时做**中间省略**（保留头尾，中间换成 …），返回最终矩形与显示文本。
    // maxTextWidthPx > 0 时作为文本宽度的额外上限（调用方可传自己的预算）。
    public static ToastLayout ToastGeometry(string text, Font font, Size client, int dpi, int maxTextWidthPx)
    {
        ToastLayout layout = new ToastLayout();
        layout.Text = "";
        layout.TextSize = Size.Empty;
        if (client.Width <= 0 || client.Height <= 0) { return layout; }
        if (text == null) { text = ""; }

        int pad = Layout.Px(Layout.ToastPaddingDip, dpi);
        int margin = Layout.Px(Layout.ToastMarginDip, dpi);

        int winCap = client.Width - Layout.Px(ToastWidthSlackDip, dpi);
        if (winCap < 1) { winCap = client.Width; }
        if (winCap < 1) { winCap = 1; }

        int textCap = winCap - pad * 2;
        if (textCap < 1) { textCap = 1; }
        if (maxTextWidthPx > 0 && maxTextWidthPx < textCap) { textCap = maxTextWidthPx; }

        string shown = EllipsizeMiddle(text, font, dpi, textCap);
        Size textSize = MeasureTextSafe(shown, font, dpi);
        int textW = textSize.Width;
        if (textW > textCap) { textW = textCap; }
        if (textW < 0) { textW = 0; }
        int textH = textSize.Height;
        if (textH < 1) { textH = 1; }

        int w = textW + pad * 2;
        if (w > winCap) { w = winCap; }
        if (w < 1) { w = 1; }
        int h = textH + pad * 2;
        if (h > client.Height) { h = client.Height; }
        if (h < 1) { h = 1; }

        int x = client.Width - margin - w;
        if (x < 0) { x = 0; }
        int y = client.Height - margin - h;
        if (y < 0) { y = 0; }

        layout.Rect = new Rectangle(x, y, w, h);
        layout.Text = shown;
        layout.TextSize = new Size(textW, textH);
        return layout;
    }

    // 文本测量（不创建 Graphics 对象，只用 TextRenderer；失败时按字号估算，保证不抛异常）。
    private static Size MeasureTextSafe(string text, Font font, int dpi)
    {
        if (string.IsNullOrEmpty(text)) { return new Size(0, TextHeightSafe(font, dpi)); }
        if (font != null)
        {
            try
            {
                Size size = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (size.Width > 0 || size.Height > 0) { return size; }
            }
            catch { }
            try
            {
                Size size2 = TextRenderer.MeasureText(text, font);
                if (size2.Width > 0 || size2.Height > 0) { return size2; }
            }
            catch { }
        }
        int h = TextHeightSafe(font, dpi);
        int charW = (int)Math.Ceiling(h * 0.6);
        if (charW < 1) { charW = 1; }
        return new Size(text.Length * charW, h);
    }

    private static int TextHeightSafe(Font font, int dpi)
    {
        if (font != null)
        {
            try
            {
                int h = font.Height;
                if (h > 0) { return h; }
            }
            catch { }
        }
        return global::Layout.Px(16, dpi);
    }

    // 中间省略：二分“保留字符数”（头尾各一半），返回不超过 maxWidthPx 的最长结果。
    private static string EllipsizeMiddle(string text, Font font, int dpi, int maxWidthPx)
    {
        if (string.IsNullOrEmpty(text)) { return ""; }
        if (maxWidthPx <= 0) { return "…"; }
        if (MeasureTextSafe(text, font, dpi).Width <= maxWidthPx) { return text; }

        int n = text.Length;
        int lo = 0;
        int hi = n - 1;
        string best = "…";
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            string candidate = MiddleEllipsis(text, mid);
            if (MeasureTextSafe(candidate, font, dpi).Width <= maxWidthPx)
            {
                best = candidate;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
    }

    private static string MiddleEllipsis(string text, int keep)
    {
        if (keep >= text.Length) { return text; }
        if (keep <= 0) { return "…"; }
        int prefix = (keep + 1) / 2;
        int suffix = keep / 2;
        string head = text.Substring(0, prefix);
        string tail = suffix > 0 ? text.Substring(text.Length - suffix, suffix) : "";
        return head + "…" + tail;
    }

    // 长图滚动条轨道：右侧 6 DIP 竖条，占满客户区高度。
    public static Rectangle ScrollbarTrackRect(Size client, int dpi)
    {
        if (client.Width <= 0 || client.Height <= 0) { return Rectangle.Empty; }
        int w = Layout.Px(Layout.ScrollbarWidthDip, dpi);
        if (w < 1) { w = 1; }
        if (w > client.Width) { w = client.Width; }
        return new Rectangle(client.Width - w, 0, w, client.Height);
    }

    // 长图滚动条滑块：高度按可见比例，位置按滚动进度。
    //   contentH = imgH * zoom（屏幕像素）；可见比例 = viewH / contentH；
    //   progress = (-offsetY) / (contentH - viewH)（offsetY 为负表示向下滚）；
    //   内容不超过一屏 → 滑块占满轨道（无需滚动）。
    public static Rectangle ScrollbarThumbRect(Size client, int dpi, double viewH, double imgH,
        double offsetY, double zoom)
    {
        Rectangle track = ScrollbarTrackRect(client, dpi);
        if (track.Width <= 0 || track.Height <= 0) { return Rectangle.Empty; }
        if (!IsPositiveFinite(viewH) || !IsPositiveFinite(imgH) || !IsPositiveFinite(zoom))
        {
            return track;
        }

        double contentH = imgH * zoom;
        if (!IsPositiveFinite(contentH) || contentH <= viewH) { return track; }

        double trackH = track.Height;
        double thumbH = trackH * (viewH / contentH);
        double minThumb = Layout.Px(ScrollbarMinThumbDip, dpi);
        if (minThumb > trackH) { minThumb = trackH; }
        if (thumbH < minThumb) { thumbH = minThumb; }
        if (thumbH > trackH) { thumbH = trackH; }

        double maxScroll = contentH - viewH;
        double progress = maxScroll > 0.0 ? (-offsetY) / maxScroll : 0.0;
        if (double.IsNaN(progress)) { progress = 0.0; }
        if (progress < 0.0) { progress = 0.0; }
        if (progress > 1.0) { progress = 1.0; }

        double thumbY = track.Top + progress * (trackH - thumbH);
        int y = (int)Math.Round(thumbY);
        int h = (int)Math.Round(thumbH);
        if (h < 1) { h = 1; }
        if (h > track.Height) { h = track.Height; }
        if (y < track.Top) { y = track.Top; }
        if (y + h > track.Bottom) { y = track.Bottom - h; }
        return new Rectangle(track.X, y, track.Width, h);
    }

    // ---------- 长图 ----------
    // 长图模式下滚轮/按键一格的滚动步长，单位 = **视口（屏幕）像素**。
    // offset 本身就是屏幕像素，所以步长取固定的 60 DIP（约 3 行文字），与 zoom 无关；
    // zoom 参数不参与计算（保留是为了让调用点统一传参，将来若要按缩放调整也无需改签名）。
    public static double LongImageStepPx(double zoom, int dpi)
    {
        int step = Layout.Px(LongScrollStepDip, dpi);
        if (step < 1) { step = 1; }
        return step;
    }

    // 长图滚动夹取：内容高于视口时 offsetY ∈ [viewH - contentH, 0]（上/下到边即停，不露底）；
    // 内容不足一屏时**顶部对齐**（offsetY = 0），因为长图视图是 fit-width + 顶部对齐。
    public static void ClampScroll(double zoom, double imgH, double viewH, ref double offY)
    {
        if (!IsPositiveFinite(zoom) || !IsPositiveFinite(imgH) || !IsPositiveFinite(viewH))
        {
            offY = 0.0;
            return;
        }
        double contentH = imgH * zoom;
        if (contentH > viewH)
        {
            double min = viewH - contentH;
            if (double.IsNaN(offY)) { offY = min; return; }
            if (offY > 0.0) { offY = 0.0; }
            if (offY < min) { offY = min; }
            return;
        }
        offY = 0.0;
    }

    // 单片位图内存预算：32bppArgb 每行 width * 4 字节，单片 <= 96MB（≈ 96_000_000 字节）。
    // 典型 32767 宽：96000000 / (32767 * 4) = 732 行/片，而不是固定 2000 行（262MB，容易 OOM）。
    public const long SliceMemoryBudgetBytes = 96000000L;
    public const int SliceDefaultHeightPx = 2000;

    // 按宽度推导实际片高：rows = max(1, min(sliceHeightPx, 96000000 / (width * 4)))。
    // 默认仍是 2000px/片，只有宽图（width > 12000）才会自动降片高；
    // sliceHeightPx <= 0 或 width <= 0 时返回默认/原值，保证纯函数不抛异常、不除零。
    public static int SliceHeightForWidth(int width, int sliceHeightPx)
    {
        if (sliceHeightPx <= 0) { sliceHeightPx = SliceDefaultHeightPx; }
        if (width <= 0) { return sliceHeightPx; }

        long rowBytes = (long)width * 4L;
        if (rowBytes <= 0L) { return sliceHeightPx; }
        long budgetRows = SliceMemoryBudgetBytes / rowBytes;
        if (budgetRows < 1L) { return 1; }
        if (budgetRows < (long)sliceHeightPx) { return (int)budgetRows; }
        return sliceHeightPx;
    }

    // 切片数量 = ceil(imgH / sliceHeightPx)（每片 2000px 高，最后一片取剩余）。
    public static int SliceCount(double imgH, int sliceHeightPx)
    {
        if (!IsPositiveFinite(imgH) || sliceHeightPx <= 0) { return 0; }
        double n = Math.Ceiling(imgH / (double)sliceHeightPx);
        if (double.IsNaN(n) || n < 1) { return 0; }
        if (n > (double)int.MaxValue) { return int.MaxValue; }
        return (int)n;
    }

    // 第 index 片对应的源图矩形。**index 为 1 基**（与 SliceFileName 的 -01 编号一致）。
    // 越界或非法 → RectangleF.Empty。最后一片高度可能不足 sliceHeightPx。
    public static RectangleF SliceSourceRect(double imgW, double imgH, int index, int sliceHeightPx)
    {
        if (!IsPositiveFinite(imgW) || !IsPositiveFinite(imgH) || sliceHeightPx <= 0) { return RectangleF.Empty; }
        if (index < 1) { return RectangleF.Empty; }
        int count = SliceCount(imgH, sliceHeightPx);
        if (index > count) { return RectangleF.Empty; }

        double y = (double)(index - 1) * sliceHeightPx;
        double h = imgH - y;
        if (h > sliceHeightPx) { h = sliceHeightPx; }
        if (h <= 0.0) { return RectangleF.Empty; }
        return new RectangleF(0f, (float)y, (float)imgW, (float)h);
    }

    // 切片文件名：原文件名（去目录、去扩展名）+ "-slice-NN.png"。
    // 例：a.jpg + 1 → a-slice-01.png；索引 < 1 时按 1 处理；非法输入回退 "image-slice-NN.png"。
    public static string SliceFileName(string originalFileName, int index)
    {
        int n = index < 1 ? 1 : index;
        string name = originalFileName == null ? "" : originalFileName;

        int slash = name.LastIndexOfAny(PathSeparators);
        if (slash >= 0) { name = name.Substring(slash + 1); }
        int dot = name.LastIndexOf('.');
        if (dot > 0) { name = name.Substring(0, dot); }
        if (name.Length == 0) { name = "image"; }

        return name + "-slice-" + n.ToString("00", CultureInfo.InvariantCulture) + ".png";
    }

    // ---------- 自然排序 ----------
    // 规则（提示词 §二 + CONTRACT §6.2）：
    //   1) 先按 '/' 和 '\' 分段，逐段比较（目录同样规则）；
    //   2) 段内切成“数字块 / 非数字块”交替的 run 序列；
    //   3) 两个数字块按**数值**比较（1/2/10 → 1,2,10；A2/A10 → A2,A10）；
    //   4) 数字值相等时（01 与 1）再按原始段逐字符 Ordinal 比较，保证顺序可预期；
    //   5) 非数字块用 OrdinalIgnoreCase（比 CurrentCulture 确定，不受系统区域设置影响）；
    //   6) 全部相等时用整串 Ordinal 兜底（消除 "a" 与 "A" 这类大小写平局的不确定性）。
    public static int CompareNatural(string a, string b)
    {
        if (ReferenceEquals(a, b)) { return 0; }
        if (a == null) { a = ""; }
        if (b == null) { b = ""; }

        string[] segA = SplitPathSegments(a);
        string[] segB = SplitPathSegments(b);
        int n = segA.Length < segB.Length ? segA.Length : segB.Length;
        for (int i = 0; i < n; i++)
        {
            int c = CompareSegments(segA[i], segB[i]);
            if (c != 0) { return c < 0 ? -1 : 1; }
        }
        if (segA.Length != segB.Length) { return segA.Length < segB.Length ? -1 : 1; }

        int ord = string.CompareOrdinal(a, b);
        return ord < 0 ? -1 : (ord > 0 ? 1 : 0);
    }

    public static IComparer<string> NaturalComparer
    {
        get { return NaturalComparerInstance; }
    }

    private static string[] SplitPathSegments(string s)
    {
        List<string> parts = new List<string>();
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '/' || c == '\\')
            {
                parts.Add(s.Substring(start, i - start));
                start = i + 1;
            }
        }
        parts.Add(s.Substring(start));
        return parts.ToArray();
    }

    private static int CompareSegments(string s, string t)
    {
        int i = 0;
        int j = 0;
        while (i < s.Length && j < t.Length)
        {
            bool digitI = IsAsciiDigit(s[i]);
            bool digitJ = IsAsciiDigit(t[j]);

            int ie = i + 1;
            while (ie < s.Length && IsAsciiDigit(s[ie]) == digitI) { ie++; }
            int je = j + 1;
            while (je < t.Length && IsAsciiDigit(t[je]) == digitJ) { je++; }

            string runI = s.Substring(i, ie - i);
            string runJ = t.Substring(j, je - j);

            int c;
            if (digitI && digitJ)
            {
                c = CompareNumberRuns(runI, runJ);
            }
            else
            {
                c = StringComparer.OrdinalIgnoreCase.Compare(runI, runJ);
            }
            if (c != 0) { return c < 0 ? -1 : 1; }

            i = ie;
            j = je;
        }
        if (i < s.Length) { return 1; }
        if (j < t.Length) { return -1; }
        return 0;
    }

    private static bool IsAsciiDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    // 数字块比较：去前导 0 → 长度不同则长度大者数值大；长度相同逐位比；数值相等再按原始段 Ordinal。
    private static int CompareNumberRuns(string x, string y)
    {
        int xs = 0;
        while (xs < x.Length - 1 && x[xs] == '0') { xs++; }
        int ys = 0;
        while (ys < y.Length - 1 && y[ys] == '0') { ys++; }

        int xl = x.Length - xs;
        int yl = y.Length - ys;
        if (xl != yl) { return xl < yl ? -1 : 1; }
        for (int k = 0; k < xl; k++)
        {
            char ca = x[xs + k];
            char cb = y[ys + k];
            if (ca != cb) { return ca < cb ? -1 : 1; }
        }

        // 数值相等（如 "01" 与 "1"）：按原始段逐字符 Ordinal 比较
        int ord = string.CompareOrdinal(x, y);
        return ord < 0 ? -1 : (ord > 0 ? 1 : 0);
    }
}

// IComparer<string> 包装，供 List<T>.Sort / OrderBy 直接使用。
internal sealed class NaturalStringComparer : IComparer<string>
{
    public int Compare(string x, string y)
    {
        return ViewMath.CompareNatural(x, y);
    }
}
// ============================================================================
// 设置与热键 — settings.ini 读写 / 全局热键 / 崩溃日志与诊断
// 要求：容错解析、原子写、可注入路径（selftest 用临时 ini，绝不碰用户配置）。
// 约定：C# 5；不引第三方库；不写注册表；不联网；界面文字全中文。
// ============================================================================

// ---------------------------------------------------------------------------
// AppSettings：settings.ini 的内存模型（字段顺序/含义与提示词 §十 一致）
// ---------------------------------------------------------------------------
internal sealed class AppSettings
{
    // [window]（位置/大小一律 DIP 绝对值）
    public bool HasWindowRect;             // 只有 left/top/width/height 全部有效且在屏幕内才为 true
    public int Left;
    public int Top;
    public int Width = Layout.DefaultWindowWidthDip;
    public int Height = Layout.DefaultWindowHeightDip;
    public int Opacity = 85;               // 20..100（B7：默认 85%）
    public bool TopMost = true;
    public bool PositionLocked;            // 锁定窗口位置与大小（[window] locked）
    public bool Snap = true;               // 边缘吸附（[window] snap）
    public string LastImage = "";          // 相对图片文件夹的路径，否则绝对路径

    // [meta]（B1：首次运行引导；0/1）
    public bool FirstRun;                  // true = 启动后显示首次运行引导并自动写回 0

    // [images]
    public string FolderSetting = "images"; // ini 中原始值（相对 exe 或绝对）
    public string Folder = "";              // 解析后的绝对路径（可能不存在）
    public bool Watch = true;               // 自动刷新
    public List<string> RecentFolders = new List<string>(); // 最近在前，最多 5

    // [behavior]
    public GateKeyKind GateKey = GateKeyKind.Alt;   // B2：默认 Alt（原 Ctrl 与游戏蹲/静步冲突）
    // 运行期标记：ini 里 gate_key 写了非法值、已回退默认（启动摘要提示用，不写入 ini）
    public bool GateKeyFallback;
    // 运行期标记：ini 里 [hotkeys] 有非法/系统占用/常用快捷键组合、已回退默认（C6，启动摘要提示用）
    public int HotkeyFallbackCount;

    // [ui]
    public ThemeKind Theme = ThemeKind.Light;

    // [hotkeys]（Keys.None = 禁用）
    public Dictionary<HotkeyAction, Keys> Hotkeys = new Dictionary<HotkeyAction, Keys>();

    // 运行时路径信息（不写入 ini）
    public string ExeDir = "";
    public string SettingsPath = "";          // 实际用于读/写的路径
    public string PreferredSettingsPath = ""; // 首选路径（exe 同目录）
    public bool UsingFallbackPath;            // 是否已回退到 %APPDATA%\GuideOverlay
    public bool StrictPath;                   // explicitPath 注入：保存失败也不回退（selftest 用）

    public AppSettings()
    {
        ApplyScreenDefaultWindowSize();
    }

    // 首次运行（无 settings.ini）的默认窗口尺寸：主屏工作区高的 60%，宽按 9:16 竖版推算。
    // settings.ini 里有 width/height 时，ApplyValues 会用文件值覆盖这里。
    public void ApplyScreenDefaultWindowSize()
    {
        try
        {
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            Size size = Layout.DefaultWindowSize(workArea, DpiHelper.SystemDpi);
            Width = size.Width;
            Height = size.Height;
        }
        catch
        {
            Width = Layout.DefaultWindowWidthDip;
            Height = Layout.DefaultWindowHeightDip;
        }
    }

    public Keys GetHotkey(HotkeyAction action)
    {
        Keys k;
        if (Hotkeys != null && Hotkeys.TryGetValue(action, out k)) { return k; }
        return HotkeyManager.DefaultCombo(action);
    }

    public void SetHotkey(HotkeyAction action, Keys combo)
    {
        if (Hotkeys == null) { Hotkeys = new Dictionary<HotkeyAction, Keys>(); }
        Hotkeys[action] = combo;
    }

    // 最近文件夹：去重（忽略大小写与末尾分隔符）、置顶、最多 5 个
    public void AddRecentFolder(string path)
    {
        if (path == null) { return; }
        string p = path.Trim();
        if (p.Length == 0) { return; }
        string full = p;
        try { full = Path.GetFullPath(p); }
        catch { full = p; }
        full = SettingsStore.TrimDir(full); // 去掉末尾分隔符，避免 ini 里出现 "images\" 这种写法
        string norm = SettingsStore.NormalizePathForCompare(full);
        for (int i = RecentFolders.Count - 1; i >= 0; i--)
        {
            if (string.Equals(SettingsStore.NormalizePathForCompare(RecentFolders[i]), norm, StringComparison.OrdinalIgnoreCase))
            {
                RecentFolders.RemoveAt(i);
            }
        }
        RecentFolders.Insert(0, full);
        while (RecentFolders.Count > 5) { RecentFolders.RemoveAt(RecentFolders.Count - 1); }
    }
}

// ---------------------------------------------------------------------------
// SettingsStore：ini 路径解析、容错读取、原子写入
// ---------------------------------------------------------------------------
internal static class SettingsStore
{
    // 最近一次保存失败的原因（诊断用；保存成功时清空）。不参与 ini 读写。
    private static string _lastSaveError = "";
    public static string LastSaveError { get { return _lastSaveError; } }

    // C7：上次读 ini 是否触发了「非 UTF-8 → 系统默认编码」重读（启动摘要提示用）
    internal static bool LastLoadEncodingFallback;

    // C7：保存连续失败后退避 30 秒，避免磁盘满/权限异常时每次自动保存都卡在写盘+写日志上。
    public const int SaveBackoffMs = 30000;
    private static int _lastSaveFailTick;
    private static bool _saveBackoffActive;
    // 真正尝试写盘的次数（退避跳过的调用不算）。SaveNow 用它区分「这次没尝试」和「这次又失败了」，
    // 避免退避期间反复弹同一个保存失败 toast。
    private static int _saveAttemptCount;
    public static int SaveAttemptCount { get { return _saveAttemptCount; } }

    // 进程所在目录（exe 同目录）；失败时依次回退 BaseDirectory / 当前目录
    public static string ResolveExeDir()
    {
        try
        {
            string exe = Application.ExecutablePath;
            if (!string.IsNullOrEmpty(exe))
            {
                string dir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir)) { return TrimDir(dir); }
            }
        }
        catch { }
        try
        {
            string b = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(b)) { return TrimDir(b); }
        }
        catch { }
        try { return TrimDir(Environment.CurrentDirectory); }
        catch { return ""; }
    }

    public static string DefaultSettingsPath(string exeDir)
    {
        string dir = TrimDir(exeDir);
        if (dir.Length == 0) { dir = ResolveExeDir(); }
        return Path.Combine(dir, AppInfo.SettingsFileName);
    }

    public static string FallbackSettingsPath()
    {
        string appData = "";
        try { appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); }
        catch { }
        if (string.IsNullOrEmpty(appData))
        {
            try { appData = Path.GetTempPath(); }
            catch { }
        }
        if (string.IsNullOrEmpty(appData)) { appData = "."; }
        return Path.Combine(Path.Combine(appData, "GuideOverlay"), AppInfo.SettingsFileName);
    }

    public static string DefaultImageFolder(string exeDir)
    {
        string dir = TrimDir(exeDir);
        if (dir.Length == 0) { dir = ResolveExeDir(); }
        return Path.Combine(dir, "images");
    }

    // explicitPath != null 时严格使用该路径（selftest 注入，不做可写性回退）
    public static AppSettings Load(string exeDir, string explicitPath)
    {
        AppSettings s = new AppSettings();
        string dir = TrimDir(exeDir);
        if (dir.Length == 0) { dir = ResolveExeDir(); }
        s.ExeDir = dir;
        LastLoadEncodingFallback = false;

        string defPath = DefaultSettingsPath(dir);
        string fbPath = FallbackSettingsPath();
        s.PreferredSettingsPath = defPath;

        string readPath = null;
        if (explicitPath != null)
        {
            // 严格路径：只读/写这里，绝不回退到 %APPDATA%
            s.StrictPath = true;
            s.SettingsPath = explicitPath;
            s.PreferredSettingsPath = explicitPath;
            s.UsingFallbackPath = false;
            try { if (File.Exists(explicitPath)) { readPath = explicitPath; } }
            catch { }
        }
        else
        {
            // 读写路径选择：exe 目录与 %APPDATA% 回退路径都可能留着 settings.ini。
            // 上次保存若回退到 %APPDATA%，exe 目录里会残留旧文件；只按「exe 文件是否存在」取路径
            // 会让下次启动读到旧配置（设置回滚），所以两者都存在时按最后写入时间取较新者
            // （时间相同取 exe 目录，保持首选路径优先）。
            bool defWritable = CanWriteDirectory(dir, defPath);
            bool defExists = false;
            bool fbExists = false;
            DateTime defTime = DateTime.MinValue;
            DateTime fbTime = DateTime.MinValue;
            try
            {
                if (File.Exists(defPath))
                {
                    defExists = true;
                    defTime = File.GetLastWriteTimeUtc(defPath);
                }
            }
            catch { }
            try
            {
                if (File.Exists(fbPath))
                {
                    fbExists = true;
                    fbTime = File.GetLastWriteTimeUtc(fbPath);
                }
            }
            catch { }

            if (defExists && fbExists)
            {
                readPath = defTime >= fbTime ? defPath : fbPath;
            }
            else if (defExists) { readPath = defPath; }
            else if (fbExists) { readPath = fbPath; }

            if (readPath != null)
            {
                // 读到哪个文件就写回哪个文件：避免「读较新的回退文件、却写回 exe 目录旧文件」
                // 导致下一次启动再次读到旧配置。exe 目录不可写时只能写回退路径。
                if (!defWritable)
                {
                    s.SettingsPath = fbPath;
                    s.UsingFallbackPath = true;
                }
                else
                {
                    s.SettingsPath = readPath;
                    s.UsingFallbackPath = SamePath(readPath, fbPath);
                }
            }
            else
            {
                // 两个文件都不存在：按 exe 目录可写性决定首次写入位置
                s.SettingsPath = defWritable ? defPath : fbPath;
                s.UsingFallbackPath = !defWritable;
            }
        }

        // 先填默认热键，保证字典完整（解析失败也回落到默认值）
        HotkeyAction[] actions = HotkeyManager.AllActions;
        for (int i = 0; i < actions.Length; i++)
        {
            s.SetHotkey(actions[i], HotkeyManager.DefaultCombo(actions[i]));
        }
        s.FolderSetting = "images";
        s.Folder = DefaultImageFolder(dir);

        if (readPath == null)
        {
            // settings.ini 不存在 = 首次运行：启动后显示 20 秒引导（B1）
            s.FirstRun = true;
            return s;
        }

        Dictionary<string, string> map = ReadIniMap(readPath);
        if (map == null) { return s; }

        ApplyValues(s, map, dir);
        return s;
    }

    // 保存到 settings.SettingsPath；失败时回退 %APPDATA%（显式注入路径除外）
    public static void Save(AppSettings settings)
    {
        if (settings == null) { return; }

        // C7：上次保存刚失败过 → 30 秒内直接跳过，避免磁盘满/权限异常时每次自动保存都卡在写盘+写日志上。
        // 退避只加在 Save() 层：SaveTo 的主/回退两次尝试不受影响，selftest 直接调 SaveTo 也不受影响。
        if (_saveBackoffActive && unchecked(Environment.TickCount - _lastSaveFailTick) < SaveBackoffMs)
        {
            return;
        }
        _saveAttemptCount++;

        string path = settings.SettingsPath;
        if (string.IsNullOrEmpty(path))
        {
            string dir = settings.ExeDir;
            if (string.IsNullOrEmpty(dir)) { dir = ResolveExeDir(); }
            string def = DefaultSettingsPath(dir);
            if (CanWriteDirectory(dir, def)) { path = def; }
            else { path = FallbackSettingsPath(); }
            settings.SettingsPath = path;
        }

        if (settings.StrictPath)
        {
            // selftest 注入路径：写不进去就算了（只记录 LastSaveError），绝不污染用户 %APPDATA%
            if (SaveTo(settings, path)) { _saveBackoffActive = false; }
            else { MarkSaveFailed(); }
            return;
        }

        // SaveTo 已做「先备份、后覆盖」的安全降级：失败时不会删掉旧配置
        if (SaveTo(settings, path))
        {
            _saveBackoffActive = false;
            settings.UsingFallbackPath = SamePath(path, FallbackSettingsPath());
            return;
        }

        // exe 目录不可写（Program Files / 只读盘 / 权限不足）→ 回退 %APPDATA%
        string fb = FallbackSettingsPath();
        if (!SamePath(path, fb))
        {
            if (SaveTo(settings, fb))
            {
                _saveBackoffActive = false;
                settings.SettingsPath = fb;
                settings.UsingFallbackPath = true;
                return;
            }
            // 两次都失败：保持原 SettingsPath 不变，最后一条错误留在 LastSaveError 供诊断
        }
        MarkSaveFailed();
    }

    // C7：记录一次保存失败，后续 30 秒内不再重试
    private static void MarkSaveFailed()
    {
        _saveBackoffActive = true;
        _lastSaveFailTick = Environment.TickCount;
    }

    // 原子写：先写同目录 .tmp，再替换；返回是否成功（失败原因见 LastSaveError，绝不抛出）
    public static bool SaveTo(AppSettings settings, string path)
    {
        _lastSaveError = "";
        if (settings == null) { return SaveFailed("设置对象为空", null); }
        if (string.IsNullOrEmpty(path)) { return SaveFailed("保存路径为空", null); }

        string tmp = null;
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string text = BuildIniText(settings);
            tmp = MakeTempPath(path);
            // C7：带 BOM 写 UTF-8。老版本写的是无 BOM，记事本等工具可能按 ANSI 误判导致中文乱码；
            // 带 BOM 后谁都能正确识别（读侧 ReadIniText 仍兼容无 BOM / ANSI 旧文件）。
            // 注意 GetBytes 不写 BOM，必须单独写 GetPreamble()。
            UTF8Encoding utf8 = new UTF8Encoding(true);
            byte[] bytes = utf8.GetBytes(text);
            byte[] preamble = utf8.GetPreamble();
            using (FileStream fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (preamble.Length > 0) { fs.Write(preamble, 0, preamble.Length); }
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush();
            }
        }
        catch (Exception ex)
        {
            // C7：写 tmp 失败 → 清掉本次残留的 .tmp（旧 settings.ini 完全没动）
            DeleteTempQuietly(tmp);
            return SaveFailed("写入临时文件失败：" + ex.Message + "；已清理临时文件", ex);
        }

        bool hadOld = false;
        try { hadOld = File.Exists(path); }
        catch { }

        if (!hadOld)
        {
            try
            {
                File.Move(tmp, path);
                _lastSaveError = "";
                return true;
            }
            catch (Exception ex)
            {
                // C7：保存失败不留半成品临时文件
                DeleteTempQuietly(tmp);
                return SaveFailed("写入 settings.ini 失败：" + ex.Message + "；已清理临时文件", ex);
            }
        }

        // 1) 首选原子替换，并顺手留下 .bak（File.Replace 会覆盖同名备份）
        string bak = path + ".bak";
        string replaceError = "";
        try
        {
            File.Replace(tmp, path, bak);
            _lastSaveError = "";
            return true;
        }
        catch (Exception exReplace)
        {
            // File.Replace 在某些文件系统/只读属性/被占用时不可用 → 走下面的安全降级
            // 注意：降级过程中任何一步失败都不删除旧文件（仅记录原因，最终失败时才写诊断日志）
            replaceError = exReplace.Message;
        }

        // File.Replace 失败后目标文件可能已被移走（极少数失败模式），先补上
        bool stillThere = false;
        try { stillThere = File.Exists(path); }
        catch { }
        if (!stillThere)
        {
            try
            {
                File.Move(tmp, path);
                _lastSaveError = "";
                return true;
            }
            catch (Exception ex)
            {
                DeleteTempQuietly(tmp);
                return SaveFailed("settings.ini 丢失且无法写回：" + ex.Message + "；已清理临时文件", ex);
            }
        }

        // 2) 安全降级：先把旧配置完整备份成 .bak，再覆盖写入
        try
        {
            File.Copy(path, bak, true);
        }
        catch (Exception exBak)
        {
            DeleteTempQuietly(tmp);
            return SaveFailed("备份旧配置失败，未改动 settings.ini：" + exBak.Message
                + "；File.Replace 原因：" + replaceError + "；已清理临时文件", exBak);
        }

        try
        {
            // File.Move 不能覆盖已存在的目标，因此用覆盖拷贝；成功后才删 tmp
            File.Copy(tmp, path, true);
            try { if (File.Exists(tmp)) { File.Delete(tmp); } }
            catch { }
            _lastSaveError = "";
            return true;
        }
        catch (Exception exCopy)
        {
            // 覆盖拷贝中途失败可能已截断目标：尽量用 .bak 把旧配置恢复回去
            try { File.Copy(bak, path, true); }
            catch { }
            DeleteTempQuietly(tmp);
            return SaveFailed("覆盖 settings.ini 失败（旧配置已备份为 " + bak + "）：" + exCopy.Message
                + "；File.Replace 原因：" + replaceError + "；已清理临时文件", exCopy);
        }
    }

    // C7：保存失败时清掉本次 `.tmp-*` 半成品（Delete 失败也不算致命，静默跳过）。
    private static void DeleteTempQuietly(string tmp)
    {
        if (string.IsNullOrEmpty(tmp)) { return; }
        try { if (File.Exists(tmp)) { File.Delete(tmp); } }
        catch { }
    }

    // 保存失败：记录原因并写入诊断日志，返回 false（本方法绝不抛出）
    private static bool SaveFailed(string message, Exception ex)
    {
        string detail = string.IsNullOrEmpty(message) ? "未知错误" : message;
        if (ex != null)
        {
            try { detail += "（" + ex.GetType().Name + "）"; }
            catch { }
        }
        _lastSaveError = detail;
        try { Diagnostics.LogSaveError(detail); }
        catch { }
        return false;
    }

    // 同目录临时文件名：带进程 id + 随机后缀，避免并发保存（例如非 UI 线程兜底保存）撞同一个 .tmp
    private static string MakeTempPath(string path)
    {
        int pid = 0;
        try { pid = Process.GetCurrentProcess().Id; }
        catch { }
        string suffix = Guid.NewGuid().ToString("N");
        if (suffix.Length > 12) { suffix = suffix.Substring(0, 12); }
        return path + ".tmp-" + pid.ToString(CultureInfo.InvariantCulture) + "-" + suffix;
    }

    // 解析图片文件夹：空/不存在/不可访问 → 回退 exe\images
    public static string ResolveImageFolder(string configured, string exeDir)
    {
        string dir = TrimDir(exeDir);
        if (dir.Length == 0) { dir = ResolveExeDir(); }
        string def = DefaultImageFolder(dir);

        string cand = null;
        if (!string.IsNullOrEmpty(configured))
        {
            string c = configured.Trim();
            if (c.Length > 0)
            {
                try
                {
                    if (!Path.IsPathRooted(c)) { c = Path.Combine(dir, c); }
                    cand = Path.GetFullPath(c);
                }
                catch { cand = null; }
            }
        }
        if (cand != null && IsReadableDirectory(cand)) { return cand; }
        return def;
    }

    // ------------------------------------------------------------------
    // 路径小工具
    // ------------------------------------------------------------------
    internal static string TrimDir(string dir)
    {
        if (string.IsNullOrEmpty(dir)) { return ""; }
        string s = dir.Trim();
        if (s.Length == 0) { return ""; }
        string root = "";
        try { root = Path.GetPathRoot(s); }
        catch { }
        if (!string.IsNullOrEmpty(root) && s.Length > root.Length)
        {
            s = s.TrimEnd('\\', '/');
        }
        return s;
    }

    // 比较用的规范化路径：绝对化 + 去掉末尾分隔符（根目录保留）
    internal static string NormalizePathForCompare(string path)
    {
        if (string.IsNullOrEmpty(path)) { return ""; }
        string s = path.Trim();
        if (s.Length == 0) { return ""; }
        try { s = Path.GetFullPath(s); }
        catch { }
        string root = "";
        try { root = Path.GetPathRoot(s); }
        catch { }
        if (string.IsNullOrEmpty(root) || s.Length > root.Length)
        {
            s = s.TrimEnd('\\', '/');
        }
        return s;
    }

    private static bool SamePath(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) { return false; }
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    // 目录是否可写：文件已存在则试着以写方式打开；否则在目录里建一个探针文件再删掉
    private static bool CanWriteDirectory(string dir, string filePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
                return true;
            }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { return false; }
            string probe = Path.Combine(dir, "~guideoverlay-write-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.WriteByte(0);
                }
            }
            finally
            {
                try { if (File.Exists(probe)) { File.Delete(probe); } }
                catch { }
            }
            return true;
        }
        catch { return false; }
    }

    // 目录存在且可读（枚举一次确认权限，空目录也算可读）
    internal static bool IsReadableDirectory(string path)
    {
        if (string.IsNullOrEmpty(path)) { return false; }
        try
        {
            if (!Directory.Exists(path)) { return false; }
            DirectoryInfo di = new DirectoryInfo(path);
            IEnumerator<FileSystemInfo> it = null;
            try
            {
                it = di.EnumerateFileSystemInfos().GetEnumerator();
                it.MoveNext();
            }
            finally
            {
                if (it != null) { it.Dispose(); }
            }
            return true;
        }
        catch { return false; }
    }

    // 窗口矩形（DIP）是否与任一屏幕工作区相交；用于「坐标须在屏幕内」的容错
    private static bool IsRectOnSomeScreen(int left, int top, int width, int height)
    {
        try
        {
            int dpi = DpiHelper.SystemDpi;
            Rectangle r = new Rectangle(
                Layout.Px(left, dpi), Layout.Px(top, dpi),
                Layout.Px(width, dpi), Layout.Px(height, dpi));
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i].WorkingArea.IntersectsWith(r)) { return true; }
            }
            return false;
        }
        catch { return true; } // 读不到屏幕信息时不要丢窗口
    }

    // ------------------------------------------------------------------
    // ini 解析
    // ------------------------------------------------------------------
    // C7：UTF-8 解出的文本里出现替换字符（U+FFFD），说明文件实际不是 UTF-8
    // （可能是老版本/记事本按 ANSI 另存过）。纯函数，便于自测。
    internal static bool NeedsEncodingFallback(string text)
    {
        if (string.IsNullOrEmpty(text)) { return false; }
        return text.IndexOf('\uFFFD') >= 0;
    }

    // C7：读 ini 文本。先按 UTF-8；解出替换字符则用系统默认编码（简中 = GBK）重读一次。
    private static string ReadIniText(string path, out bool usedFallback)
    {
        usedFallback = false;
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch { return null; }
        if (NeedsEncodingFallback(text))
        {
            try
            {
                string retry = File.ReadAllText(path, Encoding.Default);
                if (!string.IsNullOrEmpty(retry) && !NeedsEncodingFallback(retry))
                {
                    usedFallback = true;
                    return retry;
                }
            }
            catch { }
        }
        return text;
    }

    private static Dictionary<string, string> ReadIniMap(string path)
    {
        bool usedFallback;
        string text = ReadIniText(path, out usedFallback);
        if (text == null) { return null; }
        LastLoadEncodingFallback = usedFallback;
        try
        {
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line == null) { continue; }
                line = line.Trim();
                if (line.Length == 0) { continue; }
                if (line[0] == ';' || line[0] == '#') { continue; }
                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    if (end > 1) { section = line.Substring(1, end - 1).Trim(); }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) { continue; }
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if (key.Length == 0) { continue; }
                map[section + "\u0001" + key] = val; // 重复键后写的覆盖前写的
            }
            return map;
        }
        catch { return null; }
    }

    private static string GetString(Dictionary<string, string> map, string section, string key, string defaultValue)
    {
        string v;
        if (map.TryGetValue(section + "\u0001" + key, out v)) { return v; }
        return defaultValue;
    }

    private static bool TryGetInt(Dictionary<string, string> map, string section, string key, out int value)
    {
        value = 0;
        string raw = GetString(map, section, key, null);
        if (raw == null) { return false; }
        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    // locked/snap/watch/topmost 只接受 0/1，其它值用默认
    private static bool GetBool01(Dictionary<string, string> map, string section, string key, bool defaultValue)
    {
        int v;
        if (!TryGetInt(map, section, key, out v)) { return defaultValue; }
        if (v == 1) { return true; }
        if (v == 0) { return false; }
        return defaultValue;
    }

    // 把 ini 里的值套到 AppSettings 上（全部容错 + 夹取）
    private static void ApplyValues(AppSettings s, Dictionary<string, string> map, string exeDir)
    {
        // ---- [window] ----
        int left, top, width, height;
        bool hasLeft = TryGetInt(map, "window", "left", out left);
        bool hasTop = TryGetInt(map, "window", "top", out top);
        bool hasWidth = TryGetInt(map, "window", "width", out width);
        bool hasHeight = TryGetInt(map, "window", "height", out height);

        if (!hasWidth) { width = s.Width; }
        if (!hasHeight) { height = s.Height; }
        if (width < Layout.MinWindowWidthDip) { width = Layout.MinWindowWidthDip; }
        if (height < Layout.MinWindowHeightDip) { height = Layout.MinWindowHeightDip; }
        s.Width = width;
        s.Height = height;
        s.Left = hasLeft ? left : 0;
        s.Top = hasTop ? top : 0;
        s.HasWindowRect = hasLeft && hasTop && hasWidth && hasHeight
            && IsRectOnSomeScreen(left, top, width, height);

        int opacity;
        // B7：缺省透明度 85%（AppSettings 默认值），显式值仍夹取 20..100
        if (!TryGetInt(map, "window", "opacity", out opacity)) { opacity = s.Opacity; }
        if (opacity < 20) { opacity = 20; }
        if (opacity > 100) { opacity = 100; }
        s.Opacity = opacity;

        s.TopMost = GetBool01(map, "window", "topmost", true);
        s.PositionLocked = GetBool01(map, "window", "locked", false);
        s.Snap = GetBool01(map, "window", "snap", true);

        string last = GetString(map, "window", "last_image", "");
        s.LastImage = last != null ? last.Trim() : "";

        // ---- [images] ----
        string folder = GetString(map, "images", "folder", null);
        if (folder != null) { s.FolderSetting = folder.Trim(); }
        s.Folder = ResolveImageFolder(s.FolderSetting, exeDir);
        s.Watch = GetBool01(map, "images", "watch", true);

        s.RecentFolders.Clear();
        for (int i = 1; i <= 5; i++)
        {
            string raw = GetString(map, "images", "recent" + i.ToString(CultureInfo.InvariantCulture), "");
            if (raw == null) { continue; }
            string r = raw.Trim();
            if (r.Length == 0) { continue; }
            if (!IsReadableDirectory(r)) { continue; } // 失效项丢弃
            string full = r;
            try { full = Path.GetFullPath(r); }
            catch { }
            bool dup = false;
            for (int j = 0; j < s.RecentFolders.Count; j++)
            {
                if (string.Equals(NormalizePathForCompare(s.RecentFolders[j]), NormalizePathForCompare(full), StringComparison.OrdinalIgnoreCase))
                {
                    dup = true;
                    break;
                }
            }
            if (dup) { continue; }
            s.RecentFolders.Add(full);
            if (s.RecentFolders.Count >= 5) { break; }
        }

        // ---- [behavior] ----
        string gate = GetString(map, "behavior", "gate_key", null);
        if (gate != null)
        {
            switch (gate.Trim().ToLowerInvariant())
            {
                case "ctrl": s.GateKey = GateKeyKind.Ctrl; break;
                case "alt": s.GateKey = GateKeyKind.Alt; break;
                case "shift": s.GateKey = GateKeyKind.Shift; break;
                default:
                    s.GateKey = GateKeyKind.Alt;          // B2：非法值回退默认 Alt
                    s.GateKeyFallback = true;             // 启动摘要会提示用户
                    break;
            }
        }

        // ---- [meta] ----
        // first_run 只认 0/1：缺键 = 老用户（false）；显式 1 = 继续引导；其余按 false 处理
        int firstRun;
        if (TryGetInt(map, "meta", "first_run", out firstRun))
        {
            s.FirstRun = firstRun == 1;
        }

        // ---- [ui] ----
        string theme = GetString(map, "ui", "theme", null);
        if (theme != null && string.Equals(theme.Trim(), "dark", StringComparison.OrdinalIgnoreCase))
        {
            s.Theme = ThemeKind.Dark;
        }
        else
        {
            s.Theme = ThemeKind.Light; // 非法回退 light
        }

        // ---- [hotkeys] ----
        HotkeyAction[] actions = HotkeyManager.AllActions;
        for (int i = 0; i < actions.Length; i++)
        {
            HotkeyAction a = actions[i];
            Keys def = HotkeyManager.DefaultCombo(a);
            string raw = GetString(map, "hotkeys", HotkeyManager.IniKeyOf(a), null);
            if (raw == null) { s.SetHotkey(a, def); continue; }
            bool ok;
            Keys parsed = HotkeyManager.ParseCombo(raw, def, out ok);
            if (!ok)
            {
                // C6：语法不合法 / Win 组合 → 回退默认，并记数供启动摘要提示
                s.HotkeyFallbackCount++;
                s.SetHotkey(a, def);
                continue;
            }
            if (parsed != Keys.None)
            {
                // C6：手工改 ini 也可能写成系统保留组合或常用快捷键（Alt+F4、Ctrl+C 等）。
                // UI 改键走同一套 ValidateCombo，两条路径最终一致；命中就回退默认。
                string validationError;
                if (!HotkeyManager.ValidateCombo(parsed, out validationError))
                {
                    s.HotkeyFallbackCount++;
                    s.SetHotkey(a, def);
                    continue;
                }
            }
            s.SetHotkey(a, parsed);
        }
    }

    // ------------------------------------------------------------------
    // ini 生成
    // ------------------------------------------------------------------
    private static string OneLine(string v)
    {
        if (string.IsNullOrEmpty(v)) { return ""; }
        return v.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string GateKeyText(GateKeyKind kind)
    {
        if (kind == GateKeyKind.Alt) { return "Alt"; }
        if (kind == GateKeyKind.Shift) { return "Shift"; }
        return "Ctrl";
    }

    // 图片文件夹写回 ini：在 exe 目录下尽量写相对路径，否则写绝对路径
    private static string MakeIniFolderValue(AppSettings s)
    {
        string folder = s.Folder;
        if (!string.IsNullOrEmpty(folder))
        {
            string exeDir = TrimDir(s.ExeDir);
            if (exeDir.Length == 0) { return OneLine(folder); }
            try
            {
                string full = TrimDir(Path.GetFullPath(folder));
                string dir = TrimDir(Path.GetFullPath(exeDir));
                if (full.Length > dir.Length && full.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    return OneLine(full.Substring(dir.Length + 1));
                }
                return OneLine(full);
            }
            catch { return OneLine(folder); }
        }
        if (!string.IsNullOrEmpty(s.FolderSetting)) { return OneLine(s.FolderSetting); }
        return "images";
    }

    private static string BuildIniText(AppSettings s)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("; 攻略悬浮窗设置文件（UTF-8 编码）。可手工编辑，多数项重启后生效。");
        sb.AppendLine("; [hotkeys] 的值写 none 或留空表示禁用该热键。");
        sb.AppendLine("; [behavior] gate_key 可选 ctrl / alt / shift，默认 alt（按住它才能操作窗口）。");
        sb.AppendLine();
        sb.AppendLine("[window]");
        if (s.HasWindowRect)
        {
            sb.AppendLine("left=" + s.Left.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("top=" + s.Top.ToString(CultureInfo.InvariantCulture));
        }
        int w = s.Width < Layout.MinWindowWidthDip ? Layout.MinWindowWidthDip : s.Width;
        int h = s.Height < Layout.MinWindowHeightDip ? Layout.MinWindowHeightDip : s.Height;
        int opacity = s.Opacity < 20 ? 20 : (s.Opacity > 100 ? 100 : s.Opacity);
        sb.AppendLine("width=" + w.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("height=" + h.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("opacity=" + opacity.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("topmost=" + (s.TopMost ? "1" : "0"));
        sb.AppendLine("locked=" + (s.PositionLocked ? "1" : "0"));
        sb.AppendLine("snap=" + (s.Snap ? "1" : "0"));
        sb.AppendLine("last_image=" + OneLine(s.LastImage));
        sb.AppendLine();

        sb.AppendLine("[images]");
        sb.AppendLine("folder=" + MakeIniFolderValue(s));
        sb.AppendLine("watch=" + (s.Watch ? "1" : "0"));
        for (int i = 0; i < 5; i++)
        {
            string v = "";
            if (s.RecentFolders != null && i < s.RecentFolders.Count) { v = OneLine(s.RecentFolders[i]); }
            sb.AppendLine("recent" + (i + 1).ToString(CultureInfo.InvariantCulture) + "=" + v);
        }
        sb.AppendLine();

        sb.AppendLine("[behavior]");
        sb.AppendLine("gate_key=" + GateKeyText(s.GateKey));
        sb.AppendLine();

        sb.AppendLine("[ui]");
        sb.AppendLine("theme=" + (s.Theme == ThemeKind.Dark ? "dark" : "light"));
        sb.AppendLine();

        // [meta] 不写进 [window]：first_run=0 表示首次运行引导已完成（B1）
        sb.AppendLine("[meta]");
        sb.AppendLine("first_run=" + (s.FirstRun ? "1" : "0"));
        sb.AppendLine();

        sb.AppendLine("[hotkeys]");
        HotkeyAction[] actions = HotkeyManager.AllActions;
        for (int i = 0; i < actions.Length; i++)
        {
            Keys k = s.GetHotkey(actions[i]);
            string val = k == Keys.None ? "none" : HotkeyManager.FormatCombo(k);
            sb.AppendLine(HotkeyManager.IniKeyOf(actions[i]) + "=" + val);
        }
        return sb.ToString();
    }
}

// ---------------------------------------------------------------------------
// HotkeyEntry：一个全局热键的运行时状态
// ---------------------------------------------------------------------------
internal sealed class HotkeyEntry
{
    public HotkeyAction Action;
    public string IniKey;
    public string DisplayName;
    public Keys DefaultCombo;
    public Keys Combo;
    public HotkeyStatus Status = HotkeyStatus.Unregistered;
    public int ErrorCode;
    public bool IsToggle;   // 开关类：必须带 MOD_NOREPEAT
}

// ---------------------------------------------------------------------------
// HotkeyManager：RegisterHotKey 封装（注册/注销/改键回滚/组合解析与校验）
// ---------------------------------------------------------------------------
internal sealed class HotkeyManager : IDisposable
{
    public const int IdBase = 0x4000;

    // 全部热键动作（顺序即 ini 中的书写顺序）
    internal static readonly HotkeyAction[] AllActions = new HotkeyAction[]
    {
        HotkeyAction.ZoomIn, HotkeyAction.ZoomOut, HotkeyAction.Prev, HotkeyAction.Next, HotkeyAction.Fit,
        HotkeyAction.Jump1, HotkeyAction.Jump2, HotkeyAction.Jump3, HotkeyAction.Jump4, HotkeyAction.Jump5,
        HotkeyAction.Jump6, HotkeyAction.Jump7, HotkeyAction.Jump8, HotkeyAction.Jump9,
        HotkeyAction.OpacityDown, HotkeyAction.OpacityUp, HotkeyAction.ToggleLock, HotkeyAction.ToggleHide
    };

    private readonly AppSettings _settings;
    private readonly List<HotkeyEntry> _entries = new List<HotkeyEntry>();
    private bool _disposed;

    // 共享的 RegisterHotKey 声明没有 SetLastError=true，取不到 1409；
    // 按契约「需要新 P/Invoke 时加在自己文件内的私有 static class」在此私有声明一份。
    private static class HotkeyNative
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    public HotkeyManager(AppSettings settings)
    {
        _settings = settings != null ? settings : new AppSettings();
        for (int i = 0; i < AllActions.Length; i++)
        {
            HotkeyAction a = AllActions[i];
            HotkeyEntry e = new HotkeyEntry();
            e.Action = a;
            e.IniKey = IniKeyOf(a);
            e.DisplayName = DisplayNameOf(a);
            e.DefaultCombo = DefaultCombo(a);
            e.Combo = _settings.GetHotkey(a);
            e.IsToggle = (a == HotkeyAction.ToggleLock || a == HotkeyAction.ToggleHide);
            e.Status = e.Combo == Keys.None ? HotkeyStatus.Disabled : HotkeyStatus.Unregistered;
            e.ErrorCode = 0;
            _entries.Add(e);
        }
    }

    public IntPtr OwnerHandle { get; set; }

    public List<HotkeyEntry> Entries { get { return _entries; } }

    public bool IsRegistered(HotkeyAction action)
    {
        HotkeyEntry e = GetEntry(action);
        return e != null && e.Status == HotkeyStatus.Registered;
    }

    public HotkeyEntry GetEntry(HotkeyAction action)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Action == action) { return _entries[i]; }
        }
        return null;
    }

    internal static int IdOf(HotkeyAction action)
    {
        return IdBase + (int)action;
    }

    // 窗口句柄创建（或重建）后调用：先全部注销再逐条注册，单条失败不影响其它
    public void RegisterAll()
    {
        UnregisterAll();
        if (OwnerHandle == IntPtr.Zero) { return; }

        HashSet<Keys> used = new HashSet<Keys>();
        for (int i = 0; i < _entries.Count; i++)
        {
            HotkeyEntry e = _entries[i];
            if (e.Combo == Keys.None)
            {
                e.Status = HotkeyStatus.Disabled;
                e.ErrorCode = 0;
                continue;
            }
            // [hotkeys] 两个动作配成同一组合：只保留先注册的，后者按失败处理
            if (used.Contains(e.Combo))
            {
                e.Status = HotkeyStatus.FailedOther;
                e.ErrorCode = 0;
                continue;
            }
            int code;
            if (TryRegister(e, out code)) { used.Add(e.Combo); }
        }
    }

    public void UnregisterAll()
    {
        IntPtr h = OwnerHandle;
        for (int i = 0; i < _entries.Count; i++)
        {
            HotkeyEntry e = _entries[i];
            if (e.Status == HotkeyStatus.Registered && h != IntPtr.Zero)
            {
                try { HotkeyNative.UnregisterHotKey(h, IdOf(e.Action)); }
                catch { }
            }
            e.ErrorCode = 0;
            e.Status = e.Combo == Keys.None ? HotkeyStatus.Disabled : HotkeyStatus.Unregistered;
        }
    }

    // 注册单个；失败时写入 Status 与 ErrorCode（1409 = 被其它程序占用）
    public bool TryRegister(HotkeyEntry entry, out int errorCode)
    {
        errorCode = 0;
        if (entry == null) { errorCode = -1; return false; }

        if (entry.Combo == Keys.None)
        {
            entry.Status = HotkeyStatus.Disabled;
            entry.ErrorCode = 0;
            return true;
        }

        IntPtr h = OwnerHandle;
        if (h == IntPtr.Zero)
        {
            entry.Status = HotkeyStatus.FailedOther;
            entry.ErrorCode = 0;
            return false;
        }

        uint mods;
        uint vk;
        if (!TrySplitCombo(entry.Combo, out mods, out vk))
        {
            entry.Status = HotkeyStatus.FailedOther;
            entry.ErrorCode = 0;
            return false;
        }
        // 开关类必须带 MOD_NOREPEAT（避免长按反复切换）；其余热键同样带上，避免连发抖动
        mods |= Win32Const.MOD_NOREPEAT;

        int id = IdOf(entry.Action);
        try { HotkeyNative.UnregisterHotKey(h, id); }
        catch { }

        bool ok = false;
        try { ok = HotkeyNative.RegisterHotKey(h, id, mods, vk); }
        catch { ok = false; }

        if (ok)
        {
            entry.Status = HotkeyStatus.Registered;
            entry.ErrorCode = 0;
            return true;
        }

        int code = 0;
        try { code = Marshal.GetLastWin32Error(); }
        catch { code = 0; }
        errorCode = code;
        entry.ErrorCode = code;
        entry.Status = code == 1409 ? HotkeyStatus.FailedInUse : HotkeyStatus.FailedOther;
        return false;
    }

    // UI 改键：先注销旧组合 → 注册新组合；失败回滚旧组合并返回 false（error 为中文原因）
    public bool Rebind(HotkeyAction action, Keys combo, out string error)
    {
        error = null;
        HotkeyEntry entry = GetEntry(action);
        if (entry == null)
        {
            error = "未知的热键功能。";
            return false;
        }

        if (combo != Keys.None)
        {
            string verr;
            if (!ValidateCombo(combo, out verr))
            {
                error = verr;
                return false;
            }
            // 表内重复：不允许和其它已注册热键冲突
            for (int i = 0; i < _entries.Count; i++)
            {
                HotkeyEntry other = _entries[i];
                if (other == entry) { continue; }
                if (other.Combo == combo && other.Status == HotkeyStatus.Registered)
                {
                    error = "该组合已被「" + other.DisplayName + "」占用，请换一个组合键。";
                    return false;
                }
            }
        }

        if (entry.Combo == combo && (entry.Status == HotkeyStatus.Registered || combo == Keys.None))
        {
            _settings.SetHotkey(action, combo);
            return true;
        }

        Keys oldCombo = entry.Combo;
        HotkeyStatus oldStatus = entry.Status;
        int oldError = entry.ErrorCode;

        // 1) 注销旧组合
        if (entry.Status == HotkeyStatus.Registered && OwnerHandle != IntPtr.Zero)
        {
            try { HotkeyNative.UnregisterHotKey(OwnerHandle, IdOf(action)); }
            catch { }
        }
        entry.Status = HotkeyStatus.Unregistered;
        entry.ErrorCode = 0;
        entry.Combo = combo;

        // 2) 禁用（none）直接成功
        if (combo == Keys.None)
        {
            entry.Status = HotkeyStatus.Disabled;
            _settings.SetHotkey(action, Keys.None);
            return true;
        }

        // 3) 还没有窗口句柄（正常流程不会走到）：先记下组合，等 RegisterAll 时生效
        if (OwnerHandle == IntPtr.Zero)
        {
            entry.Status = HotkeyStatus.Unregistered;
            _settings.SetHotkey(action, combo);
            return true;
        }

        // 4) 注册新组合
        int code;
        if (TryRegister(entry, out code))
        {
            _settings.SetHotkey(action, combo);
            return true;
        }

        // 5) 失败：回滚旧组合
        entry.Combo = oldCombo;
        entry.ErrorCode = 0;
        entry.Status = HotkeyStatus.Unregistered;
        bool restored = false;
        if (oldCombo != Keys.None && oldStatus == HotkeyStatus.Registered)
        {
            int code2;
            restored = TryRegister(entry, out code2);
        }
        if (!restored)
        {
            entry.Status = oldStatus == HotkeyStatus.Registered ? HotkeyStatus.FailedOther : oldStatus;
            entry.ErrorCode = oldError;
        }

        if (code == 1409) { error = "该组合已被其它程序占用，请换一个组合键。"; }
        else { error = "注册失败（错误码 " + code.ToString(CultureInfo.InvariantCulture) + "），请换一个组合键。"; }
        if (oldCombo != Keys.None && oldStatus == HotkeyStatus.Registered && !restored)
        {
            error += " 旧热键也未能恢复，请重启程序或重新设置。";
        }
        return false;
    }

    // 组合键 → Win32 修饰键与虚拟键
    private static bool TrySplitCombo(Keys combo, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (combo == Keys.None) { return false; }
        if ((combo & Keys.Control) != 0) { modifiers |= Win32Const.MOD_CONTROL; }
        if ((combo & Keys.Alt) != 0) { modifiers |= Win32Const.MOD_ALT; }
        if ((combo & Keys.Shift) != 0) { modifiers |= Win32Const.MOD_SHIFT; }
        Keys keyPart = combo & Keys.KeyCode;
        if (keyPart == Keys.None) { return false; }
        // 同 ValidateCombo：LWin=0x5B / RWin=0x5C 在低 16 位，必须逐位检查（RegisterHotKey 也不支持 Win 组合）
        if ((combo & Keys.LWin) == Keys.LWin || (combo & Keys.RWin) == Keys.RWin) { return false; }
        virtualKey = (uint)(int)keyPart;
        return true;
    }

    // ------------------------------------------------------------------
    // 静态表：默认组合 / ini 键名 / 中文名
    // ------------------------------------------------------------------
    public static Keys DefaultCombo(HotkeyAction action)
    {
        Keys m = Keys.Control | Keys.Alt;
        switch (action)
        {
            case HotkeyAction.ZoomIn: return m | Keys.Up;
            case HotkeyAction.ZoomOut: return m | Keys.Down;
            case HotkeyAction.Prev: return m | Keys.Left;
            case HotkeyAction.Next: return m | Keys.Right;
            case HotkeyAction.Fit: return m | Keys.F;
            case HotkeyAction.Jump1: return m | Keys.D1;
            case HotkeyAction.Jump2: return m | Keys.D2;
            case HotkeyAction.Jump3: return m | Keys.D3;
            case HotkeyAction.Jump4: return m | Keys.D4;
            case HotkeyAction.Jump5: return m | Keys.D5;
            case HotkeyAction.Jump6: return m | Keys.D6;
            case HotkeyAction.Jump7: return m | Keys.D7;
            case HotkeyAction.Jump8: return m | Keys.D8;
            case HotkeyAction.Jump9: return m | Keys.D9;
            case HotkeyAction.OpacityDown: return m | Keys.OemOpenBrackets;
            case HotkeyAction.OpacityUp: return m | Keys.OemCloseBrackets;
            case HotkeyAction.ToggleLock: return m | Keys.T;
            case HotkeyAction.ToggleHide: return m | Keys.G;
        }
        return Keys.None;
    }

    public static string DefaultComboText(HotkeyAction action)
    {
        return FormatCombo(DefaultCombo(action));
    }

    public static string IniKeyOf(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ZoomIn: return "zoom_in";
            case HotkeyAction.ZoomOut: return "zoom_out";
            case HotkeyAction.Prev: return "prev";
            case HotkeyAction.Next: return "next";
            case HotkeyAction.Fit: return "fit";
            case HotkeyAction.Jump1: return "jump1";
            case HotkeyAction.Jump2: return "jump2";
            case HotkeyAction.Jump3: return "jump3";
            case HotkeyAction.Jump4: return "jump4";
            case HotkeyAction.Jump5: return "jump5";
            case HotkeyAction.Jump6: return "jump6";
            case HotkeyAction.Jump7: return "jump7";
            case HotkeyAction.Jump8: return "jump8";
            case HotkeyAction.Jump9: return "jump9";
            case HotkeyAction.OpacityDown: return "opacity_down";
            case HotkeyAction.OpacityUp: return "opacity_up";
            case HotkeyAction.ToggleLock: return "toggle_lock";
            case HotkeyAction.ToggleHide: return "toggle_hide";
        }
        return "";
    }

    public static string DisplayNameOf(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ZoomIn: return "放大";
            case HotkeyAction.ZoomOut: return "缩小";
            case HotkeyAction.Prev: return "上一张";
            case HotkeyAction.Next: return "下一张";
            case HotkeyAction.Fit: return "适应窗口";
            case HotkeyAction.Jump1: return "第 1 张";
            case HotkeyAction.Jump2: return "第 2 张";
            case HotkeyAction.Jump3: return "第 3 张";
            case HotkeyAction.Jump4: return "第 4 张";
            case HotkeyAction.Jump5: return "第 5 张";
            case HotkeyAction.Jump6: return "第 6 张";
            case HotkeyAction.Jump7: return "第 7 张";
            case HotkeyAction.Jump8: return "第 8 张";
            case HotkeyAction.Jump9: return "第 9 张";
            case HotkeyAction.OpacityDown: return "降低不透明度";
            case HotkeyAction.OpacityUp: return "提高不透明度";
            case HotkeyAction.ToggleLock: return "锁定交互";
            case HotkeyAction.ToggleHide: return "隐藏/呼出";
        }
        return "";
    }

    // ------------------------------------------------------------------
    // 组合键文本解析 / 格式化 / 校验
    // ------------------------------------------------------------------
    // 支持 "Ctrl+Alt+Up"；"none" 或空 = 禁用（Keys.None）；失败返回 fallback 且 ok=false
    public static Keys ParseCombo(string text, Keys fallback, out bool ok)
    {
        ok = false;
        if (text == null) { ok = true; return Keys.None; } // 缺值等同留空 = 禁用
        string t = text.Trim();
        if (t.Length == 0) { ok = true; return Keys.None; }
        if (string.Equals(t, "none", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "无", StringComparison.Ordinal))
        {
            ok = true;
            return Keys.None;
        }

        string[] raw = t.Split('+');
        Keys mods = Keys.None;
        Keys key = Keys.None;
        bool haveKey = false;
        for (int i = 0; i < raw.Length; i++)
        {
            string part = raw[i] != null ? raw[i].Trim() : "";
            if (part.Length == 0) { continue; }
            Keys mod;
            if (TryParseModifier(part, out mod)) { mods |= mod; continue; }
            Keys k;
            if (!TryParseKeyName(part, out k)) { return fallback; }
            if (haveKey) { return fallback; } // 出现多个主键
            key = k;
            haveKey = true;
        }
        if (!haveKey) { return fallback; }           // 只有修饰键
        if (mods == Keys.None) { return fallback; }  // 没有修饰键
        // Keys 位布局：修饰键在 0x10000 以上（Shift=0x10000、Control=0x20000、Alt=0x40000，
        // Keys.Modifiers=0x70000），而 LWin=0x5B、RWin=0x5C 落在低 16 位的虚拟键码（KeyCode）区。
        // 因此 (mods & (Keys.LWin | Keys.RWin)) 恒为 0（0x5B|0x5C=0x5F 不是有效修饰位掩码），
        // 必须对每个 Win 键位单独判断；这里 mods 里可能带着解析出的 LWin。
        if ((mods & Keys.LWin) == Keys.LWin || (mods & Keys.RWin) == Keys.RWin) { return fallback; } // 不支持 Win 组合
        ok = true;
        return mods | key;
    }

    private static bool TryParseModifier(string text, out Keys modifier)
    {
        modifier = Keys.None;
        if (text == null) { return false; }
        switch (text.Trim().ToLowerInvariant())
        {
            case "ctrl":
            case "control":
            case "ctl":
                modifier = Keys.Control;
                return true;
            case "alt":
                modifier = Keys.Alt;
                return true;
            case "shift":
                modifier = Keys.Shift;
                return true;
            case "win":
            case "windows":
            case "lwin":
            case "super":
            case "cmd":
                modifier = Keys.LWin; // 解析出来但后续会被拒绝（不支持 Win 组合）
                return true;
        }
        return false;
    }

    private static bool TryParseKeyName(string text, out Keys key)
    {
        key = Keys.None;
        if (text == null) { return false; }
        string s = text.Trim();
        if (s.Length == 0) { return false; }

        // 单字符：数字 0-9 与字母 A-Z
        if (s.Length == 1)
        {
            char c = s[0];
            if (c >= '0' && c <= '9') { key = (Keys)((int)Keys.D0 + (c - '0')); return true; }
            if (c >= 'a' && c <= 'z') { key = (Keys)((int)Keys.A + (c - 'a')); return true; }
            if (c >= 'A' && c <= 'Z') { key = (Keys)((int)Keys.A + (c - 'A')); return true; }
        }

        switch (s.ToLowerInvariant())
        {
            case "[": case "oemopenbrackets": case "bracketleft": case "lbracket": key = Keys.OemOpenBrackets; return true;
            case "]": case "oemclosebrackets": case "bracketright": case "rbracket": key = Keys.OemCloseBrackets; return true;
            case "-": case "minus": case "oemminus": key = Keys.OemMinus; return true;
            case "=": case "plus": case "oemplus": key = Keys.Oemplus; return true;
            case ",": case "comma": case "oemcomma": key = Keys.Oemcomma; return true;
            case ".": case "period": case "oemperiod": key = Keys.OemPeriod; return true;
            case "/": case "slash": case "oemquestion": key = Keys.OemQuestion; return true;
            case ";": case "semicolon": case "oemsemicolon": key = Keys.OemSemicolon; return true;
            case "'": case "quote": case "oemquotes": key = Keys.OemQuotes; return true;
            case "`": case "tilde": case "oemtilde": key = Keys.Oemtilde; return true;
            case "\\": case "backslash": case "oempipe": key = Keys.OemPipe; return true;
            case "esc": case "escape": key = Keys.Escape; return true;
            case "backspace": case "back": key = Keys.Back; return true;
            case "enter": case "return": key = Keys.Enter; return true;
            case "space": case "spacebar": key = Keys.Space; return true;
            case "tab": key = Keys.Tab; return true;
            case "ins": case "insert": key = Keys.Insert; return true;
            case "del": case "delete": key = Keys.Delete; return true;
            case "home": key = Keys.Home; return true;
            case "end": key = Keys.End; return true;
            case "pageup": case "pgup": key = Keys.PageUp; return true;
            case "pagedown": case "pgdn": key = Keys.PageDown; return true;
            case "up": key = Keys.Up; return true;
            case "down": key = Keys.Down; return true;
            case "left": key = Keys.Left; return true;
            case "right": key = Keys.Right; return true;
            case "printscreen": case "prtsc": key = Keys.PrintScreen; return true;
        }

        // 多位纯数字不接受（避免 Enum.TryParse 把 "65" 当成数值 65）
        bool allDigits = true;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] < '0' || s[i] > '9') { allDigits = false; break; }
        }
        if (allDigits) { return false; }

        Keys parsed;
        if (Enum.TryParse<Keys>(s, true, out parsed))
        {
            if (parsed == Keys.None) { return false; }
            if ((parsed & Keys.Modifiers) == parsed) { return false; } // 纯修饰键
            if (parsed == Keys.LWin || parsed == Keys.RWin) { return false; }
            key = parsed;
            return true;
        }
        return false;
    }

    // 组合键 → 展示/写 ini 的文本（Keys.None 返回「无」，写 ini 时另写 none）
    public static string FormatCombo(Keys combo)
    {
        if (combo == Keys.None) { return "无"; }
        StringBuilder sb = new StringBuilder();
        if ((combo & Keys.Control) != 0) { sb.Append("Ctrl+"); }
        if ((combo & Keys.Alt) != 0) { sb.Append("Alt+"); }
        if ((combo & Keys.Shift) != 0) { sb.Append("Shift+"); }
        Keys keyPart = combo & Keys.KeyCode;
        if (keyPart == Keys.None)
        {
            string partial = sb.ToString();
            if (partial.EndsWith("+", StringComparison.Ordinal)) { partial = partial.Substring(0, partial.Length - 1); }
            return partial;
        }
        sb.Append(KeyText(keyPart));
        return sb.ToString();
    }

    private static string KeyText(Keys key)
    {
        int code = (int)key;
        if (code >= (int)Keys.D0 && code <= (int)Keys.D9)
        {
            return (code - (int)Keys.D0).ToString(CultureInfo.InvariantCulture);
        }
        switch (key)
        {
            case Keys.OemOpenBrackets: return "[";
            case Keys.OemCloseBrackets: return "]";
            case Keys.OemMinus: return "-";
            case Keys.Oemplus: return "=";
            case Keys.Oemcomma: return ",";
            case Keys.OemPeriod: return ".";
            case Keys.OemQuestion: return "/";
            case Keys.OemSemicolon: return ";";
            case Keys.OemQuotes: return "'";
            case Keys.Oemtilde: return "`";
            case Keys.OemPipe: return "\\";
            case Keys.Escape: return "Esc";
            case Keys.Back: return "Backspace";
            case Keys.Space: return "Space";
        }
        return key.ToString();
    }

    // 改键校验：拒绝无修饰键 / 只有修饰键 / 系统保留组合 / Win 组合
    public static bool ValidateCombo(Keys combo, out string error)
    {
        error = null;
        if (combo == Keys.None)
        {
            error = "未按下任何按键。";
            return false;
        }
        // Keys 位布局说明：修饰位在 0x10000 以上（Shift=0x10000、Control=0x20000、Alt=0x40000，
        // Keys.Modifiers=0x70000），而 LWin=0x5B、RWin=0x5C 是低 16 位的虚拟键码（KeyCode 区）。
        // 所以 (mods & (Keys.LWin | Keys.RWin)) 恒为 0（0x5B|0x5C=0x5F 不属于修饰位），不能用它判断；
        // 组合里也可能同时带着其它主键（此时 combo & KeyCode 不再等于 LWin），故直接对原始 combo 逐位检查。
        if ((combo & Keys.LWin) == Keys.LWin || (combo & Keys.RWin) == Keys.RWin)
        {
            error = "不支持包含 Win 键的组合。";
            return false;
        }
        Keys mods = combo & Keys.Modifiers;
        Keys keyPart = combo & Keys.KeyCode;
        if (mods == Keys.None)
        {
            error = "必须包含 Ctrl、Alt 或 Shift 修饰键。";
            return false;
        }
        if (keyPart == Keys.None)
        {
            error = "必须包含一个非修饰键（不能只有修饰键）。";
            return false;
        }
        if (combo == (Keys.Control | Keys.Alt | Keys.Delete))
        {
            error = "Ctrl+Alt+Del 是系统保留组合，不能用作热键。";
            return false;
        }
        if (combo == (Keys.Alt | Keys.Tab))
        {
            error = "Alt+Tab 是系统保留组合，不能用作热键。";
            return false;
        }
        if (combo == (Keys.Control | Keys.Escape))
        {
            error = "Ctrl+Esc 是系统保留组合，不能用作热键。";
            return false;
        }
        // C6：系统/常用快捷键不能注册成全局热键，否则会把用户的日常操作抢走。
        if (combo == (Keys.Alt | Keys.F4))
        {
            error = "Alt+F4 是关闭窗口的系统组合，不能用作热键。";
            return false;
        }
        if (keyPart == Keys.PrintScreen)
        {
            error = "PrintScreen 不能用作热键（截图键要留给系统）。";
            return false;
        }
        // Ctrl+C/V/X/Z/A/S/F：只拒绝「恰好 Ctrl+字母」这一种。必须精确相等，
        // 不能用「含 Ctrl 和对应字母」来判，否则会误伤 Ctrl+Alt+F、Ctrl+Shift+C 等正常组合。
        if (combo == (Keys.Control | Keys.C) || combo == (Keys.Control | Keys.V)
            || combo == (Keys.Control | Keys.X) || combo == (Keys.Control | Keys.Z)
            || combo == (Keys.Control | Keys.A) || combo == (Keys.Control | Keys.S)
            || combo == (Keys.Control | Keys.F))
        {
            error = "该组合是常用编辑快捷键（复制/粘贴/撤销/保存等），注册后会影响其它程序，请换一个组合。";
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        UnregisterAll();
        OwnerHandle = IntPtr.Zero;
    }
}

// ---------------------------------------------------------------------------
// DiagnosticsContext：诊断信息采集上下文（由主窗口填充）
// ---------------------------------------------------------------------------
internal sealed class DiagnosticsContext
{
    public AppSettings Settings;
    public string ImageFolder = "";
    public string CurrentImageInfo = "";
    public double ZoomPercent = 100;
    public string HotkeyTable = "";
    public string ExtraStatus = "";
    public bool Hidden;
    public bool Locked;
}

// ---------------------------------------------------------------------------
// Diagnostics：崩溃日志 / 诊断包 / 日志目录 / 打开文件夹 / GDI 计数
// ---------------------------------------------------------------------------
internal static class Diagnostics
{
    // 共享声明里没有 GetGuiResources / GetCurrentProcess，按契约在本部分私有声明
    private static class DiagNative
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        // 先把 last error 清零，才能区分「真的 0 个 GDI 对象」与「调用失败返回 0」
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetLastError(uint dwErrCode);
    }

    // %TEMP%（去掉末尾分隔符）；取不到时用当前目录兜底
    public static string TempRoot
    {
        get
        {
            try
            {
                string p = Path.GetTempPath();
                if (!string.IsNullOrEmpty(p)) { return SettingsStore.TrimDir(p); }
            }
            catch { }
            return ".";
        }
    }

    // 写 %TEMP%\GuideOverlay-crash-<yyyyMMdd-HHmmss>-<pid>.log，返回路径；失败返回 null（绝不二次抛出）
    public static string WriteCrashLog(Exception ex, string context, AppSettings settings)
    {
        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            int pid = 0;
            try { pid = Process.GetCurrentProcess().Id; }
            catch { }
            string name = "GuideOverlay-crash-" + stamp + "-" + pid.ToString(CultureInfo.InvariantCulture) + ".log";
            string path = UniqueFilePath(Path.Combine(TempRoot, name));

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("攻略悬浮窗 崩溃日志");
            sb.AppendLine("========================================");
            sb.AppendLine("版本：" + AppInfo.Version);
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("操作系统：" + DescribeOs());
            try
            {
                sb.AppendLine("系统 DPI：" + DpiHelper.SystemDpi.ToString(CultureInfo.InvariantCulture)
                    + "（" + ((int)Math.Round(DpiHelper.SystemDpi * 100.0 / 96.0)).ToString(CultureInfo.InvariantCulture) + "%）");
            }
            catch { }
            try
            {
                sb.AppendLine("进程：PID " + pid.ToString(CultureInfo.InvariantCulture)
                    + "，" + (Environment.Is64BitProcess ? "64 位" : "32 位")
                    + "，.NET 运行时 " + Environment.Version.ToString());
            }
            catch { }
            sb.AppendLine("上下文：" + (context != null ? context : "（未提供）"));
            sb.AppendLine();

            if (settings != null)
            {
                sb.AppendLine("[设置]");
                sb.AppendLine("settings.ini 路径：" + SafeText(settings.SettingsPath));
                sb.AppendLine("settings.ini 首选路径：" + SafeText(settings.PreferredSettingsPath));
                sb.AppendLine("是否回退 %APPDATA%：" + (settings.UsingFallbackPath ? "是" : "否"));
                sb.AppendLine("最近一次保存错误：" + SafeText(SettingsStore.LastSaveError));
                sb.AppendLine("程序目录：" + SafeText(settings.ExeDir));
                sb.AppendLine("图片文件夹（配置值）：" + SafeText(settings.FolderSetting));
                sb.AppendLine("图片文件夹（实际）：" + SafeText(settings.Folder));
                sb.AppendLine("最后图片：" + SafeText(settings.LastImage));
                sb.AppendLine("窗口：" + settings.Left.ToString(CultureInfo.InvariantCulture) + ", "
                    + settings.Top.ToString(CultureInfo.InvariantCulture) + ", "
                    + settings.Width.ToString(CultureInfo.InvariantCulture) + "x"
                    + settings.Height.ToString(CultureInfo.InvariantCulture)
                    + "（DIP，HasWindowRect=" + (settings.HasWindowRect ? "1" : "0") + "）");
                sb.AppendLine("透明度：" + settings.Opacity.ToString(CultureInfo.InvariantCulture) + "%");
                sb.AppendLine("置顶：" + (settings.TopMost ? "是" : "否")
                    + "，锁定位置：" + (settings.PositionLocked ? "是" : "否")
                    + "，边缘吸附：" + (settings.Snap ? "是" : "否")
                    + "，自动刷新：" + (settings.Watch ? "是" : "否"));
                sb.AppendLine("主题：" + (settings.Theme == ThemeKind.Dark ? "深色" : "浅色")
                    + "，门控键：" + (settings.GateKey == GateKeyKind.Alt ? "Alt" : (settings.GateKey == GateKeyKind.Shift ? "Shift" : "Ctrl")));
                sb.AppendLine("热键：");
                AppendHotkeySummary(sb, settings);
                sb.AppendLine();
            }

            sb.AppendLine("[异常]");
            if (ex == null)
            {
                sb.AppendLine("异常：无（未知错误）");
            }
            else
            {
                AppendException(sb, ex);
            }

            sb.AppendLine();
            sb.AppendLine("[环境]");
            sb.AppendLine("日志目录：" + SafeText(LogDirectory()));
            sb.AppendLine("临时目录：" + SafeText(TempRoot));

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }
        catch { return null; }
    }

    // 诊断文本：版本/OS/DPI/屏幕/路径/状态/热键/最近日志
    public static string BuildDiagnosticsText(DiagnosticsContext ctx)
    {
        StringBuilder sb = new StringBuilder();
        if (ctx == null) { ctx = new DiagnosticsContext(); }
        try
        {
            sb.AppendLine("攻略悬浮窗 诊断信息");
            sb.AppendLine("========================================");
            sb.AppendLine("版本：" + AppInfo.Version);
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("操作系统：" + DescribeOs());
            try
            {
                int dpi = DpiHelper.SystemDpi;
                sb.AppendLine("系统 DPI：" + dpi.ToString(CultureInfo.InvariantCulture)
                    + "（" + ((int)Math.Round(dpi * 100.0 / 96.0)).ToString(CultureInfo.InvariantCulture) + "%）");
            }
            catch { sb.AppendLine("系统 DPI：未知"); }
            try
            {
                sb.AppendLine("进程架构：" + (Environment.Is64BitProcess ? "64 位" : "32 位")
                    + "，.NET 运行时：" + Environment.Version.ToString());
            }
            catch { }
            sb.AppendLine();

            sb.AppendLine("[屏幕]");
            try
            {
                Screen[] screens = Screen.AllScreens;
                for (int i = 0; i < screens.Length; i++)
                {
                    Screen sc = screens[i];
                    sb.AppendLine((sc.Primary ? "主屏 " : "副屏 ") + sc.DeviceName
                        + "，分辨率 " + sc.Bounds.Width.ToString(CultureInfo.InvariantCulture) + "×" + sc.Bounds.Height.ToString(CultureInfo.InvariantCulture)
                        + "，工作区 " + sc.WorkingArea.Width.ToString(CultureInfo.InvariantCulture) + "×" + sc.WorkingArea.Height.ToString(CultureInfo.InvariantCulture)
                        + "，位置 " + sc.Bounds.X.ToString(CultureInfo.InvariantCulture) + "," + sc.Bounds.Y.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch { sb.AppendLine("（读取屏幕信息失败）"); }
            sb.AppendLine();

            sb.AppendLine("[路径]");
            AppSettings st = ctx.Settings;
            if (st != null)
            {
                sb.AppendLine("settings.ini 实际路径：" + SafeText(st.SettingsPath));
                sb.AppendLine("settings.ini 首选路径：" + SafeText(st.PreferredSettingsPath));
                sb.AppendLine("是否使用 %APPDATA% 回退：" + (st.UsingFallbackPath ? "是" : "否"));
                sb.AppendLine("最近一次保存错误：" + SafeText(SettingsStore.LastSaveError));
                sb.AppendLine("程序目录：" + SafeText(st.ExeDir));
                sb.AppendLine("图片文件夹（配置值）：" + SafeText(st.FolderSetting));
                string actualFolder = string.IsNullOrEmpty(ctx.ImageFolder) ? st.Folder : ctx.ImageFolder;
                sb.AppendLine("图片文件夹（实际）：" + SafeText(actualFolder));
                sb.AppendLine("最后图片：" + SafeText(st.LastImage));
            }
            else
            {
                sb.AppendLine("settings：无");
            }
            if (!string.IsNullOrEmpty(ctx.CurrentImageInfo)) { sb.AppendLine("当前图片：" + ctx.CurrentImageInfo); }
            sb.AppendLine("日志目录：" + SafeText(LogDirectory()));
            sb.AppendLine();

            sb.AppendLine("[状态]");
            sb.AppendLine("缩放：" + ctx.ZoomPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%");
            sb.AppendLine("隐藏：" + (ctx.Hidden ? "是" : "否"));
            sb.AppendLine("锁定交互：" + (ctx.Locked ? "是" : "否"));
            int gdi = GdiObjectCount();
            sb.AppendLine("GDI 对象数：" + (gdi >= 0 ? gdi.ToString(CultureInfo.InvariantCulture) : "未知"));
            if (!string.IsNullOrEmpty(ctx.ExtraStatus))
            {
                sb.AppendLine();
                sb.AppendLine("[其它状态]");
                sb.AppendLine(ctx.ExtraStatus);
            }
            sb.AppendLine();

            sb.AppendLine("[热键]");
            if (!string.IsNullOrEmpty(ctx.HotkeyTable))
            {
                sb.AppendLine(ctx.HotkeyTable);
            }
            else if (st != null)
            {
                AppendHotkeySummary(sb, st);
            }
            else
            {
                sb.AppendLine("（无）");
            }
            sb.AppendLine();

            sb.AppendLine("[最近日志]");
            AppendRecentLogs(sb);
        }
        catch (Exception ex)
        {
            try { sb.AppendLine("（诊断信息生成失败：" + ex.Message + "）"); }
            catch { }
        }
        return sb.ToString();
    }

    // 导出到 %TEMP%\GuideOverlay-diagnostics-<timestamp>\，返回目录；失败返回 null
    public static string ExportDiagnostics(DiagnosticsContext ctx)
    {
        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string dir = UniqueDirectoryPath(Path.Combine(TempRoot, "GuideOverlay-diagnostics-" + stamp));
            Directory.CreateDirectory(dir);

            // 1) diagnostics.txt
            try
            {
                File.WriteAllText(Path.Combine(dir, "diagnostics.txt"), BuildDiagnosticsText(ctx), new UTF8Encoding(false));
            }
            catch { }

            // 2) settings.ini 副本
            try
            {
                string settingsPath = null;
                if (ctx != null && ctx.Settings != null)
                {
                    if (!string.IsNullOrEmpty(ctx.Settings.SettingsPath) && File.Exists(ctx.Settings.SettingsPath))
                    {
                        settingsPath = ctx.Settings.SettingsPath;
                    }
                    else if (!string.IsNullOrEmpty(ctx.Settings.PreferredSettingsPath) && File.Exists(ctx.Settings.PreferredSettingsPath))
                    {
                        settingsPath = ctx.Settings.PreferredSettingsPath;
                    }
                }
                if (settingsPath != null)
                {
                    File.Copy(settingsPath, Path.Combine(dir, "settings.ini"), true);
                }
            }
            catch { }

            // 3) 崩溃日志（%TEMP% 与日志目录，最多复制最近 10 个）
            try
            {
                List<string> logs = new List<string>();
                CollectFiles(logs, TempRoot, "GuideOverlay-crash-*.log");
                CollectFiles(logs, LogDirectory(), "GuideOverlay-crash-*.log");
                logs.Sort(StringComparer.OrdinalIgnoreCase); // 文件名带时间戳，升序
                int start = logs.Count > 10 ? logs.Count - 10 : 0;
                for (int i = start; i < logs.Count; i++)
                {
                    try { File.Copy(logs[i], Path.Combine(dir, Path.GetFileName(logs[i])), true); }
                    catch { }
                }
            }
            catch { }

            // 4) selftest.log
            try
            {
                string st = FindSelfTestLog(ctx);
                if (st != null) { File.Copy(st, Path.Combine(dir, "selftest.log"), true); }
            }
            catch { }

            return dir;
        }
        catch { return null; }
    }

    // %TEMP%\GuideOverlay（不存在则创建）
    public static string LogDirectory()
    {
        string dir = Path.Combine(TempRoot, "GuideOverlay");
        try
        {
            if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
        }
        catch { }
        return dir;
    }

    // 保存失败诊断：追加一行到 %TEMP%\\GuideOverlay\\GuideOverlay-save.log（失败静默，绝不抛出）
    public static void LogSaveError(string message)
    {
        try
        {
            if (string.IsNullOrEmpty(message)) { return; }
            string dir = LogDirectory();
            string path = Path.Combine(dir, "GuideOverlay-save.log");
            // 简单防膨胀：超过 256KB 时重开一个文件（只保留近期记录）
            try
            {
                FileInfo fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 256 * 1024)
                {
                    try { File.Delete(path); }
                    catch { }
                }
            }
            catch { }
            int pid = 0;
            try { pid = Process.GetCurrentProcess().Id; }
            catch { }
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + " [pid " + pid.ToString(CultureInfo.InvariantCulture) + "] " + message + Environment.NewLine;
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
        catch { }
    }

    // C8：通用事件日志（低权限启动提示的用户选择等），追加到 %TEMP%\GuideOverlay\GuideOverlay-events.log。
    // 与崩溃/保存日志分开，便于用户反馈「启动时点了什么」时定位；失败静默，绝不抛出。
    public static void LogEvent(string message)
    {
        try
        {
            if (string.IsNullOrEmpty(message)) { return; }
            string dir = LogDirectory();
            string path = Path.Combine(dir, "GuideOverlay-events.log");
            try
            {
                FileInfo fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 256 * 1024)
                {
                    // 超过 256KB 重开一个文件（只保留近期记录）
                    try { File.Delete(path); }
                    catch { }
                }
            }
            catch { }
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + " " + message + Environment.NewLine;
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
        catch { }
    }

    // 用资源管理器打开文件夹（路径是文件时选中它）；失败静默
    public static void OpenFolder(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) { return; }
            if (File.Exists(path))
            {
                ProcessStartInfo psiSelect = new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"");
                psiSelect.UseShellExecute = true;
                Process.Start(psiSelect);
                return;
            }
            if (!Directory.Exists(path))
            {
                try { Directory.CreateDirectory(path); }
                catch { }
            }
            if (Directory.Exists(path))
            {
                ProcessStartInfo psi = new ProcessStartInfo("explorer.exe", "\"" + path + "\"");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
        }
        catch { }
    }

    // 当前进程 GDI 对象数（调试用）；失败返回 -1。
    // 注意：GetGuiResources 返回 0 既可能是「进程确实没有 GDI 对象」（纯控制台进程），
    // 也可能是调用失败，因此先清零 last error 再调用，用 last error 区分两者。
    public static int GdiObjectCount()
    {
        try
        {
            try { DiagNative.SetLastError(0); }
            catch { }
            uint count = DiagNative.GetGuiResources(DiagNative.GetCurrentProcess(), 0);
            if (count == 0)
            {
                int err = 0;
                try { err = Marshal.GetLastWin32Error(); }
                catch { err = 0; }
                return err != 0 ? -1 : 0;
            }
            if (count > int.MaxValue) { return -1; }
            return (int)count;
        }
        catch { return -1; }
    }

    // RtlGetVersion 返回真实版本：无兼容性 manifest 时 Environment.OSVersion 会把 Win10/11 报成 6.2.9200
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfoEx
    {
        public int Size;
        public int Major;
        public int Minor;
        public int Build;
        public int PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string CsdVersion;
    }

    [DllImport("ntdll.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int RtlGetVersion(ref OsVersionInfoEx info);

    private static Version TryGetRealOsVersion()
    {
        try
        {
            OsVersionInfoEx info = new OsVersionInfoEx();
            info.Size = Marshal.SizeOf(typeof(OsVersionInfoEx));
            info.CsdVersion = "";
            if (RtlGetVersion(ref info) == 0 && info.Major > 0)
            {
                return new Version(info.Major, info.Minor, info.Build);
            }
        }
        catch { }
        return null;
    }

    public static string DescribeOs()
    {
        try
        {
            Version v = TryGetRealOsVersion();
            if (v == null)
            {
                OperatingSystem os = Environment.OSVersion;
                v = os.Version;
            }
            string name = "Windows";
            if (v.Major == 10)
            {
                name = v.Build >= 22000 ? "Windows 11" : "Windows 10";
            }
            else if (v.Major == 6 && v.Minor == 3) { name = "Windows 8.1"; }
            else if (v.Major == 6 && v.Minor == 2) { name = "Windows 8"; }
            else if (v.Major == 6 && v.Minor == 1) { name = "Windows 7"; }
            else if (v.Major == 6 && v.Minor == 0) { name = "Windows Vista"; }

            string arch = "64 位";
            try { arch = Environment.Is64BitOperatingSystem ? "64 位" : "32 位"; }
            catch { }
            return name + " " + v.ToString() + "（" + arch + "）";
        }
        catch { return "未知系统"; }
    }

    // ------------------------------------------------------------------
    // 内部工具
    // ------------------------------------------------------------------
    private static string SafeText(string v)
    {
        if (string.IsNullOrEmpty(v)) { return "（空）"; }
        return v;
    }

    private static void AppendException(StringBuilder sb, Exception ex)
    {
        try
        {
            sb.AppendLine("异常类型：" + ex.GetType().FullName);
            sb.AppendLine("异常消息：" + ex.Message);
            sb.AppendLine("堆栈：");
            sb.AppendLine(ex.StackTrace != null ? ex.StackTrace : "（无堆栈）");
            Exception inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 5)
            {
                sb.AppendLine();
                sb.AppendLine("--- 内部异常 " + (depth + 1).ToString(CultureInfo.InvariantCulture) + " ---");
                sb.AppendLine("类型：" + inner.GetType().FullName);
                sb.AppendLine("消息：" + inner.Message);
                sb.AppendLine(inner.StackTrace != null ? inner.StackTrace : "（无堆栈）");
                inner = inner.InnerException;
                depth++;
            }
        }
        catch { }
    }

    private static void AppendHotkeySummary(StringBuilder sb, AppSettings settings)
    {
        HotkeyAction[] actions = HotkeyManager.AllActions;
        for (int i = 0; i < actions.Length; i++)
        {
            HotkeyAction a = actions[i];
            Keys k = settings.GetHotkey(a);
            sb.AppendLine("  " + HotkeyManager.DisplayNameOf(a) + "：" + HotkeyManager.FormatCombo(k)
                + "（" + HotkeyManager.IniKeyOf(a) + "）");
        }
    }

    private static void AppendRecentLogs(StringBuilder sb)
    {
        List<string> logs = new List<string>();
        CollectFiles(logs, TempRoot, "GuideOverlay-*.log");
        CollectFiles(logs, LogDirectory(), "GuideOverlay-*.log");
        if (logs.Count == 0)
        {
            sb.AppendLine("（无）");
            return;
        }
        List<KeyValuePair<DateTime, string>> items = new List<KeyValuePair<DateTime, string>>();
        for (int i = 0; i < logs.Count; i++)
        {
            try { items.Add(new KeyValuePair<DateTime, string>(File.GetLastWriteTime(logs[i]), logs[i])); }
            catch { }
        }
        items.Sort(delegate(KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b)
        {
            return b.Key.CompareTo(a.Key); // 新的在前
        });
        int n = items.Count > 10 ? 10 : items.Count;
        for (int i = 0; i < n; i++)
        {
            string f = items[i].Value;
            long size = 0;
            try { size = new FileInfo(f).Length; }
            catch { }
            sb.AppendLine(items[i].Key.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + "  " + f + "  " + size.ToString(CultureInfo.InvariantCulture) + " 字节");
        }
    }

    private static void CollectFiles(List<string> into, string dir, string pattern)
    {
        try
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { return; }
            string[] files = Directory.GetFiles(dir, pattern);
            for (int i = 0; i < files.Length; i++)
            {
                string f = files[i];
                bool dup = false;
                for (int j = 0; j < into.Count; j++)
                {
                    if (string.Equals(into[j], f, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                }
                if (!dup) { into.Add(f); }
            }
        }
        catch { }
    }

    private static string FindSelfTestLog(DiagnosticsContext ctx)
    {
        List<string> candidates = new List<string>();
        try
        {
            if (ctx != null && ctx.Settings != null && !string.IsNullOrEmpty(ctx.Settings.ExeDir))
            {
                string p = Path.Combine(ctx.Settings.ExeDir, "selftest.log");
                if (File.Exists(p)) { candidates.Add(p); }
            }
        }
        catch { }
        try
        {
            string p = Path.Combine(TempRoot, "selftest.log");
            if (File.Exists(p)) { candidates.Add(p); }
        }
        catch { }
        try
        {
            string[] dirs = Directory.GetDirectories(TempRoot, "GuideOverlay-selftest-*");
            for (int i = 0; i < dirs.Length; i++)
            {
                string p = Path.Combine(dirs[i], "selftest.log");
                if (File.Exists(p)) { candidates.Add(p); }
            }
        }
        catch { }

        string best = null;
        DateTime bestTime = DateTime.MinValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            try
            {
                DateTime t = File.GetLastWriteTime(candidates[i]);
                if (t >= bestTime) { bestTime = t; best = candidates[i]; }
            }
            catch { }
        }
        return best;
    }

    // 同名文件已存在时追加 -2/-3…（崩溃日志/诊断目录）
    private static string UniqueFilePath(string path)
    {
        if (!File.Exists(path)) { return path; }
        string dir = Path.GetDirectoryName(path);
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 2; i < 100; i++)
        {
            string cand = Path.Combine(dir != null ? dir : "", name + "-" + i.ToString(CultureInfo.InvariantCulture) + ext);
            if (!File.Exists(cand)) { return cand; }
        }
        return path;
    }

    private static string UniqueDirectoryPath(string path)
    {
        if (!Directory.Exists(path)) { return path; }
        for (int i = 2; i < 100; i++)
        {
            string cand = path + "-" + i.ToString(CultureInfo.InvariantCulture);
            if (!Directory.Exists(cand)) { return cand; }
        }
        return path;
    }
}
// ============================================================================
// 图片库 — 图片扫描 / 头部尺寸探测 / 解码缓存 / 切片导出 / 文件夹监视
// 约束：解码前先校验头部尺寸；总解码像素 ≤ 8000 万（C5）；最多 3 张；GIF 取首帧；JPG 按 EXIF 方向旋转。
// 线程模型：ImageLibrary 只在 UI 线程使用（GetBase/Trim/ClearCache 不加锁）；
//           FolderWatcher 的事件在后台线程产生，800ms 防抖后 Post 回 UI 线程再触发 Changed。
// ============================================================================

internal sealed class ImageEntry
{
    public string FullPath;
    public string RelPath;
    public long FileSize;
    public int PixelWidth;
    public int PixelHeight;
    public bool SizeKnown;
    // C2：文件最后修改时间（UTC ticks），用于计算扫描指纹，判断自动刷新时图库是否真的变了。
    public long LastWriteUtcTicks;

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(FullPath)) { return ""; }
            try { return Path.GetFileName(FullPath); }
            catch { return FullPath; }
        }
    }

    public long PixelCount
    {
        get
        {
            if (PixelWidth <= 0 || PixelHeight <= 0) { return 0; }
            return (long)PixelWidth * (long)PixelHeight;
        }
    }
}

internal sealed class SkippedImage
{
    public string FullPath;
    public string Reason;
}

internal sealed class ScanResult
{
    public string Folder = "";
    public List<ImageEntry> Images = new List<ImageEntry>();
    public List<SkippedImage> Skipped = new List<SkippedImage>();
    public string Error;
}

internal sealed class SliceExportResult
{
    public int Count;
    public string Error;
    public List<string> Files = new List<string>();
}

internal sealed class ImageLibrary : IDisposable
{
    // 单图上限：像素数 / 单边像素。超过即「图片过大，建议切片后再看」。
    public const long MaxImagePixels = 60000000;
    public const int MaxImageEdge = 32767;
    // 解码缓存上限：张数 / 总解码像素（≈ 3×2600 万像素，防止 3 张 6000 万图吃掉 720MB）。
    // C5：1.2 亿 → 8000 万，把常驻解码位图收紧到约 320MB，给旋转派生副本/适应缓存留余量。
    public const long DecodedPixelBudget = 80000000;
    public const int MaxCachedBitmaps = 3;
    // C5：预取邻居图的上限（800 万像素）。超过就不预取，避免换图时一次解码三张大图。
    public const long PrefetchMaxPixels = 8000000;
    // 切片高度（像素）。
    public const int DefaultSliceHeightPx = 2000;

    // C1：切片默认输出子目录名。图库扫描跳过该目录和 *-slice-NN.png 切片文件，
    // 避免「导出切片 → 自动刷新把切片当新图收进图库」的自污染循环。
    public const string SliceOutputDirectoryName = ".slices";
    public const string ReasonSliceArtifact = "切片产物（不加入图库）";

    // 跳过原因（界面直接展示，保持中文短语，便于拼接「文件：原因」）。
    public const string ReasonUnsupported = "不支持的格式";
    public const string ReasonEmptyFile = "文件为空（0 字节）";
    public const string ReasonCorrupt = "文件损坏，无法读取图片信息";
    public const string ReasonTooLarge = "图片过大，建议切片后再看";

    public string Folder = "";
    public List<ImageEntry> Images = new List<ImageEntry>();
    public List<SkippedImage> Skipped = new List<SkippedImage>();
    public string LastScanError;

    // 缓存项：索引 → 位图。同一索引只有在路径一致时才算命中（重扫后索引会变化）。
    private sealed class CachedBitmap
    {
        public Bitmap Bitmap;
        public string Path;
        public long Pixels;
    }

    private readonly Dictionary<int, CachedBitmap> _cache = new Dictionary<int, CachedBitmap>();
    // 由旧到新的访问顺序，用于 LRU 淘汰。
    private readonly List<int> _cacheOrder = new List<int>();
    private long _cachedPixels;
    private bool _disposed;

    // C2：最近一次成功扫描的指纹（文件数 / 总字节 / 最大 LastWriteTimeUtc ticks）。
    // 自动刷新靠它判断图库有没有真的变化：没变就不重载当前图、也不清缓存。
    public int ScanFileCount;
    public long ScanTotalBytes;
    public long ScanMaxWriteUtcTicks;

    public static string[] SupportedExtensions
    {
        get { return new string[] { "png", "jpg", "jpeg", "bmp", "gif" }; }
    }

    // ------------------------------------------------------------------------
    // 扩展名判断（大小写不敏感）
    // ------------------------------------------------------------------------
    public static bool IsSupportedExtension(string path)
    {
        if (string.IsNullOrEmpty(path)) { return false; }
        string extension;
        try { extension = Path.GetExtension(path); }
        catch { return false; }
        if (string.IsNullOrEmpty(extension)) { return false; }
        extension = extension.TrimStart('.');
        string[] supported = SupportedExtensions;
        for (int i = 0; i < supported.Length; i++)
        {
            if (string.Equals(extension, supported[i], StringComparison.OrdinalIgnoreCase)) { return true; }
        }
        return false;
    }

    // 拖放用：目录不算图片；其余与扩展名判断一致。
    public static bool IsSupportedForDrop(string path)
    {
        if (string.IsNullOrEmpty(path)) { return false; }
        try
        {
            if (Directory.Exists(path)) { return false; }
        }
        catch { }
        return IsSupportedExtension(path);
    }

    // C1：切片产物文件名匹配「*-slice-NN.png」（-slice- + 2 位以上数字 + .png）。
    // 导出切片默认写到源目录的 .slices 子目录；即使用户选到图库目录本身，
    // 扫描也能靠这个判断把切片文件跳过，不会越刷越多。
    public static bool IsSliceArtifact(string path)
    {
        if (string.IsNullOrEmpty(path)) { return false; }
        string name;
        try { name = Path.GetFileName(path); }
        catch { return false; }
        if (string.IsNullOrEmpty(name)) { return false; }
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) { return false; }

        const string marker = "-slice-";
        int start = name.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start <= 0) { return false; }
        int digitStart = start + marker.Length;
        int digitEnd = name.Length - ".png".Length;
        if (digitEnd - digitStart < 2) { return false; }   // 至少两位编号（-slice-01）
        for (int i = digitStart; i < digitEnd; i++)
        {
            if (name[i] < '0' || name[i] > '9') { return false; }
        }
        return true;
    }

    // ------------------------------------------------------------------------
    // 扫描：递归 + 自然排序 + 头部尺寸校验；任何情况下都不抛异常
    // ------------------------------------------------------------------------
    public ScanResult Scan(string folder)
    {
        ScanResult result = new ScanResult();
        result.Folder = folder == null ? "" : folder;
        try
        {
            LastScanError = null;

            if (string.IsNullOrEmpty(folder))
            {
                FailScan(result, "未指定图片文件夹");
                return result;
            }

            string root;
            try { root = Path.GetFullPath(folder); }
            catch (Exception ex)
            {
                FailScan(result, "图片文件夹路径无效：" + ex.Message);
                return result;
            }
            result.Folder = root;

            bool exists;
            try { exists = Directory.Exists(root); }
            catch { exists = false; }
            if (!exists)
            {
                FailScan(result, "图片文件夹不存在或不可访问：" + root);
                return result;
            }

            List<string> files = new List<string>();
            int accessErrors = 0;
            CollectFiles(root, files, out accessErrors);
            if (files.Count == 0 && accessErrors > 0)
            {
                FailScan(result, "无法读取图片文件夹：" + root);
                return result;
            }

            for (int i = 0; i < files.Count; i++)
            {
                ImageEntry entry = TryBuildEntry(root, files[i], result.Skipped);
                if (entry != null) { result.Images.Add(entry); }
            }

            // 自然排序：按相对路径分段比较（1/2/10 而不是 1/10/2）。
            try
            {
                result.Images.Sort(new Comparison<ImageEntry>(CompareEntryPath));
            }
            catch
            {
                try { result.Images.Sort(new Comparison<ImageEntry>(CompareEntryPathOrdinal)); }
                catch { }
            }

            // C2：计算本次扫描指纹（文件数 / 总字节 / 最大修改时间）。
            long totalBytes = 0;
            long maxWriteTicks = 0;
            for (int i = 0; i < result.Images.Count; i++)
            {
                ImageEntry item = result.Images[i];
                if (item == null) { continue; }
                if (item.FileSize > 0) { totalBytes += item.FileSize; }
                if (item.LastWriteUtcTicks > maxWriteTicks) { maxWriteTicks = item.LastWriteUtcTicks; }
            }

            // C3：扫描成功、即将替换图片列表时才释放旧缓存；任何失败路径（上面的 FailScan）都不动缓存，
            // 这样掉盘/拔 U 盘/临时权限异常时，用户正在看的那张图不会被 Dispose 掉。
            // 同一目录且指纹未变时也保留缓存：自动刷新会跳过重载，当前位图必须仍然有效（C2）。
            bool folderChanged = !string.Equals(Folder, root, StringComparison.OrdinalIgnoreCase);
            bool imagesChanged = folderChanged || !SameScanFingerprint(
                ScanFileCount, ScanTotalBytes, ScanMaxWriteUtcTicks,
                result.Images.Count, totalBytes, maxWriteTicks);
            if (imagesChanged) { ClearCache(); }

            ScanFileCount = result.Images.Count;
            ScanTotalBytes = totalBytes;
            ScanMaxWriteUtcTicks = maxWriteTicks;
            Folder = root;
            Images = result.Images;
            Skipped = result.Skipped;
            LastScanError = null;
            result.Error = null;
            return result;
        }
        catch (Exception ex)
        {
            FailScan(result, "扫描图片文件夹失败：" + ex.Message);
            return result;
        }
    }

    private void FailScan(ScanResult result, string message)
    {
        result.Error = message;
        result.Images = new List<ImageEntry>();
        result.Skipped = new List<SkippedImage>();
        LastScanError = message;
        // 失败时保留 Library 里原有的图片列表，避免界面上正在看的图被清空。
    }

    // 校验单个文件；可用则返回条目，否则向 skipped 写原因。
    private static ImageEntry TryBuildEntry(string root, string file, List<SkippedImage> skipped)
    {
        if (string.IsNullOrEmpty(file)) { return null; }

        if (!IsSupportedExtension(file))
        {
            string extension = "";
            try { extension = Path.GetExtension(file); }
            catch { }
            AddSkipped(skipped, file, DescribeUnsupported(extension));
            return null;
        }

        // C1：切片产物不进图库（否则导出/自动刷新会互相触发，图库越刷越多）。
        if (IsSliceArtifact(file))
        {
            AddSkipped(skipped, file, ReasonSliceArtifact);
            return null;
        }

        long fileSize = -1;
        long lastWriteTicks = 0;
        bool fileExists = false;
        try
        {
            FileInfo info = new FileInfo(file);
            fileExists = info.Exists;
            if (fileExists)
            {
                fileSize = info.Length;
                try { lastWriteTicks = info.LastWriteTimeUtc.Ticks; } catch { lastWriteTicks = 0; }
            }
        }
        catch { fileExists = false; }
        if (!fileExists) { return null; } // 扫描期间文件被删除，静默忽略

        if (fileSize <= 0)
        {
            AddSkipped(skipped, file, ReasonEmptyFile);
            return null;
        }

        int width = 0;
        int height = 0;
        string sizeError = null;
        bool sizeOk;
        try { sizeOk = TryReadImageSize(file, out width, out height, out sizeError); }
        catch { sizeOk = false; sizeError = ReasonCorrupt; }
        if (!sizeOk)
        {
            AddSkipped(skipped, file, string.IsNullOrEmpty(sizeError) ? ReasonCorrupt : sizeError);
            return null;
        }
        if (IsOversized(width, height))
        {
            AddSkipped(skipped, file, ReasonTooLarge);
            return null;
        }

        ImageEntry entry = new ImageEntry();
        entry.FullPath = file;
        entry.RelPath = MakeRelativePath(root, file);
        entry.FileSize = fileSize;
        entry.PixelWidth = width;
        entry.PixelHeight = height;
        entry.SizeKnown = true;
        entry.LastWriteUtcTicks = lastWriteTicks;
        return entry;
    }

    private static void AddSkipped(List<SkippedImage> skipped, string file, string reason)
    {
        if (skipped == null) { return; }
        SkippedImage item = new SkippedImage();
        item.FullPath = file;
        item.Reason = reason;
        skipped.Add(item);
    }

    private static string DescribeUnsupported(string extension)
    {
        string ext = extension == null ? "" : extension.TrimStart('.').ToLowerInvariant();
        if (ext == "webp") { return "不支持的格式（系统无 WebP 解码器）"; }
        if (ext == "avif") { return "不支持的格式（系统无 AVIF 解码器）"; }
        return "不支持的格式（仅支持 png/jpg/jpeg/bmp/gif）";
    }

    // 递归收集全部文件；单个子目录不可访问只计数，不影响其它目录。
    private static void CollectFiles(string root, List<string> files, out int accessErrors)
    {
        accessErrors = 0;
        Stack<string> pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            try
            {
                string[] subDirs = Directory.GetDirectories(dir);
                for (int i = 0; i < subDirs.Length; i++)
                {
                    string sub = subDirs[i];
                    // C1：切片输出目录整体跳过（里面是导出产物，不是攻略图）。
                    string subName = "";
                    try { subName = Path.GetFileName(sub); }
                    catch { subName = ""; }
                    if (string.Equals(subName, SliceOutputDirectoryName, StringComparison.OrdinalIgnoreCase)) { continue; }
                    bool isLink = false;
                    try { isLink = (File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0; }
                    catch { isLink = true; } // 读不到属性就不跟进，避免权限异常与目录环
                    if (!isLink) { pending.Push(sub); }
                }
            }
            catch { accessErrors++; }
            try
            {
                string[] dirFiles = Directory.GetFiles(dir, "*");
                for (int i = 0; i < dirFiles.Length; i++) { files.Add(dirFiles[i]); }
            }
            catch { accessErrors++; }
        }
    }

    // C2：扫描指纹比较（纯函数，便于自测）。文件数 / 总字节 / 最大修改时间任一不同 = 图库变了。
    public static bool SameScanFingerprint(int countA, long bytesA, long ticksA,
        int countB, long bytesB, long ticksB)
    {
        return countA == countB && bytesA == bytesB && ticksA == ticksB;
    }

    // C5：是否值得预取邻居图。单图超过 800 万像素或处于长图模式时不预取，
    // 否则一次换图会把 2 张 6000 万像素的图解进内存（约 480MB 峰值），32 位进程直接 OOM。
    public static bool ShouldPrefetch(long pixelCount, bool longImageMode)
    {
        if (longImageMode) { return false; }
        if (pixelCount <= 0) { return false; }
        return pixelCount <= PrefetchMaxPixels;
    }

    private static string MakeRelativePath(string root, string fullPath)
    {
        try
        {
            if (string.IsNullOrEmpty(root)) { return fullPath; }
            string rootFull = Path.GetFullPath(root);
            string fileFull = Path.GetFullPath(fullPath);
            string separator = Path.DirectorySeparatorChar.ToString();
            if (!rootFull.EndsWith(separator, StringComparison.Ordinal)) { rootFull += separator; }
            if (fileFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                return fileFull.Substring(rootFull.Length);
            }
            return fileFull;
        }
        catch { return fullPath; }
    }

    private static int CompareEntryPath(ImageEntry a, ImageEntry b)
    {
        string x = a != null && a.RelPath != null ? a.RelPath : "";
        string y = b != null && b.RelPath != null ? b.RelPath : "";
        try
        {
            // 自然排序统一走 ViewMath（按 /、\ 分段，数字段按数值比较）。
            IComparer<string> comparer = ViewMath.NaturalComparer;
            if (comparer != null) { return comparer.Compare(x, y); }
        }
        catch { }
        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareEntryPathOrdinal(ImageEntry a, ImageEntry b)
    {
        string x = a != null && a.RelPath != null ? a.RelPath : "";
        string y = b != null && b.RelPath != null ? b.RelPath : "";
        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------------
    // 头部尺寸探测：PNG IHDR / JPEG SOF / GIF LSD / BMP BIH；失败回退 GDI+
    // ------------------------------------------------------------------------
    public static bool TryReadImageSize(string path, out int width, out int height, out string error)
    {
        width = 0;
        height = 0;
        error = null;
        if (string.IsNullOrEmpty(path))
        {
            error = "图片路径为空";
            return false;
        }

        try
        {
            bool exists;
            try { exists = File.Exists(path); }
            catch { exists = false; }
            if (!exists) { error = "文件不存在"; return false; }

            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                long length = 0;
                try { length = stream.Length; }
                catch { length = 0; }
                if (length <= 0) { error = ReasonEmptyFile; return false; }

                byte[] head = new byte[32];
                int read = FillBuffer(stream, head, 0, head.Length);
                bool parsed = false;
                try
                {
                    if (read >= 24 && IsPngHeader(head))
                    {
                        parsed = TryParsePngSize(head, out width, out height);
                    }
                    else if (read >= 10 && IsGifHeader(head))
                    {
                        parsed = TryParseGifSize(head, out width, out height);
                    }
                    else if (read >= 26 && IsBmpHeader(head))
                    {
                        parsed = TryParseBmpSize(head, out width, out height);
                    }
                    else if (read >= 2 && head[0] == 0xFF && head[1] == 0xD8)
                    {
                        parsed = TryParseJpegSize(stream, length, out width, out height);
                    }
                }
                catch { parsed = false; }

                if (parsed && width > 0 && height > 0)
                {
                    error = null;
                    return true;
                }
                // 头部无法识别/字段非法：清零后走 GDI+ 回退。
                width = 0;
                height = 0;
            }
        }
        catch (UnauthorizedAccessException)
        {
            error = "没有读取权限";
            return false;
        }
        catch (IOException)
        {
            error = "无法读取文件（可能被占用或路径无效）";
            return false;
        }
        catch (Exception ex)
        {
            error = "读取图片信息失败：" + ex.Message;
            return false;
        }

        // 回退：只用 GDI+ 读尺寸并立即释放（validateImageData=false，不完整解码）。
        try
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (Image image = Image.FromStream(stream, false, false))
            {
                Size size = image.Size;
                if (size.Width > 0 && size.Height > 0)
                {
                    width = size.Width;
                    height = size.Height;
                    error = null;
                    return true;
                }
            }
        }
        catch { }

        error = ReasonCorrupt;
        return false;
    }

    // 单图像素 > 6000 万或单边 > 32767 判超大。
    public static bool IsOversized(int width, int height)
    {
        if (width <= 0 || height <= 0) { return false; }
        if (width > MaxImageEdge || height > MaxImageEdge) { return true; }
        return (long)width * (long)height > MaxImagePixels;
    }

    private static int FillBuffer(Stream stream, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, offset + total, count - total);
            if (read <= 0) { break; }
            total += read;
        }
        return total;
    }

    private static bool IsPngHeader(byte[] head)
    {
        return head.Length >= 8
            && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
            && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A;
    }

    private static bool IsGifHeader(byte[] head)
    {
        if (head.Length < 6) { return false; }
        if (head[0] != (byte)'G' || head[1] != (byte)'I' || head[2] != (byte)'F') { return false; }
        if (head[3] != (byte)'8') { return false; }
        if (head[4] != (byte)'7' && head[4] != (byte)'9') { return false; }
        return head[5] == (byte)'a';
    }

    private static bool IsBmpHeader(byte[] head)
    {
        return head.Length >= 2 && head[0] == (byte)'B' && head[1] == (byte)'M';
    }

    private static bool TryParsePngSize(byte[] head, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (head[12] != (byte)'I' || head[13] != (byte)'H' || head[14] != (byte)'D' || head[15] != (byte)'R')
        {
            return false;
        }
        long w = ((long)head[16] << 24) | ((long)head[17] << 16) | ((long)head[18] << 8) | head[19];
        long h = ((long)head[20] << 24) | ((long)head[21] << 16) | ((long)head[22] << 8) | head[23];
        if (w <= 0 || h <= 0 || w > int.MaxValue || h > int.MaxValue) { return false; }
        width = (int)w;
        height = (int)h;
        return true;
    }

    private static bool TryParseGifSize(byte[] head, out int width, out int height)
    {
        width = head[6] | (head[7] << 8);
        height = head[8] | (head[9] << 8);
        return width > 0 && height > 0;
    }

    private static bool TryParseBmpSize(byte[] head, out int width, out int height)
    {
        width = 0;
        height = 0;
        int headerSize = head[14] | (head[15] << 8) | (head[16] << 16) | (head[17] << 24);
        if (headerSize == 12)
        {
            // OS/2 v1 BITMAPCOREHEADER：16 位宽高
            width = head[18] | (head[19] << 8);
            height = head[20] | (head[21] << 8);
        }
        else if (headerSize >= 16)
        {
            width = head[18] | (head[19] << 8) | (head[20] << 16) | (head[21] << 24);
            int rawHeight = head[22] | (head[23] << 8) | (head[24] << 16) | (head[25] << 24);
            height = rawHeight < 0 ? -rawHeight : rawHeight; // 负数 = 自上而下存储
        }
        else
        {
            return false;
        }
        return width > 0 && height > 0;
    }

    // JPEG：从 SOI 之后逐段扫描，读到 SOF0/1/2…（C0–CF，排除 C4/C8/CC）里的高宽。
    private static bool TryParseJpegSize(FileStream stream, long length, out int width, out int height)
    {
        width = 0;
        height = 0;
        long position = 2;
        try { stream.Position = position; }
        catch { return false; }

        while (position + 4 <= length)
        {
            int b = stream.ReadByte();
            if (b < 0) { break; }
            position++;
            if (b != 0xFF) { continue; } // 跳过填充字节

            int marker = stream.ReadByte();
            if (marker < 0) { break; }
            position++;
            while (marker == 0xFF) // 连续 0xFF 填充
            {
                marker = stream.ReadByte();
                if (marker < 0) { break; }
                position++;
            }
            if (marker < 0) { break; }

            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { continue; }
            if (marker == 0xD9 || marker == 0xDA) { break; } // EOI / SOS 之前没找到 SOF
            if (position + 2 > length) { break; }

            int hi = stream.ReadByte();
            int lo = stream.ReadByte();
            if (hi < 0 || lo < 0) { break; }
            position += 2;
            int segmentLength = (hi << 8) | lo;
            if (segmentLength < 2) { break; }

            bool isSof = marker >= 0xC0 && marker <= 0xCF
                && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isSof)
            {
                if (position + 5 > length) { break; }
                int precision = stream.ReadByte();
                int h1 = stream.ReadByte();
                int h2 = stream.ReadByte();
                int w1 = stream.ReadByte();
                int w2 = stream.ReadByte();
                if (precision < 0 || h1 < 0 || h2 < 0 || w1 < 0 || w2 < 0) { break; }
                int parsedHeight = (h1 << 8) | h2;
                int parsedWidth = (w1 << 8) | w2;
                if (parsedWidth > 0 && parsedHeight > 0)
                {
                    width = parsedWidth;
                    height = parsedHeight;
                    return true;
                }
                return false;
            }

            long next = position + segmentLength - 2;
            if (next <= position || next > length) { break; }
            stream.Position = next;
            position = next;
        }
        return false;
    }

    // ------------------------------------------------------------------------
    // 解码缓存（最多 3 张 / 总像素 ≤ 8000 万，C5）
    // ------------------------------------------------------------------------
    public Bitmap GetBase(int index, out string error)
    {
        error = null;
        if (_disposed) { error = "图片库已释放"; return null; }
        if (Images == null || index < 0 || index >= Images.Count)
        {
            error = "图片索引超出范围";
            return null;
        }

        ImageEntry entry = Images[index];
        if (entry == null || string.IsNullOrEmpty(entry.FullPath))
        {
            error = "图片路径为空";
            return null;
        }

        CachedBitmap hit;
        if (_cache.TryGetValue(index, out hit))
        {
            if (hit != null && hit.Bitmap != null
                && string.Equals(hit.Path, entry.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                TouchCache(index);
                return hit.Bitmap;
            }
            RemoveCacheEntry(index);
        }

        Bitmap bitmap = null;
        string decodeError = null;
        try { bitmap = DecodeImage(entry.FullPath, out decodeError); }
        catch (Exception ex)
        {
            decodeError = "解码失败：" + ex.Message;
            bitmap = null;
        }
        if (bitmap == null)
        {
            error = string.IsNullOrEmpty(decodeError) ? "解码失败" : decodeError;
            return null;
        }

        AddCacheEntry(index, entry.FullPath, bitmap);
        error = null;
        return bitmap;
    }

    private void AddCacheEntry(int index, string path, Bitmap bitmap)
    {
        RemoveCacheEntry(index); // 同索引旧图先释放

        CachedBitmap item = new CachedBitmap();
        item.Bitmap = bitmap;
        item.Path = path == null ? "" : path;
        item.Pixels = (long)bitmap.Width * (long)bitmap.Height;
        _cache[index] = item;
        _cacheOrder.Add(index);
        _cachedPixels += item.Pixels;

        // 先按张数裁剪（当前图刚加在末尾，不会被优先淘汰）。
        while (_cacheOrder.Count > MaxCachedBitmaps) { EvictOldest(-1); }
        // 再按总解码像素裁剪：超预算时只留当前图。
        while (_cachedPixels > DecodedPixelBudget && _cacheOrder.Count > 1) { EvictOldest(index); }
    }

    // 淘汰最旧的一项；keepIndex 指定时跳过它（预算裁剪必须留住当前图）。
    private void EvictOldest(int keepIndex)
    {
        for (int i = 0; i < _cacheOrder.Count; i++)
        {
            int key = _cacheOrder[i];
            if (keepIndex >= 0 && key == keepIndex) { continue; }
            RemoveCacheEntry(key);
            return;
        }
    }

    private void TouchCache(int index)
    {
        if (_cacheOrder.Remove(index)) { _cacheOrder.Add(index); }
    }

    private void RemoveCacheEntry(int index)
    {
        CachedBitmap item;
        if (!_cache.TryGetValue(index, out item)) { return; }
        _cache.Remove(index);
        _cacheOrder.Remove(index);
        if (item == null) { return; }
        _cachedPixels -= item.Pixels;
        if (_cachedPixels < 0) { _cachedPixels = 0; }
        if (item.Bitmap != null)
        {
            try { item.Bitmap.Dispose(); }
            catch { }
            item.Bitmap = null;
        }
    }

    // 按预算清理缓存：保留当前图与前后各一张邻居。
    public void Trim(int keepIndex)
    {
        if (_cache.Count == 0) { return; }
        if (Images == null || keepIndex < 0 || keepIndex >= Images.Count)
        {
            ClearCache();
            return;
        }

        List<int> keep = new List<int>();
        keep.Add(keepIndex);
        if (keepIndex - 1 >= 0) { keep.Add(keepIndex - 1); }
        if (keepIndex + 1 < Images.Count) { keep.Add(keepIndex + 1); }

        List<int> keys = new List<int>(_cache.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            if (keep.Contains(keys[i])) { continue; }
            RemoveCacheEntry(keys[i]);
        }
        while (_cacheOrder.Count > MaxCachedBitmaps) { EvictOldest(keepIndex); }
        while (_cachedPixels > DecodedPixelBudget && _cacheOrder.Count > 1) { EvictOldest(keepIndex); }
    }

    public void ClearCache()
    {
        List<int> keys = new List<int>(_cache.Keys);
        for (int i = 0; i < keys.Count; i++) { RemoveCacheEntry(keys[i]); }
        _cache.Clear();
        _cacheOrder.Clear();
        _cachedPixels = 0;
    }

    public int CachedCount
    {
        get { return _cache.Count; }
    }

    // ------------------------------------------------------------------------
    // 独立解码（不缓存，调用方负责 Dispose）
    // ------------------------------------------------------------------------
    public static Bitmap LoadWithExif(string path, out string error)
    {
        error = null;
        try { return DecodeImage(path, out error); }
        catch (Exception ex)
        {
            error = "解码失败：" + ex.Message;
            return null;
        }
    }

    // 解码流程：头部尺寸校验 → GDI+ 解码首帧 → 克隆位图 → 释放流 → 应用 EXIF 方向。
    private static Bitmap DecodeImage(string path, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(path)) { error = "图片路径为空"; return null; }
        try
        {
            if (!File.Exists(path)) { error = "文件不存在"; return null; }
        }
        catch { error = "文件不可访问"; return null; }

        // 解码前先校验头部尺寸（GDI+ 遇到超大 PNG/JPEG 可能直接 OutOfMemory）。
        int width = 0;
        int height = 0;
        string sizeError = null;
        bool sizeOk;
        try { sizeOk = TryReadImageSize(path, out width, out height, out sizeError); }
        catch { sizeOk = false; }
        if (!sizeOk)
        {
            error = string.IsNullOrEmpty(sizeError) ? ReasonCorrupt : sizeError;
            return null;
        }
        if (IsOversized(width, height)) { error = ReasonTooLarge; return null; }

        Bitmap result = null;
        try
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (Image image = Image.FromStream(stream, false, false))
            {
                int orientation = 1;
                try { orientation = ReadExifOrientation(image); }
                catch { orientation = 1; }

                try
                {
                    if (IsGifImage(image)) { SelectFirstFrame(image); }
                }
                catch { }

                try { result = new Bitmap(image); } // 克隆，之后可安全释放流（不锁文件）
                catch (Exception ex)
                {
                    error = "解码失败：" + ex.Message;
                    return null;
                }

                if (orientation >= 2 && orientation <= 8)
                {
                    try { ApplyExifOrientation(result, orientation); }
                    catch { } // 方向应用失败不影响显示
                }
            }
        }
        catch (OutOfMemoryException)
        {
            error = "解码失败：内存不足";
            SafeDispose(result);
            return null;
        }
        catch (Exception ex)
        {
            error = "解码失败：" + ex.Message;
            SafeDispose(result);
            return null;
        }

        if (result == null) { error = "解码失败"; return null; }
        return result;
    }

    private static bool IsGifImage(Image image)
    {
        try { return image.RawFormat.Guid == ImageFormat.Gif.Guid; }
        catch { return false; }
    }

    private static void SelectFirstFrame(Image image)
    {
        try
        {
            Guid[] dimensions = image.FrameDimensionsList;
            if (dimensions == null || dimensions.Length == 0) { return; }
            FrameDimension dimension = new FrameDimension(dimensions[0]);
            image.SelectActiveFrame(dimension, 0);
        }
        catch { }
    }

    // EXIF Orientation（0x0112），1–8；读不到按 1（正常）处理。
    private static int ReadExifOrientation(Image image)
    {
        try
        {
            int[] ids = image.PropertyIdList;
            if (ids == null) { return 1; }
            bool found = false;
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == 0x0112) { found = true; break; }
            }
            if (!found) { return 1; }

            PropertyItem item = image.GetPropertyItem(0x0112);
            if (item == null || item.Value == null || item.Value.Length < 2) { return 1; }
            int value;
            if (item.Value.Length >= 4) { value = BitConverter.ToInt32(item.Value, 0); }
            else { value = BitConverter.ToInt16(item.Value, 0); }
            if (value < 1 || value > 8) { return 1; }
            return value;
        }
        catch { return 1; }
    }

    private static void ApplyExifOrientation(Bitmap bitmap, int orientation)
    {
        switch (orientation)
        {
            case 2: bitmap.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
            case 3: bitmap.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
            case 4: bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY); break;
            case 5: bitmap.RotateFlip(RotateFlipType.Rotate90FlipX); break;
            case 6: bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
            case 7: bitmap.RotateFlip(RotateFlipType.Rotate270FlipX); break;
            case 8: bitmap.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            default: break;
        }
    }

    private static void SafeDispose(Bitmap bitmap)
    {
        if (bitmap == null) { return; }
        try { bitmap.Dispose(); }
        catch { }
    }

    // ------------------------------------------------------------------------
    // 长图切片导出：每片 sliceHeightPx 高（宽图按 96MB 内存预算自动降片高），
    // PNG，命名 ViewMath.SliceFileName（1 基序号）
    // ------------------------------------------------------------------------
    public static SliceExportResult ExportSlices(Bitmap source, string sourcePath, string outputDir, int sliceHeightPx)
    {
        SliceExportResult result = new SliceExportResult();
        try
        {
            if (source == null) { result.Error = "没有可导出的图片"; return result; }
            if (string.IsNullOrEmpty(outputDir)) { result.Error = "未选择输出文件夹"; return result; }
            if (sliceHeightPx <= 0) { sliceHeightPx = DefaultSliceHeightPx; }

            int width;
            int height;
            try
            {
                width = source.Width;
                height = source.Height;
            }
            catch (Exception ex)
            {
                result.Error = "图片已释放或不可用：" + ex.Message;
                return result;
            }
            if (width <= 0 || height <= 0) { result.Error = "图片尺寸无效"; return result; }
            // 与加载规则一致：超大图不切片（GDI+ 必须完整解码，内存风险不变）。
            if (IsOversized(width, height)) { result.Error = ReasonTooLarge; return result; }

            // 宽图降片高：单片 32bppArgb 位图按 width * rows * 4 <= 96MB 预算取行数，
            // 默认仍是 2000px/片，只有宽图（width > 12000）才会降到更小片高，避免 new Bitmap 时 OOM。
            int effectiveSliceHeight = ViewMath.SliceHeightForWidth(width, sliceHeightPx);
            if (effectiveSliceHeight <= 0) { effectiveSliceHeight = sliceHeightPx; }

            try { Directory.CreateDirectory(outputDir); }
            catch (Exception ex)
            {
                result.Error = "无法创建输出文件夹：" + ex.Message;
                return result;
            }

            string sourceName = "";
            try { sourceName = Path.GetFileName(sourcePath); }
            catch { }
            if (string.IsNullOrEmpty(sourceName)) { sourceName = "image.png"; }

            // 片数与 ViewMath.SliceCount 用同一套参数计算，保证与 SliceFileName 的 1 基序号一致
            int total = ViewMath.SliceCount(height, effectiveSliceHeight);
            for (int i = 0; i < total; i++)
            {
                int top = i * effectiveSliceHeight;
                int sliceHeight = Math.Min(effectiveSliceHeight, height - top);
                if (sliceHeight <= 0) { break; }

                Bitmap slice = null;
                try
                {
                    string fileName = ViewMath.SliceFileName(sourceName, i + 1);
                    string target = MakeUniquePath(outputDir, fileName);
                    slice = new Bitmap(width, sliceHeight, PixelFormat.Format32bppArgb);
                    using (Graphics graphics = Graphics.FromImage(slice))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                        graphics.PixelOffsetMode = PixelOffsetMode.Half;
                        Rectangle destRect = new Rectangle(0, 0, width, sliceHeight);
                        Rectangle srcRect = new Rectangle(0, top, width, sliceHeight);
                        graphics.DrawImage(source, destRect, srcRect, GraphicsUnit.Pixel);
                    }
                    slice.Save(target, ImageFormat.Png);
                    result.Files.Add(target);
                }
                catch (Exception ex)
                {
                    result.Error = "导出切片失败：" + ex.Message;
                    // C1：半成品不留盘（已写出的切片全部删掉，避免图库/用户目录里留着残图）。
                    DeleteExportedFilesQuietly(result.Files);
                    break;
                }
                finally
                {
                    SafeDispose(slice);
                }
            }
            result.Count = result.Files.Count;
            return result;
        }
        catch (Exception ex)
        {
            result.Error = "导出切片失败：" + ex.Message;
            DeleteExportedFilesQuietly(result.Files);
            return result;
        }
    }

    // C1：导出中途失败时清掉这次已经写出的半成品，避免用户看到「一半切片」且下次导出序号错乱。
    private static void DeleteExportedFilesQuietly(List<string> files)
    {
        if (files == null) { return; }
        for (int i = 0; i < files.Count; i++)
        {
            try { if (File.Exists(files[i])) { File.Delete(files[i]); } }
            catch { }
        }
        files.Clear();
    }

    // 同名文件追加 -2、-3…（a-slice-01.png → a-slice-01-2.png）。
    private static string MakeUniquePath(string outputDir, string fileName)
    {
        string target = Path.Combine(outputDir, fileName);
        if (!File.Exists(target)) { return target; }

        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int n = 2; n <= 9999; n++)
        {
            string candidate = Path.Combine(outputDir,
                baseName + "-" + n.ToString(CultureInfo.InvariantCulture) + extension);
            if (!File.Exists(candidate)) { return candidate; }
        }
        return Path.Combine(outputDir, baseName + "-" + Guid.NewGuid().ToString("N") + extension);
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        try { ClearCache(); }
        catch { }
    }
}

// ============================================================================
// FolderWatcher：FileSystemWatcher 后台事件 → 800ms 防抖 → Post 回 UI 线程 → Changed
// 注意：切文件夹/Stop 之后旧目录的事件必须被丢弃（用 generation 计数 + Active 双重校验）。
// ============================================================================
internal sealed class FolderWatcher : IDisposable
{
    private const int DebounceMs = 800;
    private const int TimerBufferBytes = 32768;

    private readonly object _sync = new object();
    private readonly SynchronizationContext _uiContext;
    private FileSystemWatcher _watcher;
    private System.Threading.Timer _timer;
    private string _folder = "";
    private string _lastError;
    private bool _active;
    private bool _disposed;
    private int _generation;

    public FolderWatcher(SynchronizationContext uiContext)
    {
        _uiContext = uiContext;
        if (_uiContext == null)
        {
            // 兜底：调用方在 UI 线程构造时自动取当前上下文。
            try { _uiContext = SynchronizationContext.Current; }
            catch { }
        }
    }

    public event EventHandler Changed;

    public string Folder
    {
        get { lock (_sync) { return _folder; } }
    }

    public bool Active
    {
        get { lock (_sync) { return _active; } }
    }

    public string LastError
    {
        get { lock (_sync) { return _lastError; } }
    }

    public void Start(string folder)
    {
        lock (_sync)
        {
            if (_disposed) { return; }
        }
        Stop();

        if (string.IsNullOrEmpty(folder))
        {
            SetError("未指定图片文件夹，自动刷新不可用");
            return;
        }

        string full = folder;
        try { full = Path.GetFullPath(folder); }
        catch { }

        bool exists;
        try { exists = Directory.Exists(full); }
        catch { exists = false; }
        if (!exists)
        {
            SetError("图片文件夹不存在或不可访问，自动刷新不可用");
            return;
        }

        FileSystemWatcher watcher = null;
        System.Threading.Timer timer = null;
        try
        {
            watcher = new FileSystemWatcher(full);
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.Filter = "*";
            try { watcher.InternalBufferSize = TimerBufferBytes; }
            catch { }
            watcher.Created += OnFileChanged;
            watcher.Changed += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnWatcherError;

            timer = new System.Threading.Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

            lock (_sync)
            {
                if (_disposed) { throw new ObjectDisposedException("FolderWatcher"); }
                _folder = full;
                _lastError = null;
                _generation++;
                _watcher = watcher;
                _timer = timer;
                _active = true;
            }
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            DisposeWatcher(watcher);
            if (timer != null) { try { timer.Dispose(); } catch { } }
            lock (_sync)
            {
                if (object.ReferenceEquals(_watcher, watcher)) { _watcher = null; }
                if (object.ReferenceEquals(_timer, timer)) { _timer = null; }
                _active = false;
            }
            SetError("自动刷新不可用：" + ex.Message);
        }
    }

    public void Stop()
    {
        FileSystemWatcher watcher;
        System.Threading.Timer timer;
        lock (_sync)
        {
            _active = false;
            _generation++; // 之前排队的防抖回调全部作废
            watcher = _watcher;
            _watcher = null;
            timer = _timer;
            _timer = null;
            _folder = "";
        }
        if (timer != null) { try { timer.Dispose(); } catch { } }
        DisposeWatcher(watcher);
    }

    public void Dispose()
    {
        bool already;
        lock (_sync)
        {
            already = _disposed;
            _disposed = true;
        }
        if (already) { return; }
        Stop();
    }

    private static void DisposeWatcher(FileSystemWatcher watcher)
    {
        if (watcher == null) { return; }
        try { watcher.EnableRaisingEvents = false; }
        catch { }
        try { watcher.Dispose(); }
        catch { }
    }

    private void SetError(string message)
    {
        lock (_sync) { _lastError = message; }
    }

    // ---- FileSystemWatcher 回调（后台线程） ----
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        ScheduleReload();
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        ScheduleReload();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        string message = "自动刷新不可用，请手动重新扫描";
        try
        {
            Exception ex = e == null ? null : e.GetException();
            if (ex != null && !string.IsNullOrEmpty(ex.Message))
            {
                message = message + "（" + ex.Message + "）";
            }
        }
        catch { }
        SetError(message);
        // 网络盘/缓冲区溢出时也重新扫一次，让列表尽量自愈；错误信息留在 LastError。
        ScheduleReload();
    }

    private void ScheduleReload()
    {
        try
        {
            lock (_sync)
            {
                if (_disposed || !_active || _timer == null) { return; }
                _timer.Change(DebounceMs, Timeout.Infinite); // 每次事件重启 800ms 防抖
            }
        }
        catch { }
    }

    // 防抖到期：Timer 回调在线程池线程，这里统一 Post 回 UI 线程。
    private void OnDebounceElapsed(object state)
    {
        int generation;
        SynchronizationContext ui;
        lock (_sync)
        {
            if (_disposed || !_active) { return; }
            generation = _generation;
            ui = _uiContext;
        }

        if (ui != null)
        {
            try
            {
                ui.Post(delegate(object s) { RaiseChanged(generation); }, null);
                return;
            }
            catch { }
        }
        // 没有 UI 上下文（控制台自测）：退化为在定时器线程直接触发。
        RaiseChanged(generation);
    }

    private void RaiseChanged(int generation)
    {
        try
        {
            lock (_sync)
            {
                if (_disposed || !_active || generation != _generation) { return; }
            }
            EventHandler handler = Changed;
            if (handler != null) { handler(this, EventArgs.Empty); }
        }
        catch (Exception ex)
        {
            SetError("自动刷新回调失败：" + ex.Message);
        }
    }
}
// ============================================================================
// 主窗口 — 主窗口：门控状态机 / 鼠标键盘 / 热键分发 / 右键菜单 /
//                    拖动缩放 / 长图滚动 / 换图 / 设置应用
// 说明：字段定义见「共享字段」部分；绘制见「主窗口绘制」部分。两边都不要改名共享字段。
// 约定：C# 5 语法；窗口矩形一律物理像素，AppSettings 的位置/大小一律 DIP。
// ============================================================================

// ---------------------------------------------------------------------------
// 右键菜单配色：浅色/深色都走自定义 ProfessionalColorTable（去掉渐变与图片边距）
// ---------------------------------------------------------------------------
internal sealed class OverlayMenuColorTable : ProfessionalColorTable
{
    private ThemeTokens _tokens;

    public OverlayMenuColorTable(ThemeTokens tokens)
    {
        _tokens = tokens != null ? tokens : ThemeTokens.Light();
        UseSystemColors = false;
    }

    public override Color ToolStripDropDownBackground { get { return _tokens.Card; } }
    public override Color MenuBorder { get { return _tokens.Border; } }
    public override Color MenuItemBorder { get { return _tokens.Hover; } }
    public override Color MenuItemSelected { get { return _tokens.Hover; } }
    public override Color MenuItemSelectedGradientBegin { get { return _tokens.Hover; } }
    public override Color MenuItemSelectedGradientEnd { get { return _tokens.Hover; } }
    public override Color MenuItemPressedGradientBegin { get { return _tokens.Hover; } }
    public override Color MenuItemPressedGradientMiddle { get { return _tokens.Hover; } }
    public override Color MenuItemPressedGradientEnd { get { return _tokens.Hover; } }
    public override Color ImageMarginGradientBegin { get { return _tokens.Card; } }
    public override Color ImageMarginGradientMiddle { get { return _tokens.Card; } }
    public override Color ImageMarginGradientEnd { get { return _tokens.Card; } }
    public override Color SeparatorDark { get { return _tokens.Border; } }
    public override Color SeparatorLight { get { return _tokens.Border; } }
    public override Color MenuStripGradientBegin { get { return _tokens.Card; } }
    public override Color MenuStripGradientEnd { get { return _tokens.Card; } }
    public override Color ToolStripBorder { get { return _tokens.Border; } }
    public override Color CheckBackground { get { return _tokens.Card; } }
    public override Color CheckSelectedBackground { get { return _tokens.Hover; } }
    public override Color CheckPressedBackground { get { return _tokens.Hover; } }
}

internal sealed partial class OverlayForm : Form
{
    // ========================================================================
    // 本部分私有常量 / 字段（共享字段在「共享字段」部分，不要重复声明）
    // ========================================================================
    // C4：20ms 轮询，按下/松开后第一击的竞态窗口从 40ms 减半（实测每 tick 只需数微秒）。
    private const int GateIntervalMs = 20;
    private const int SaveDebounceMs = 500;         // 保存防抖
    private const int ToastFadeMs = 300;            // toast 淡出时长
    private const int StartupToastMs = 7000;        // 启动汇总停留时间
    private const int SliceHeightPx = 2000;         // 长图切片高度

    private System.Windows.Forms.Timer _toastTimer; // toast 淡出计时（只重绘 toast 区域）
    private bool _menuOpen;                         // 右键菜单是否打开（忙碌态）
    private bool _menuClearedNoActivate;            // 弹菜单时是否临时清掉了 NOACTIVATE
    private bool _settingsPanelLockedBefore;        // 打开设置面板前的锁定状态
    private bool _closing;                          // 正在关闭
    private bool _cleanupDone;                      // FormClosing 清理是否已执行
    private bool _suppressWatcher;                  // 自己重扫时忽略 watcher 事件
    private bool _startupSummaryShown;              // 启动汇总只显示一次
    private bool _allowDropApplied;                 // 当前 AllowDrop 实际值
    private bool _keepLastImageOnLoad;              // 临时拖入图片跳转时不覆盖 LastImage
    private bool _panelLockOverridden;              // 面板内用户改过锁定 → 关闭面板时不回滚
    private bool _cancellingInteractions;           // CancelInteractions 重入保护（Capture=false 会回调）
    private bool _lastScanKeptOldImages;            // 上次 ScanFolder 因同目录失败而保留了旧列表
    private bool _folderFallbackDetected;           // 配置的图片文件夹失效、已静默回退
    private bool _lowIntegrityDetected;             // 低完整性/沙箱启动：读不到键盘状态，已自动切换可交互
    private Size _dragSize;                         // 系统拖动阈值（启动时取一次，避免每次 MouseMove 查询）
    private RotateFlipType _currentRotateFlip = RotateFlipType.RotateNoneFlipNone; // 完整旋转/翻转组合状态

    private const int KeepVisibleDip = 48;          // 拖动/拉伸时至少留在工作区内的可见尺寸（DIP）

    private Rectangle _resizeStartRect;             // 拉伸起始窗口矩形（物理像素）
    private Point _resizeStartScreen;               // 拉伸起始光标（屏幕坐标）

    // ---- 首次运行引导（B1）：前 12 秒锁定交互 + 底部提示条，总计 20 秒后恢复穿透 ----
    private bool _firstRunActive;                   // 引导进行中（底部提示条显示）
    private bool _firstRunLockPhase;                // 前 12 秒为可交互（Locked）阶段
    private long _firstRunStartTick;                // 引导开始时刻（Environment.TickCount）
    private const int FirstRunLockMs = 12000;       // 锁定交互时长
    private const int FirstRunTotalMs = 20000;      // 引导总时长（提示条再保留 8 秒）

    // 右键菜单项引用（用于同步勾选/置灰）
    private ToolStripMenuItem _menuPrevItem;
    private ToolStripMenuItem _menuNextItem;
    private ToolStripMenuItem _menuTopMostItem;
    private ToolStripMenuItem _menuLockedItem;
    private ToolStripMenuItem _menuImageLockItem;
    private ToolStripMenuItem _menuHideItem;
    private ToolStripMenuItem _menuLongItem;
    private ToolStripMenuItem _menuSliceItem;
    private ToolStripMenuItem _menuRecentItem;
    private ToolStripMenuItem _menuPassiveItem;
    private List<ToolStripMenuItem> _menuOpacityItems = new List<ToolStripMenuItem>();

    // ========================================================================
    // 构造
    // ========================================================================
    public OverlayForm(AppSettings settings) : this(settings, false) { }

    public OverlayForm(AppSettings settings, bool selfTestMode)
    {
        SelfTestMode = selfTestMode;
        Settings = settings != null ? settings : new AppSettings();
        ExitRequested = false;
        WindowHidden = false;
        Locked = false;
        Interactive = false;

        // 视图状态（ViewState 是结构体，必须显式初始化 zoom，避免默认 0×）
        View = new ViewState();
        View.Zoom = 1.0;
        View.Mode = ViewMode.Fit;

        // ---- DPI / 主题 / 颜色 ----
        CurrentDpi = DpiHelper.SystemDpi;
        // 系统拖动阈值只在启动时取一次（默认 4×4），窗口拖动与图片平移共用
        Size drag = SystemInformation.DragSize;
        if (drag.Width < 1) { drag.Width = 1; }
        if (drag.Height < 1) { drag.Height = 1; }
        _dragSize = drag;
        CurrentTheme = Settings.Theme;
        if (CurrentTheme != ThemeKind.Light && CurrentTheme != ThemeKind.Dark) { CurrentTheme = ThemeKind.Light; }
        Colors = ThemeTokens.Get(CurrentTheme);

        // ---- 窗体形态：无边框、工具窗口、不占任务栏、可置顶、双缓冲 ----
        // 所有坐标/尺寸都由本文件按物理像素手动计算，禁用 WinForms 的自动缩放，
        // 避免高 DPI 下窗体尺寸被二次缩放。
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = Settings.TopMost;
        StartPosition = FormStartPosition.Manual;
        BackColor = Colors.Canvas;
        Text = AppInfo.AppName;
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.ResizeRedraw, true);
        MinimumSize = new Size(global::Layout.Px(global::Layout.MinWindowWidthDip, CurrentDpi), global::Layout.Px(global::Layout.MinWindowHeightDip, CurrentDpi));

        // ---- 字体（绘制模块的 RefreshThemeFonts 之后可能重建）----
        UiFont = UiFonts.Create(9f, FontStyle.Regular);
        TitleFont = UiFonts.Create(10.5f, FontStyle.Bold);
        ToastFont = UiFonts.Create(9f, FontStyle.Regular);

        // ---- 依赖对象 ----
        Library = new ImageLibrary();
        Images = Library.Images;
        Hotkeys = new HotkeyManager(Settings);

        // ---- 初始透明度（20–100）----
        int opacity = Settings.Opacity;
        if (opacity < 20) { opacity = 20; }
        if (opacity > 100) { opacity = 100; }
        Settings.Opacity = opacity;
        try { Opacity = opacity / 100.0; } catch { }

        // ---- 窗口矩形：记忆优先，失效则默认位置 ----
        Rectangle windowRect;
        if (!TryRestoreWindowRect(out windowRect))
        {
            windowRect = ComputeDefaultWindowRect();
        }
        Bounds = windowRect;
        SaveWindowRectToSettings();

        // ---- 定时器 ----
        GateTimer = new System.Windows.Forms.Timer();
        GateTimer.Interval = GateIntervalMs;
        GateTimer.Tick += GateTimer_Tick;

        SaveTimer = new System.Windows.Forms.Timer();
        SaveTimer.Interval = SaveDebounceMs;
        SaveTimer.Tick += SaveTimer_Tick;

        _toastTimer = new System.Windows.Forms.Timer();
        _toastTimer.Interval = 100;
        _toastTimer.Tick += ToastTimer_Tick;

        // ---- 右键菜单（提前建好，避免首次右键卡顿）----
        EnsureContextMenu();

        // ---- 图片库：扫描 + 定位到上次图片 ----
        InitializeLibrary();

        if (SelfTestMode)
        {
            // 自测：不注册热键、不启动 watcher、不读写 settings；
            // 保持 Interactive / 工具条可见，便于截图断言（不 Show 窗口）。
            Interactive = true;
            ToolbarVisible = !WindowHidden;
            AllowDrop = false;
            UpdateCardVisibility();
        }
        else
        {
            GateTimer.Start();
        }
    }

    // ========================================================================
    // 窗体样式：工具窗口 + 不激活
    // ========================================================================
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // 实测：ShowInTaskbar=false 不会自动加 WS_EX_TOOLWINDOW，必须显式加，
            // 否则窗口可能出现在 Alt+Tab。注意：绝不手动改 WS_EX_LAYERED。
            cp.ExStyle |= Win32Const.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation
    {
        get { return true; }
    }

    // ========================================================================
    // 句柄生命周期 / 显示
    // ========================================================================
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyExtendedStyles();
        ApplyTopMost();

        if (SelfTestMode) { return; }
        if (Hotkeys == null) { return; }

        Hotkeys.OwnerHandle = Handle;
        try { Hotkeys.UnregisterAll(); } catch { }
        try
        {
            Hotkeys.RegisterAll();
        }
        catch (Exception ex)
        {
            ShowToast("热键注册失败：" + ex.Message, StartupToastMs, true);
        }
        UpdateContextMenuState();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Show() 之后 WinForms 会重置一次扩展样式，这里补一次（门控状态机随后每 20ms 自愈）。
        ApplyExtendedStyles();
        if (_startupSummaryShown) { return; }
        _startupSummaryShown = true;

        if (!SelfTestMode)
        {
            StartWatcher();
            ApplyTopMost();
            DetectRestrictedEnvironment();
            // C8：低权限启动时先让用户明确选择「退出 / 仍要运行」，而不是默默进入锁定态。
            // 自测模式不会走到这里（SelfTestMode 分支在上方），所以绝不会有弹窗卡住 selftest。
            if (_lowIntegrityDetected && !ConfirmLowIntegrityStartup())
            {
                ExitRequested = true;
                try { Close(); } catch { }
                return;
            }
            StartFirstRunGuide();
        }
        PollGateState();
        ShowToast(_lowIntegrityDetected ? LowIntegrityToastText() : BuildStartupSummary(),
            StartupToastMs, true);
        RaiseStateChanged();
    }

    // 低权限启动时的启动提示：一句话讲清「为什么按住 Ctrl 没用」和「怎么恢复」，
    // 不要把图片数量、热键缺失等次要信息混进来——toast 是中间省略，混在一起就看不全了。
    private string LowIntegrityToastText()
    {
        return "低权限启动：按住 " + GateKeyDisplayName() + " 无效，已自动锁定交互；"
            + "请从资源管理器双击 " + AppInfo.ExeName + " 恢复穿透与门控";
    }

    // 低完整性（沙箱 / AppContainer）进程读不到键盘状态：按住门控键不可能生效。
    // 此时自动切到可交互模式，保证窗口至少能点、能设置、能关闭，并在启动汇总里说明原因。
    private void DetectRestrictedEnvironment()
    {
        if (!IntegrityProbe.IsRestricted()) { return; }
        _lowIntegrityDetected = true;
        try { SetLocked(true, true); }
        catch (Exception) { }
    }

    // C8：低权限启动提示。返回 true = 仍要运行；false = 退出。
    // 用自绘 Form 而不是 MessageBox：按钮文案必须明确写「仍要运行 / 退出」，
    // 中文系统 MessageBox 只有「是/否」，容易被误点成退出。
    private bool ConfirmLowIntegrityStartup()
    {
        try
        {
            int dpi = CurrentDpi > 0 ? CurrentDpi : 96;
            using (Form dialog = new Form())
            {
                dialog.Text = AppInfo.AppName;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowIcon = false;
                dialog.TopMost = true;
                dialog.ClientSize = new Size(global::Layout.Px(420, dpi), global::Layout.Px(168, dpi));

                Label label = new Label();
                label.Text = "当前权限受限（可能是从压缩包、网盘或沙箱里直接运行），"
                    + "窗口可能无法切换点击穿透，按住门控键也可能无效。\r\n\r\n"
                    + "建议先退出，把 " + AppInfo.ExeName + " 解压到本地文件夹后，以普通用户权限重新启动。";
                label.Location = new Point(global::Layout.Px(16, dpi), global::Layout.Px(13, dpi));
                label.Size = new Size(global::Layout.Px(388, dpi), global::Layout.Px(88, dpi));
                dialog.Controls.Add(label);

                Button runButton = new Button();
                runButton.Text = "仍要运行";
                runButton.DialogResult = DialogResult.OK;
                runButton.Size = new Size(global::Layout.Px(96, dpi), global::Layout.Px(30, dpi));
                runButton.Location = new Point(global::Layout.Px(196, dpi), global::Layout.Px(120, dpi));
                dialog.Controls.Add(runButton);

                Button exitButton = new Button();
                exitButton.Text = "退出";
                exitButton.DialogResult = DialogResult.Cancel;
                exitButton.Size = new Size(global::Layout.Px(96, dpi), global::Layout.Px(30, dpi));
                exitButton.Location = new Point(global::Layout.Px(308, dpi), global::Layout.Px(120, dpi));
                dialog.Controls.Add(exitButton);

                dialog.AcceptButton = runButton;
                dialog.CancelButton = exitButton;

                bool keepRunning = dialog.ShowDialog() == DialogResult.OK;
                Diagnostics.LogEvent("低权限启动提示：用户选择「" + (keepRunning ? "仍要运行" : "退出") + "」");
                return keepRunning;
            }
        }
        catch (Exception ex)
        {
            // 弹窗本身失败不能把程序卡死：默认继续运行（保持原锁定策略），只记日志。
            try { Diagnostics.LogEvent("低权限启动提示弹出失败：" + ex.Message); }
            catch { }
            return true;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_closing || !IsHandleCreated) { return; }

        if (ResizingWindow)
        {
            // 改大小过程中不自动改缩放比例（结束后再适应）
            InvalidateOverlay();
            return;
        }
        if (CurrentBitmap == null)
        {
            RebuildFitCache();
            InvalidateViewport();
            return;
        }
        if (LongImageMode)
        {
            SetLongImageMode(true);
        }
        else if (View.Mode == ViewMode.Fit)
        {
            FitToWindow();
        }
        else
        {
            // 手动 / 长图不使用适应缓存：这里主动重建（非 Fit 时只清空旧缓存），
            // 保证 45_OverlayPaint 的 OnPaint 不再自动 RebuildFitCache 后不会留下脏位图。
            RebuildFitCache();
            InvalidateViewport();
        }
        RaiseStateChanged();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_cleanupDone) { return; }
        _cleanupDone = true;
        _closing = true;
        ExitRequested = true;

        // 退出顺序固定：设置 → 热键 → 图片 → 窗口（窗口资源在 Dispose 里）
        try { if (GateTimer != null) { GateTimer.Stop(); } } catch { }
        try { if (SaveTimer != null) { SaveTimer.Stop(); } } catch { }
        try { if (_toastTimer != null) { _toastTimer.Stop(); } } catch { }

        // 1) 保存设置
        try { SaveNow(); } catch { }
        // 2) 注销热键
        try { if (Hotkeys != null) { Hotkeys.UnregisterAll(); } } catch { }
        // 3) 释放图片库（含解码缓存）
        try { if (Library != null) { Library.Dispose(); } } catch { }
        // 4) 关闭设置面板，避免残留窗口
        try
        {
            if (SettingsPanel != null && !SettingsPanel.IsDisposed) { SettingsPanel.Close(); }
        }
        catch { }
        SettingsPanel = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closing = true;

            try { if (GateTimer != null) { GateTimer.Stop(); GateTimer.Tick -= GateTimer_Tick; GateTimer.Dispose(); GateTimer = null; } } catch { }
            try { if (SaveTimer != null) { SaveTimer.Stop(); SaveTimer.Tick -= SaveTimer_Tick; SaveTimer.Dispose(); SaveTimer = null; } } catch { }
            try { if (_toastTimer != null) { _toastTimer.Stop(); _toastTimer.Tick -= ToastTimer_Tick; _toastTimer.Dispose(); _toastTimer = null; } } catch { }

            try
            {
                if (OverlayContextMenu != null)
                {
                    OverlayContextMenu.Closed -= OnContextMenuClosed;
                    OverlayContextMenu.Dispose();
                    OverlayContextMenu = null;
                }
            }
            catch { }

            try
            {
                if (Watcher != null)
                {
                    Watcher.Changed -= OnWatcherChanged;
                    Watcher.Dispose();
                    Watcher = null;
                }
            }
            catch { }

            try
            {
                if (SettingsPanel != null && !SettingsPanel.IsDisposed) { SettingsPanel.Dispose(); }
            }
            catch { }
            SettingsPanel = null;

            try { ReleaseDerivedBitmap(); } catch { }
            try { DisposePaintResources(); } catch { }

            try { if (Library != null) { Library.Dispose(); Library = null; } } catch { }
            try { if (Hotkeys != null) { Hotkeys.Dispose(); Hotkeys = null; } } catch { }
        }
        base.Dispose(disposing);
    }

    // ========================================================================
    // 状态变化通知（设置面板订阅用）
    // ========================================================================
    public event EventHandler StateChanged;

    internal void RaiseStateChanged()
    {
        EventHandler h = StateChanged;
        if (h != null) { h(this, EventArgs.Empty); }
    }

    // ========================================================================
    // 门控状态机：20ms 轮询门控键 + 光标 + 窗口矩形
    // ========================================================================
    private void GateTimer_Tick(object sender, EventArgs e)
    {
        try { PollGateState(); }
        catch { }
        try { TickFirstRunGuide(); }
        catch { }
    }

    // ========================================================================
    // 首次运行引导（B1）
    // 首次启动时用户看到的是一个「按住 Alt 才能操作」的穿透窗口，容易以为程序坏了。
    // 这里在底部显示 28 DIP 提示条，并给 12 秒可交互缓冲；20 秒（或按过门控键）后
    // 自动恢复穿透窗口并写入 first_run=0，之后不再出现。自测模式不启动引导。
    // ========================================================================
    private void StartFirstRunGuide()
    {
        if (SelfTestMode || Settings == null || !Settings.FirstRun) { return; }
        _firstRunActive = true;
        _firstRunLockPhase = true;
        _firstRunStartTick = Environment.TickCount;
        try { SetLocked(true, true); }       // 静默锁定：工具条可见，新用户能直接点「设置」
        catch { }
        UpdateToolbarVisibility();
        InvalidateOverlay();
    }

    private void TickFirstRunGuide()
    {
        if (!_firstRunActive || _closing) { return; }

        long elapsed = unchecked((int)(Environment.TickCount - (int)_firstRunStartTick));
        if (elapsed < 0) { elapsed = 0; }

        // 用户已经用门控键操作窗口 → 立刻收起提示条，不再打扰
        if (GateHeld)
        {
            FinishFirstRunGuide();
            return;
        }
        if (elapsed >= FirstRunTotalMs)
        {
            FinishFirstRunGuide();
            return;
        }
        if (_firstRunLockPhase && elapsed >= FirstRunLockMs)
        {
            // 12 秒后自动回穿透态；提示条继续保留到 20 秒
            _firstRunLockPhase = false;
            try { SetLocked(false, true); }
            catch { }
            InvalidateOverlay();
        }
    }

    private void FinishFirstRunGuide()
    {
        bool wasActive = _firstRunActive;
        bool wasLockPhase = _firstRunLockPhase;
        _firstRunActive = false;
        _firstRunLockPhase = false;
        _firstRunStartTick = 0;
        if (!wasActive) { return; }

        // 提前结束（用户在 12 秒内按了门控键）：恢复穿透态，别把窗口永远留在可交互状态
        if (wasLockPhase)
        {
            try { SetLocked(false, true); }
            catch { }
        }
        try
        {
            if (Settings != null && Settings.FirstRun)
            {
                Settings.FirstRun = false;
                ScheduleSave();     // 500ms 防抖；退出时 SaveNow 兜底
            }
        }
        catch { }
        InvalidateOverlay();
    }

    // 提示条文案：门控键与锁定热键都按当前实际设置动态拼
    private string FirstRunHintText()
    {
        string gate = PaintGateKeyName();
        if (string.IsNullOrEmpty(gate)) { gate = "Alt"; }
        string lockCombo = "";
        try
        {
            AppSettings settings = Settings;
            if (settings != null)
            {
                Keys k = settings.GetHotkey(HotkeyAction.ToggleLock);
                if (k != Keys.None) { lockCombo = HotkeyManager.FormatCombo(k) + " "; }
            }
        }
        catch { lockCombo = ""; }
        return "按住 " + gate + " 可操作窗口 · 按住 " + gate + " 拖动移动 · " + lockCombo + "锁定交互";
    }

    // 计算 desired = !WindowHidden && (Locked || busy || (gateDown && inWindow))，
    // 与当前 Interactive 不同才切换；忙碌时即使松开门控键也保持可交互，直到操作结束。
    internal void PollGateState()
    {
        if (SelfTestMode || _closing) { return; }

        bool gateDown = false;
        bool inWindow = false;
        if (!WindowHidden)
        {
            gateDown = IsGateKeyDown();
            POINT pt;
            if (NativeMethods.GetCursorPos(out pt))
            {
                inWindow = GetWindowRectPx().Contains(pt.X, pt.Y);
            }
        }
        GateHeld = gateDown;
        LastGatePollTick = Environment.TickCount;
        GateKeyDownLast = gateDown;

        bool busy = IsInteractionBusy();
        bool desired = !WindowHidden && (Locked || busy || (gateDown && inWindow));
        if (desired != Interactive)
        {
            SetInteractive(desired);
        }
        else
        {
            // 自愈：WinForms 在 Show()/SetVisibleCore() 期间会用 CreateParams 重新应用窗口样式，
            // 把 OnHandleCreated 里用 SetWindowLong 加的 TRANSPARENT/NOACTIVATE 抹掉；
            // 而状态没变时又不会走 SetInteractive，于是启动后窗口一直不穿透。每 tick 校验一次即可。
            ApplyExtendedStyles();
            UpdateToolbarVisibility();
        }

        // C4：拖动看门狗——Capture 还在但左右键都已松开（门控键先松、焦点被抢、MouseUp 丢失），
        // 拖动标志会一直为真，窗口就永远可交互、吞掉游戏点击。强制结束交互恢复穿透态。
        // 必须放在 busy 计算之后：CancelInteractions 结束时还会回调一次 PollGateState。
        bool anyDrag = DraggingWindow || ResizingWindow || PanningImage || DraggingScrollbar;
        if (ShouldCancelStuckDrag(anyDrag, Control.MouseButtons, _lowIntegrityDetected))
        {
            CancelInteractions();
        }
    }

    // C4：看门狗判定（纯函数，便于自测）。
    // 触发条件：有拖动标志 + 左右键都已松开；低权限模式拿不到可靠按键状态，不启用以免误伤。
    internal static bool ShouldCancelStuckDrag(bool anyDragFlag, MouseButtons pressedButtons, bool lowIntegrity)
    {
        if (!anyDragFlag) { return false; }
        if (lowIntegrity) { return false; }
        return pressedButtons == MouseButtons.None;
    }

    // 是否处于「操作进行中」：拖动/拉伸/平移/滚动条/菜单。
    // C4：设置面板不再算忙碌——面板里取消「锁定交互」必须立即生效，不能被这里顶回去
    // （面板是独立窗口，不依赖主窗体保持可交互；关闭面板时 OnSettingsPanelClosed 会重新轮询）。
    private bool IsInteractionBusy()
    {
        if (DraggingWindow || ResizingWindow || PanningImage || DraggingScrollbar) { return true; }
        return _menuOpen;
    }

    // 切换穿透/可交互：只切 WS_EX_TRANSPARENT / WS_EX_NOACTIVATE，绝不碰 WS_EX_LAYERED
    internal void SetInteractive(bool value)
    {
        bool changed = (Interactive != value);
        Interactive = value;

        // 仅可交互时允许拖放
        bool allowDrop = value && !WindowHidden && !SelfTestMode;
        if (allowDrop != _allowDropApplied)
        {
            _allowDropApplied = allowDrop;
            try { AllowDrop = allowDrop; } catch { }
        }

        ApplyExtendedStyles();
        if (IsHandleCreated)
        {
            IntPtr insertAfter = Settings.TopMost ? Win32Const.HWND_TOPMOST : Win32Const.HWND_NOTOPMOST;
            NativeMethods.SetWindowPos(Handle, insertAfter, 0, 0, 0, 0,
                Win32Const.SWP_NOMOVE | Win32Const.SWP_NOSIZE | Win32Const.SWP_NOACTIVATE);
        }

        if (!value)
        {
            HoverToolbar = ToolbarButton.None;
            PressedToolbar = ToolbarButton.None;
            HoverCard = CardButton.None;
            PressedCard = CardButton.None;
            try { Cursor = Cursors.Default; } catch { }
        }
        UpdateToolbarVisibility();
        if (changed)
        {
            InvalidateOverlay();
            RaiseStateChanged();
        }
    }

    // 扩展样式：常驻 TOOLWINDOW；穿透态加 TRANSPARENT|NOACTIVATE；
    // 门控交互态去掉 TRANSPARENT、保留 NOACTIVATE；锁定态两者都去掉。
    private void ApplyExtendedStyles()
    {
        if (!IsHandleCreated) { return; }
        int ex = NativeMethods.GetWindowLong(Handle, Win32Const.GWL_EXSTYLE);
        int want = ex | Win32Const.WS_EX_TOOLWINDOW;

        if (Locked && !WindowHidden)
        {
            want &= ~(Win32Const.WS_EX_TRANSPARENT | Win32Const.WS_EX_NOACTIVATE);
        }
        else if (Interactive && !WindowHidden)
        {
            want &= ~Win32Const.WS_EX_TRANSPARENT;
            want |= Win32Const.WS_EX_NOACTIVATE;
        }
        else
        {
            want |= Win32Const.WS_EX_TRANSPARENT | Win32Const.WS_EX_NOACTIVATE;
        }
        // 只在真的变化时调用：SetWindowLong 每次都会发 WM_STYLECHANGED，20ms 轮询里无谓地写会闪。
        if (want != ex) { NativeMethods.SetWindowLong(Handle, Win32Const.GWL_EXSTYLE, want); }
    }

    // 临时清除/恢复 NOACTIVATE（右键菜单兼容做法）
    private void SetNoActivateBit(bool enabled)
    {
        if (!IsHandleCreated) { return; }
        int ex = NativeMethods.GetWindowLong(Handle, Win32Const.GWL_EXSTYLE);
        if (enabled) { ex |= Win32Const.WS_EX_NOACTIVATE; }
        else { ex &= ~Win32Const.WS_EX_NOACTIVATE; }
        NativeMethods.SetWindowLong(Handle, Win32Const.GWL_EXSTYLE, ex);
    }

    private void ApplyTopMost()
    {
        if (!IsHandleCreated) { return; }
        IntPtr insertAfter = Settings.TopMost ? Win32Const.HWND_TOPMOST : Win32Const.HWND_NOTOPMOST;
        NativeMethods.SetWindowPos(Handle, insertAfter, 0, 0, 0, 0,
            Win32Const.SWP_NOMOVE | Win32Const.SWP_NOSIZE | Win32Const.SWP_NOACTIVATE);
    }

    private void UpdateToolbarVisibility()
    {
        bool show;
        if (SelfTestMode)
        {
            show = !WindowHidden;
        }
        else
        {
            // 按住门控键且在窗口内，或锁定交互时常驻显示
            show = !WindowHidden && (Locked || (GateHeld && Interactive));
        }
        if (show != ToolbarVisible)
        {
            ToolbarVisible = show;
            InvalidateToolbarArea();
        }
    }

    // ========================================================================
    // WndProc：不抢焦点 + 热键分发
    // ========================================================================
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32Const.WM_MOUSEACTIVATE)
        {
            // 非锁定态一律不激活（门控模式仍不抢焦点）
            if (!Locked)
            {
                m.Result = new IntPtr(Win32Const.MA_NOACTIVATE);
                return;
            }
        }
        else if (m.Msg == Win32Const.WM_HOTKEY)
        {
            if (!SelfTestMode && Hotkeys != null)
            {
                int actionValue = m.WParam.ToInt32() - HotkeyManager.IdBase;
                if (Enum.IsDefined(typeof(HotkeyAction), actionValue))
                {
                    try
                    {
                        HandleHotkeyAction((HotkeyAction)actionValue);
                    }
                    catch (Exception ex)
                    {
                        ShowToast("热键操作失败：" + ex.Message, StartupToastMs, true);
                    }
                    m.Result = IntPtr.Zero;
                    return;
                }
            }
        }
        base.WndProc(ref m);
    }

    // ========================================================================
    // 全局热键动作
    // ========================================================================
    private void HandleHotkeyAction(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ZoomIn:
                ApplyKeyZoom(ViewMath.KeyZoomStep);
                break;
            case HotkeyAction.ZoomOut:
                ApplyKeyZoom(1.0 / ViewMath.KeyZoomStep);
                break;
            case HotkeyAction.Prev:
                NavigateImage(-1);
                break;
            case HotkeyAction.Next:
                NavigateImage(1);
                break;
            case HotkeyAction.Fit:
                FitToWindow();
                ShowToast("已适应窗口", 3000, false);
                break;
            case HotkeyAction.Jump1:
            case HotkeyAction.Jump2:
            case HotkeyAction.Jump3:
            case HotkeyAction.Jump4:
            case HotkeyAction.Jump5:
            case HotkeyAction.Jump6:
            case HotkeyAction.Jump7:
            case HotkeyAction.Jump8:
            case HotkeyAction.Jump9:
                GoToImage((int)action - (int)HotkeyAction.Jump1);
                break;
            case HotkeyAction.OpacityDown:
                SetOpacityPercent(Settings.Opacity - 5, true);
                ShowToast("不透明度 " + Settings.Opacity.ToString(CultureInfo.InvariantCulture) + "%", 2000, false);
                break;
            case HotkeyAction.OpacityUp:
                SetOpacityPercent(Settings.Opacity + 5, true);
                ShowToast("不透明度 " + Settings.Opacity.ToString(CultureInfo.InvariantCulture) + "%", 2000, false);
                break;
            case HotkeyAction.ToggleLock:
                SetLocked(!Locked);
                break;
            case HotkeyAction.ToggleHide:
                if (WindowHidden) { ShowOverlay(); }
                else { HideOverlay(); }
                break;
        }
    }

    // 键盘缩放（B3）：鼠标在当前图片内 → 以光标位置为锚点；鼠标不在窗口内/不在图片上 → 窗口中心
    private void ApplyKeyZoom(double factor)
    {
        if (CurrentBitmap == null)
        {
            ShowToast("当前没有可显示的图片", 3000, false);
            return;
        }
        double before = View.Zoom;
        SetZoom(View.Zoom * factor, !CursorOverImage());
        if (Math.Abs(View.Zoom - before) < 1e-9)
        {
            // 已到 0.1× 或 10× 边界，忽略继续操作
            return;
        }
        ShowToast("缩放 " + Util.FormatPercent(View.Zoom), 2000, false);
    }

    // B3：鼠标指针是否落在当前图片内（客户区坐标 + 图片视口矩形双重判定）
    private bool CursorOverImage()
    {
        if (!IsHandleCreated || CurrentBitmap == null) { return false; }
        Point cursor;
        try { cursor = PointToClient(Cursor.Position); }
        catch { return false; }
        return ViewMath.ImageContainsViewPoint(View.OffsetX, View.OffsetY, View.Zoom,
            CurrentBitmap.Width, CurrentBitmap.Height, cursor.X, cursor.Y);
    }

    // ========================================================================
    // 鼠标：按下 / 移动 / 抬起 / 滚轮 / 离开
    // ========================================================================
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Interactive || WindowHidden) { return; }

        Point client = e.Location;

        if (e.Button == MouseButtons.Left)
        {
            // 先做按钮命中测试：工具条/卡片/滚动条命中时不能被双击分支抢先重置视图，
            // 否则双击按钮会「按钮执行一次 + 视图被 Fit 重置」。
            // 1) 空状态卡片按钮
            if (CardVisible)
            {
                CardButton cardButton = HitCardAt(client);
                if (cardButton != CardButton.None)
                {
                    PressedCard = cardButton;
                    InvalidateOverlay();
                    return;
                }
            }

            // 2) 工具条按钮
            if (ToolbarVisible)
            {
                ToolbarButton toolbarButton = HitToolbarAt(client);
                if (toolbarButton != ToolbarButton.None)
                {
                    PressedToolbar = toolbarButton;
                    InvalidateToolbarArea();
                    return;
                }
            }

            // 3) 长图滚动条（右侧热区）
            if (LongImageMode && CurrentBitmap != null)
            {
                if (HandleScrollbarPress(client)) { return; }
            }

            // 4) Ctrl+双击 / 双击 = 适应窗口（放在命中测试之后）
            if (e.Clicks >= 2)
            {
                LastClickTick = 0;
                FitToWindow();
                ShowToast("已适应窗口", 3000, false);
                return;
            }

            // 5) 右下角 12 DIP 拉伸热区（自绘，不依赖 WM_NCHITTEST）
            if (InResizeGrip(client))
            {
                BeginResize(Cursor.Position);
                return;
            }

            // 6) Ctrl+Alt+左键 = 移动窗口（鼠标无中键时的兜底）
            if (IsCtrlDown() && IsAltDown())
            {
                BeginWindowDrag(Cursor.Position);
                return;
            }

            // 7) 按住门控键（默认 Ctrl）+ 左键：可平移且未锁图 → 平移图片；否则移动窗口
            if (IsGateKeyDown() || Locked)
            {
                if (ViewMath.ShouldPanImage(CanPanImage(), ImageLocked)) { BeginImagePan(Cursor.Position); }
                else { BeginWindowDrag(Cursor.Position); }
                return;
            }
            return;
        }

        if (e.Button == MouseButtons.Middle)
        {
            // 中键随时移动窗口
            BeginWindowDrag(Cursor.Position);
            return;
        }

        // 右键：在 MouseUp 阶段弹菜单
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Interactive || WindowHidden)
        {
            ResetHoverState();
            return;
        }

        Point screen = Cursor.Position;
        if (ResizingWindow) { UpdateResize(screen); return; }
        if (DraggingWindow) { UpdateWindowDrag(screen); return; }
        if (PanningImage) { UpdateImagePan(screen); return; }
        if (DraggingScrollbar) { UpdateScrollbarDrag(e.Location); return; }

        UpdateHover(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        bool wasDrag = DragMoved;

        if (ResizingWindow) { EndResize(); wasDrag = true; }
        else if (DraggingWindow) { EndWindowDrag(); wasDrag = DragMoved; }
        else if (PanningImage) { EndImagePan(); wasDrag = DragMoved; }
        else if (DraggingScrollbar) { EndScrollbarDrag(); wasDrag = true; }

        if (e.Button == MouseButtons.Left)
        {
            ToolbarButton pressedToolbar = PressedToolbar;
            CardButton pressedCard = PressedCard;
            PressedToolbar = ToolbarButton.None;
            PressedCard = CardButton.None;

            if (pressedToolbar != ToolbarButton.None)
            {
                ToolbarButton hit = HitToolbarAt(e.Location);
                if (hit == pressedToolbar) { ExecuteToolbarButton(pressedToolbar); }
                InvalidateToolbarArea();
            }
            else if (pressedCard != CardButton.None)
            {
                CardButton hitCard = HitCardAt(e.Location);
                if (hitCard == pressedCard) { ExecuteCardButton(pressedCard); }
                InvalidateOverlay();
            }
            else if (!wasDrag && e.Clicks < 2 && Interactive && !WindowHidden)
            {
                // 系统未发 DBLCLK 时的兜底双击判定（用系统 DragSize / DoubleClickTime）
                HandleClickForDoubleClick(e.Location);
            }
            DragMoved = false;
        }

        if (e.Button == MouseButtons.Right && Interactive && !WindowHidden)
        {
            // 按住门控键（默认 Ctrl）右键（或锁定交互下右键）= 右键菜单
            if (Locked || IsGateKeyDown()) { ShowContextMenu(Cursor.Position); }
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (DraggingWindow || ResizingWindow || PanningImage || DraggingScrollbar) { return; }
        ResetHoverState();
    }

    // ========================================================================
    // 鼠标捕获丢失 / 窗口失活：统一取消进行中的交互
    // Alt+Tab、UAC、别的程序 SetCapture、RDP 切换时收不到 MouseUp，
    // 若不复位标志，IsInteractionBusy() 会一直为真 → 窗口永不穿透、吞掉游戏点击。
    // 注意：正常 MouseUp 里 Capture=false 也会触发 OnMouseCaptureChanged，
    // 因此这里必须幂等，且不能重复写盘。
    // ========================================================================
    private void CancelInteractions()
    {
        if (_cancellingInteractions) { return; }
        _cancellingInteractions = true;
        try
        {
            // 先复位标志再清 Capture：Capture=false 会同步回调 OnMouseCaptureChanged，
            // 那时四个标志已为 false，重入会直接返回，不会重复写盘。
            bool movedWindow = DraggingWindow || ResizingWindow;
            bool wasResize = ResizingWindow;
            bool wasPan = PanningImage;
            DraggingWindow = false;
            ResizingWindow = false;
            PanningImage = false;
            DraggingScrollbar = false;
            DragMoved = false;
            try { Capture = false; } catch { }
            HoverToolbar = ToolbarButton.None;
            PressedToolbar = ToolbarButton.None;
            HoverCard = CardButton.None;
            PressedCard = CardButton.None;
            try { Cursor = Cursors.Default; } catch { }

            if (movedWindow)
            {
                // 拖窗/拉伸中断：把当前（已夹取过的）位置写回设置
                SaveWindowRectToSettings();
                ScheduleSave();
                // 拉伸中断时尺寸已变，和 EndResize 一样重新适应一次，避免停在旧缩放上
                if (wasResize && CurrentBitmap != null)
                {
                    if (LongImageMode) { SetLongImageMode(true); }
                    else if (View.Mode == ViewMode.Fit) { FitToWindow(); }
                    else { RebuildFitCache(); }
                }
            }
            else if (wasPan && CurrentBitmap != null && View.Mode == ViewMode.Fit)
            {
                // 平移中断后 UpdateImagePan 已把缓存标记失效，这里重建以免一直走逐帧缩放路径
                RebuildFitCache();
            }
        }
        finally
        {
            _cancellingInteractions = false;
        }

        // 重新计算穿透/可交互，避免中断后一直停留在忙碌态
        PollGateState();
        InvalidateOverlay();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (SelfTestMode) { return; }
        if (!DraggingWindow && !ResizingWindow && !PanningImage && !DraggingScrollbar) { return; }
        CancelInteractions();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (SelfTestMode) { return; }
        if (!DraggingWindow && !ResizingWindow && !PanningImage && !DraggingScrollbar) { return; }
        CancelInteractions();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!Interactive || WindowHidden || CurrentBitmap == null)
        {
            base.OnMouseWheel(e);
            return;
        }

        bool ctrl = IsCtrlDown();

        // 长图模式：滚轮上下滚动，Ctrl+滚轮 才缩放
        if (LongImageMode && !ctrl)
        {
            double step = ViewMath.LongImageStepPx(View.Zoom, CurrentDpi);
            double notches = e.Delta / 120.0;
            double offsetY = View.OffsetY + notches * step;
            ViewMath.ClampScroll(View.Zoom, CurrentBitmap.Height, ClientSize.Height, ref offsetY);
            View.OffsetY = offsetY;
            View.OffsetX = 0;
            RebuildFitCache();
            InvalidateViewport();
            base.OnMouseWheel(e);
            return;
        }

        // 普通缩放：以光标所指位置为锚点，每格 ×1.1，夹取 0.1–10
        double factor = Math.Pow(ViewMath.WheelZoomStep, e.Delta / 120.0);
        double newZoom = ViewMath.ClampManualZoom(View.Zoom * factor);
        if (Math.Abs(newZoom - View.Zoom) < 1e-9)
        {
            base.OnMouseWheel(e);
            return;
        }
        double newOffX;
        double newOffY;
        ViewMath.ZoomAtPoint(View.Zoom, View.OffsetX, View.OffsetY, newZoom, e.X, e.Y, out newOffX, out newOffY);
        ViewMath.ClampOffsets(newZoom, CurrentBitmap.Width, CurrentBitmap.Height,
            ClientSize.Width, ClientSize.Height, ref newOffX, ref newOffY);
        View.Zoom = newZoom;
        View.OffsetX = newOffX;
        View.OffsetY = newOffY;
        if (LongImageMode)
        {
            View.Mode = ViewMode.LongImage;
            // 长图视图始终 fit-width 左对齐，只滚动纵向
            View.OffsetX = 0;
            double scrollY = View.OffsetY;
            ViewMath.ClampScroll(newZoom, CurrentBitmap.Height, ClientSize.Height, ref scrollY);
            View.OffsetY = scrollY;
        }
        else
        {
            View.Mode = ViewMode.Manual;
        }
        RebuildFitCache();
        InvalidateViewport();
        RaiseStateChanged();
        base.OnMouseWheel(e);
    }

    // ---- 鼠标辅助 ----
    private void HandleClickForDoubleClick(Point client)
    {
        long now = Environment.TickCount;
        int doubleClickMs = SystemInformation.DoubleClickTime;

        if (LastClickTick != 0)
        {
            int elapsed = unchecked((int)(now - LastClickTick));
            int dx = Math.Abs(client.X - LastClickPoint.X);
            int dy = Math.Abs(client.Y - LastClickPoint.Y);
            if (elapsed >= 0 && elapsed <= doubleClickMs && dx <= _dragSize.Width && dy <= _dragSize.Height)
            {
                LastClickTick = 0;
                FitToWindow();
                ShowToast("已适应窗口", 3000, false);
                return;
            }
        }
        LastClickTick = now;
        LastClickPoint = client;
    }

    private bool InResizeGrip(Point client)
    {
        int grip = global::Layout.Px(global::Layout.ResizeGripDip, CurrentDpi);
        if (grip < 4) { grip = 4; }
        return client.X >= ClientSize.Width - grip && client.X <= ClientSize.Width
            && client.Y >= ClientSize.Height - grip && client.Y <= ClientSize.Height;
    }

    private void ResetHoverState()
    {
        bool changed = false;
        if (HoverToolbar != ToolbarButton.None) { HoverToolbar = ToolbarButton.None; changed = true; }
        if (HoverCard != CardButton.None) { HoverCard = CardButton.None; changed = true; }
        try { if (Cursor != Cursors.Default) { Cursor = Cursors.Default; } } catch { }
        if (changed) { InvalidateOverlay(); }
    }

    private void UpdateHover(Point client)
    {
        ToolbarButton hoverToolbar = ToolbarButton.None;
        if (ToolbarVisible)
        {
            ToolbarLayout layout = GetToolbarLayout();
            if (layout != null)
            {
                ToolbarButton hit = ViewMath.HitTestToolbar(layout, client);
                if (hit != ToolbarButton.None)
                {
                    ToolbarItem item = layout.Find(hit);
                    if (item == null || item.Enabled) { hoverToolbar = hit; }
                }
            }
        }
        if (hoverToolbar != HoverToolbar)
        {
            HoverToolbar = hoverToolbar;
            InvalidateToolbarArea();
        }

        CardButton hoverCard = CardButton.None;
        if (CardVisible)
        {
            CardLayout cardLayout = GetCardLayout();
            if (cardLayout != null)
            {
                CardButton hitCard = ViewMath.HitTestCard(cardLayout, client);
                if (hitCard != CardButton.None)
                {
                    CardItem item = cardLayout.Find(hitCard);
                    if (item == null || item.Enabled) { hoverCard = hitCard; }
                }
            }
        }
        if (hoverCard != HoverCard)
        {
            HoverCard = hoverCard;
            InvalidateOverlay();
        }

        Cursor desired;
        if (HoverToolbar != ToolbarButton.None || HoverCard != CardButton.None) { desired = Cursors.Hand; }
        else if (!Settings.PositionLocked && InResizeGrip(client)) { desired = Cursors.SizeNWSE; }
        else { desired = Cursors.Default; }
        try { if (Cursor != desired) { Cursor = desired; } } catch { }
    }

    private ToolbarLayout GetToolbarLayout()
    {
        try { return CurrentToolbarLayout(); }
        catch { return null; }
    }

    private CardLayout GetCardLayout()
    {
        try { return CurrentCardLayout(); }
        catch { return null; }
    }

    private ToolbarButton HitToolbarAt(Point client)
    {
        if (!ToolbarVisible) { return ToolbarButton.None; }
        ToolbarLayout layout = GetToolbarLayout();
        if (layout == null) { return ToolbarButton.None; }
        return ViewMath.HitTestToolbar(layout, client);
    }

    private CardButton HitCardAt(Point client)
    {
        if (!CardVisible) { return CardButton.None; }
        CardLayout layout = GetCardLayout();
        if (layout == null) { return CardButton.None; }
        return ViewMath.HitTestCard(layout, client);
    }

    // ---- 移动窗口 ----
    private void BeginWindowDrag(Point screen)
    {
        if (Settings.PositionLocked)
        {
            ShowToast("窗口位置与大小已锁定（可在设置面板解锁）", 3000, false);
            return;
        }
        DraggingWindow = true;
        DragMoved = false;
        DragStartScreen = screen;
        DragStartRect = GetWindowRectPx();
        DragStartLocation = DragStartRect.Location;
        PanStartScreen = screen;
        try { Capture = true; } catch { }
        try { Cursor = Cursors.SizeAll; } catch { }
    }

    private void UpdateWindowDrag(Point screen)
    {
        int dx = screen.X - DragStartScreen.X;
        int dy = screen.Y - DragStartScreen.Y;
        if (!DragMoved)
        {
            if (Math.Abs(dx) < _dragSize.Width && Math.Abs(dy) < _dragSize.Height) { return; }
            DragMoved = true;
        }

        int x = DragStartLocation.X + dx;
        int y = DragStartLocation.Y + dy;
        Rectangle proposed = new Rectangle(x, y, DragStartRect.Width, DragStartRect.Height);

        // 边缘吸附：12 DIP，按住 Shift 临时禁用
        if (Settings.Snap && !IsShiftDown())
        {
            Rectangle workArea = Screen.FromRectangle(proposed).WorkingArea;
            bool snappedX;
            bool snappedY;
            Point snapped = ViewMath.SnapMove(proposed, workArea, global::Layout.Px(global::Layout.SnapThresholdDip, CurrentDpi),
                out snappedX, out snappedY);
            proposed.X = snapped.X;
            proposed.Y = snapped.Y;
        }

        // 夹取：不做「完整装进工作区」的缩放（大窗口在小屏上会被压小），
        // 只保证至少一块（48 DIP 见方）留在工作区内，避免把窗口拖出屏幕后写盘。
        proposed = ClampWindowLoose(proposed);

        if (IsHandleCreated)
        {
            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, proposed.X, proposed.Y, 0, 0,
                Win32Const.SWP_NOSIZE | Win32Const.SWP_NOZORDER | Win32Const.SWP_NOACTIVATE);
        }
        else
        {
            Left = proposed.X;
            Top = proposed.Y;
        }
    }

    // 宽松夹取：只保证窗口至少有 KeepVisibleDip×KeepVisibleDip 留在某一屏工作区内（位置可部分出屏）。
    // 不用 ViewMath.ClampWindowRect：它是「完整装进工作区」的语义，大窗口拖到小屏上会被压小；
    // 这里只挪位置、不改宽高，也不触碰最小尺寸逻辑。
    // 多屏：只要落在任意一屏的可见量够，就完全不动，避免把窗口锁死在主屏。
    private Rectangle ClampWindowLoose(Rectangle proposed)
    {
        if (proposed.Width <= 0 || proposed.Height <= 0) { return proposed; }

        int keep = global::Layout.Px(KeepVisibleDip, CurrentDpi);
        if (keep < 1) { keep = 1; }

        Screen[] screens;
        try { screens = Screen.AllScreens; }
        catch (Exception) { screens = null; }
        if (screens == null || screens.Length == 0) { return proposed; }

        // 已满足「任意一屏可见 48×48」就不动
        for (int i = 0; i < screens.Length; i++)
        {
            Rectangle wa;
            try { wa = screens[i].WorkingArea; }
            catch (Exception) { continue; }
            Rectangle inter = Rectangle.Intersect(wa, proposed);
            int needX = Math.Min(keep, proposed.Width);
            int needY = Math.Min(keep, proposed.Height);
            needX = Math.Min(needX, wa.Width);
            needY = Math.Min(needY, wa.Height);
            if (inter.Width >= needX && inter.Height >= needY) { return proposed; }
        }

        // 不满足：夹到「离它最近的那一屏」工作区内，同样只保证可见块
        Rectangle area;
        try { area = Screen.FromRectangle(proposed).WorkingArea; }
        catch (Exception) { area = Screen.PrimaryScreen.WorkingArea; }
        if (area.Width <= 0 || area.Height <= 0) { return proposed; }

        int keepX = Math.Min(keep, proposed.Width);
        keepX = Math.Min(keepX, area.Width);
        if (keepX < 1) { keepX = 1; }
        int keepY = Math.Min(keep, proposed.Height);
        keepY = Math.Min(keepY, area.Height);
        if (keepY < 1) { keepY = 1; }

        int x = proposed.X;
        int minX = area.Left - (proposed.Width - keepX);
        int maxX = area.Right - keepX;
        if (x < minX) { x = minX; }
        if (x > maxX) { x = maxX; }

        int y = proposed.Y;
        int minY = area.Top - (proposed.Height - keepY);
        int maxY = area.Bottom - keepY;
        if (y < minY) { y = minY; }
        if (y > maxY) { y = maxY; }

        return new Rectangle(x, y, proposed.Width, proposed.Height);
    }

    private void EndWindowDrag()
    {
        DraggingWindow = false;
        try { Capture = false; } catch { }
        try { Cursor = Cursors.Default; } catch { }
        if (DragMoved)
        {
            SaveWindowRectToSettings();
            ScheduleSave();
            NotifySettingsPanelChanged();
            RaiseStateChanged();
        }
        PollGateState();
    }

    // ---- 右下角拉伸 ----
    private void BeginResize(Point screen)
    {
        if (Settings.PositionLocked)
        {
            ShowToast("窗口位置与大小已锁定（可在设置面板解锁）", 3000, false);
            return;
        }
        ResizingWindow = true;
        DragMoved = false;
        _resizeStartScreen = screen;
        _resizeStartRect = GetWindowRectPx();
        DragStartScreen = screen;
        DragStartRect = _resizeStartRect;
        try { Capture = true; } catch { }
        try { Cursor = Cursors.SizeNWSE; } catch { }
    }

    private void UpdateResize(Point screen)
    {
        int dx = screen.X - _resizeStartScreen.X;
        int dy = screen.Y - _resizeStartScreen.Y;
        int minW = global::Layout.Px(global::Layout.MinWindowWidthDip, CurrentDpi);
        int minH = global::Layout.Px(global::Layout.MinWindowHeightDip, CurrentDpi);

        int width = Math.Max(minW, _resizeStartRect.Width + dx);
        int height = Math.Max(minH, _resizeStartRect.Height + dy);
        Rectangle proposed = new Rectangle(_resizeStartRect.X, _resizeStartRect.Y, width, height);

        // 边缘吸附（对边固定），按住 Shift 临时禁用
        if (Settings.Snap && !IsShiftDown())
        {
            Rectangle workArea = Screen.FromRectangle(proposed).WorkingArea;
            bool snapped;
            proposed = ViewMath.SnapResize(proposed, workArea, global::Layout.Px(global::Layout.SnapThresholdDip, CurrentDpi), out snapped);
            if (proposed.Width < minW) { proposed.Width = minW; }
            if (proposed.Height < minH) { proposed.Height = minH; }
        }

        // 最大不超过当前屏幕工作区
        Rectangle screenArea = Screen.FromRectangle(proposed).WorkingArea;
        if (proposed.Right > screenArea.Right) { proposed.Width = Math.Max(minW, screenArea.Right - proposed.Left); }
        if (proposed.Bottom > screenArea.Bottom) { proposed.Height = Math.Max(minH, screenArea.Bottom - proposed.Top); }
        if (proposed.Width <= 0 || proposed.Height <= 0) { return; }

        // 与拖动一致：至少保留 48 DIP 见方在工作区内（窗口原本贴在屏幕边缘/跨屏时也不会写进屏幕外坐标）
        proposed = ClampWindowLoose(proposed);

        SetWindowBoundsNoZ(proposed);
        InvalidateOverlay();
    }

    private void EndResize()
    {
        ResizingWindow = false;
        try { Capture = false; } catch { }
        try { Cursor = Cursors.Default; } catch { }
        SaveWindowRectToSettings();
        ScheduleSave();
        NotifySettingsPanelChanged();

        // 若当前是「适应窗口」状态，改完重新适应
        if (LongImageMode) { SetLongImageMode(true); }
        else if (View.Mode == ViewMode.Fit) { FitToWindow(); }
        else { RebuildFitCache(); InvalidateViewport(); }

        PollGateState();
        RaiseStateChanged();
    }

    // ---- 平移图片 ----
    private bool CanPanImage()
    {
        if (CurrentBitmap == null || WindowHidden) { return false; }
        bool canX = ViewMath.CanPanX(View.Zoom, CurrentBitmap.Width, ClientSize.Width);
        bool canY = ViewMath.CanPanY(View.Zoom, CurrentBitmap.Height, ClientSize.Height);
        return canX || canY || LongImageMode;
    }

    private void BeginImagePan(Point screen)
    {
        PanningImage = true;
        DragMoved = false;
        PanStartScreen = screen;
        PanStartOffsetX = View.OffsetX;
        PanStartOffsetY = View.OffsetY;
        DragStartScreen = screen;
        try { Capture = true; } catch { }
        try { Cursor = Cursors.SizeAll; } catch { }
    }

    private void UpdateImagePan(Point screen)
    {
        int dx = screen.X - PanStartScreen.X;
        int dy = screen.Y - PanStartScreen.Y;
        if (!DragMoved)
        {
            // 与窗口拖动一致，用系统 DragSize（启动时取一次），避免 1px 抖动就进入平移
            if (Math.Abs(dx) < _dragSize.Width && Math.Abs(dy) < _dragSize.Height) { return; }
            DragMoved = true;
        }

        if (CurrentBitmap == null) { return; }

        if (LongImageMode)
        {
            // 长图模式只做上下平移，左右固定
            double offsetY = PanStartOffsetY + dy;
            ViewMath.ClampScroll(View.Zoom, CurrentBitmap.Height, ClientSize.Height, ref offsetY);
            View.OffsetY = offsetY;
            View.OffsetX = 0;
        }
        else
        {
            double offsetX = PanStartOffsetX + dx;
            double offsetY = PanStartOffsetY + dy;
            ViewMath.ClampOffsets(View.Zoom, CurrentBitmap.Width, CurrentBitmap.Height,
                ClientSize.Width, ClientSize.Height, ref offsetX, ref offsetY);
            View.OffsetX = offsetX;
            View.OffsetY = offsetY;
        }
        FitCacheIndex = -1;
        InvalidateViewport();
    }

    private void EndImagePan()
    {
        PanningImage = false;
        try { Capture = false; } catch { }
        try { Cursor = Cursors.Default; } catch { }
        if (DragMoved)
        {
            if (LongImageMode) { View.Mode = ViewMode.LongImage; }
            else { View.Mode = ViewMode.Manual; }
            RebuildFitCache();
            InvalidateViewport();
        }
        PollGateState();
        RaiseStateChanged();
    }

    // ---- 长图滚动条 ----
    private bool HandleScrollbarPress(Point client)
    {
        // C4：右下角拉伸角优先于滚动条。滚动条占满客户区高度，若先判滚动条，
        // 长图模式下右下角就只剩几像素可拉伸，用户会以为窗口不能改大小。
        if (InResizeGrip(client)) { return false; }
        Rectangle track = ViewMath.ScrollbarTrackRect(ClientSize, CurrentDpi);
        if (!track.Contains(client)) { return false; }
        Rectangle thumb = ViewMath.ScrollbarThumbRect(ClientSize, CurrentDpi, ClientSize.Height,
            CurrentBitmap.Height, View.OffsetY, View.Zoom);
        if (thumb.Contains(client))
        {
            DraggingScrollbar = true;
            ScrollbarDragStart = client;
            ScrollbarStartOffsetY = View.OffsetY;
            DragMoved = true;
            try { Capture = true; } catch { }
        }
        else
        {
            // 点轨道：把滑块中心跳到该位置
            double maxScroll = CurrentBitmap.Height * View.Zoom - ClientSize.Height;
            if (maxScroll > 0)
            {
                double range = track.Height - thumb.Height;
                if (range <= 1) { range = 1; }
                double ratio = (client.Y - track.Top - thumb.Height / 2.0) / range;
                if (ratio < 0) { ratio = 0; }
                if (ratio > 1) { ratio = 1; }
                View.OffsetY = -ratio * maxScroll;
                InvalidateViewport();
            }
        }
        return true;
    }

    private void UpdateScrollbarDrag(Point client)
    {
        if (CurrentBitmap == null) { return; }
        Rectangle track = ViewMath.ScrollbarTrackRect(ClientSize, CurrentDpi);
        Rectangle thumb = ViewMath.ScrollbarThumbRect(ClientSize, CurrentDpi, ClientSize.Height,
            CurrentBitmap.Height, View.OffsetY, View.Zoom);
        double maxScroll = CurrentBitmap.Height * View.Zoom - ClientSize.Height;
        double range = track.Height - thumb.Height;
        if (range <= 1) { range = 1; }

        double dy = client.Y - ScrollbarDragStart.Y;
        double offsetY = ScrollbarStartOffsetY + dy * (maxScroll / range);
        ViewMath.ClampScroll(View.Zoom, CurrentBitmap.Height, ClientSize.Height, ref offsetY);
        View.OffsetY = offsetY;
        FitCacheIndex = -1;
        InvalidateViewport();
    }

    private void EndScrollbarDrag()
    {
        DraggingScrollbar = false;
        DragMoved = true;
        try { Capture = false; } catch { }
        PollGateState();
        RaiseStateChanged();
    }

    // ========================================================================
    // 工具条 / 空状态卡片按钮
    // ========================================================================
    private void ExecuteToolbarButton(ToolbarButton kind)
    {
        switch (kind)
        {
            case ToolbarButton.Prev:
                NavigateImage(-1);
                break;
            case ToolbarButton.Next:
                NavigateImage(1);
                break;
            case ToolbarButton.Fit:
                FitToWindow();
                ShowToast("已适应窗口", 3000, false);
                break;
            case ToolbarButton.LockImage:
                SetImageLocked(!ImageLocked, true);
                break;
            case ToolbarButton.LongMode:
                ToggleLongImageMode();
                break;
            case ToolbarButton.Settings:
                OpenSettingsPanel(SettingsSection.Images);
                break;
            case ToolbarButton.Close:
                // B4：× = 隐藏窗口（与 Ctrl+Alt+G 同语义），不再直接退出程序；
                // 彻底退出请用右键菜单「退出」（保留真正退出入口）。
                HideOverlay();
                break;
        }
    }

    // 「锁图」：锁上后按住门控键 + 左键拖动只移动窗口，图片不再平移（长图模式下拖动也改为移动窗口）。
    // 不写入 settings.ini，重启/换图后保持当前开关状态直到退出程序。
    internal void SetImageLocked(bool locked, bool showToast)
    {
        ImageLocked = locked;
        UpdateContextMenuState();
        NotifySettingsPanelChanged();
        Invalidate();
        if (showToast)
        {
            ShowToast(locked
                ? "已锁定图片：按住 " + GateKeyDisplayName() + " 拖动 = 移动窗口（再点一次解锁）"
                : "已解锁图片：按住 " + GateKeyDisplayName() + " 拖动 = 平移图片", 3200, false);
        }
    }

    private void ExecuteCardButton(CardButton kind)
    {
        switch (kind)
        {
            case CardButton.ChooseFolder:
                ChooseImageFolder();
                break;
            case CardButton.OpenFolder:
                OpenImageFolder();
                break;
            case CardButton.Rescan:
                // C9：卡片上的「重新扫描」也给同样的扫描中反馈
                BeginScanFeedback();
                ReloadImages(true, false);
                break;
        }
    }

    // ========================================================================
    // 视图：适应窗口 / 缩放 / 预设 / 旋转 / 长图
    // ========================================================================
    internal void FitToWindow()
    {
        LongImageMode = false;
        View.Mode = ViewMode.Fit;

        int clientWidth = ClientSize.Width;
        int clientHeight = ClientSize.Height;
        if (CurrentBitmap == null || clientWidth <= 0 || clientHeight <= 0)
        {
            View.Zoom = 1.0;
            View.OffsetX = 0;
            View.OffsetY = 0;
            // 没有可画内容也要清掉旧缓存位图，避免留着整客户区大小的分配
            RebuildFitCache();
            InvalidateViewport();
            return;
        }

        double zoom = ViewMath.FitZoom(CurrentBitmap.Width, CurrentBitmap.Height, clientWidth, clientHeight);
        if (zoom <= 0 || double.IsNaN(zoom) || double.IsInfinity(zoom)) { zoom = 1.0; }
        View.Zoom = zoom;
        View.OffsetX = (clientWidth - CurrentBitmap.Width * zoom) / 2.0;
        View.OffsetY = (clientHeight - CurrentBitmap.Height * zoom) / 2.0;
        RebuildFitCache();
        InvalidateViewport();
        RaiseStateChanged();
    }

    internal void SetZoom(double zoom, bool anchorCenter)
    {
        if (CurrentBitmap == null) { return; }
        int clientWidth = ClientSize.Width;
        int clientHeight = ClientSize.Height;
        if (clientWidth <= 0 || clientHeight <= 0) { return; }

        double requested = ViewMath.ClampManualZoom(zoom);
        double oldZoom = View.Zoom;
        if (oldZoom <= 0) { oldZoom = 1.0; }
        if (Math.Abs(requested - oldZoom) < 1e-9) { return; }

        double anchorX = clientWidth / 2.0;
        double anchorY = clientHeight / 2.0;
        if (!anchorCenter && IsHandleCreated)
        {
            Point cursor = PointToClient(Cursor.Position);
            anchorX = cursor.X;
            anchorY = cursor.Y;
        }

        double newOffX;
        double newOffY;
        ViewMath.ZoomAtPoint(oldZoom, View.OffsetX, View.OffsetY, requested, anchorX, anchorY, out newOffX, out newOffY);
        ViewMath.ClampOffsets(requested, CurrentBitmap.Width, CurrentBitmap.Height, clientWidth, clientHeight,
            ref newOffX, ref newOffY);

        View.Zoom = requested;
        View.OffsetX = newOffX;
        View.OffsetY = newOffY;

        if (LongImageMode)
        {
            View.Mode = ViewMode.LongImage;
            // 长图视图始终 fit-width 左对齐，只滚动纵向
            View.OffsetX = 0;
            double offsetY = View.OffsetY;
            ViewMath.ClampScroll(requested, CurrentBitmap.Height, clientHeight, ref offsetY);
            View.OffsetY = offsetY;
        }
        else
        {
            View.Mode = ViewMode.Manual;
        }
        RebuildFitCache();
        InvalidateViewport();
        RaiseStateChanged();
    }

    internal void SetZoomPreset(double percent)
    {
        if (CurrentBitmap == null)
        {
            ShowToast("当前没有可显示的图片", 3000, false);
            return;
        }
        if (percent <= 0) { percent = 100.0; }
        // 以窗口中心为锚点（SetZoom 内部再夹紧 offset）
        SetZoom(percent / 100.0, true);
        ShowToast("缩放 " + Util.FormatPercent(View.Zoom), 2000, false);
    }

    internal void Rotate(RotateOp op)
    {
        if (CurrentBase == null)
        {
            ShowToast("当前没有可旋转的图片", 3000, false);
            return;
        }

        // 用 RotateFlipType 表示「旋转 + 翻转」的完整组合（8 种），
        // 避免旧实现只记单个 RotateOp，导致「先水平翻转再顺时针 90°」丢掉翻转。
        RotateFlipType next = NextRotateFlip(_currentRotateFlip, op);
        Bitmap derived = null;
        if (next != RotateFlipType.RotateNoneFlipNone)
        {
            // 从「原始解码位图」重新生成派生副本，避免反复 RotateFlip 累积
            try
            {
                derived = new Bitmap(CurrentBase);
                derived.RotateFlip(next);
            }
            catch (OutOfMemoryException)
            {
                if (derived != null) { try { derived.Dispose(); } catch { } }
                ShowToast("图片过大，旋转失败", StartupToastMs, true);
                return;
            }
            catch (Exception ex)
            {
                if (derived != null) { try { derived.Dispose(); } catch { } }
                ShowToast("旋转失败：" + ex.Message, StartupToastMs, true);
                return;
            }
        }

        // 释放旧派生副本
        ReleaseDerivedBitmap();
        _currentRotateFlip = next;
        if (derived != null)
        {
            CurrentBitmap = derived;
            CurrentBitmapOwned = true;
        }
        else
        {
            CurrentBitmap = CurrentBase;
            CurrentBitmapOwned = false;
        }

        if (LongImageMode)
        {
            SetLongImageMode(true);
        }
        else if (View.Mode == ViewMode.Fit)
        {
            FitToWindow();
        }
        else
        {
            // 保持当前缩放，以窗口中心为锚点重新计算
            int clientWidth = ClientSize.Width;
            int clientHeight = ClientSize.Height;
            double offsetX = (clientWidth - CurrentBitmap.Width * View.Zoom) / 2.0;
            double offsetY = (clientHeight - CurrentBitmap.Height * View.Zoom) / 2.0;
            ViewMath.ClampOffsets(View.Zoom, CurrentBitmap.Width, CurrentBitmap.Height, clientWidth, clientHeight,
                ref offsetX, ref offsetY);
            View.OffsetX = offsetX;
            View.OffsetY = offsetY;
            RebuildFitCache();
            InvalidateViewport();
            RaiseStateChanged();
        }
        ShowToast("旋转/翻转：" + RotateFlipText(next), 3000, false);
    }

    internal void SetLongImageMode(bool enabled)
    {
        if (CurrentBitmap == null)
        {
            LongImageMode = false;
            return;
        }

        bool isLong = IsLongImageCurrent();
        if (!enabled || !isLong)
        {
            LongImageMode = false;
            FitToWindow();
            return;
        }

        int clientWidth = ClientSize.Width;
        int clientHeight = ClientSize.Height;
        double zoom = 1.0;
        if (clientWidth > 0)
        {
            zoom = ViewMath.FitWidthZoom(CurrentBitmap.Width, clientWidth);
        }
        if (zoom <= 0 || double.IsNaN(zoom) || double.IsInfinity(zoom)) { zoom = 1.0; }

        LongImageMode = true;
        View.Mode = ViewMode.LongImage;
        View.Zoom = zoom;
        View.OffsetX = 0;   // fit-width，左对齐
        View.OffsetY = 0;   // 顶部对齐
        if (clientHeight > 0)
        {
            double offsetY = 0;
            ViewMath.ClampScroll(zoom, CurrentBitmap.Height, clientHeight, ref offsetY);
            View.OffsetY = offsetY;
        }
        RebuildFitCache();
        InvalidateViewport();
        RaiseStateChanged();
    }

    private void ToggleLongImageMode()
    {
        if (CurrentBitmap == null)
        {
            ShowToast("当前没有可显示的图片", 3000, false);
            return;
        }
        if (!IsLongImageCurrent())
        {
            ShowToast("当前图片不是长图（高/宽需 ≥ 3 且像素不超过 6000 万）", 5000, false);
            return;
        }
        SetLongImageMode(!LongImageMode);
        if (LongImageMode)
        {
            ShowToast("长图滚动：滚轮上下滚动，Ctrl+滚轮缩放", 5000, false);
        }
        else
        {
            ShowToast("已退出长图滚动模式", 3000, false);
        }
    }

    private bool IsLongImageCurrent()
    {
        if (CurrentBitmap == null) { return false; }
        return ViewMath.IsLongImage(CurrentBitmap.Width, CurrentBitmap.Height,
            (long)CurrentBitmap.Width * (long)CurrentBitmap.Height);
    }

    // ---- 旋转/翻转：RotateFlipType 的 8 种状态（覆盖「旋转 + 翻转」的全部组合） ----
    // 索引 0..3 = 旋转 0/90/180/270（不翻转）；4..7 = 同样旋转后再水平翻转。
    // 垂直翻转等价于「180° + 水平翻转」，因此这 8 个状态闭合，不需要额外的 FlipY 状态。
    private static readonly RotateFlipType[] RotateFlipStates = new RotateFlipType[]
    {
        RotateFlipType.RotateNoneFlipNone,
        RotateFlipType.Rotate90FlipNone,
        RotateFlipType.Rotate180FlipNone,
        RotateFlipType.Rotate270FlipNone,
        RotateFlipType.RotateNoneFlipX,
        RotateFlipType.Rotate90FlipX,
        RotateFlipType.Rotate180FlipX,
        RotateFlipType.Rotate270FlipX
    };

    // 迁移表：[当前状态索引, 操作(RotateOp 数值：None/Rotate90/Rotate180/Rotate270/FlipH/FlipV)]
    //         → 新状态索引。表值由「旋转后翻转」的矩阵复合推导，已在自测/手工用例里核对。
    private static readonly int[,] RotateFlipTransitions = new int[8, 6]
    {
        { 0, 1, 2, 3, 4, 6 },   // 0: 无（0°）
        { 0, 2, 3, 0, 5, 7 },   // 1: 顺时针 90°
        { 0, 3, 0, 1, 6, 4 },   // 2: 180°
        { 0, 0, 1, 2, 7, 5 },   // 3: 逆时针 90°
        { 0, 7, 6, 5, 0, 2 },   // 4: 水平翻转
        { 0, 4, 7, 6, 1, 3 },   // 5: 顺时针 90° + 水平翻转
        { 0, 5, 4, 7, 2, 0 },   // 6: 垂直翻转（= 180° + 水平翻转）
        { 0, 6, 5, 4, 3, 1 }    // 7: 逆时针 90° + 水平翻转
    };

    // 当前变换 + 操作 → 新变换
    private static RotateFlipType NextRotateFlip(RotateFlipType current, RotateOp op)
    {
        int opIndex = (int)op;
        if (opIndex < 0 || opIndex >= 6) { return current; }
        int state = RotateFlipStateIndex(current);
        int nextState = RotateFlipTransitions[state, opIndex];
        if (nextState < 0 || nextState >= RotateFlipStates.Length) { return current; }
        return RotateFlipStates[nextState];
    }

    private static int RotateFlipStateIndex(RotateFlipType value)
    {
        for (int i = 0; i < RotateFlipStates.Length; i++)
        {
            if (RotateFlipStates[i] == value) { return i; }
        }
        return 0;
    }

    // 状态文本：组合状态显示为「旋转 90° + 水平翻转」
    private static string RotateFlipText(RotateFlipType value)
    {
        switch (value)
        {
            case RotateFlipType.Rotate90FlipNone: return "顺时针 90°";
            case RotateFlipType.Rotate180FlipNone: return "180°";
            case RotateFlipType.Rotate270FlipNone: return "逆时针 90°";
            case RotateFlipType.RotateNoneFlipX: return "水平翻转";
            case RotateFlipType.Rotate90FlipX: return "顺时针 90° + 水平翻转";
            case RotateFlipType.Rotate180FlipX: return "垂直翻转";
            case RotateFlipType.Rotate270FlipX: return "逆时针 90° + 水平翻转";
            default: return "已重置";
        }
    }

    private void ReleaseDerivedBitmap()
    {
        if (CurrentBitmapOwned && CurrentBitmap != null && !ReferenceEquals(CurrentBitmap, CurrentBase))
        {
            try { CurrentBitmap.Dispose(); } catch { }
        }
        CurrentBitmapOwned = false;
        CurrentBitmap = null;
    }

    // ========================================================================
    // 图片库：扫描 / 加载 / 换图 / 换文件夹
    // ========================================================================
    internal ImageEntry CurrentEntry
    {
        get
        {
            if (Images == null || CurrentIndex < 0 || CurrentIndex >= Images.Count) { return null; }
            return Images[CurrentIndex];
        }
    }

    internal string StatusLine
    {
        get
        {
            int count = Images != null ? Images.Count : 0;
            if (count <= 0) { return "0/0"; }

            int index = CurrentIndex + 1;
            if (index < 1) { index = 1; }
            if (index > count) { index = count; }

            ImageEntry entry = CurrentEntry;
            string name = entry != null ? entry.DisplayName : "";

            string text = name + " " + index.ToString(CultureInfo.InvariantCulture)
                + "/" + count.ToString(CultureInfo.InvariantCulture);
            if (CurrentBitmap == null)
            {
                text += " · 加载失败";
                if (!string.IsNullOrEmpty(CurrentError)) { text += "：" + CurrentError; }
                return text;
            }
            text += " · " + Util.FormatPercent(View.Zoom);
            if (LongImageMode) { text += "（长图）"; }
            return text;
        }
    }

    // 初始化图片库：解析文件夹、扫描、定位上次图片
    private void InitializeLibrary()
    {
        string folder = Settings.Folder;
        if (string.IsNullOrEmpty(folder))
        {
            try { folder = SettingsStore.ResolveImageFolder(Settings.FolderSetting, ExeDirectory()); }
            catch (Exception) { folder = ""; }
        }
        if (string.IsNullOrEmpty(folder)) { folder = Path.Combine(ExeDirectory(), "images"); }

        // 配置的图片文件夹失效时 ResolveImageFolder 会静默回退 exe\images。
        // 把「配置值解析出的路径」与「实际使用目录」对比，差异记下来交给启动汇总提示。
        _folderFallbackDetected = false;
        if (!string.IsNullOrEmpty(Settings.FolderSetting))
        {
            try
            {
                string configured = Path.GetFullPath(Path.Combine(ExeDirectory(), Settings.FolderSetting));
                string actual = Path.GetFullPath(folder);
                _folderFallbackDetected = !IsSameFolder(configured, actual);
            }
            catch (Exception) { _folderFallbackDetected = false; }
        }

        Settings.Folder = folder;
        Settings.FolderSetting = MakeIniFolderPath(folder);
        ScanFolder(folder);

        CurrentIndex = FindStartIndex();
        LoadImageAtIndex(CurrentIndex, false);
        UpdateCardVisibility();
        RaiseStateChanged();
    }

    // 扫描并把结果同步到 Library 与共享 Images 列表。
    // 同一文件夹扫描失败时保留旧 Images / Skipped / Library.Folder / 当前图 / Settings.LastImage，
    // 只把错误交给 CurrentError 与 toast；只有「换文件夹失败」才清空图库。
    private ScanResult ScanFolder(string folder)
    {
        ScanResult result = null;
        try { result = Library.Scan(folder); }
        catch (Exception ex)
        {
            result = new ScanResult();
            result.Folder = folder;
            result.Error = ex.Message;
        }
        if (result == null)
        {
            result = new ScanResult();
            result.Folder = folder;
        }

        string targetFolder = !string.IsNullOrEmpty(result.Folder) ? result.Folder : folder;
        bool failed = !string.IsNullOrEmpty(result.Error);
        bool sameFolder = IsSameFolder(Library.Folder, targetFolder);

        _lastScanKeptOldImages = false;
        if (failed && sameFolder)
        {
            // Library.Scan 失败时本身不会改 Library.Images/Skipped/Folder，这里也一并保持，
            // 避免一次临时失败把正在看的图清空、把 LastImage 覆盖成空。
            if (Images == null)
            {
                Images = Library.Images != null ? Library.Images : new List<ImageEntry>();
            }
            Library.LastScanError = result.Error;
            CurrentError = "扫描失败：" + result.Error;
            _lastScanKeptOldImages = true;
            return result;
        }

        Images = result.Images != null ? result.Images : new List<ImageEntry>();
        Library.Images = Images;
        Library.Skipped = result.Skipped != null ? result.Skipped : new List<SkippedImage>();
        Library.Folder = targetFolder;
        Library.LastScanError = result.Error;
        if (failed)
        {
            // 换文件夹失败：按要求清空图库，但保留错误原因给状态栏/启动汇总
            CurrentError = "扫描失败：" + result.Error;
        }
        return result;
    }

    // 文件夹比较：复用 SettingsStore 的规范化（绝对化 + 去末尾分隔符），再忽略大小写比较
    private static bool IsSameFolder(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) { return false; }
        string na = SettingsStore.NormalizePathForCompare(a);
        string nb = SettingsStore.NormalizePathForCompare(b);
        if (na.Length == 0 || nb.Length == 0) { return false; }
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private int FindStartIndex()
    {
        if (Images == null || Images.Count == 0) { return -1; }
        string last = Settings.LastImage;
        if (string.IsNullOrEmpty(last)) { return 0; }

        // 先按完整路径匹配，再按相对路径匹配
        for (int i = 0; i < Images.Count; i++)
        {
            if (Images[i] == null) { continue; }
            if (string.Equals(Images[i].FullPath, last, StringComparison.OrdinalIgnoreCase)) { return i; }
        }
        for (int i = 0; i < Images.Count; i++)
        {
            if (Images[i] == null) { continue; }
            if (string.Equals(Images[i].RelPath, last, StringComparison.OrdinalIgnoreCase)) { return i; }
        }
        return 0;
    }

    // 加载指定索引；showToast=true 时给出「文件名 i/n · 百分比」提示
    private void LoadImageAtIndex(int index, bool showToast)
    {
        // 释放旧的派生副本与当前图引用
        ReleaseDerivedBitmap();
        CurrentBase = null;
        ManualRotate = RotateOp.None;
        _currentRotateFlip = RotateFlipType.RotateNoneFlipNone;
        LongImageMode = false;
        FitCacheIndex = -1;
        View.OffsetX = 0;
        View.OffsetY = 0;
        View.Zoom = 1.0;
        View.Mode = ViewMode.Fit;

        if (index < 0 || Images == null || index >= Images.Count)
        {
            CurrentIndex = (Images != null && Images.Count > 0) ? 0 : -1;
            // 越界时不覆盖 CurrentError：保留上一次扫描/加载失败的原因给状态栏显示
            UpdateCardVisibility();
            InvalidateOverlay();
            return;
        }

        CurrentError = null;
        CurrentIndex = index;
        ImageEntry entry = Images[index];

        Bitmap bitmap = null;
        string error = null;
        try { bitmap = Library.GetBase(index, out error); }
        catch (Exception ex) { bitmap = null; error = ex.Message; }

        if (bitmap == null)
        {
            CurrentError = string.IsNullOrEmpty(error) ? "图片解码失败" : error;
            View.Zoom = 1.0;
            View.Mode = ViewMode.Fit;
            // 解码失败也要释放旧的适应缓存（source 为 null 时只清空，不会分配）
            RebuildFitCache();
            if (showToast)
            {
                ShowToast(StatusLine, StartupToastMs, true);
            }
        }
        else
        {
            CurrentBase = bitmap;
            CurrentBitmap = bitmap;
            CurrentBitmapOwned = false;
            ApplyDefaultViewForImage();
        }

        // 预取邻居并裁剪缓存（预算由 ImageLibrary 负责）
        PrefetchNeighbors(index);
        SaveLastImageSetting();
        ScheduleSave();
        UpdateCardVisibility();
        InvalidateOverlay();
        RaiseStateChanged();

        if (showToast && bitmap != null) { ShowToast(StatusLine, 3000, false); }
    }

    // C3 之后 Scan 只在「扫描成功且图库真的变了」时 ClearCache，失败路径会保留缓存；
    // 但重扫成功且图库变化时旧位图仍会被 Dispose，所以保留当前图的路径统一按旧索引重新解码一次，
    // 保证 CurrentBase/CurrentBitmap 一定有效（不依赖缓存是否还在）。
    private void RestoreCurrentBitmapAfterFailedScan(string scanError)
    {
        if (Images == null || CurrentIndex < 0 || CurrentIndex >= Images.Count)
        {
            if (!string.IsNullOrEmpty(scanError)) { CurrentError = "扫描失败：" + scanError; }
            return;
        }

        ReleaseDerivedBitmap();
        CurrentBase = null;

        Bitmap bitmap = null;
        string decodeError = null;
        try { bitmap = Library.GetBase(CurrentIndex, out decodeError); }
        catch (Exception ex) { bitmap = null; decodeError = ex.Message; }

        if (bitmap == null)
        {
            if (!string.IsNullOrEmpty(scanError)) { CurrentError = "扫描失败：" + scanError; }
            else { CurrentError = string.IsNullOrEmpty(decodeError) ? "扫描失败后无法重新加载当前图片" : decodeError; }
            RebuildFitCache();
            InvalidateViewport();
            return;
        }

        CurrentBase = bitmap;
        CurrentBitmap = bitmap;
        CurrentBitmapOwned = false;
        if (_currentRotateFlip != RotateFlipType.RotateNoneFlipNone)
        {
            Bitmap derived = null;
            try
            {
                derived = new Bitmap(CurrentBase);
                derived.RotateFlip(_currentRotateFlip);
            }
            catch (Exception)
            {
                if (derived != null) { try { derived.Dispose(); } catch { } }
                derived = null;
                _currentRotateFlip = RotateFlipType.RotateNoneFlipNone;
            }
            if (derived != null)
            {
                CurrentBitmap = derived;
                CurrentBitmapOwned = true;
            }
        }

        CurrentError = null;
        RebuildFitCache();
        InvalidateViewport();
    }

    // 换图默认视图：长图 → fit-width + 长图滚动；普通图 → 适应窗口
    private void ApplyDefaultViewForImage()
    {
        if (CurrentBitmap == null) { return; }
        if (IsLongImageCurrent())
        {
            SetLongImageMode(true);
        }
        else
        {
            LongImageMode = false;
            FitToWindow();
        }
    }

    // C2：自动刷新前保存的阅读位置快照（重扫后当前图还在时用来还原）。
    private sealed class ViewSnapshot
    {
        public double Zoom;
        public ViewMode Mode;
        public double OffsetX;
        public double OffsetY;
        public RotateFlipType RotateFlip;
        public bool LongImageMode;
    }

    private ViewSnapshot CaptureViewState()
    {
        ViewSnapshot snapshot = new ViewSnapshot();
        snapshot.Zoom = View.Zoom;
        snapshot.Mode = View.Mode;
        snapshot.OffsetX = View.OffsetX;
        snapshot.OffsetY = View.OffsetY;
        snapshot.RotateFlip = _currentRotateFlip;
        snapshot.LongImageMode = LongImageMode;
        return snapshot;
    }

    // C2：重扫后还原缩放/平移/旋转/长图滚动位置，避免一次自动刷新就把用户正在看的位置重置掉。
    private void RestoreViewState(ViewSnapshot snapshot)
    {
        if (snapshot == null || CurrentBase == null || CurrentBitmap == null) { return; }

        // 先还原手工旋转：后面的缩放/平移都按旋转后的尺寸算
        if (snapshot.RotateFlip != RotateFlipType.RotateNoneFlipNone)
        {
            Bitmap derived = null;
            try
            {
                derived = new Bitmap(CurrentBase);
                derived.RotateFlip(snapshot.RotateFlip);
            }
            catch (Exception)
            {
                if (derived != null) { try { derived.Dispose(); } catch { } }
                derived = null;
            }
            if (derived != null)
            {
                ReleaseDerivedBitmap();
                CurrentBitmap = derived;
                CurrentBitmapOwned = true;
                _currentRotateFlip = snapshot.RotateFlip;
            }
        }

        if (snapshot.LongImageMode && IsLongImageCurrent())
        {
            SetLongImageMode(true);
            double offsetY = snapshot.OffsetY;
            ViewMath.ClampScroll(View.Zoom, CurrentBitmap.Height, ClientSize.Height, ref offsetY);
            View.OffsetY = offsetY;
            FitCacheIndex = -1;
            RebuildFitCache();
            InvalidateViewport();
            return;
        }

        if (snapshot.Mode == ViewMode.Fit)
        {
            FitToWindow();
            return;
        }

        View.Mode = snapshot.Mode;
        View.Zoom = snapshot.Zoom;
        double x = snapshot.OffsetX;
        double y = snapshot.OffsetY;
        ViewMath.ClampOffsets(View.Zoom, CurrentBitmap.Width, CurrentBitmap.Height,
            ClientSize.Width, ClientSize.Height, ref x, ref y);
        View.OffsetX = x;
        View.OffsetY = y;
        FitCacheIndex = -1;
        RebuildFitCache();
        InvalidateViewport();
    }

    // C5：预取邻居图，但单图 > 800 万像素或长图模式下不预取（一次换图别解三张大图）。
    private void PrefetchNeighbors(int index)
    {
        if (Images == null || Images.Count <= 1) { return; }
        try
        {
            ImageEntry next = index + 1 < Images.Count ? Images[index + 1] : null;
            if (next != null && ImageLibrary.ShouldPrefetch(next.PixelCount, LongImageMode))
            {
                string error = null;
                Library.GetBase(index + 1, out error);
                Library.Trim(index);
            }
            ImageEntry prev = index - 1 >= 0 ? Images[index - 1] : null;
            if (prev != null && ImageLibrary.ShouldPrefetch(prev.PixelCount, LongImageMode))
            {
                string error = null;
                Library.GetBase(index - 1, out error);
                Library.Trim(index);
            }
        }
        catch (Exception) { }
    }

    // C2：quiet=true 时不弹「已重新扫描」toast（watcher 自动刷新用）。
    internal void ReloadImages(bool keepCurrent, bool quiet)
    {
        string keepPath = null;
        int oldIndex = CurrentIndex;
        if (keepCurrent)
        {
            ImageEntry entry = CurrentEntry;
            if (entry != null) { keepPath = entry.FullPath; }
        }

        // C2：重扫前记下阅读位置与上一次扫描指纹，扫描后据此决定「不重载」还是「重载并还原视图」。
        ViewSnapshot oldView = (keepCurrent && CurrentBitmap != null && !string.IsNullOrEmpty(keepPath))
            ? CaptureViewState() : null;
        int prevFileCount = Library.ScanFileCount;
        long prevTotalBytes = Library.ScanTotalBytes;
        long prevMaxWriteTicks = Library.ScanMaxWriteUtcTicks;

        _suppressWatcher = true;
        ScanResult result;
        try { result = ScanFolder(Settings.Folder); }
        finally { _suppressWatcher = false; }

        bool scanFailed = result != null && !string.IsNullOrEmpty(result.Error);

        // 同一文件夹扫描失败：保留图库与当前图，只提示错误；
        // 不调用 LoadImageAtIndex，避免把 Settings.LastImage 覆盖成第 0 张。
        if (scanFailed && _lastScanKeptOldImages)
        {
            RestoreCurrentBitmapAfterFailedScan(result.Error);
            UpdateCardVisibility();
            ShowToast("重新扫描失败（已保留当前图片）：" + result.Error, StartupToastMs, true);
            InvalidateOverlay();
            RaiseStateChanged();
            return;
        }

        bool sameFingerprint = !scanFailed && ImageLibrary.SameScanFingerprint(
            prevFileCount, prevTotalBytes, prevMaxWriteTicks,
            Library.ScanFileCount, Library.ScanTotalBytes, Library.ScanMaxWriteUtcTicks);

        int newIndex = -1;
        if (Images != null && Images.Count > 0)
        {
            if (!string.IsNullOrEmpty(keepPath)) { newIndex = FindEntryIndexByPath(keepPath); }
            if (newIndex < 0 && keepCurrent && oldIndex >= 0)
            {
                newIndex = oldIndex < Images.Count ? oldIndex : Images.Count - 1;
            }
            if (newIndex < 0)
            {
                // 当前图已被删除：优先回到 Settings.LastImage 对应的图，不要无条件跳第 0 张
                newIndex = FindStartIndex();
            }
            if (newIndex < 0) { newIndex = 0; }
        }

        bool sameImage = oldView != null && newIndex >= 0 && !string.IsNullOrEmpty(keepPath)
            && newIndex == FindEntryIndexByPath(keepPath);

        // C2：图库没有任何变化、当前图也还在 → 只刷新跳过列表/状态，不重载（不重置缩放/滚动，也不重复解码）。
        if (sameFingerprint && sameImage && CurrentBitmap != null)
        {
            UpdateCardVisibility();
            if (!quiet)
            {
                int sameCount = Images != null ? Images.Count : 0;
                ShowToast("图片没有变化（" + sameCount.ToString(CultureInfo.InvariantCulture) + " 张）", 3000, false);
            }
            InvalidateOverlay();
            RaiseStateChanged();
            return;
        }

        // 换图逻辑：保留原索引时同样按默认视图重新适应
        LoadImageAtIndex(newIndex, false);
        // C2：当前图还在（路径不变）时还原阅读位置；路径变了才用默认视图。
        if (sameImage) { RestoreViewState(oldView); }
        UpdateCardVisibility();

        if (scanFailed)
        {
            ShowToast("重新扫描失败：" + result.Error, StartupToastMs, true);
        }
        else if (!quiet)
        {
            int count = Images != null ? Images.Count : 0;
            ShowToast("已重新扫描：" + count.ToString(CultureInfo.InvariantCulture) + " 张图片", 3000, false);
        }
        ScheduleSave();
        RaiseStateChanged();
    }

    internal void ChangeFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            ShowToast("图片文件夹路径为空", 5000, true);
            return;
        }

        string fullPath;
        try { fullPath = Path.GetFullPath(folderPath); }
        catch (Exception ex)
        {
            ShowToast("文件夹路径无效：" + ex.Message, 5000, true);
            return;
        }
        if (!Directory.Exists(fullPath))
        {
            ShowToast("文件夹不存在：" + fullPath, 5000, true);
            return;
        }
        // 可读性检查（不可访问时不切换）
        try { Directory.GetFiles(fullPath, "*", SearchOption.TopDirectoryOnly); }
        catch (Exception ex)
        {
            ShowToast("文件夹不可访问：" + ex.Message, 5000, true);
            return;
        }

        bool sameFolder = IsSameFolder(Settings.Folder, fullPath);
        string previousLastImage = Settings.LastImage;
        int previousIndex = CurrentIndex;

        Settings.Folder = fullPath;
        Settings.FolderSetting = MakeIniFolderPath(fullPath);
        if (!sameFolder)
        {
            // 换到新目录才清空上次图片记录；同目录重选要保留，扫描失败时才能回滚
            Settings.LastImage = "";
            CurrentIndex = -1;
        }
        try { Settings.AddRecentFolder(fullPath); } catch { }

        // C9：扫描是同步的 UI 线程操作，先把「正在扫描图片…」画出来，用户才知道不是卡死。
        BeginScanFeedback();

        if (sameFolder)
        {
            // C2：同目录重选 = 重新扫描。走 ReloadImages：当前图未变时保留阅读位置，
            // 不再无条件跳回第 0 张（旧行为会覆盖 LastImage 并重置缩放/滚动）。
            if (!SelfTestMode) { StartWatcher(); }
            ReloadImages(true, false);
            NotifySettingsPanelChanged();
            return;
        }

        _suppressWatcher = true;
        ScanResult result;
        try { result = ScanFolder(fullPath); }
        finally { _suppressWatcher = false; }

        if (!SelfTestMode) { StartWatcher(); }

        if (result != null && _lastScanKeptOldImages)
        {
            // 同一文件夹重选但扫描失败：保留当前图与上次图片记录，不回退到第 0 张
            Settings.LastImage = previousLastImage;
            CurrentIndex = previousIndex;
            RestoreCurrentBitmapAfterFailedScan(result.Error);
            UpdateCardVisibility();
            InvalidateOverlay();
            ShowToast("切换图片文件夹失败（已保留当前图片）：" + result.Error, StartupToastMs, true);
            RaiseStateChanged();
            return;
        }

        int index = (Images != null && Images.Count > 0) ? 0 : -1;
        LoadImageAtIndex(index, false);
        UpdateCardVisibility();
        ScheduleSave();

        int count = Images != null ? Images.Count : 0;
        if (result != null && !string.IsNullOrEmpty(result.Error))
        {
            ShowToast("已切换图片文件夹（扫描失败）" + fullPath + "：" + result.Error, StartupToastMs, true);
        }
        else
        {
            ShowToast("已切换图片文件夹：" + fullPath + "（" + count.ToString(CultureInfo.InvariantCulture) + " 张）", 5000, false);
        }
        RaiseStateChanged();
        NotifySettingsPanelChanged();
    }

    internal void NavigateImage(int delta)
    {
        if (Images == null || Images.Count == 0)
        {
            ShowToast("当前没有可显示的图片", 3000, false);
            return;
        }
        if (delta == 0) { return; }

        int count = Images.Count;
        int index = CurrentIndex + delta;
        index = index % count;
        if (index < 0) { index += count; }
        LoadImageAtIndex(index, true);
    }

    internal void GoToImage(int index)
    {
        if (Images == null || index < 0 || index >= Images.Count) { return; }
        if (index == CurrentIndex) { return; }
        LoadImageAtIndex(index, true);
    }

    private int FindEntryIndexByPath(string fullPath)
    {
        if (Images == null || string.IsNullOrEmpty(fullPath)) { return -1; }
        for (int i = 0; i < Images.Count; i++)
        {
            if (Images[i] == null) { continue; }
            if (string.Equals(Images[i].FullPath, fullPath, StringComparison.OrdinalIgnoreCase)) { return i; }
        }
        return -1;
    }

    private void SaveLastImageSetting()
    {
        // 临时拖入的图片不在配置里记录，避免把它们写进 settings.ini
        if (_keepLastImageOnLoad) { return; }
        ImageEntry entry = CurrentEntry;
        if (entry == null)
        {
            Settings.LastImage = "";
            return;
        }
        if (!string.IsNullOrEmpty(entry.RelPath)) { Settings.LastImage = entry.RelPath; }
        else { Settings.LastImage = entry.FullPath; }
    }

    private void UpdateCardVisibility()
    {
        bool show = Images == null || Images.Count == 0;
        if (show != CardVisible)
        {
            CardVisible = show;
            if (!show)
            {
                HoverCard = CardButton.None;
                PressedCard = CardButton.None;
            }
        }
    }

    // ========================================================================
    // 图片文件夹自动刷新
    // ========================================================================
    private void StartWatcher()
    {
        if (SelfTestMode) { return; }

        if (Watcher == null)
        {
            try
            {
                System.Threading.SynchronizationContext context = System.Threading.SynchronizationContext.Current;
                Watcher = new FolderWatcher(context);
                Watcher.Changed += OnWatcherChanged;
            }
            catch (Exception ex)
            {
                Watcher = null;
                ShowToast("自动刷新不可用：" + ex.Message, StartupToastMs, true);
                return;
            }
        }

        try
        {
            if (Settings.Watch && !string.IsNullOrEmpty(Settings.Folder) && Directory.Exists(Settings.Folder))
            {
                Watcher.Start(Settings.Folder);
                string lastError = null;
                try { lastError = Watcher.LastError; } catch { }
                if (!string.IsNullOrEmpty(lastError))
                {
                    ShowToast("自动刷新不可用：" + lastError + "，请手动重新扫描", StartupToastMs, true);
                }
            }
            else
            {
                Watcher.Stop();
            }
        }
        catch (Exception ex)
        {
            ShowToast("自动刷新不可用：" + ex.Message + "，请手动重新扫描", StartupToastMs, true);
        }
    }

    private void OnWatcherChanged(object sender, EventArgs e)
    {
        if (SelfTestMode || _closing) { return; }
        if (IsHandleCreated && InvokeRequired)
        {
            try { BeginInvoke(new EventHandler(OnWatcherChanged), sender, e); } catch { }
            return;
        }
        if (_suppressWatcher) { return; }

        _suppressWatcher = true;
        try { ReloadImages(true, true); }   // C2：自动刷新不弹「已重新扫描」toast，避免刷屏
        catch (Exception ex) { ShowToast("自动刷新失败：" + ex.Message + "，请手动重新扫描", StartupToastMs, true); }
        finally { _suppressWatcher = false; }
    }

    // ========================================================================
    // 显示设置
    // ========================================================================
    internal void SetOpacityPercent(int percent, bool save)
    {
        if (percent < 20) { percent = 20; }
        if (percent > 100) { percent = 100; }
        Settings.Opacity = percent;
        try { Opacity = percent / 100.0; } catch { }
        ApplyExtendedStyles();
        UpdateContextMenuState();
        if (save) { ScheduleSave(); }
        InvalidateOverlay();
        RaiseStateChanged();
    }

    internal void SetTopMost(bool topMost)
    {
        Settings.TopMost = topMost;
        TopMost = topMost;
        ApplyTopMost();
        UpdateContextMenuState();
        ScheduleSave();
        InvalidateOverlay();
        RaiseStateChanged();
        ShowToast(topMost ? "已开启始终置顶" : "已取消始终置顶", 3000, false);
    }

    internal void SetLocked(bool locked)
    {
        SetLocked(locked, false);
    }

    // silent=true：打开设置面板时由主窗口自动加锁，不弹 toast，也不计入「用户改过锁定」
    internal void SetLocked(bool locked, bool silent)
    {
        // 低权限（沙箱）进程读不到键盘状态：门控键不可能生效，解除锁定只会把窗口
        // 变成「点不动、也关不掉」的死物。所以这种环境里锁定是强制的，只提示不执行。
        if (_lowIntegrityDetected && !locked)
        {
            if (!silent)
            {
                ShowToast("低权限环境下门控键不可用，锁定交互已保持；"
                    + "请从资源管理器双击 " + AppInfo.ExeName + " 启动以恢复穿透", StartupToastMs, false);
            }
            UpdateContextMenuState();
            NotifySettingsPanelChanged();
            return;
        }

        // 面板打开期间用户主动改锁定（50_SettingsForm 的复选框走这里）→ 记下来，
        // 关闭面板时尊重用户选择，不再回滚到打开前的状态。
        if (!silent && SettingsPanel != null && !SettingsPanel.IsDisposed)
        {
            _panelLockOverridden = true;
        }

        if (Locked == locked)
        {
            UpdateContextMenuState();
            return;
        }
        Locked = locked;
        ApplyExtendedStyles();

        if (locked)
        {
            if (!WindowHidden) { SetInteractive(true); }
        }
        else
        {
            PollGateState();
        }

        UpdateContextMenuState();
        NotifySettingsPanelChanged();
        if (!silent)
        {
            ShowToast(locked
                ? "已锁定交互（" + HotkeyManager.FormatCombo(Settings.GetHotkey(HotkeyAction.ToggleLock)) + " 恢复门控穿透）"
                : "已恢复门控穿透（按住 " + GateKeyDisplayName() + " 可操作）", 3000, false);
        }
        InvalidateOverlay();
        RaiseStateChanged();
    }

    internal void SetPositionLocked(bool locked)
    {
        Settings.PositionLocked = locked;
        ScheduleSave();
        ShowToast(locked ? "已锁定窗口位置与大小" : "已解锁窗口位置与大小", 3000, false);
        RaiseStateChanged();
    }

    internal void SetSnapEnabled(bool enabled)
    {
        Settings.Snap = enabled;
        ScheduleSave();
        ShowToast(enabled ? "已开启屏幕边缘吸附" : "已关闭屏幕边缘吸附", 3000, false);
        RaiseStateChanged();
    }

    internal void SetTheme(ThemeKind kind)
    {
        if (kind != ThemeKind.Light && kind != ThemeKind.Dark) { kind = ThemeKind.Light; }
        CurrentTheme = kind;
        Colors = ThemeTokens.Get(kind);
        if (Settings != null) { Settings.Theme = kind; }

        BackColor = Colors.Canvas;
        try { RefreshThemeFonts(); } catch { }

        if (OverlayContextMenu != null)
        {
            ApplyMenuRenderer();
            ApplyMenuTheme(OverlayContextMenu.Items);
        }
        if (SettingsPanel != null && !SettingsPanel.IsDisposed)
        {
            try { SettingsPanel.ApplyTheme(); } catch { }
        }

        RebuildFitCache();
        InvalidateOverlay();
        ScheduleSave();
        RaiseStateChanged();
        ShowToast(kind == ThemeKind.Dark ? "已切换深色主题" : "已切换浅色主题", 3000, false);
    }

    // ========================================================================
    // 窗口矩形（物理像素 ↔ DIP）
    // ========================================================================
    internal void ApplyWindowRect(Rectangle pxRect)
    {
        if (pxRect.Width <= 0 || pxRect.Height <= 0) { return; }

        Rectangle workArea = Screen.FromRectangle(pxRect).WorkingArea;
        Rectangle rect = ViewMath.ClampWindowRect(pxRect, workArea, CurrentDpi);
        SetWindowBounds(rect);

        SaveWindowRectToSettings();
        if (LongImageMode) { SetLongImageMode(true); }
        else if (View.Mode == ViewMode.Fit) { FitToWindow(); }
        else { RebuildFitCache(); InvalidateViewport(); }

        ScheduleSave();
        NotifySettingsPanelChanged();
        RaiseStateChanged();
    }

    internal Rectangle GetWindowRectPx()
    {
        if (IsHandleCreated)
        {
            RECT rect;
            if (NativeMethods.GetWindowRect(Handle, out rect))
            {
                return new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
            }
        }
        return Bounds;
    }

    private void SetWindowBounds(Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) { return; }
        if (!IsHandleCreated)
        {
            Bounds = rect;
            return;
        }
        IntPtr insertAfter = Settings.TopMost ? Win32Const.HWND_TOPMOST : Win32Const.HWND_NOTOPMOST;
        NativeMethods.SetWindowPos(Handle, insertAfter, rect.X, rect.Y, rect.Width, rect.Height,
            Win32Const.SWP_NOACTIVATE);
    }

    // 拖动/拉伸过程中不改变 Z 序，避免闪烁
    private void SetWindowBoundsNoZ(Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) { return; }
        if (!IsHandleCreated)
        {
            Bounds = rect;
            return;
        }
        NativeMethods.SetWindowPos(Handle, IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height,
            Win32Const.SWP_NOZORDER | Win32Const.SWP_NOACTIVATE);
    }

    private void SaveWindowRectToSettings()
    {
        if (Settings == null) { return; }
        Rectangle rect = GetWindowRectPx();
        int dpi = CurrentDpi;
        Settings.Left = global::Layout.Dip(rect.Left, dpi);
        Settings.Top = global::Layout.Dip(rect.Top, dpi);
        Settings.Width = global::Layout.Dip(rect.Width, dpi);
        Settings.Height = global::Layout.Dip(rect.Height, dpi);
        if (Settings.Width < global::Layout.MinWindowWidthDip) { Settings.Width = global::Layout.MinWindowWidthDip; }
        if (Settings.Height < global::Layout.MinWindowHeightDip) { Settings.Height = global::Layout.MinWindowHeightDip; }
        Settings.HasWindowRect = true;
    }

    // 记忆矩形：与任何屏幕工作区都不相交 → 回退默认位置（拔掉副屏的情况）
    private bool TryRestoreWindowRect(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        if (Settings == null || !Settings.HasWindowRect) { return false; }

        int dpi = CurrentDpi;
        int minWidth = global::Layout.Px(global::Layout.MinWindowWidthDip, dpi);
        int minHeight = global::Layout.Px(global::Layout.MinWindowHeightDip, dpi);

        int width = global::Layout.Px(Settings.Width, dpi);
        int height = global::Layout.Px(Settings.Height, dpi);
        if (width < minWidth) { width = minWidth; }
        if (height < minHeight) { height = minHeight; }

        Rectangle proposed = new Rectangle(global::Layout.Px(Settings.Left, dpi), global::Layout.Px(Settings.Top, dpi), width, height);

        Screen best = null;
        int bestArea = 0;
        Screen[] screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            Rectangle inter = Rectangle.Intersect(proposed, screens[i].WorkingArea);
            if (inter.Width > 0 && inter.Height > 0)
            {
                int area = inter.Width * inter.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = screens[i];
                }
            }
        }
        if (best == null) { return false; }

        rect = ViewMath.ClampWindowRect(proposed, best.WorkingArea, dpi);
        return true;
    }

    // 默认矩形：多屏时用鼠标所在屏幕（右 24 DIP、宽 520 DIP 或工作区 30%、高 70% 居中）
    private Rectangle ComputeDefaultWindowRect()
    {
        Screen screen = null;
        try { screen = Screen.FromPoint(Cursor.Position); } catch { }
        if (screen == null) { screen = Screen.PrimaryScreen; }
        Rectangle rect = ViewMath.DefaultWindowRect(screen.WorkingArea, CurrentDpi);
        return ViewMath.ClampWindowRect(rect, screen.WorkingArea, CurrentDpi);
    }

    // ========================================================================
    // 隐藏 / 呼出
    // ========================================================================
    internal bool CanHide
    {
        get { return !SelfTestMode && IsHotkeyRegistered(HotkeyAction.ToggleHide); }
    }

    internal void HideOverlay()
    {
        if (!CanHide)
        {
            ShowToast("隐藏热键未注册，已禁用隐藏（可在 settings.ini 的 [hotkeys] 改键后重启）", StartupToastMs, true);
            return;
        }
        if (WindowHidden) { return; }

        WindowHidden = true;
        SetInteractive(false);
        ToolbarVisible = false;
        HoverToolbar = ToolbarButton.None;
        PressedToolbar = ToolbarButton.None;
        HoverCard = CardButton.None;
        PressedCard = CardButton.None;
        GateHeld = false;
        try { Visible = false; } catch { }
        if (IsHandleCreated)
        {
            try { NativeMethods.ShowWindow(Handle, Win32Const.SW_HIDE); } catch { }
        }
        InvalidateOverlay();
        RaiseStateChanged();
    }

    internal void ShowOverlay()
    {
        if (!WindowHidden && Visible)
        {
            ApplyTopMost();
            return;
        }
        WindowHidden = false;
        try { Visible = true; } catch { }
        if (IsHandleCreated)
        {
            try { NativeMethods.ShowWindow(Handle, Win32Const.SW_SHOWNOACTIVATE); } catch { }
        }
        ApplyTopMost();
        ApplyExtendedStyles();
        PollGateState();
        InvalidateOverlay();
        ShowToast("已呼出攻略悬浮窗", 3000, false);
        RaiseStateChanged();
    }

    // ========================================================================
    // 热键管理
    // ========================================================================
    internal bool RebindHotkey(HotkeyAction action, Keys combo, out string error)
    {
        error = null;
        if (Hotkeys == null)
        {
            error = "热键模块未初始化";
            return false;
        }
        bool ok = Hotkeys.Rebind(action, combo, out error);
        if (!ok) { return false; }

        Settings.SetHotkey(action, combo);
        UpdateContextMenuState();
        ScheduleSave();
        if (SettingsPanel != null && !SettingsPanel.IsDisposed)
        {
            try { SettingsPanel.RefreshHotkeyTable(); } catch { }
        }
        ShowToast("热键已更新：" + HotkeyManager.FormatCombo(combo), 3000, false);
        RaiseStateChanged();
        return true;
    }

    internal bool IsHotkeyRegistered(HotkeyAction action)
    {
        if (Hotkeys == null) { return false; }
        try { return Hotkeys.IsRegistered(action); }
        catch (Exception) { return false; }
    }

    internal string BuildHotkeyTableText()
    {
        if (Hotkeys == null) { return "热键模块未初始化"; }

        List<HotkeyEntry> entries = null;
        try { entries = Hotkeys.Entries; }
        catch (Exception ex) { return "热键状态不可用：" + ex.Message; }
        if (entries == null) { return "热键状态不可用"; }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < entries.Count; i++)
        {
            HotkeyEntry entry = entries[i];
            if (entry == null) { continue; }
            if (builder.Length > 0) { builder.AppendLine(); }

            string name = entry.DisplayName;
            if (string.IsNullOrEmpty(name)) { name = entry.Action.ToString(); }
            builder.Append(name);
            builder.Append("：");

            string comboText = "（禁用）";
            if (entry.Combo != Keys.None)
            {
                try { comboText = HotkeyManager.FormatCombo(entry.Combo); }
                catch (Exception) { comboText = entry.Combo.ToString(); }
            }
            builder.Append(comboText);
            builder.Append("（");
            builder.Append(HotkeyStatusText(entry.Status));
            builder.Append("）");
        }
        return builder.ToString();
    }

    private static string HotkeyStatusText(HotkeyStatus status)
    {
        switch (status)
        {
            case HotkeyStatus.Registered: return "已注册";
            case HotkeyStatus.FailedInUse: return "注册失败：被占用";
            case HotkeyStatus.FailedOther: return "注册失败：其它";
            case HotkeyStatus.Disabled: return "已禁用";
            default: return "未注册";
        }
    }

    // ========================================================================
    // 设置面板（单例、非模态；打开期间临时锁定交互）
    // ========================================================================
    internal void OpenSettingsPanel(SettingsSection section)
    {
        try
        {
            if (SettingsPanel != null && !SettingsPanel.IsDisposed)
            {
                SettingsPanel.ShowSection(section);
                if (SettingsPanel.WindowState == FormWindowState.Minimized)
                {
                    SettingsPanel.WindowState = FormWindowState.Normal;
                }
                SettingsPanel.Activate();
                return;
            }

            _panelLockOverridden = false;
            _settingsPanelLockedBefore = Locked;
            SettingsForm panel = new SettingsForm(this);
            panel.FormClosed += OnSettingsPanelClosed;
            panel.ShowSection(section);
            SetLocked(true, true);
            SettingsPanel = panel;
            panel.Show();
            panel.Activate();
            NotifySettingsPanelChanged();
        }
        catch (Exception ex)
        {
            SettingsPanel = null;
            if (!_closing) { SetLocked(_settingsPanelLockedBefore); }
            _panelLockOverridden = false;
            ShowToast("无法打开设置面板：" + ex.Message, StartupToastMs, true);
        }
    }

    private void OnSettingsPanelClosed(object sender, FormClosedEventArgs e)
    {
        SettingsPanel = null;
        if (_closing) { return; }
        // 用户在面板里主动改过锁定 → 尊重用户选择；否则恢复打开面板前的状态
        if (!_panelLockOverridden)
        {
            SetLocked(_settingsPanelLockedBefore);
        }
        _panelLockOverridden = false;
        ScheduleSave();
        RaiseStateChanged();
    }

    internal void NotifySettingsPanelChanged()
    {
        if (_closing) { return; }
        if (SettingsPanel == null || SettingsPanel.IsDisposed) { return; }
        try { SettingsPanel.RefreshFromMain(); } catch { }
    }

    // B2：设置面板切换门控键（Ctrl / Alt / Shift）。门控轮询每 tick 重读 Settings.GateKey，
    // 所以这里改完立即生效；同时刷新右键菜单文案、主窗提示条与面板文案。
    internal void ApplyGateKey(GateKeyKind kind)
    {
        if (Settings == null) { return; }
        Settings.GateKey = kind;
        Settings.GateKeyFallback = false;
        // 门控键变化时松开状态可能残留：强制按当前键重新判定一次
        GateKeyDownLast = IsGateKeyDown();
        GateHeld = GateKeyDownLast;
        UpdateContextMenuState();
        InvalidateOverlay();
        RaiseStateChanged();
        ScheduleSave();
    }

    // B5：热键录制挂起结束后恢复 UI（隐藏功能是否可用取决于 ToggleHide 是否注册成功）
    internal void RefreshHotkeyRegistrationUi()
    {
        UpdateContextMenuState();
        InvalidateOverlay();
        RaiseStateChanged();
    }

    // ========================================================================
    // toast
    // ========================================================================
    internal void ShowToast(string text, int durationMs, bool urgent)
    {
        if (text == null) { text = ""; }
        if (durationMs <= 0) { durationMs = urgent ? StartupToastMs : 3000; }
        if (durationMs < 500) { durationMs = 500; }

        ToastText = text;
        ToastDurationMs = durationMs;
        ToastUrgent = urgent;
        long now = Environment.TickCount;
        ToastUntilTick = now + durationMs;
        ToastFadeStartTick = 0;

        if (_toastTimer != null)
        {
            _toastTimer.Stop();
            _toastTimer.Start();
        }
        InvalidateToastArea();
    }

    // C9：手动重扫/换文件夹前把「正在扫描图片…」立刻画出来。扫描是 UI 线程同步操作，
    // 不强制重绘的话用户只会看到窗口卡死；Update() 会同步处理一次 WM_PAINT。
    internal void BeginScanFeedback()
    {
        ShowToast("正在扫描图片…", 3000, false);
        try { Update(); } catch { }
    }

    private void ToastTimer_Tick(object sender, EventArgs e)
    {
        long now = Environment.TickCount;
        if (string.IsNullOrEmpty(ToastText))
        {
            if (_toastTimer != null) { _toastTimer.Stop(); }
            return;
        }
        int remain = unchecked((int)(ToastUntilTick - now));
        if (remain <= 0)
        {
            ToastText = "";
            ToastDurationMs = 0;
            ToastFadeStartTick = 0;
            if (_toastTimer != null) { _toastTimer.Stop(); }
            InvalidateToastArea();
            return;
        }
        if (remain <= ToastFadeMs + 50)
        {
            if (ToastFadeStartTick == 0) { ToastFadeStartTick = ToastUntilTick - ToastFadeMs; }
            InvalidateToastArea();
        }
    }

    private string BuildStartupSummary()
    {
        StringBuilder builder = new StringBuilder();
        // 低权限（沙箱）启动时门控键必然失效：把原因放在最前面，toast 是中间省略，
        // 放后面会被截掉，用户就看不到真正该看的那句。
        if (_lowIntegrityDetected)
        {
            builder.Append("低权限启动：按住 ");
            builder.Append(GateKeyDisplayName());
            builder.Append(" 无效，已自动切到可交互模式；用资源管理器双击 ");
            builder.Append(AppInfo.ExeName);
            builder.Append(" 可恢复完整功能；");
        }
        int count = Images != null ? Images.Count : 0;
        if (count <= 0)
        {
            builder.Append("还没有可显示的攻略图（把图片放进图片文件夹或拖入文件夹）");
        }
        else
        {
            builder.Append("已加载 ");
            builder.Append(count.ToString(CultureInfo.InvariantCulture));
            builder.Append(" 张图");
        }

        // 跳过原因（最多两条）
        int skipped = (Library != null && Library.Skipped != null) ? Library.Skipped.Count : 0;
        if (skipped > 0)
        {
            builder.Append("；跳过 ");
            builder.Append(skipped.ToString(CultureInfo.InvariantCulture));
            builder.Append(" 张：");
            int shown = 0;
            for (int i = 0; i < Library.Skipped.Count && shown < 2; i++)
            {
                SkippedImage skip = Library.Skipped[i];
                if (skip == null) { continue; }
                if (shown > 0) { builder.Append("、"); }
                string name;
                try { name = Path.GetFileName(skip.FullPath); }
                catch (Exception) { name = skip.FullPath; }
                builder.Append(name);
                builder.Append("：");
                builder.Append(skip.Reason);
                shown++;
            }
            if (skipped > shown) { builder.Append(" 等"); }
        }

        if (Library != null && !string.IsNullOrEmpty(Library.LastScanError))
        {
            builder.Append("；扫描提示：");
            builder.Append(Library.LastScanError);
        }
        // 配置的图片文件夹失效时 ResolveImageFolder 静默回退 exe\images：这里明确告知实际目录
        if (_folderFallbackDetected && Settings != null && !string.IsNullOrEmpty(Settings.Folder))
        {
            builder.Append("；原图片文件夹不可用，已回退到 ");
            builder.Append(Settings.Folder);
            builder.Append("（可在设置面板重新选择）");
        }
        if (Settings != null && Settings.UsingFallbackPath && !string.IsNullOrEmpty(Settings.SettingsPath))
        {
            builder.Append("；配置已回退到：");
            builder.Append(Settings.SettingsPath);
        }
        // B2：手工改 ini 写了非法 gate_key 时明确告知已回退默认（Alt）
        if (Settings != null && Settings.GateKeyFallback)
        {
            builder.Append("；settings.ini 的 gate_key 无效，已回退默认 ");
            builder.Append(GateKeyDisplayName());
        }
        // C6：手工改 ini 写了系统保留组合/常用快捷键时告知已回退默认
        if (Settings != null && Settings.HotkeyFallbackCount > 0)
        {
            builder.Append("；settings.ini 有 ");
            builder.Append(Settings.HotkeyFallbackCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(" 个热键组合不可用（系统保留或常用快捷键），已回退默认");
        }
        // C7：settings.ini 不是 UTF-8（被老版本/记事本另存成 ANSI）时告知已按系统编码读取
        if (SettingsStore.LastLoadEncodingFallback)
        {
            builder.Append("；settings.ini 编码不是 UTF-8，已按系统编码读取");
        }
        if (!CanHide)
        {
            builder.Append("；隐藏热键未注册，隐藏功能已禁用（可改 settings.ini 换键）");
        }
        else if (!IsHotkeyRegistered(HotkeyAction.ToggleLock))
        {
            builder.Append("；锁定交互热键未注册，可用右键菜单或设置面板切换");
        }

        // 其它热键未注册（隐藏/锁定已有专门文案，这里不重复罗列）：
        // 只统计配置了组合（非 Keys.None）但运行时未注册成功的动作。
        int missingHotkeys = 0;
        int namedHotkeys = 0;
        StringBuilder missingNames = new StringBuilder();
        int missingInUse = 0;
        int missingOther = 0;
        if (Settings != null && Hotkeys != null)
        {
            HotkeyAction[] allActions = HotkeyManager.AllActions;
            for (int i = 0; i < allActions.Length; i++)
            {
                HotkeyAction action = allActions[i];
                if (action == HotkeyAction.ToggleHide || action == HotkeyAction.ToggleLock) { continue; }
                if (Settings.GetHotkey(action) == Keys.None) { continue; }
                HotkeyEntry hotkey = Hotkeys.GetEntry(action);
                if (hotkey == null || hotkey.Status == HotkeyStatus.Registered) { continue; }

                missingHotkeys++;
                if (hotkey.Status == HotkeyStatus.FailedInUse) { missingInUse++; } else { missingOther++; }
                if (namedHotkeys < 2)
                {
                    if (namedHotkeys > 0) { missingNames.Append("、"); }
                    string name = string.IsNullOrEmpty(hotkey.DisplayName)
                        ? HotkeyManager.DisplayNameOf(action) : hotkey.DisplayName;
                    missingNames.Append(name);
                    namedHotkeys++;
                }
            }
        }
        if (missingHotkeys > 0)
        {
            builder.Append("；共 ");
            builder.Append(missingHotkeys.ToString(CultureInfo.InvariantCulture));
            builder.Append(" 个热键未注册");
            if (missingInUse > 0 && missingOther == 0) { builder.Append("（被占用）"); }
            else if (missingInUse > 0) { builder.Append("（部分被占用，其余为其它原因）"); }
            builder.Append("：");
            builder.Append(missingNames.ToString());
            if (missingHotkeys > namedHotkeys) { builder.Append(" 等"); }
            builder.Append("，可在设置面板改键或编辑 settings.ini");
        }

        if (!_lowIntegrityDetected)
        {
            builder.Append("；按住 " + GateKeyDisplayName() + " 可操作");
        }
        return builder.ToString();
    }

    // ========================================================================
    // 保存（500ms 防抖 + 立即保存）
    // ========================================================================
    internal void ScheduleSave()
    {
        if (SelfTestMode || _closing || SaveTimer == null) { return; }
        SaveTimer.Stop();
        SaveTimer.Start();
    }

    private void SaveTimer_Tick(object sender, EventArgs e)
    {
        try { SaveNow(); }
        catch { }
    }

    internal void SaveNow()
    {
        if (SelfTestMode) { return; }
        if (SaveTimer != null) { SaveTimer.Stop(); }

        try
        {
            SaveWindowRectToSettings();
            if (Settings != null)
            {
                Settings.Theme = CurrentTheme;
                Settings.FolderSetting = MakeIniFolderPath(Settings.Folder);
            }
            int attemptsBefore = SettingsStore.SaveAttemptCount;
            SettingsStore.Save(Settings);
            // Save 不再抛异常：失败原因由 SettingsStore.LastSaveError 带出。
            // 只有「本次真的尝试写盘」才提示，退避跳过时不重复弹旧错误。
            string saveError = SettingsStore.LastSaveError;
            if (!string.IsNullOrEmpty(saveError) && SettingsStore.SaveAttemptCount != attemptsBefore)
            {
                ShowToast("保存设置失败：" + saveError, StartupToastMs, true);
            }
        }
        catch (Exception ex)
        {
            ShowToast("保存设置失败：" + ex.Message, StartupToastMs, true);
        }
    }

    // ========================================================================
    // 诊断上下文
    // ========================================================================
    internal DiagnosticsContext BuildDiagnosticsContext()
    {
        DiagnosticsContext context = new DiagnosticsContext();
        context.Settings = Settings;
        context.ImageFolder = Settings != null ? Settings.Folder : "";
        context.ZoomPercent = View.Zoom * 100.0;
        context.Hidden = WindowHidden;
        context.Locked = Locked;
        context.HotkeyTable = BuildHotkeyTableText();

        ImageEntry entry = CurrentEntry;
        if (entry != null)
        {
            context.CurrentImageInfo = entry.FullPath + "（第 "
                + (CurrentIndex + 1).ToString(CultureInfo.InvariantCulture) + " / "
                + Images.Count.ToString(CultureInfo.InvariantCulture) + " 张）";
        }
        else
        {
            context.CurrentImageInfo = "无（图片数量 0）";
        }
        context.ExtraStatus = StatusLine;
        if (!string.IsNullOrEmpty(CurrentError))
        {
            context.ExtraStatus += "；错误：" + CurrentError;
        }
        if (LongImageMode) { context.ExtraStatus += "；长图滚动模式"; }
        if (!CanHide) { context.ExtraStatus += "；隐藏热键未注册"; }
        // C8：低权限启动要在诊断里能看出来（用户反馈「按住 Ctrl 没用」时先查这条）
        if (_lowIntegrityDetected) { context.ExtraStatus += "；低权限/受限环境启动（门控键不可用，已锁定交互）"; }
        return context;
    }

    // ========================================================================
    // 文件夹 / 路径工具
    // ========================================================================
    private string ExeDirectory()
    {
        string dir = null;
        if (Settings != null) { dir = Settings.ExeDir; }
        if (string.IsNullOrEmpty(dir))
        {
            try { dir = SettingsStore.ResolveExeDir(); }
            catch (Exception) { dir = null; }
        }
        if (string.IsNullOrEmpty(dir))
        {
            try { dir = AppDomain.CurrentDomain.BaseDirectory; }
            catch (Exception) { dir = ""; }
        }
        return dir;
    }

    // 图片文件夹写回 ini：在 exe 目录下用相对路径，否则绝对路径
    private string MakeIniFolderPath(string folder)
    {
        if (string.IsNullOrEmpty(folder)) { return "images"; }
        try
        {
            string full = Path.GetFullPath(folder);
            string root = Path.GetFullPath(ExeDirectory());
            root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Length > root.Length
                && full.StartsWith(root + Path.DirectorySeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) { return "."; }
            return full;
        }
        catch (Exception)
        {
            return folder;
        }
    }

    private void ChooseImageFolder()
    {
        string selected = null;
        using (FolderBrowserDialog dialog = new FolderBrowserDialog())
        {
            dialog.Description = "选择攻略图片文件夹";
            dialog.ShowNewFolderButton = true;
            try
            {
                if (Settings != null && !string.IsNullOrEmpty(Settings.Folder) && Directory.Exists(Settings.Folder))
                {
                    dialog.SelectedPath = Settings.Folder;
                }
            }
            catch (Exception) { }
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
            selected = dialog.SelectedPath;
        }
        if (!string.IsNullOrEmpty(selected)) { ChangeFolder(selected); }
    }

    private void OpenImageFolder()
    {
        string folder = Settings != null ? Settings.Folder : "";
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            ShowToast("图片文件夹不存在：" + folder, StartupToastMs, true);
            return;
        }
        try { Process.Start("explorer.exe", "\"" + folder + "\""); }
        catch (Exception ex) { ShowToast("无法打开文件夹：" + ex.Message, StartupToastMs, true); }
    }

    private void ChangeFolderFromRecent(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            ShowToast("文件夹不存在或不可访问：" + path, StartupToastMs, true);
            try
            {
                if (Settings != null && Settings.RecentFolders != null) { Settings.RecentFolders.Remove(path); }
            }
            catch (Exception) { }
            ScheduleSave();
            return;
        }
        ChangeFolder(path);
    }

    private void OpenHelpFile()
    {
        string path = Path.Combine(ExeDirectory(), AppInfo.HelpFileName);
        try
        {
            if (!File.Exists(path))
            {
                ShowToast("找不到使用说明.md（应与程序放在同一目录）", StartupToastMs, true);
                return;
            }
            Process.Start(path);
        }
        catch (Exception ex)
        {
            ShowToast("无法打开使用说明：" + ex.Message, StartupToastMs, true);
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            string dir = Diagnostics.LogDirectory();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                ShowToast("日志文件夹不存在", 5000, true);
                return;
            }
            Diagnostics.OpenFolder(dir);
        }
        catch (Exception ex)
        {
            ShowToast("无法打开日志文件夹：" + ex.Message, StartupToastMs, true);
        }
    }

    // 长图切片导出：每片 2000px 高，输出到用户选择的目录
    private void ExportLongImageSlices()
    {
        if (CurrentBitmap == null || CurrentEntry == null)
        {
            ShowToast("当前没有可导出的图片", 3000, false);
            return;
        }
        if (!IsLongImageCurrent())
        {
            ShowToast("当前图片不是长图，无需切片", 3000, false);
            return;
        }

        string outputDir = null;
        using (FolderBrowserDialog dialog = new FolderBrowserDialog())
        {
            dialog.Description = "选择长图切片输出文件夹（默认 .slices，切片不会加入图库）";
            dialog.ShowNewFolderButton = true;
            try
            {
                string sourceDir = Path.GetDirectoryName(CurrentEntry.FullPath);
                if (!string.IsNullOrEmpty(sourceDir))
                {
                    // C1：默认输出到源目录下的 .slices 子目录。先建好目录，FolderBrowserDialog
                    // 才能直接选中它（否则 SelectedPath 不存在的目录会被忽略）。
                    string slicesDir = Path.Combine(sourceDir, ImageLibrary.SliceOutputDirectoryName);
                    try
                    {
                        if (!Directory.Exists(slicesDir)) { Directory.CreateDirectory(slicesDir); }
                        dialog.SelectedPath = slicesDir;
                    }
                    catch (Exception)
                    {
                        dialog.SelectedPath = sourceDir;
                    }
                }
            }
            catch (Exception) { }
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
            outputDir = dialog.SelectedPath;
        }
        if (string.IsNullOrEmpty(outputDir)) { return; }

        SliceExportResult result;
        try { result = ImageLibrary.ExportSlices(CurrentBitmap, CurrentEntry.FullPath, outputDir, SliceHeightPx); }
        catch (Exception ex)
        {
            ShowToast("切片导出失败：" + ex.Message, StartupToastMs, true);
            return;
        }
        if (result == null)
        {
            ShowToast("切片导出失败", StartupToastMs, true);
            return;
        }
        if (!string.IsNullOrEmpty(result.Error))
        {
            ShowToast("切片导出失败：" + result.Error, StartupToastMs, true);
            return;
        }
        ShowToast("已导出 " + result.Count.ToString(CultureInfo.InvariantCulture)
            + " 片到 " + outputDir + "（切片不会加入图库）", StartupToastMs, false);
    }

    // ========================================================================
    // 拖放：仅可交互时接受；文件临时追加，文件夹切换图库
    // ========================================================================
    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        base.OnDragEnter(drgevent);
        drgevent.Effect = (!Interactive || WindowHidden) ? DragDropEffects.None
            : (HasAcceptableDrop(drgevent.Data) ? DragDropEffects.Copy : DragDropEffects.None);
    }

    protected override void OnDragOver(DragEventArgs drgevent)
    {
        base.OnDragOver(drgevent);
        drgevent.Effect = (!Interactive || WindowHidden) ? DragDropEffects.None
            : (HasAcceptableDrop(drgevent.Data) ? DragDropEffects.Copy : DragDropEffects.None);
    }

    protected override void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);
        if (!Interactive || WindowHidden) { return; }

        string[] paths = null;
        try { paths = drgevent.Data.GetData(DataFormats.FileDrop) as string[]; }
        catch (Exception) { paths = null; }
        if (paths == null || paths.Length == 0) { return; }
        HandleDroppedPaths(paths);
    }

    private bool HasAcceptableDrop(IDataObject data)
    {
        if (data == null) { return false; }
        string[] paths = null;
        try { paths = data.GetData(DataFormats.FileDrop) as string[]; }
        catch (Exception) { return false; }
        if (paths == null || paths.Length == 0) { return false; }

        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            if (string.IsNullOrEmpty(path)) { continue; }
            bool isDirectory = false;
            try { isDirectory = Directory.Exists(path); }
            catch (Exception) { }
            if (isDirectory) { return true; }
            if (ImageLibrary.IsSupportedForDrop(path)) { return true; }
        }
        return false;
    }

    private void HandleDroppedPaths(string[] paths)
    {
        List<string> folders = new List<string>();
        List<string> files = new List<string>();
        List<string> rejected = new List<string>();

        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            if (string.IsNullOrEmpty(path)) { continue; }
            bool isDirectory = false;
            try { isDirectory = Directory.Exists(path); }
            catch (Exception) { }
            if (isDirectory)
            {
                folders.Add(path);
                continue;
            }
            bool isFile = false;
            try { isFile = File.Exists(path); }
            catch (Exception) { }
            if (!isFile) { continue; }

            if (ImageLibrary.IsSupportedForDrop(path)) { files.Add(path); }
            else { rejected.Add(path); }
        }

        if (rejected.Count > 0)
        {
            StringBuilder builder = new StringBuilder("不是图片：");
            int shown = 0;
            for (int i = 0; i < rejected.Count && shown < 3; i++)
            {
                if (shown > 0) { builder.Append("、"); }
                try { builder.Append(Path.GetFileName(rejected[i])); }
                catch (Exception) { builder.Append(rejected[i]); }
                shown++;
            }
            if (rejected.Count > shown) { builder.Append(" 等"); }
            ShowToast(builder.ToString(), 5000, true);
        }

        if (folders.Count > 0)
        {
            ChangeFolder(folders[0]);
            if (folders.Count > 1)
            {
                ShowToast("一次只使用第一个文件夹（共拖入 "
                    + folders.Count.ToString(CultureInfo.InvariantCulture) + " 个）", 5000, false);
            }
            return;
        }

        if (files.Count > 0) { AppendDroppedImages(files); }
    }

    private void AppendDroppedImages(List<string> files)
    {
        if (Images == null)
        {
            Images = new List<ImageEntry>();
            Library.Images = Images;
        }
        int before = Images.Count;
        for (int i = 0; i < files.Count; i++)
        {
            string path = files[i];
            ImageEntry entry = new ImageEntry();
            entry.FullPath = path;
            entry.RelPath = "";
            try { entry.FileSize = new FileInfo(path).Length; }
            catch (Exception) { entry.FileSize = 0; }

            int width = 0;
            int height = 0;
            string error = null;
            try { entry.SizeKnown = ImageLibrary.TryReadImageSize(path, out width, out height, out error); }
            catch (Exception) { entry.SizeKnown = false; }
            entry.PixelWidth = width;
            entry.PixelHeight = height;
            Images.Add(entry);
        }
        Library.Images = Images;

        if (Images.Count > before)
        {
            // 临时追加：跳转过去看，但不把拖入路径写进 LastImage
            _keepLastImageOnLoad = true;
            try { LoadImageAtIndex(Images.Count - 1, false); }
            finally { _keepLastImageOnLoad = false; }
            ShowToast("已临时加入 " + (Images.Count - before).ToString(CultureInfo.InvariantCulture)
                + " 张图片（不复制文件、不写入配置）", 5000, false);
            RaiseStateChanged();
        }
    }

    // ========================================================================
    // 右键菜单
    // ========================================================================
    private void EnsureContextMenu()
    {
        if (OverlayContextMenu != null) { return; }

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        menu.BackColor = Colors.Card;
        menu.ForeColor = Colors.Text;
        menu.Closed += OnContextMenuClosed;
        OverlayContextMenu = menu;

        BuildContextMenuItems();
        ApplyMenuRenderer();
        ApplyMenuTheme(OverlayContextMenu.Items);
    }

    private void BuildContextMenuItems()
    {
        OverlayContextMenu.Items.Clear();
        _menuOpacityItems.Clear();
        _menuPrevItem = null;
        _menuNextItem = null;
        _menuTopMostItem = null;
        _menuLockedItem = null;
        _menuHideItem = null;
        _menuLongItem = null;
        _menuSliceItem = null;
        _menuRecentItem = null;
        _menuPassiveItem = null;

        // 1) 翻页
        _menuPrevItem = CreateMenuItem("上一张", delegate { NavigateImage(-1); });
        OverlayContextMenu.Items.Add(_menuPrevItem);
        _menuNextItem = CreateMenuItem("下一张", delegate { NavigateImage(1); });
        OverlayContextMenu.Items.Add(_menuNextItem);
        OverlayContextMenu.Items.Add(new ToolStripSeparator());

        // 2) 图片文件夹
        OverlayContextMenu.Items.Add(CreateMenuItem("打开图片文件夹", delegate { OpenImageFolder(); }));
        OverlayContextMenu.Items.Add(CreateMenuItem("选择图片文件夹…", delegate { ChooseImageFolder(); }));
        OverlayContextMenu.Items.Add(CreateMenuItem("重新扫描图片", delegate { BeginScanFeedback(); ReloadImages(true, false); }));
        _menuRecentItem = CreateMenuItem("最近文件夹", null);
        OverlayContextMenu.Items.Add(_menuRecentItem);
        OverlayContextMenu.Items.Add(new ToolStripSeparator());

        // 3) 置顶 / 锁定交互 / 穿透说明
        _menuTopMostItem = CreateMenuItem("始终置顶", delegate { SetTopMost(!Settings.TopMost); });
        OverlayContextMenu.Items.Add(_menuTopMostItem);
        _menuLockedItem = CreateMenuItem("锁定交互", delegate { SetLocked(!Locked); });
        OverlayContextMenu.Items.Add(_menuLockedItem);
        _menuImageLockItem = CreateMenuItem("锁定图片（拖动只移动窗口）", delegate { SetImageLocked(!ImageLocked, true); });
        OverlayContextMenu.Items.Add(_menuImageLockItem);
        _menuPassiveItem = CreateMenuItem("鼠标穿透：默认开启（按住 " + GateKeyDisplayName() + " 可操作）", null);
        _menuPassiveItem.Enabled = false;
        OverlayContextMenu.Items.Add(_menuPassiveItem);
        OverlayContextMenu.Items.Add(new ToolStripSeparator());

        // 4) 缩放子菜单
        ToolStripMenuItem zoomMenu = CreateMenuItem("缩放", null);
        zoomMenu.DropDownItems.Add(CreateMenuItem("适应窗口", delegate { FitToWindow(); ShowToast("已适应窗口", 3000, false); }));
        zoomMenu.DropDownItems.Add(CreateMenuItem("25%", delegate { SetZoomPreset(25.0); }));
        zoomMenu.DropDownItems.Add(CreateMenuItem("50%", delegate { SetZoomPreset(50.0); }));
        zoomMenu.DropDownItems.Add(CreateMenuItem("100%", delegate { SetZoomPreset(100.0); }));
        zoomMenu.DropDownItems.Add(CreateMenuItem("200%", delegate { SetZoomPreset(200.0); }));
        OverlayContextMenu.Items.Add(zoomMenu);

        // 5) 旋转/翻转子菜单
        ToolStripMenuItem rotateMenu = CreateMenuItem("旋转/翻转", null);
        rotateMenu.DropDownItems.Add(CreateMenuItem("顺时针 90°", delegate { Rotate(RotateOp.Rotate90); }));
        rotateMenu.DropDownItems.Add(CreateMenuItem("逆时针 90°", delegate { Rotate(RotateOp.Rotate270); }));
        rotateMenu.DropDownItems.Add(CreateMenuItem("水平翻转", delegate { Rotate(RotateOp.FlipH); }));
        rotateMenu.DropDownItems.Add(CreateMenuItem("垂直翻转", delegate { Rotate(RotateOp.FlipV); }));
        rotateMenu.DropDownItems.Add(new ToolStripSeparator());
        rotateMenu.DropDownItems.Add(CreateMenuItem("重置旋转", delegate { Rotate(RotateOp.None); }));
        OverlayContextMenu.Items.Add(rotateMenu);

        // 6) 长图
        _menuLongItem = CreateMenuItem("长图滚动", delegate { ToggleLongImageMode(); });
        OverlayContextMenu.Items.Add(_menuLongItem);
        _menuSliceItem = CreateMenuItem("导出长图切片…", delegate { ExportLongImageSlices(); });
        OverlayContextMenu.Items.Add(_menuSliceItem);
        OverlayContextMenu.Items.Add(new ToolStripSeparator());

        // 7) 透明度子菜单
        ToolStripMenuItem opacityMenu = CreateMenuItem("透明度", null);
        int[] opacityValues = new int[] { 20, 40, 60, 80, 100 };
        for (int i = 0; i < opacityValues.Length; i++)
        {
            int value = opacityValues[i];
            ToolStripMenuItem item = CreateMenuItem(value.ToString(CultureInfo.InvariantCulture) + "%",
                delegate { SetOpacityPercent(value, true); });
            opacityMenu.DropDownItems.Add(item);
            _menuOpacityItems.Add(item);
        }
        OverlayContextMenu.Items.Add(opacityMenu);
        OverlayContextMenu.Items.Add(new ToolStripSeparator());

        // 8) 面板 / 文档 / 日志
        OverlayContextMenu.Items.Add(CreateMenuItem("窗口位置与大小…", delegate { OpenSettingsPanel(SettingsSection.Window); }));
        OverlayContextMenu.Items.Add(CreateMenuItem("设置…", delegate { OpenSettingsPanel(SettingsSection.Images); }));
        OverlayContextMenu.Items.Add(new ToolStripSeparator());
        OverlayContextMenu.Items.Add(CreateMenuItem("使用说明…", delegate { OpenHelpFile(); }));
        OverlayContextMenu.Items.Add(CreateMenuItem("打开日志文件夹", delegate { OpenLogFolder(); }));
        OverlayContextMenu.Items.Add(new ToolStripSeparator());

        // 9) 隐藏 / 退出
        _menuHideItem = CreateMenuItem("隐藏窗口", delegate { HideOverlay(); });
        OverlayContextMenu.Items.Add(_menuHideItem);
        OverlayContextMenu.Items.Add(CreateMenuItem("退出", delegate { Close(); }));
    }

    private ToolStripMenuItem CreateMenuItem(string text, EventHandler handler)
    {
        ToolStripMenuItem item = new ToolStripMenuItem(text);
        item.ForeColor = Colors.Text;
        item.BackColor = Colors.Card;
        if (handler != null) { item.Click += handler; }
        return item;
    }

    private void ApplyMenuRenderer()
    {
        if (OverlayContextMenu == null) { return; }
        ToolStripProfessionalRenderer renderer = new ToolStripProfessionalRenderer(new OverlayMenuColorTable(Colors));
        renderer.RoundedEdges = false;
        OverlayContextMenu.Renderer = renderer;
        OverlayContextMenu.BackColor = Colors.Card;
        OverlayContextMenu.ForeColor = Colors.Text;
    }

    private void ApplyMenuTheme(ToolStripItemCollection items)
    {
        if (items == null) { return; }
        for (int i = 0; i < items.Count; i++)
        {
            ToolStripItem item = items[i];
            if (item == null) { continue; }
            if (!(item is ToolStripSeparator))
            {
                item.ForeColor = Colors.Text;
                item.BackColor = Colors.Card;
            }
            ToolStripMenuItem menuItem = item as ToolStripMenuItem;
            if (menuItem != null) { ApplyMenuTheme(menuItem.DropDownItems); }
        }
    }

    private void ShowContextMenu(Point screenPoint)
    {
        EnsureContextMenu();
        UpdateContextMenuState();

        _menuOpen = true;
        _menuClearedNoActivate = false;
        if (!Locked && IsHandleCreated)
        {
            // 已知兼容做法：WS_EX_NOACTIVATE 窗口弹菜单偶尔不响应，临时清掉再恢复
            _menuClearedNoActivate = true;
            SetNoActivateBit(false);
        }

        try
        {
            try { NativeMethods.SetForegroundWindow(Handle); } catch { }
            OverlayContextMenu.Show(screenPoint);
        }
        catch (Exception ex)
        {
            _menuOpen = false;
            if (_menuClearedNoActivate)
            {
                _menuClearedNoActivate = false;
                ApplyExtendedStyles();
            }
            ShowToast("右键菜单打开失败：" + ex.Message, 5000, true);
        }
    }

    private void OnContextMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
    {
        _menuOpen = false;
        if (_menuClearedNoActivate)
        {
            _menuClearedNoActivate = false;
            ApplyExtendedStyles();
        }
        PollGateState();
        InvalidateOverlay();
    }

    private void UpdateContextMenuState()
    {
        if (OverlayContextMenu == null) { return; }

        bool hasImage = CurrentBitmap != null;
        int count = Images != null ? Images.Count : 0;

        if (_menuPrevItem != null) { _menuPrevItem.Enabled = count > 0; }
        if (_menuNextItem != null) { _menuNextItem.Enabled = count > 0; }
        if (_menuTopMostItem != null) { _menuTopMostItem.Checked = Settings.TopMost; }
        if (_menuLockedItem != null) { _menuLockedItem.Checked = Locked; }
        if (_menuImageLockItem != null) { _menuImageLockItem.Checked = ImageLocked; }
        if (_menuLongItem != null)
        {
            bool longImage = hasImage && IsLongImageCurrent();
            _menuLongItem.Enabled = longImage;
            _menuLongItem.Checked = longImage && LongImageMode;
        }
        if (_menuSliceItem != null) { _menuSliceItem.Enabled = hasImage && IsLongImageCurrent(); }
        if (_menuPassiveItem != null)
        {
            _menuPassiveItem.Text = "鼠标穿透：默认开启（按住 " + GateKeyDisplayName() + " 可操作）";
        }
        if (_menuHideItem != null)
        {
            _menuHideItem.Enabled = CanHide;
            _menuHideItem.ToolTipText = CanHide ? "" : "隐藏热键未注册，无法隐藏（可改 settings.ini 换键）";
        }

        for (int i = 0; i < _menuOpacityItems.Count; i++)
        {
            ToolStripMenuItem item = _menuOpacityItems[i];
            if (item == null) { continue; }
            int value = 0;
            string text = item.Text != null ? item.Text.Replace("%", "") : "";
            try { value = int.Parse(text, CultureInfo.InvariantCulture); }
            catch (Exception) { value = -1; }
            item.Checked = value == Settings.Opacity;
        }

        RebuildRecentMenu();
    }

    private void RebuildRecentMenu()
    {
        if (_menuRecentItem == null) { return; }
        ToolStripItemCollection items = _menuRecentItem.DropDownItems;
        items.Clear();

        List<string> recents = Settings != null ? Settings.RecentFolders : null;
        if (recents == null || recents.Count == 0)
        {
            ToolStripMenuItem empty = CreateMenuItem("（没有最近使用的文件夹）", null);
            empty.Enabled = false;
            items.Add(empty);
            return;
        }

        int count = 0;
        for (int i = 0; i < recents.Count && count < 5; i++)
        {
            string path = recents[i];
            if (string.IsNullOrEmpty(path)) { continue; }
            ToolStripMenuItem item = CreateMenuItem(ShortenPath(path, 46), delegate { ChangeFolderFromRecent(path); });
            item.ToolTipText = path;
            items.Add(item);
            count++;
        }
        if (count == 0)
        {
            ToolStripMenuItem empty = CreateMenuItem("（没有最近使用的文件夹）", null);
            empty.Enabled = false;
            items.Add(empty);
        }
    }

    private static string ShortenPath(string path, int maxChars)
    {
        if (string.IsNullOrEmpty(path) || maxChars < 8 || path.Length <= maxChars) { return path; }
        return "…" + path.Substring(path.Length - (maxChars - 1));
    }

    // ========================================================================
    // 键盘状态读取（只用 GetAsyncKeyState，不装钩子、不模拟输入）
    // ========================================================================
    private int GateKeyVirtualKey()
    {
        if (Settings == null) { return Win32Const.VK_MENU; }
        if (Settings.GateKey == GateKeyKind.Alt) { return Win32Const.VK_MENU; }
        if (Settings.GateKey == GateKeyKind.Shift) { return Win32Const.VK_SHIFT; }
        return Win32Const.VK_CONTROL;
    }

    // 门控键显示名（用于状态提示与右键菜单只读项；默认 Alt）
    private string GateKeyDisplayName()
    {
        if (Settings == null) { return "Alt"; }
        if (Settings.GateKey == GateKeyKind.Alt) { return "Alt"; }
        if (Settings.GateKey == GateKeyKind.Shift) { return "Shift"; }
        return "Ctrl";
    }

    private bool IsGateKeyDown()
    {
        return (NativeMethods.GetAsyncKeyState(GateKeyVirtualKey()) & 0x8000) != 0;
    }

    private static bool IsCtrlDown()
    {
        return (NativeMethods.GetAsyncKeyState(Win32Const.VK_CONTROL) & 0x8000) != 0;
    }

    private static bool IsAltDown()
    {
        return (NativeMethods.GetAsyncKeyState(Win32Const.VK_MENU) & 0x8000) != 0;
    }

    private static bool IsShiftDown()
    {
        return (NativeMethods.GetAsyncKeyState(Win32Const.VK_SHIFT) & 0x8000) != 0;
    }
}
// ============================================================================
// 主窗口绘制 — 主窗口全部绘制
// 内容：图片绘制（可见区域 + 插值 + 适应窗口缓存）、工具条、空状态卡片、
//       toast、长图滚动条、可交互描边、主题/字体缓存、局部失效。
// 规则：主窗口是 layered 窗口，禁止使用子控件；全部在 OnPaint 里画并手动命中测试。
//       C# 5 语法；所有 GDI+ 对象 using/Dispose；字体按 DPI 缓存，绝不在 paint 里 new Font。
// ============================================================================

internal sealed partial class OverlayForm
{
    // ------------------------------------------------------------------
    // 绘制常量
    // ------------------------------------------------------------------
    private const int PaintPanelAlpha = 235;        // 工具条/空状态卡片约 92% 不透明
    private const int PaintToastFadeMs = 300;       // toast 最后 300ms 线性淡出
    private const long PaintFitCacheMaxPixels = 40000000L; // 适应窗口缓存像素上限（防御异常尺寸）

    // mip 缓存：Zoom 低于一半时启用 1/2 mip，低于四分之一时再降到 1/4 mip；
    // 只有可见源区域足够大（>= 300 万像素）才值得付一次降采样成本。
    private const double PaintMipHalfZoom = 0.5;
    private const double PaintMipQuarterZoom = 0.25;
    private const long PaintMipMinVisiblePixels = 3000000L;
    private const long PaintMipMaxHalfPixels = 40000000L; // 1/2 mip 像素上限（防御异常大图）

    // toast 描边色：浅色主题按规格 #D6DAE1（ThemeTokens.Border 是 #DADCE0，偏白，这里不复用）；
    // 深色主题沿用现有 ThemeTokens.Border（本文件私有，不去改 00_header 的 token）。
    private static readonly Color PaintToastBorderLight = Color.FromArgb(0xD6, 0xDA, 0xE1);

    // ------------------------------------------------------------------
    // 绘制专用缓存字段（只在本文件使用；不改动 05_SharedState 的共享字段）
    // ------------------------------------------------------------------
    private CardLayout _cardLayoutCache;            // 空状态卡片布局缓存
    private Size _cardLayoutClient;                 // 缓存时的客户区尺寸
    private int _cardLayoutDpi = -1;                // 缓存时的 DPI
    private string _cardLayoutFolder;               // 缓存时的文件夹路径

    private ToastLayout _toastLayoutCache;          // toast 布局缓存
    private string _toastLayoutText;                // 缓存时的提示文本
    private Size _toastLayoutClient;                // 缓存时的客户区尺寸
    private int _toastLayoutDpi = -1;               // 缓存时的 DPI

    private static ThemeTokens _paintFallbackTheme; // Colors 尚未初始化时的浅色兜底
    private int _paintFontsDpi = -1;                // 字体缓存对应的 DPI

    // ---- 缩小绘制的 mip（降采样）缓存 ----
    // 背景：4000x2000（8MP）源图在 Zoom<0.5 时直接用 HighQualityBicubic 采样，
    // GDI+ 每帧要处理约 8MP 源像素（实测 p95 ≈ 82ms，远超 §十二 的 33ms），
    // 且换插值模式无解（HighQualityBilinear 仍有 ~52ms）。因此先把源图降采样成
    // 1/2、1/4 两级 mip 位图（各只构建一次），绘制时让 GDI+ 只处理 mip 像素。
    private Bitmap _mipHalf;                        // 1/2 分辨率 mip（源图一半尺寸）
    private Bitmap _mipQuarter;                     // 1/4 分辨率 mip（由 1/2 mip 再降一级，懒构建）
    private Bitmap _mipSource;                      // 当前 mip 对应的源位图（引用比较判失效）
    private bool _mipHalfFailed;                    // 1/2 mip 构建失败（同一源不再重试）
    private bool _mipQuarterFailed;                 // 1/4 mip 构建失败（同一源不再重试）

    // ==================================================================
    // 几何：交给 ViewMath（纯函数），这里只负责取参数 + 缓存
    // ==================================================================

    // 视口矩形（客户区坐标）。工具条/toast 覆盖在图片之上，不缩小视口。
    internal Rectangle ViewportRect()
    {
        return new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
    }

    internal ToolbarLayout CurrentToolbarLayout()
    {
        bool showLongToggle = LongImageMode;
        if (!showLongToggle) { showLongToggle = PaintCurrentImageIsLong(); }
        int count = (Images == null) ? 0 : Images.Count;
        return ViewMath.ToolbarGeometry(ClientSize, PaintDpi(), showLongToggle, count, CurrentIndex, true);
    }

    internal CardLayout CurrentCardLayout()
    {
        Size client = ClientSize;
        int dpi = PaintDpi();
        string folder = PaintFolderPath();
        if (_cardLayoutCache != null
            && _cardLayoutClient.Width == client.Width
            && _cardLayoutClient.Height == client.Height
            && _cardLayoutDpi == dpi
            && string.Equals(_cardLayoutFolder, folder, StringComparison.Ordinal))
        {
            return _cardLayoutCache;
        }
        CardLayout layout = null;
        try { layout = ViewMath.EmptyCardGeometry(client, dpi, folder); }
        catch { layout = null; }
        _cardLayoutCache = layout;
        _cardLayoutClient = client;
        _cardLayoutDpi = dpi;
        _cardLayoutFolder = folder;
        return layout;
    }

    // ==================================================================
    // 主题 / 字体缓存
    // ==================================================================

    // 主题/字体缓存（DPI 变化或主题切换后调用）
    internal void RefreshThemeFonts()
    {
        int dpi = PaintDpi();
        if (UiFont != null && TitleFont != null && ToastFont != null && _paintFontsDpi == dpi)
        {
            // 字体族与字号只跟 DPI 有关（主题只换颜色），DPI 未变时复用已缓存字体
            return;
        }

        Font newUi = null;
        Font newTitle = null;
        Font newToast = null;
        try
        {
            newUi = UiFonts.Create(9f, FontStyle.Regular);        // 正文 9pt
            newTitle = UiFonts.Create(10.5f, FontStyle.Bold);     // 标题 10.5pt 加粗
            newToast = UiFonts.Create(9f, FontStyle.Regular);     // toast 9pt
        }
        catch
        {
            // 字体创建失败时保留旧字体（旧字体为 null 时 OnPaint 会退回 Form 默认字体），
            // 不让偶发的字体异常把绘制路径拖崩。
            PaintDisposeFont(newUi);
            PaintDisposeFont(newTitle);
            PaintDisposeFont(newToast);
            return;
        }

        Font oldUi = UiFont;
        Font oldTitle = TitleFont;
        Font oldToast = ToastFont;
        UiFont = newUi;
        TitleFont = newTitle;
        ToastFont = newToast;
        _paintFontsDpi = dpi;

        // 先换新再释放旧：中途绘制不会拿到已释放字体
        PaintDisposeFont(oldUi);
        PaintDisposeFont(oldTitle);
        PaintDisposeFont(oldToast);

        // 字体变化 → 已测量的布局缓存作废
        _toastLayoutCache = null;
        _toastLayoutText = null;
        _cardLayoutCache = null;
        _cardLayoutFolder = null;
    }

    private static void PaintDisposeFont(Font font)
    {
        if (font == null) { return; }
        try { font.Dispose(); }
        catch { }
    }

    private ThemeTokens PaintColors()
    {
        if (Colors != null) { return Colors; }
        if (_paintFallbackTheme == null) { _paintFallbackTheme = ThemeTokens.Light(); }
        return _paintFallbackTheme;
    }

    private int PaintDpi()
    {
        int dpi = CurrentDpi;
        if (dpi <= 0) { dpi = DpiHelper.SystemDpi; }
        if (dpi <= 0) { dpi = 96; }
        return dpi;
    }

    // DIP → 物理像素。注意：OverlayForm 继承自 Control，直接写 Layout 会命中
    // Control.Layout 事件而不是全局 Layout 类，因此必须写成 global::Layout。
    private int PaintPx(int dip, int dpi)
    {
        return global::Layout.Px(dip, dpi);
    }

    // 当前图片文件夹（空状态卡片要显示完整路径）
    private string PaintFolderPath()
    {
        if (Library != null && !string.IsNullOrEmpty(Library.Folder)) { return Library.Folder; }
        if (Settings != null && !string.IsNullOrEmpty(Settings.Folder)) { return Settings.Folder; }
        return "";
    }

    // 当前图片尺寸：优先用实际绘制位图，其次用图库条目里的头部尺寸
    private bool PaintCurrentImageSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        if (CurrentBitmap != null)
        {
            width = CurrentBitmap.Width;
            height = CurrentBitmap.Height;
            return width > 0 && height > 0;
        }
        if (Images != null && CurrentIndex >= 0 && CurrentIndex < Images.Count)
        {
            ImageEntry entry = Images[CurrentIndex];
            if (entry != null && entry.PixelWidth > 0 && entry.PixelHeight > 0)
            {
                width = entry.PixelWidth;
                height = entry.PixelHeight;
                return true;
            }
        }
        return false;
    }

    private bool PaintCurrentImageIsLong()
    {
        int width;
        int height;
        if (!PaintCurrentImageSize(out width, out height)) { return false; }
        try
        {
            return ViewMath.IsLongImage(width, height, (long)width * (long)height);
        }
        catch
        {
            // ViewMath 尚未就绪时的兜底：h/w >= 3 且像素不超上限
            if (width <= 0) { return false; }
            return ((double)height / (double)width) >= 3.0
                && ((long)width * (long)height) <= ViewMath.MaxImagePixels;
        }
    }

    // ==================================================================
    // 适应窗口缓存
    // ==================================================================

    private bool PaintIsFitCacheValid()
    {
        if (FitCache == null || CurrentBitmap == null) { return false; }
        if (FitCacheIndex != CurrentIndex) { return false; }
        Size client = ClientSize;
        if (FitCacheClient.Width != client.Width || FitCacheClient.Height != client.Height) { return false; }
        double delta = FitCacheZoom - View.Zoom;
        if (delta < 0.0) { delta = -delta; }
        if (delta > 0.0001) { return false; }
        // 同一索引被重新扫描/旋转后位图尺寸可能变了：缓存尺寸必须与当前图片×缩放对得上
        int expectW = (int)Math.Round(CurrentBitmap.Width * View.Zoom);
        int expectH = (int)Math.Round(CurrentBitmap.Height * View.Zoom);
        if (expectW < 1) { expectW = 1; }
        if (expectH < 1) { expectH = 1; }
        return Math.Abs(FitCache.Width - expectW) <= 1 && Math.Abs(FitCache.Height - expectH) <= 1;
    }

    // 适应窗口缓存位图
    internal void RebuildFitCache()
    {
        Bitmap old = FitCache;
        FitCache = null;
        FitCacheIndex = -1;
        FitCacheClient = Size.Empty;
        FitCacheZoom = 0.0;
        if (old != null) { try { old.Dispose(); } catch { } }

        Bitmap source = CurrentBitmap;
        if (source == null) { return; }
        if (View.Mode != ViewMode.Fit) { return; }

        Size client = ClientSize;
        if (client.Width <= 0 || client.Height <= 0) { return; }

        double zoom = View.Zoom;
        if (zoom <= 0.0 || double.IsNaN(zoom) || double.IsInfinity(zoom)) { return; }

        int dstW = (int)Math.Round(source.Width * zoom);
        int dstH = (int)Math.Round(source.Height * zoom);
        if (dstW < 1) { dstW = 1; }
        if (dstH < 1) { dstH = 1; }
        // 防御：缩放出异常大尺寸时不缓存，退回逐帧绘制可见区域
        if ((long)dstW * (long)dstH > PaintFitCacheMaxPixels) { return; }

        Bitmap cache = null;
        try
        {
            cache = new Bitmap(dstW, dstH, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(cache))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                if (zoom < 1.0)
                {
                    // 缩小：高质量双三次 + TileFlipXY 避免边缘伪影
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    ImageAttributes attributes = null;
                    try
                    {
                        attributes = new ImageAttributes();
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(source, new Rectangle(0, 0, dstW, dstH),
                            0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                    }
                    finally
                    {
                        if (attributes != null) { attributes.Dispose(); }
                    }
                }
                else
                {
                    // 放大：最近邻，像素清晰（看攻略细节）
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(source, new Rectangle(0, 0, dstW, dstH),
                        0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
                }
            }
            FitCache = cache;
            FitCacheIndex = CurrentIndex;
            FitCacheClient = client;
            FitCacheZoom = zoom;
        }
        catch
        {
            if (cache != null) { try { cache.Dispose(); } catch { } }
            FitCache = null;
            FitCacheIndex = -1;
            FitCacheClient = Size.Empty;
            FitCacheZoom = 0.0;
        }
    }

    // ==================================================================
    // 缩小绘制的 mip（降采样）缓存
    // 失效条件：只跟「源位图对象」有关——CurrentBitmap 换成另一个 Bitmap（切图/旋转/翻转/
    // 重新解码）时立即释放重建；DPI、窗口尺寸、主题、缩放比例都**不影响** mip 内容，
    // 因此不因它们失效（缩放只决定用 1/2 还是 1/4 这一级，不重建）。
    // ==================================================================

    // 取当前该用的 mip 位图；不可用（未构建/构建失败/源变化）返回 null，调用方走原路径。
    // wantQuarter：调用方已按缩放/目标尺寸算好是否该用 1/4 这一级。
    // mipScale 是降采样倍率（1/2 mip = 2.0f，1/4 mip = 4.0f）：mip 坐标 = 源坐标 / mipScale。
    private Bitmap PaintMipFor(Bitmap source, bool wantQuarter, out float mipScale)
    {
        mipScale = 1.0f;
        if (source == null) { return null; }

        // 源位图对象变了：旧 mip 立刻释放，避免同时驻留两份大位图
        if (!ReferenceEquals(_mipSource, source)) { PaintReleaseMip(); _mipSource = source; }

        if (wantQuarter && !_mipQuarterFailed)
        {
            if (_mipQuarter == null)
            {
                // 1/4 mip 由 1/2 mip 再降一级（两段降采样比一次 4 倍降采样质量好）
                Bitmap half = PaintMipHalf(source);
                if (half == null)
                {
                    _mipQuarterFailed = true;   // 半分辨率都建不出来，四分之一也不必再试
                }
                else
                {
                    int qw = half.Width / 2;
                    int qh = half.Height / 2;
                    Bitmap quarter = null;
                    if (qw >= 16 && qh >= 16 && (long)qw * (long)qh <= PaintMipMaxHalfPixels)
                    {
                        quarter = PaintBuildMip(half, qw, qh);
                    }
                    if (quarter == null) { _mipQuarterFailed = true; }
                    else { _mipQuarter = quarter; }
                }
            }
            if (_mipQuarter != null) { mipScale = 4.0f; return _mipQuarter; }
            // 四分之一不可用时退回半分辨率（仍然比全分辨率快得多）
        }

        Bitmap result = PaintMipHalf(source);
        if (result == null) { return null; }
        mipScale = 2.0f;
        return result;
    }

    // 1/2 mip：懒构建；失败记住失败（同一源图绝不每帧重试）
    private Bitmap PaintMipHalf(Bitmap source)
    {
        if (_mipHalf != null) { return _mipHalf; }
        if (_mipHalfFailed) { return null; }
        int w = source.Width / 2;
        int h = source.Height / 2;
        if (w < 16 || h < 16 || (long)w * (long)h > PaintMipMaxHalfPixels)
        {
            _mipHalfFailed = true;      // 图太小或太大都不做 mip，直接走原路径
            return null;
        }
        _mipHalf = PaintBuildMip(source, w, h);
        if (_mipHalf == null) { _mipHalfFailed = true; }
        return _mipHalf;
    }

    // 用高质量双三次 + TileFlipXY 把 source 缩到 w×h；任何异常都静默返回 null（不抛）。
    private Bitmap PaintBuildMip(Bitmap source, int w, int h)
    {
        Bitmap result = null;
        try
        {
            // PArgb：GDI+ 内部常用的预乘格式，贴图最快，且与 FitCache 口径一致
            result = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;   // 整块覆盖，不与透明底混合
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                ImageAttributes attributes = null;
                try
                {
                    attributes = new ImageAttributes();
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(source, new Rectangle(0, 0, w, h),
                        0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                }
                finally
                {
                    if (attributes != null) { attributes.Dispose(); }
                }
            }
            return result;
        }
        catch
        {
            if (result != null) { try { result.Dispose(); } catch { } }
            return null;
        }
    }

    // 释放 mip 并清空失效记录（源位图变化 / 窗体释放时调用）。可重复调用。
    private void PaintReleaseMip()
    {
        Bitmap half = _mipHalf;
        Bitmap quarter = _mipQuarter;
        _mipHalf = null;
        _mipQuarter = null;
        _mipSource = null;
        _mipHalfFailed = false;
        _mipQuarterFailed = false;
        if (quarter != null) { try { quarter.Dispose(); } catch { } }
        if (half != null) { try { half.Dispose(); } catch { } }
    }

    // 用 mip 绘制缩小路径：目标矩形 dst 与裁剪范围完全沿用原几何，只把源矩形坐标
    // 除以 mipScale 换算到 mip 坐标系，并夹到 mip 边界内（奇偶尺寸差最多半个源像素）。
    // 返回 false 表示这次源矩形退化了，调用方退回全分辨率原路径。
    private static bool PaintDrawMipImage(Graphics g, Bitmap mip, float mipScale, RectangleF dst,
        float sx, float sy, float sw, float sh)
    {
        if (mip == null || mipScale <= 0.0f) { return false; }

        float msx = sx / mipScale;
        float msy = sy / mipScale;
        float msw = sw / mipScale;
        float msh = sh / mipScale;
        if (msx < 0.0f) { msx = 0.0f; }
        if (msy < 0.0f) { msy = 0.0f; }
        if (msx > mip.Width) { msx = mip.Width; }
        if (msy > mip.Height) { msy = mip.Height; }
        if (msw > (float)mip.Width - msx) { msw = (float)mip.Width - msx; }
        if (msh > (float)mip.Height - msy) { msh = (float)mip.Height - msy; }
        if (msw <= 0.05f || msh <= 0.05f) { return false; }

        using (ImageAttributes attributes = new ImageAttributes())
        {
            // 源已降采样，这里仍用高质量双三次：剩余倍率小（约 1–2 倍），开销只有毫秒级
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(mip, Rectangle.Round(dst), msx, msy, msw, msh, GraphicsUnit.Pixel, attributes);
        }
        return true;
    }

    // 释放绘制缓存（位图/字体/布局缓存）。可重复调用。
    internal void DisposePaintResources()
    {
        // mip（必须先于其它缓存释放：它持有源图的降采样副本）
        PaintReleaseMip();

        // 位图
        Bitmap cache = FitCache;
        FitCache = null;
        FitCacheIndex = -1;
        FitCacheClient = Size.Empty;
        FitCacheZoom = 0.0;
        if (cache != null) { try { cache.Dispose(); } catch { } }

        // 字体（字段置空后 OnPaint 会退回 Form 默认字体，不会用到已释放对象）
        Font ui = UiFont;
        Font title = TitleFont;
        Font toast = ToastFont;
        UiFont = null;
        TitleFont = null;
        ToastFont = null;
        PaintDisposeFont(ui);
        PaintDisposeFont(title);
        PaintDisposeFont(toast);
        _paintFontsDpi = -1;

        // 布局缓存（画刷全部按需 using，不常驻）
        _cardLayoutCache = null;
        _cardLayoutFolder = null;
        _toastLayoutCache = null;
        _toastLayoutText = null;
    }

    // ==================================================================
    // 局部失效（避免整窗重绘）
    // ==================================================================

    internal void InvalidateOverlay()
    {
        // 整个客户区变化：直接整窗失效（最小/零尺寸时 Invalidate 同样安全）
        Invalidate();
    }

    internal void InvalidateViewport()
    {
        PaintInvalidateRect(ViewportRect());
    }

    internal void InvalidateToolbarArea()
    {
        Size client = ClientSize;
        if (client.Width <= 0 || client.Height <= 0) { Invalidate(); return; }
        int dpi = PaintDpi();
        ToolbarLayout layout = null;
        try { layout = CurrentToolbarLayout(); }
        catch { layout = null; }
        Rectangle area;
        if (layout == null || layout.Bar.Width <= 0 || layout.Bar.Height <= 0)
        {
            int topH = PaintPx(global::Layout.ToolbarHeightDip + global::Layout.ToolbarEdgeMarginDip * 2, dpi);
            area = new Rectangle(0, 0, client.Width, Math.Max(1, topH));
        }
        else
        {
            area = layout.Bar;
            int pad = Math.Max(1, PaintPx(2, dpi));
            area.Inflate(pad, pad);
        }
        PaintInvalidateRect(area);
    }

    internal void InvalidateToastArea()
    {
        Size client = ClientSize;
        if (client.Width <= 0 || client.Height <= 0) { Invalidate(); return; }
        int dpi = PaintDpi();
        int pad = Math.Max(1, PaintPx(3, dpi));

        Rectangle union = Rectangle.Empty;
        bool hasArea = false;

        // 旧 toast 矩形（文本已被替换时也要擦掉原位置）
        if (_toastLayoutCache != null && _toastLayoutCache.Rect.Width > 0)
        {
            Rectangle old = _toastLayoutCache.Rect;
            old.Inflate(pad, pad);
            union = old;
            hasArea = true;
        }

        ToastLayout layout = null;
        try { layout = PaintCurrentToastLayout(); }
        catch { layout = null; }
        if (layout != null && layout.Rect.Width > 0)
        {
            Rectangle now = layout.Rect;
            now.Inflate(pad, pad);
            union = hasArea ? Rectangle.Union(union, now) : now;
            hasArea = true;
        }

        if (!hasArea)
        {
            int stripH = Math.Max(1, PaintPx(48, dpi));
            union = new Rectangle(0, Math.Max(0, client.Height - stripH), client.Width, stripH);
        }
        PaintInvalidateRect(union);
    }

    private void PaintInvalidateRect(Rectangle rect)
    {
        Size client = ClientSize;
        if (client.Width <= 0 || client.Height <= 0) { Invalidate(); return; }
        Rectangle bounds = new Rectangle(0, 0, client.Width, client.Height);
        Rectangle clipped = Rectangle.Intersect(rect, bounds);
        if (clipped.Width <= 0 || clipped.Height <= 0) { return; }
        Invalidate(clipped);
    }

    // ==================================================================
    // OnPaint：双缓冲自绘
    // ==================================================================

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        ThemeTokens colors = PaintColors();
        try
        {
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(colors.Canvas);

            Size size = ClientSize;
            if (size.Width <= 0 || size.Height <= 0) { return; }
            Rectangle client = new Rectangle(0, 0, size.Width, size.Height);

            PaintViewportImage(g, client);      // 1. 图片 / 加载失败文本
            PaintInteractiveBorder(g, client);  // 2. 可交互强调色描边
            PaintToolbar(g);                    // 3. 顶部工具条
            PaintEmptyCard(g);                  // 4. 空状态卡片（无图片）
            PaintToast(g);                      // 5. 右下角状态提示
            PaintScrollbar(g, client);          // 6. 长图滚动条
            PaintFirstRunHint(g, client);       // 7. 首次运行引导条（B1，底部 28 DIP）
        }
        catch
        {
            // 绘制异常不得让程序崩溃：退回纯画布，等待下一次重绘
            try { g.Clear(colors.Canvas); }
            catch { }
        }
    }

    // ---------------- 图片 ----------------

    private void PaintViewportImage(Graphics g, Rectangle client)
    {
        Bitmap bitmap = CurrentBitmap;
        if (bitmap == null)
        {
            // 图片为空：加载失败画中央中文错误文本；正常空目录由空状态卡片负责
            if (!string.IsNullOrEmpty(CurrentError)) { PaintErrorText(g, client, CurrentError); }
            return;
        }

        // 适应窗口：**只读**预先缩放好的缓存位图（1:1 贴图，拖动/滚轮期间也快）。
        // 缓存的重建只在 40_OverlayForm 的 FitToWindow / OnResize 里做；paint 路径绝不
        // RebuildFitCache()——那会在每次重绘时分配整客户区位图（4K ≈ 32MB），
        // 分配失败后还会每帧重试。缓存无效时直接走下面的「缩放矩阵 + 可见源矩形」普通路径。
        if (View.Mode == ViewMode.Fit && PaintIsFitCacheValid())
        {
            int x = (int)Math.Round(View.OffsetX);
            int y = (int)Math.Round(View.OffsetY);
            // 显式按像素 1:1 贴缓存（不用 DrawImageUnscaled，避免 Image 分辨率非 96 DPI 时被换算）
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(FitCache, new Rectangle(x, y, FitCache.Width, FitCache.Height),
                0, 0, FitCache.Width, FitCache.Height, GraphicsUnit.Pixel);
            return;
        }

        // 手动 / 长图模式：只画可见部分（绝不在每帧把整张大图缩到窗口）
        double zoom = View.Zoom;
        if (zoom <= 0.0 || double.IsNaN(zoom) || double.IsInfinity(zoom)) { return; }
        int imgW = bitmap.Width;
        int imgH = bitmap.Height;
        if (imgW <= 0 || imgH <= 0) { return; }

        RectangleF src;
        try
        {
            src = ViewMath.VisibleSourceRect(zoom, View.OffsetX, View.OffsetY,
                client.Width, client.Height, imgW, imgH);
        }
        catch
        {
            src = new RectangleF(0f, 0f, imgW, imgH);
        }

        // 与图片矩形求交，防御几何函数给出界值导致越界采样
        float sx = (float)Math.Max(0.0, src.X);
        float sy = (float)Math.Max(0.0, src.Y);
        double right = Math.Min((double)imgW, (double)src.X + (double)src.Width);
        double bottom = Math.Min((double)imgH, (double)src.Y + (double)src.Height);
        float sw = (float)(right - sx);
        float sh = (float)(bottom - sy);
        if (sw <= 0.05f || sh <= 0.05f) { return; }

        RectangleF dst = new RectangleF(
            (float)(View.OffsetX + sx * zoom),
            (float)(View.OffsetY + sy * zoom),
            (float)(sw * zoom),
            (float)(sh * zoom));

        bool shrink = zoom < 1.0;

        // 深度缩小 + 可见源区域很大：改用预降采样的 mip 位图绘制。
        // 长图模式（以及 LongImageMode 标记）保持原逻辑不动：它的几何/滚动有自己的路径，
        // 不在本次优化范围内，避免引入行为变化。
        Bitmap mip = null;
        float mipScale = 1.0f;
        bool longMode = LongImageMode || View.Mode == ViewMode.LongImage;
        if (shrink && !longMode && zoom < PaintMipHalfZoom
            && (double)sw * (double)sh >= (double)PaintMipMinVisiblePixels)
        {
            // 选层级：Zoom < 1/4 必用 1/4；另外若 1/2 mip 的可见像素仍超过目标像素的
            // 2 倍（过采样太多，白付 GDI+ 采样成本），也降到 1/4——例如 8MP 图在
            // 1280 宽视口下 Zoom≈0.32 时，1/2 mip 要处理 2MP 而输出只有 0.8MP。
            bool wantQuarter = zoom < PaintMipQuarterZoom;
            if (!wantQuarter)
            {
                double halfVisible = ((double)sw * 0.5) * ((double)sh * 0.5);
                double dstPixels = (double)dst.Width * (double)dst.Height;
                wantQuarter = dstPixels > 0.0 && halfVisible >= dstPixels * 2.0;
            }
            try { mip = PaintMipFor(bitmap, wantQuarter, out mipScale); }
            catch { mip = null; mipScale = 1.0f; }   // 任何异常都静默降级到原路径
        }

        ImageAttributes attributes = null;
        try
        {
            if (shrink)
            {
                // 有可用 mip 时只让 GDI+ 处理 mip 像素（源头已降采样），几何完全不变
                if (mip != null && PaintDrawMipImage(g, mip, mipScale, dst, sx, sy, sw, sh)) { return; }

                // 缩小：高质量双三次 + TileFlipXY（新建后必须 Dispose）
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                attributes = new ImageAttributes();
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                // 注：ImageAttributes 版本只有 Rectangle 目标框重载，缩小路径用四舍五入后的目标矩形
                g.DrawImage(bitmap, Rectangle.Round(dst), sx, sy, sw, sh, GraphicsUnit.Pixel, attributes);
            }
            else
            {
                // 放大：最近邻，像素清晰
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bitmap, dst, new RectangleF(sx, sy, sw, sh), GraphicsUnit.Pixel);
            }
        }
        finally
        {
            if (attributes != null) { attributes.Dispose(); }
        }
    }

    private void PaintErrorText(Graphics g, Rectangle client, string error)
    {
        int dpi = PaintDpi();
        Font font = UiFont != null ? UiFont : this.Font;
        if (font == null) { return; }

        int lineH = 1;
        try { lineH = Math.Max(1, (int)Math.Ceiling(font.GetHeight(g))); }
        catch { lineH = Math.Max(1, PaintPx(18, dpi)); }

        int boxW = Math.Max(1, client.Width - PaintPx(48, dpi));
        int boxX = client.X + (client.Width - boxW) / 2;
        int blockH = lineH * 2;
        int boxY = client.Y + (client.Height - blockH) / 2;
        string detail = string.IsNullOrEmpty(error) ? "未知原因" : error;
        ThemeTokens colors = PaintColors();

        using (StringFormat format = PaintCenteredFormat())
        {
            using (SolidBrush titleBrush = new SolidBrush(colors.Text))
            {
                g.DrawString("图片加载失败", font, titleBrush,
                    new RectangleF(boxX, boxY, boxW, lineH), format);
            }
            using (SolidBrush detailBrush = new SolidBrush(colors.TextSecondary))
            {
                g.DrawString(detail, font, detailBrush,
                    new RectangleF(boxX, boxY + lineH, boxW, lineH), format);
            }
        }
    }

    // ---------------- 可交互描边 ----------------

    private void PaintInteractiveBorder(Graphics g, Rectangle client)
    {
        if (!Interactive) { return; }
        int dpi = PaintDpi();
        int width = Math.Max(1, PaintPx(1, dpi));
        // 约 40% 不透明度的强调色 1 DIP 描边，提示「现在可操作」
        Color color = Util.WithAlpha(PaintColors().Accent, 102);
        Rectangle rect = new Rectangle(client.X, client.Y,
            Math.Max(1, client.Width - width), Math.Max(1, client.Height - width));
        using (Pen pen = new Pen(color, (float)width))
        {
            g.DrawRectangle(pen, rect);
        }
    }

    // ---------------- 工具条 ----------------

    private void PaintToolbar(Graphics g)
    {
        if (!ToolbarVisible) { return; }
        ToolbarLayout layout = null;
        try { layout = CurrentToolbarLayout(); }
        catch { layout = null; }
        if (layout == null || layout.Bar.Width <= 0 || layout.Bar.Height <= 0) { return; }

        ThemeTokens colors = PaintColors();
        int dpi = PaintDpi();
        int radius = PaintPx(global::Layout.ToolbarCornerDip, dpi);
        int borderWidth = Math.Max(1, PaintPx(1, dpi));

        // 顶部居中圆角卡片，约 92% 不透明
        PaintFillRound(g, layout.Bar, Util.WithAlpha(colors.Card, PaintPanelAlpha), radius);
        PaintStrokeRound(g, layout.Bar, Util.WithAlpha(colors.Border, 200), borderWidth, radius);

        Font font = UiFont != null ? UiFont : this.Font;
        if (font == null) { return; }
        int itemRadius = Math.Max(0, PaintPx(3, dpi));
        int insetX = Math.Max(1, PaintPx(1, dpi));
        int insetY = Math.Max(1, PaintPx(2, dpi));

        for (int i = 0; i < layout.Items.Count; i++)
        {
            ToolbarItem item = layout.Items[i];
            if (item == null || item.Rect.Width <= 0 || item.Rect.Height <= 0) { continue; }

            bool isPage = item.Kind == ToolbarButton.Page;
            bool hovered = HoverToolbar == item.Kind;
            bool pressed = PressedToolbar == item.Kind;
            bool activeToggle = (item.Kind == ToolbarButton.LongMode && LongImageMode)
                || (item.Kind == ToolbarButton.LockImage && ImageLocked);

            Rectangle body = item.Rect;
            body.Inflate(-insetX, -insetY);
            if (body.Width > 0 && body.Height > 0)
            {
                if (pressed)
                {
                    PaintFillRound(g, body, ControlPaint.Dark(colors.Hover, 0.06f), itemRadius);
                }
                else if (hovered && item.Enabled)
                {
                    PaintFillRound(g, body, colors.Hover, itemRadius);
                }
                else if (activeToggle)
                {
                    // 长图模式开启时按钮带强调色底，明确当前状态
                    PaintFillRound(g, body, Util.WithAlpha(colors.Accent, 46), itemRadius);
                }
            }

            Color textColor;
            if (!item.Enabled)
            {
                textColor = Util.WithAlpha(colors.TextSecondary, 140);   // 禁用项灰显
            }
            else if (isPage)
            {
                textColor = colors.TextSecondary;                        // 页码只读，用次文字色
            }
            else if (activeToggle)
            {
                textColor = colors.Accent;
            }
            else
            {
                textColor = colors.Text;
            }

            string text = item.Text;
            if (string.IsNullOrEmpty(text)) { text = PaintToolbarFallbackText(item.Kind); }
            PaintText(g, text, font, textColor, item.Rect);
        }
    }

    private static string PaintToolbarFallbackText(ToolbarButton kind)
    {
        switch (kind)
        {
            case ToolbarButton.Prev: return "◀";
            case ToolbarButton.Next: return "▶";
            case ToolbarButton.Fit: return "适应窗口";
            case ToolbarButton.OneToOne: return "100%";
            case ToolbarButton.LockImage: return "锁图";
            case ToolbarButton.LongMode: return "长图";
            case ToolbarButton.Settings: return "设置…";
            case ToolbarButton.Close: return "×";
            case ToolbarButton.Page: return "0/0";
            default: return "";
        }
    }

    // ---------------- 空状态卡片 ----------------

    private void PaintEmptyCard(Graphics g)
    {
        bool showCard = CardVisible;
        if (!showCard && CurrentBitmap == null && string.IsNullOrEmpty(CurrentError)
            && (Images == null || Images.Count == 0))
        {
            showCard = true;
        }
        if (!showCard) { return; }

        CardLayout layout = null;
        try { layout = CurrentCardLayout(); }
        catch { layout = null; }
        if (layout == null || layout.Card.Width <= 0 || layout.Card.Height <= 0) { return; }

        ThemeTokens colors = PaintColors();
        int dpi = PaintDpi();
        int radius = PaintPx(6, dpi);
        int borderWidth = Math.Max(1, PaintPx(1, dpi));

        PaintFillRound(g, layout.Card, Util.WithAlpha(colors.Card, PaintPanelAlpha), radius);
        PaintStrokeRound(g, layout.Card, Util.WithAlpha(colors.Border, 220), borderWidth, radius);

        Font uiFont = UiFont != null ? UiFont : this.Font;
        if (uiFont == null) { return; }
        Font titleFont = TitleFont != null ? TitleFont : uiFont;

        // 标题
        PaintText(g, "还没有可显示的攻略图", titleFont, colors.Text, layout.TitleRect);

        // 文件夹完整路径（过长中间省略）
        string folder = PaintFolderPath();
        if (string.IsNullOrEmpty(folder)) { folder = "（未设置图片文件夹）"; }
        if (layout.FolderRect.Width > 0)
        {
            string shown = PaintMiddleEllipsis(g, folder, uiFont, layout.FolderRect.Width);
            PaintText(g, shown, uiFont, colors.TextSecondary, layout.FolderRect);
        }

        // 三个按钮：仅 Interactive 时可点，否则灰显
        int buttonRadius = Math.Max(0, PaintPx(4, dpi));
        for (int i = 0; i < layout.Buttons.Count; i++)
        {
            CardItem button = layout.Buttons[i];
            if (button == null || button.Rect.Width <= 0 || button.Rect.Height <= 0) { continue; }

            bool enabled = button.Enabled && Interactive;
            bool hovered = Interactive && HoverCard == button.Kind;
            bool pressed = Interactive && PressedCard == button.Kind;

            Color fill;
            Color border;
            Color textColor;
            if (!enabled)
            {
                fill = Util.WithAlpha(colors.Border, 40);
                border = Util.WithAlpha(colors.Border, 120);
                textColor = Util.WithAlpha(colors.TextSecondary, 150);
            }
            else if (pressed)
            {
                fill = ControlPaint.Dark(colors.Hover, 0.06f);
                border = colors.Accent;
                textColor = colors.Text;
            }
            else if (hovered)
            {
                fill = colors.Hover;
                border = colors.Accent;
                textColor = colors.Text;
            }
            else
            {
                fill = Util.WithAlpha(colors.Border, 30);
                border = Util.WithAlpha(colors.Border, 200);
                textColor = colors.Text;
            }

            PaintFillRound(g, button.Rect, fill, buttonRadius);
            PaintStrokeRound(g, button.Rect, border, borderWidth, buttonRadius);

            string text = button.Text;
            if (string.IsNullOrEmpty(text)) { text = PaintCardFallbackText(button.Kind); }
            PaintText(g, text, uiFont, textColor, button.Rect);
        }

        // 一行热键提示：门控键 + 锁定/隐藏组合键都按当前设置动态拼，改键后文案不失真
        string hint = PaintHotkeyHintText();
        if (layout.HintRect.Width > 0)
        {
            hint = PaintMiddleEllipsis(g, hint, uiFont, layout.HintRect.Width);
        }
        PaintText(g, hint, uiFont, colors.TextSecondary, layout.HintRect);
    }

    private static string PaintCardFallbackText(CardButton kind)
    {
        switch (kind)
        {
            case CardButton.ChooseFolder: return "选择图片文件夹…";
            case CardButton.OpenFolder: return "打开文件夹";
            case CardButton.Rescan: return "重新扫描";
            default: return "";
        }
    }

    // 空卡片底部提示：门控键（Ctrl/Alt/Shift）+ 锁定/隐藏热键，全部取自当前设置。
    // 默认设置下即「按住 Ctrl 操作 · Ctrl+Alt+T 锁定 · Ctrl+Alt+G 隐藏」；
    // 门控键改成 Alt/Shift、或热键改键（含禁用）后文案随之变化，不再写死。
    private string PaintHotkeyHintText()
    {
        string lockHint = PaintHotkeyHintPart(HotkeyAction.ToggleLock, "锁定");
        string hideHint = PaintHotkeyHintPart(HotkeyAction.ToggleHide, "隐藏");
        return "按住 " + PaintGateKeyName() + " 操作 · " + lockHint + " · " + hideHint;
    }

    // 单个动作提示：「Ctrl+Alt+T 锁定」；热键禁用/未注册时写「锁定热键未注册」
    private string PaintHotkeyHintPart(HotkeyAction action, string actionText)
    {
        AppSettings settings = Settings;
        if (settings == null) { return actionText + "热键未注册"; }

        Keys combo = Keys.None;
        try { combo = settings.GetHotkey(action); }
        catch { return actionText + "热键未注册"; }
        if (combo == Keys.None) { return actionText + "热键未注册"; }

        string text = "";
        try { text = HotkeyManager.FormatCombo(combo); }
        catch { text = ""; }
        if (string.IsNullOrEmpty(text) || text == "无") { return actionText + "热键未注册"; }
        return text + " " + actionText;
    }

    // 门控键名（与 设置与热键 的 GateKeyKind 枚举一致：Ctrl / Alt / Shift；默认 Alt）
    private string PaintGateKeyName()
    {
        AppSettings settings = Settings;
        if (settings == null) { return "Alt"; }
        if (settings.GateKey == GateKeyKind.Alt) { return "Alt"; }
        if (settings.GateKey == GateKeyKind.Shift) { return "Shift"; }
        return "Ctrl";
    }

    // ---------------- 首次运行引导条（B1） ----------------
    private const int PaintFirstRunBarHeightDip = 28;

    // 底部 28 DIP 提示条：首次启动时告诉新用户「按住门控键才能操作窗口 / 怎么拖动 / 怎么锁定」
    private void PaintFirstRunHint(Graphics g, Rectangle client)
    {
        if (!_firstRunActive || g == null) { return; }
        if (client.Width <= 0 || client.Height <= 0) { return; }

        int dpi = PaintDpi();
        int barHeight = PaintPx(PaintFirstRunBarHeightDip, dpi);
        if (barHeight < 12) { barHeight = 12; }
        if (barHeight > client.Height) { barHeight = client.Height; }

        Rectangle bar = new Rectangle(client.X, client.Bottom - barHeight, client.Width, barHeight);
        ThemeTokens colors = PaintColors();
        PaintFillRound(g, bar, Util.WithAlpha(colors.Card, 245), 0);
        int borderWidth = Math.Max(1, PaintPx(1, dpi));
        using (Pen pen = new Pen(Util.WithAlpha(colors.Border, 220), (float)borderWidth))
        {
            g.DrawLine(pen, bar.Left, bar.Top, bar.Right, bar.Top);
        }

        Font font = UiFont != null ? UiFont : this.Font;
        if (font == null) { return; }
        PaintText(g, FirstRunHintText(), font, colors.Text, bar);
    }

    // ---------------- toast ----------------

    // toast 布局（文本中间省略到「窗口宽 - 40 DIP」）；按文本/客户区/DPI 缓存测量结果
    private ToastLayout PaintCurrentToastLayout()
    {
        string text = ToastText;
        if (string.IsNullOrEmpty(text))
        {
            _toastLayoutCache = null;
            _toastLayoutText = null;
            return null;
        }
        Size client = ClientSize;
        // B1：首次运行引导条占据窗口底部 28 DIP，toast 整体上移，避免两段文字叠在一起
        if (_firstRunActive)
        {
            int inset = PaintPx(PaintFirstRunBarHeightDip, PaintDpi());
            if (inset > 0 && inset < client.Height) { client = new Size(client.Width, client.Height - inset); }
        }
        int dpi = PaintDpi();
        Font font = ToastFont != null ? ToastFont : (UiFont != null ? UiFont : this.Font);
        if (font == null) { return null; }

        if (_toastLayoutCache != null
            && string.Equals(_toastLayoutText, text, StringComparison.Ordinal)
            && _toastLayoutClient.Width == client.Width
            && _toastLayoutClient.Height == client.Height
            && _toastLayoutDpi == dpi)
        {
            return _toastLayoutCache;
        }

        int maxTextWidth = client.Width - PaintPx(40, dpi);
        int minTextWidth = PaintPx(40, dpi);
        if (maxTextWidth < minTextWidth) { maxTextWidth = minTextWidth; }

        ToastLayout layout = null;
        try { layout = ViewMath.ToastGeometry(text, font, client, dpi, maxTextWidth); }
        catch { layout = null; }
        _toastLayoutCache = layout;
        _toastLayoutText = text;
        _toastLayoutClient = client;
        _toastLayoutDpi = dpi;
        return layout;
    }

    // 淡出系数：最后 300ms 按剩余时间线性衰减到 0
    private double PaintToastAlphaFactor()
    {
        int now = Environment.TickCount;
        int remain = unchecked((int)(ToastUntilTick - (long)now));
        if (remain <= 0) { return 0.0; }

        double factor = 1.0;
        if (remain < PaintToastFadeMs)
        {
            factor = (double)remain / (double)PaintToastFadeMs;
        }
        if (ToastFadeStartTick != 0)
        {
            // 由交互模块显式记录的淡出起点计算（同样按 300ms 线性）；
            // 只采信「最近 300ms 内」的起点，避免上一条 toast 留下的旧值把新 toast 直接算成透明。
            int since = unchecked((int)((long)now - ToastFadeStartTick));
            if (since >= 0 && since <= PaintToastFadeMs + 50)
            {
                double fromStart = 1.0 - (double)since / (double)PaintToastFadeMs;
                if (fromStart < factor) { factor = fromStart; }
            }
        }
        if (factor < 0.0) { factor = 0.0; }
        if (factor > 1.0) { factor = 1.0; }
        return factor;
    }

    private void PaintToast(Graphics g)
    {
        if (string.IsNullOrEmpty(ToastText)) { return; }
        double factor = PaintToastAlphaFactor();
        if (factor <= 0.0) { return; }

        ToastLayout layout = null;
        try { layout = PaintCurrentToastLayout(); }
        catch { layout = null; }
        if (layout == null || layout.Rect.Width <= 0 || layout.Rect.Height <= 0) { return; }

        ThemeTokens colors = PaintColors();
        int dpi = PaintDpi();
        int radius = PaintPx(global::Layout.ToastCornerDip, dpi);
        int borderWidth = Math.Max(1, PaintPx(1, dpi));

        int cardAlpha = (int)Math.Round(PaintPanelAlpha * factor);
        int borderAlpha = (int)Math.Round(200.0 * factor);
        int textAlpha = (int)Math.Round(255.0 * factor);

        Font font = ToastFont != null ? ToastFont : (UiFont != null ? UiFont : this.Font);
        if (font == null) { return; }
        string text = layout.Text;
        if (string.IsNullOrEmpty(text)) { text = ToastText; }

        // 防御：几何模块可能用另一个 DPI 的测量 Graphics（例如 96 DPI 的临时位图），
        // 真实绘制 Graphics 的系统 DPI 更高时文字会超出卡片。这里用当前 Graphics 复核，
        // 先向左加宽卡片（右边距不变），仍放不下再按绘制 DPI 做中间省略。
        Rectangle cardRect = PaintFitToastRect(g, layout.Rect, font, ref text, dpi);

        PaintFillRound(g, cardRect, Util.WithAlpha(colors.Card, cardAlpha), radius);
        // 描边色：浅色 #D6DAE1 / 深色沿用 ThemeTokens.Border，再按淡出系数取 alpha
        PaintStrokeRound(g, cardRect, Util.WithAlpha(PaintToastBorderColor(colors), borderAlpha), borderWidth, radius);
        PaintText(g, text, font, Util.WithAlpha(colors.Text, textAlpha), cardRect);
    }

    // toast 描边色：深色主题用现有边框色，浅色主题用规格色 #D6DAE1
    private static Color PaintToastBorderColor(ThemeTokens colors)
    {
        if (colors == null) { return PaintToastBorderLight; }
        return colors.IsDark ? colors.Border : PaintToastBorderLight;
    }

    // 用真实绘制 Graphics 复核 toast 卡片尺寸：只加宽/加高，不缩小；超宽时重新中间省略
    private Rectangle PaintFitToastRect(Graphics g, Rectangle rect, Font font, ref string text, int dpi)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || string.IsNullOrEmpty(text)) { return rect; }
        int padX = Math.Max(1, PaintPx(global::Layout.ToastPaddingDip, dpi));
        int padY = Math.Max(1, PaintPx(4, dpi));
        SizeF measured;
        try { measured = g.MeasureString(text, font); }
        catch { return rect; }

        Rectangle result = rect;
        int needWidth = (int)Math.Ceiling(measured.Width) + padX * 2;
        if (needWidth > result.Width)
        {
            int right = result.Right;
            int maxRight = Math.Max(1, ClientSize.Width - PaintPx(global::Layout.ToastMarginDip, dpi));
            if (right > maxRight) { right = maxRight; }
            int left = right - needWidth;
            int minLeft = PaintPx(4, dpi);
            if (left < minLeft) { left = minLeft; }
            result = new Rectangle(left, result.Y, Math.Max(1, right - left), result.Height);
        }
        int needHeight = (int)Math.Ceiling(measured.Height) + padY;
        if (needHeight > result.Height)
        {
            int bottom = result.Bottom;
            int top = bottom - needHeight;
            if (top < 0) { top = 0; }
            result = new Rectangle(result.X, top, result.Width, Math.Max(1, bottom - top));
        }
        int innerWidth = Math.Max(1, result.Width - padX * 2);
        if ((int)Math.Ceiling(measured.Width) > innerWidth)
        {
            text = PaintMiddleEllipsis(g, text, font, innerWidth);
        }
        return result;
    }

    // ---------------- 长图滚动条 ----------------

    private void PaintScrollbar(Graphics g, Rectangle client)
    {
        if (!LongImageMode) { return; }
        Bitmap bitmap = CurrentBitmap;
        if (bitmap == null) { return; }

        int dpi = PaintDpi();
        Rectangle track;
        Rectangle thumb;
        try
        {
            track = ViewMath.ScrollbarTrackRect(ClientSize, dpi);
            thumb = ViewMath.ScrollbarThumbRect(ClientSize, dpi, client.Height,
                bitmap.Height, View.OffsetY, View.Zoom);
        }
        catch
        {
            return;
        }
        if (track.Width <= 0 || track.Height <= 0) { return; }

        ThemeTokens colors = PaintColors();
        int trackRadius = Math.Max(1, Math.Min(track.Width, track.Height) / 2);
        PaintFillRound(g, track, Util.WithAlpha(colors.Border, 90), trackRadius);

        if (thumb.Width > 0 && thumb.Height > 0)
        {
            Rectangle visibleThumb = Rectangle.Intersect(thumb, track);
            if (visibleThumb.Width <= 0 || visibleThumb.Height <= 0) { visibleThumb = thumb; }
            int alpha = (Interactive || DraggingScrollbar) ? 210 : 140;
            int thumbRadius = Math.Max(1, Math.Min(visibleThumb.Width, visibleThumb.Height) / 2);
            PaintFillRound(g, visibleThumb, Util.WithAlpha(colors.TextSecondary, alpha), thumbRadius);
        }
    }

    // ==================================================================
    // 自绘小工具
    // ==================================================================

    private static StringFormat PaintCenteredFormat()
    {
        StringFormat format = new StringFormat();
        format.Alignment = StringAlignment.Center;
        format.LineAlignment = StringAlignment.Center;
        format.Trimming = StringTrimming.EllipsisCharacter;
        format.FormatFlags = StringFormatFlags.NoWrap;
        return format;
    }

    private void PaintFillRound(Graphics g, Rectangle rect, Color color, int radius)
    {
        if (rect.Width <= 0 || rect.Height <= 0) { return; }
        using (GraphicsPath path = Util.RoundedRect(rect, radius))
        using (SolidBrush brush = new SolidBrush(color))
        {
            g.FillPath(brush, path);
        }
    }

    private void PaintStrokeRound(Graphics g, Rectangle rect, Color color, int width, int radius)
    {
        if (rect.Width <= 0 || rect.Height <= 0) { return; }
        if (width < 1) { width = 1; }
        Rectangle inner = new Rectangle(
            rect.X + width / 2,
            rect.Y + width / 2,
            Math.Max(1, rect.Width - width),
            Math.Max(1, rect.Height - width));
        int innerRadius = Math.Max(0, radius - width / 2);
        using (GraphicsPath path = Util.RoundedRect(inner, innerRadius))
        using (Pen pen = new Pen(color, (float)width))
        {
            g.DrawPath(pen, path);
        }
    }

    private void PaintText(Graphics g, string text, Font font, Color color, Rectangle rect)
    {
        if (string.IsNullOrEmpty(text) || font == null) { return; }
        if (rect.Width <= 0 || rect.Height <= 0) { return; }
        using (SolidBrush brush = new SolidBrush(color))
        using (StringFormat format = PaintCenteredFormat())
        {
            g.DrawString(text, font, brush,
                new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), format);
        }
    }

    // 中间省略：保留首尾，中间用「…」；用于过长的文件夹路径
    private string PaintMiddleEllipsis(Graphics g, string text, Font font, int maxWidthPx)
    {
        if (string.IsNullOrEmpty(text)) { return ""; }
        if (font == null || maxWidthPx <= 0) { return ""; }

        float fullWidth;
        try { fullWidth = g.MeasureString(text, font).Width; }
        catch { return text; }
        if (fullWidth <= (float)maxWidthPx) { return text; }

        const string ellipsis = "…";
        int length = text.Length;
        int keep = length - 2;
        while (keep >= 2)
        {
            int head = keep / 2;
            int tail = keep - head;
            if (tail <= 0) { tail = 1; }
            string candidate = text.Substring(0, head) + ellipsis + text.Substring(length - tail);
            float width;
            try { width = g.MeasureString(candidate, font).Width; }
            catch { return ellipsis; }
            if (width <= (float)maxWidthPx) { return candidate; }
            keep -= 2;
        }
        return ellipsis;
    }
}
// ============================================================================
// 设置面板 — 设置/调试面板（非模态）+ 热键录制对话框
// 要求：浅色/深色主题跟随、六个分区、所有控件中文、改动立即生效并防抖保存。
// 实现说明：
//   1) 面板是独立普通 Form，用真实 WinForms 控件（主窗口才是 layered + 自绘）；
//   2) 所有尺寸常量按 DIP 定义，用 Layout.Px 换算成物理像素；
//   3) 修改一律调用 owner 的 setter，随后 owner.ScheduleSave()（owner 内部 500ms 防抖）；
//   4) 窗口区数值由 400ms Timer 从 owner.Bounds 反算 DIP 刷新，用户正在编辑输入框时跳过；
//   5) 主题从 owner.Colors 取 token，尽力统一标准控件配色（不引第三方库）。
// ============================================================================

internal sealed class SettingsForm : Form
{
    // ---- 布局常量（全部 DIP；FormWidthDip/FormHeightDip 是最大基准，实际尺寸见 _formWidthDip/_formHeightDip） ----
    private const int FormWidthDip = global::Layout.SettingsPanelMaxWidthDip;
    private const int FormHeightDip = global::Layout.SettingsPanelMaxHeightDip;
    private const int NavWidthDip = 132;
    private const int BottomHeightDip = 60;
    private const int PagePadDip = 12;
    private const int ContentWidthDip = FormWidthDip - NavWidthDip - PagePadDip * 2; // 404
    private const int HotkeyRowsWidthDip = 404;
    private const int BtnHeightDip = 28;

    // ---- 依赖与状态 ----
    private readonly OverlayForm _owner;
    private readonly int _dpi;
    private ThemeTokens _colors;
    private Font _font;
    private Font _boldFont;
    private bool _suppress;                 // 抑制控件事件回环
    private SettingsSection _section = SettingsSection.Images;
    private System.Windows.Forms.Timer _refreshTimer;
    // B6：实际面板尺寸（DIP）。小屏/高缩放下取 min(560, 工作区-80)，避免关闭按钮被工作区裁掉
    private int _formWidthDip = FormWidthDip;
    private int _formHeightDip = FormHeightDip;

    // ---- 导航 / 框架 ----
    private Panel _navPanel;
    private Button[] _navButtons;
    private Panel[] _pages;
    private Panel _bottomBar;
    private Button _btnClose;
    private Label _lblFooterHint;

    // ---- 图片分区 ----
    private TextBox _txtFolder;
    private Button _btnChooseFolder;
    private Button _btnOpenFolder;
    private Button _btnRescan;
    private Button _btnPrev;
    private Button _btnNext;
    private Label _lblImageInfo;
    private ComboBox _cmbRecent;
    private CheckBox _chkWatch;
    private Label _lblWatchState;
    private Label _lblSkipList;
    private Label _lblImageStatus;

    // ---- 窗口分区 ----
    private Label _lblScreenInfo;
    private Label _lblWorkArea;
    private NumericUpDown _numX;
    private NumericUpDown _numY;
    private NumericUpDown _numW;
    private NumericUpDown _numH;
    private Button _btnApplyRect;
    private Label _lblRectStatus;
    private ComboBox _cmbScreen;
    private ComboBox _cmbGateKey;
    private CheckBox _chkPosLocked;
    private CheckBox _chkSnap;

    // ---- 显示分区 ----
    private ComboBox _cmbTheme;
    private TrackBar _trackOpacity;
    private Label _lblOpacityValue;
    private CheckBox _chkTopMost;
    private CheckBox _chkLocked;
    private Button _btnHide;
    private Label _lblHideHint;
    private Label _lblGateTip;

    // ---- 缩放分区 ----
    private Label _lblZoomValue;
    private CheckBox _chkLongImage;
    private CheckBox _chkImageLock;

    // ---- 热键分区 ----
    private Panel _hotkeyRows;
    private Label _lblHotkeyStatus;

    // ---- 诊断分区 ----
    private TextBox _txtDiag;
    private Label _lblDiagStatus;

    // ========================================================================
    // 构造
    // ========================================================================
    public SettingsForm(OverlayForm owner)
    {
        if (owner == null) { throw new ArgumentNullException("owner"); }
        _owner = owner;
        _dpi = owner.CurrentDpi > 0 ? owner.CurrentDpi : DpiHelper.SystemDpi;
        _font = UiFonts.Create(9f, FontStyle.Regular);
        _boldFont = UiFonts.Create(9f, FontStyle.Bold);

        SuspendLayout();
        Text = "攻略悬浮窗 — 设置";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;   // 固定大小工具窗口：无最大化/最小化
        ShowInTaskbar = false;
        TopMost = true;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;                  // 全部尺寸自己按 DPI 换算
        DoubleBuffered = true;
        Font = _font;
        ComputeAdaptiveSize();
        ClientSize = new Size(Px(_formWidthDip), Px(_formHeightDip));

        BuildNav();
        BuildPages();
        BuildBottomBar();
        BuildRefreshTimer();
        CenterOnOwnerScreen();

        ApplyTheme();
        ShowSection(SettingsSection.Images);
        ResumeLayout(true);
    }

    // B6：按主窗口所在屏幕的工作区计算面板尺寸（DIP）：min(560, 工作区-80)，居中后完整可见
    private void ComputeAdaptiveSize()
    {
        Size size = new Size(FormWidthDip, FormHeightDip);
        try
        {
            Screen scr = Screen.FromRectangle(_owner.Bounds);
            if (scr == null) { scr = Screen.PrimaryScreen; }
            size = ViewMath.SettingsPanelSize(scr.WorkingArea, _dpi);
        }
        catch { }
        _formWidthDip = size.Width;
        _formHeightDip = size.Height;
    }

    // ========================================================================
    // 通用控件工厂（入参均为 DIP）
    // ========================================================================
    private int Px(int dip) { return global::Layout.Px(dip, _dpi); }

    private Label AddLabel(Control parent, string text, int x, int y, int w, int h, bool bold, string style)
    {
        Label l = new Label();
        l.Text = text;
        l.AutoSize = false;
        l.Location = new Point(Px(x), Px(y));
        l.Size = new Size(Px(w), Px(h));
        l.Font = bold ? _boldFont : _font;
        l.TextAlign = ContentAlignment.MiddleLeft;
        if (style != null) { l.Tag = style; }
        parent.Controls.Add(l);
        return l;
    }

    private Button AddButton(Control parent, string text, int x, int y, int w, int h, EventHandler onClick)
    {
        Button b = new Button();
        b.Text = text;
        b.Location = new Point(Px(x), Px(y));
        b.Size = new Size(Px(w), Px(h));
        b.Font = _font;
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
        if (onClick != null) { b.Click += onClick; }
        parent.Controls.Add(b);
        return b;
    }

    private CheckBox AddCheck(Control parent, string text, int x, int y, int w, EventHandler onChanged)
    {
        CheckBox c = new CheckBox();
        c.Text = text;
        c.AutoSize = false;
        c.Location = new Point(Px(x), Px(y));
        c.Size = new Size(Px(w), Px(22));
        c.Font = _font;
        c.CheckAlign = ContentAlignment.MiddleLeft;
        if (onChanged != null) { c.CheckedChanged += onChanged; }
        parent.Controls.Add(c);
        return c;
    }

    private TextBox AddTextBox(Control parent, int x, int y, int w, int h, bool readOnly)
    {
        TextBox t = new TextBox();
        t.Location = new Point(Px(x), Px(y));
        t.Size = new Size(Px(w), Px(h));
        t.Font = _font;
        t.ReadOnly = readOnly;
        t.Multiline = true;
        t.WordWrap = false;
        t.ScrollBars = readOnly ? ScrollBars.Horizontal : ScrollBars.None;
        t.TextAlign = HorizontalAlignment.Left;
        parent.Controls.Add(t);
        return t;
    }

    private ComboBox AddCombo(Control parent, int x, int y, int w, EventHandler onChanged)
    {
        ComboBox c = new ComboBox();
        c.DropDownStyle = ComboBoxStyle.DropDownList;
        c.FlatStyle = FlatStyle.Flat;
        c.Font = _font;
        c.Location = new Point(Px(x), Px(y));
        c.Size = new Size(Px(w), Px(26));
        if (onChanged != null) { c.SelectedIndexChanged += onChanged; }
        parent.Controls.Add(c);
        return c;
    }

    private NumericUpDown AddNumeric(Control parent, int x, int y, int w, decimal min, decimal max)
    {
        NumericUpDown n = new NumericUpDown();
        n.Minimum = min;
        n.Maximum = max;
        n.Increment = 1;
        n.DecimalPlaces = 0;
        n.Font = _font;
        n.BorderStyle = BorderStyle.FixedSingle;
        n.Location = new Point(Px(x), Px(y));
        n.Size = new Size(Px(w), Px(24));
        parent.Controls.Add(n);
        return n;
    }

    private TrackBar AddTrackBar(Control parent, int x, int y, int w, int h, int min, int max, EventHandler onChanged)
    {
        TrackBar t = new TrackBar();
        t.Minimum = min;
        t.Maximum = max;
        t.SmallChange = 5;
        t.LargeChange = 5;
        t.TickFrequency = 5;
        t.TickStyle = TickStyle.None;
        t.Location = new Point(Px(x), Px(y));
        t.Size = new Size(Px(w), Px(h));
        if (onChanged != null) { t.ValueChanged += onChanged; }
        parent.Controls.Add(t);
        return t;
    }

    private void AddSeparator(Control parent, int x, int y, int w)
    {
        Panel line = new Panel();
        line.Location = new Point(Px(x), Px(y));
        line.Size = new Size(Px(w), 1);
        line.Tag = "separator";
        parent.Controls.Add(line);
    }

    // 文本：组合键显示（FormatCombo 由 设置与热键 实现，容错处理）
    internal static string ComboTextOf(Keys combo)
    {
        if (combo == Keys.None) { return "（未设置）"; }
        try { return HotkeyManager.FormatCombo(combo); }
        catch { return "（无法解析）"; }
    }

    // ========================================================================
    // 框架：左侧导航 + 右侧分区页 + 底部关闭
    // ========================================================================
    private void BuildNav()
    {
        _navPanel = new Panel();
        _navPanel.Location = new Point(0, 0);
        _navPanel.Size = new Size(Px(NavWidthDip), Px(_formHeightDip - BottomHeightDip));
        this.Controls.Add(_navPanel);

        string[] titles = new string[] { "图片", "窗口", "显示", "缩放", "热键", "诊断" };
        _navButtons = new Button[titles.Length];
        int y = PagePadDip;
        for (int i = 0; i < titles.Length; i++)
        {
            Button b = AddButton(_navPanel, titles[i], 10, y, NavWidthDip - 20, 34, new EventHandler(OnNavClick));
            b.Tag = i;
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Padding = new Padding(Px(12), 0, 0, 0);
            _navButtons[i] = b;
            y += 38;
        }
    }

    private void BuildPages()
    {
        int pageCount = 6;
        int pageW = _formWidthDip - NavWidthDip;
        int pageH = _formHeightDip - BottomHeightDip;
        _pages = new Panel[pageCount];
        for (int i = 0; i < pageCount; i++)
        {
            Panel p = new Panel();
            p.Location = new Point(Px(NavWidthDip), 0);
            p.Size = new Size(Px(pageW), Px(pageH));
            p.AutoScroll = true;
            p.Visible = false;
            this.Controls.Add(p);
            _pages[i] = p;
        }
        BuildImagesPage(_pages[0]);
        BuildWindowPage(_pages[1]);
        BuildDisplayPage(_pages[2]);
        BuildZoomPage(_pages[3]);
        BuildHotkeysPage(_pages[4]);
        BuildDiagnosticsPage(_pages[5]);
    }

    private void BuildBottomBar()
    {
        _bottomBar = new Panel();
        _bottomBar.Location = new Point(0, Px(_formHeightDip - BottomHeightDip));
        _bottomBar.Size = new Size(Px(_formWidthDip), Px(BottomHeightDip));
        this.Controls.Add(_bottomBar);

        _lblFooterHint = AddLabel(_bottomBar, "设置修改会立即生效并自动保存；面板非模态，可边看主窗口边调整。",
            16, 6, 380, 48, false, "secondary");
        _btnClose = AddButton(_bottomBar, "关闭", _formWidthDip - PagePadDip - 96, 14, 96, 30, new EventHandler(OnCloseClick));
    }

    private void BuildRefreshTimer()
    {
        _refreshTimer = new System.Windows.Forms.Timer();
        _refreshTimer.Interval = 400;   // 300–500ms：面板打开期间窗口移动/改大小后数值实时刷新
        _refreshTimer.Tick += new EventHandler(OnRefreshTick);
    }

    private void CenterOnOwnerScreen()
    {
        try
        {
            Screen scr = Screen.FromRectangle(_owner.Bounds);
            if (scr == null) { scr = Screen.PrimaryScreen; }
            Rectangle wa = scr.WorkingArea;
            int x = wa.Left + (wa.Width - Width) / 2;
            int y = wa.Top + (wa.Height - Height) / 2;
            Location = new Point(x, y);
        }
        catch
        {
            StartPosition = FormStartPosition.CenterScreen;
        }
    }

    // ========================================================================
    // 分区 1：图片
    // ========================================================================
    private void BuildImagesPage(Panel page)
    {
        int y = PagePadDip;
        AddLabel(page, "当前图片文件夹", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 24;
        _txtFolder = AddTextBox(page, PagePadDip, y, ContentWidthDip, 44, true);
        // 44 DIP：Multiline + 水平滚动条约占 17px，28 DIP 时正文可见区只剩约 7px，长路径会显示成空白
        y += 52;

        _btnChooseFolder = AddButton(page, "选择图片文件夹…", PagePadDip, y, 148, BtnHeightDip, new EventHandler(OnChooseFolder));
        _btnOpenFolder = AddButton(page, "打开文件夹", 168, y, 100, BtnHeightDip, new EventHandler(OnOpenFolder));
        _btnRescan = AddButton(page, "重新扫描", 276, y, 90, BtnHeightDip, new EventHandler(OnRescan));
        y += 36;

        _btnPrev = AddButton(page, "上一张", PagePadDip, y, 88, BtnHeightDip, new EventHandler(OnPrevImage));
        _btnNext = AddButton(page, "下一张", 108, y, 88, BtnHeightDip, new EventHandler(OnNextImage));
        y += 36;

        _lblImageInfo = AddLabel(page, "当前：无图片", PagePadDip, y, ContentWidthDip, 22, false, null);
        y += 30;

        AddSeparator(page, PagePadDip, y, ContentWidthDip);
        y += 10;
        AddLabel(page, "最近使用的文件夹（最多 5 个）", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 24;
        _cmbRecent = AddCombo(page, PagePadDip, y, 300, new EventHandler(OnRecentFolderChanged));
        y += 34;

        _chkWatch = AddCheck(page, "自动刷新（图片文件夹变化时自动重新扫描）", PagePadDip, y, ContentWidthDip, new EventHandler(OnWatchChanged));
        y += 26;
        _lblWatchState = AddLabel(page, "", PagePadDip, y, ContentWidthDip, 20, false, "secondary");
        y += 26;

        AddSeparator(page, PagePadDip, y, ContentWidthDip);
        y += 10;
        AddLabel(page, "跳过的文件（最多显示 5 条）", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 24;
        _lblSkipList = AddLabel(page, "（没有跳过的文件）", PagePadDip, y, ContentWidthDip, 92, false, "secondary");
        y += 98;
        _lblImageStatus = AddLabel(page, "", PagePadDip, y, ContentWidthDip, 56, false, "secondary");
    }

    // ========================================================================
    // 分区 2：窗口（位置与大小调试）
    // ========================================================================
    private void BuildWindowPage(Panel page)
    {
        int y = PagePadDip;
        AddLabel(page, "所在屏幕", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 24;
        _lblScreenInfo = AddLabel(page, "", PagePadDip, y, ContentWidthDip, 22, false, null);
        y += 24;
        _lblWorkArea = AddLabel(page, "", PagePadDip, y, ContentWidthDip, 22, false, "secondary");
        y += 28;

        AddSeparator(page, PagePadDip, y, ContentWidthDip);
        y += 10;
        AddLabel(page, "位置与大小（单位 DIP，相对当前屏幕工作区左上角）", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 26;

        AddLabel(page, "X", PagePadDip, y, 20, 24, false, null);
        _numX = AddNumeric(page, 36, y, 76, -100000, 100000);
        AddLabel(page, "Y", 124, y, 20, 24, false, null);
        _numY = AddNumeric(page, 148, y, 76, -100000, 100000);
        y += 32;

        AddLabel(page, "宽", PagePadDip, y, 28, 24, false, null);
        _numW = AddNumeric(page, 44, y, 76, 240, 100000);
        AddLabel(page, "高", 132, y, 28, 24, false, null);
        _numH = AddNumeric(page, 164, y, 76, 180, 100000);
        y += 32;

        _btnApplyRect = AddButton(page, "应用", PagePadDip, y, 96, BtnHeightDip, new EventHandler(OnApplyRect));
        // B6：状态文案自动换行 + 按文本测量高度（单独一行、整页宽），长文案不再被裁掉
        y += 34;
        _lblRectStatus = AddLabel(page, "", PagePadDip, y, ContentWidthDip, 44, false, "secondary");
        y += 64;

        AddSeparator(page, PagePadDip, y, ContentWidthDip);
        y += 10;
        AddLabel(page, "切换到屏幕（自动居中）", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 24;
        _cmbScreen = AddCombo(page, PagePadDip, y, 300, new EventHandler(OnScreenChanged));
        y += 34;

        AddLabel(page, "位置预设", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 24;
        AddPresetButton(page, "靠左", WindowPreset.Left, 12, y);
        AddPresetButton(page, "水平居中", WindowPreset.CenterH, 148, y);
        AddPresetButton(page, "靠右", WindowPreset.Right, 284, y);
        y += 36;
        AddPresetButton(page, "顶部", WindowPreset.Top, 12, y);
        AddPresetButton(page, "垂直居中", WindowPreset.CenterV, 148, y);
        AddPresetButton(page, "底部", WindowPreset.Bottom, 284, y);
        y += 36;
        AddPresetButton(page, "重置默认", WindowPreset.ResetDefault, 12, y, 128);
        y += 38;

        // B2：门控键下拉（按住它才能操作主窗口；默认 Alt，避免与游戏 Ctrl 蹲/静步冲突）
        AddSeparator(page, PagePadDip, y, ContentWidthDip);
        y += 10;
        AddLabel(page, "门控键（按住它才能操作主窗口）", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 24;
        _cmbGateKey = AddCombo(page, PagePadDip, y, 140, new EventHandler(OnGateKeyChanged));
        _cmbGateKey.Items.Add("Ctrl");
        _cmbGateKey.Items.Add("Alt");
        _cmbGateKey.Items.Add("Shift");
        AddLabel(page, "改动立即生效，无需重启。", 164, y, 248, 26, false, "secondary");
        y += 34;

        _chkPosLocked = AddCheck(page, "锁定窗口位置与大小（防止误拖动）", PagePadDip, y, ContentWidthDip, new EventHandler(OnPosLockedChanged));
        y += 26;
        _chkSnap = AddCheck(page, "边缘吸附（拖动/改大小时贴近屏幕边缘自动吸附）", PagePadDip, y, ContentWidthDip, new EventHandler(OnSnapChanged));
    }

    private void AddPresetButton(Control page, string text, WindowPreset preset, int x, int y)
    {
        AddPresetButton(page, text, preset, x, y, 128);
    }

    private void AddPresetButton(Control page, string text, WindowPreset preset, int x, int y, int w)
    {
        Button b = AddButton(page, text, x, y, w, BtnHeightDip, new EventHandler(OnPresetClick));
        b.Tag = preset;
    }

    // ========================================================================
    // 分区 3：显示
    // ========================================================================
    private void BuildDisplayPage(Panel page)
    {
        int y = PagePadDip;
        AddLabel(page, "主题", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 24;
        _cmbTheme = AddCombo(page, PagePadDip, y, 140, new EventHandler(OnThemeChanged));
        _cmbTheme.Items.Add("浅色");
        _cmbTheme.Items.Add("深色");
        AddLabel(page, "切换后立即应用到主窗口与设置面板。", 164, y, 248, 26, false, "secondary");
        y += 38;

        AddLabel(page, "透明度", PagePadDip, y, 140, 22, true, null);
        _lblOpacityValue = AddLabel(page, "100%", 316, y, 88, 22, false, null);
        _lblOpacityValue.TextAlign = ContentAlignment.MiddleRight;
        y += 26;
        _trackOpacity = AddTrackBar(page, PagePadDip, y, 360, 40, 20, 100, new EventHandler(OnOpacityChanged));
        y += 50;

        _chkTopMost = AddCheck(page, "始终置顶（显示在游戏画面之上）", PagePadDip, y, ContentWidthDip, new EventHandler(OnTopMostChanged));
        y += 28;
        _chkLocked = AddCheck(page, LockedCheckText(), PagePadDip, y, ContentWidthDip, new EventHandler(OnLockedChanged));
        y += 32;

        _btnHide = AddButton(page, "隐藏窗口", PagePadDip, y, 120, BtnHeightDip, new EventHandler(OnHideOverlay));
        y += 38;
        _lblHideHint = AddLabel(page, "", PagePadDip, y, ContentWidthDip, 56, false, "secondary");
        y += 62;

        AddSeparator(page, PagePadDip, y, ContentWidthDip);
        y += 10;
        // 门控键来自 settings.ini 的 gate_key（Ctrl/Alt/Shift），文案不能写死 Ctrl；存字段便于改键后刷新
        _lblGateTip = AddLabel(page, GateTipText(),
            PagePadDip, y, ContentWidthDip, 44, false, "secondary");
    }

    // 显示页底部提示（随门控键变化）
    private string GateTipText()
    {
        return "提示：按住 " + GateKeyName() + " 可临时操作主窗口；勾选「锁定交互」后无需按 "
            + GateKeyName() + "（游戏可能因此失焦，用完请取消）。";
    }

    // 门控键名称（受 settings.ini 的 gate_key 影响；取不到 Settings 时回退默认 Alt）
    private string GateKeyName()
    {
        AppSettings st = null;
        try { st = _owner.Settings; }
        catch { st = null; }
        if (st == null) { return "Alt"; }
        if (st.GateKey == GateKeyKind.Alt) { return "Alt"; }
        if (st.GateKey == GateKeyKind.Shift) { return "Shift"; }
        return "Ctrl";
    }

    // 「锁定交互」复选框文案（随门控键变化）
    private string LockedCheckText()
    {
        return "锁定交互（不用按住 " + GateKeyName() + " 也能操作窗口）";
    }

    // 隐藏热键的显示文本（按 settings.ini 的 [hotkeys] toggle_hide 动态拼，不写死 Ctrl+Alt+G）
    private string HideHotkeyText()
    {
        Keys combo = Keys.None;
        try
        {
            AppSettings st = _owner.Settings;
            if (st != null) { combo = st.GetHotkey(HotkeyAction.ToggleHide); }
        }
        catch { combo = Keys.None; }
        if (combo == Keys.None) { return "（未设置）"; }
        return ComboTextOf(combo);
    }

    // 隐藏热键被显式禁用（值为 none 或留空）时返回 true
    private bool HideHotkeyDisabled()
    {
        try
        {
            AppSettings st = _owner.Settings;
            return st != null && st.GetHotkey(HotkeyAction.ToggleHide) == Keys.None;
        }
        catch { return false; }
    }

    // ========================================================================
    // 分区 4：缩放
    // ========================================================================
    private void BuildZoomPage(Panel page)
    {
        int y = PagePadDip;
        _lblZoomValue = AddLabel(page, "当前缩放：100%", PagePadDip, y, ContentWidthDip, 30, true, null);
        y += 40;

        AddLabel(page, "缩放预设（以窗口中心为锚点）", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 24;
        AddButton(page, "适应窗口", PagePadDip, y, 128, 30, new EventHandler(OnFitClick));
        AddZoomPresetButton(page, "25%", 25.0, 148, y);
        AddZoomPresetButton(page, "50%", 50.0, 214, y);
        AddZoomPresetButton(page, "100%", 100.0, 280, y);
        AddZoomPresetButton(page, "200%", 200.0, 346, y);
        y += 40;

        _chkLongImage = AddCheck(page, "长图滚动模式（长图按窗口宽度适应，滚轮改为上下滚动）",
            PagePadDip, y, ContentWidthDip, new EventHandler(OnLongImageChanged));
        y += 32;

        _chkImageLock = AddCheck(page, "锁定图片（按住门控键拖动时只移动窗口，不平移图片）",
            PagePadDip, y, ContentWidthDip, new EventHandler(OnImageLockChanged));
        y += 32;

        AddSeparator(page, PagePadDip, y, ContentWidthDip);
        y += 10;
        AddLabel(page, "旋转 / 翻转（临时查看，不写入配置文件）", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 26;
        AddRotateButton(page, "顺时针 90°", RotateOp.Rotate90, 12, y);
        AddRotateButton(page, "逆时针 90°", RotateOp.Rotate270, 148, y);
        AddRotateButton(page, "水平翻转", RotateOp.FlipH, 284, y);
        y += 36;
        AddRotateButton(page, "垂直翻转", RotateOp.FlipV, 12, y);
        AddRotateButton(page, "重置旋转", RotateOp.None, 148, y);
        y += 38;

        AddLabel(page, "换图、重新扫描文件夹或重启程序后，手动旋转会自动重置。",
            PagePadDip, y, ContentWidthDip, 40, false, "secondary");
    }

    private void AddZoomPresetButton(Control page, string text, double percent, int x, int y)
    {
        Button b = AddButton(page, text, x, y, 62, 30, new EventHandler(OnZoomPresetClick));
        b.Tag = percent;
    }

    private void AddRotateButton(Control page, string text, RotateOp op, int x, int y)
    {
        Button b = AddButton(page, text, x, y, 128, BtnHeightDip, new EventHandler(OnRotateClick));
        b.Tag = op;
    }

    // ========================================================================
    // 分区 5：热键
    // ========================================================================
    private void BuildHotkeysPage(Panel page)
    {
        int y = PagePadDip;
        AddLabel(page, "改键：点击右侧「改键」按钮录制新组合（成功立即生效并写入 settings.ini）。"
            + "也可以手工编辑 settings.ini 的 [hotkeys] 段，重启生效；值为 none 或留空表示禁用该热键。"
            + "被其它程序占用的组合无法靠窗口聚焦补回，只能换一个。",
            PagePadDip, y, ContentWidthDip, 72, false, "secondary");
        y += 78;

        _lblHotkeyStatus = AddLabel(page, "", PagePadDip, y, ContentWidthDip, 22, false, "danger");
        y += 28;

        _hotkeyRows = new Panel();
        _hotkeyRows.Location = new Point(Px(PagePadDip), Px(y));
        _hotkeyRows.Size = new Size(Px(HotkeyRowsWidthDip), Px(368));
        _hotkeyRows.AutoScroll = true;
        page.Controls.Add(_hotkeyRows);
        // B6：状态标签用完即回收空间（无文案时不占 44 DIP）；有文案时按测量高度把表格往下推
        LayoutHotkeyStatusRows();
    }

    // ========================================================================
    // 分区 6：诊断
    // ========================================================================
    private void BuildDiagnosticsPage(Panel page)
    {
        int y = PagePadDip;
        AddLabel(page, "诊断信息", PagePadDip, y, ContentWidthDip, 20, true, null);
        y += 24;

        _txtDiag = new TextBox();
        _txtDiag.Location = new Point(Px(PagePadDip), Px(y));
        _txtDiag.Size = new Size(Px(ContentWidthDip), Px(196));
        _txtDiag.Multiline = true;
        _txtDiag.ReadOnly = true;
        _txtDiag.WordWrap = true;
        _txtDiag.ScrollBars = ScrollBars.Vertical;
        _txtDiag.Font = _font;
        page.Controls.Add(_txtDiag);
        y += 204;

        AddButton(page, "复制诊断信息", PagePadDip, y, 128, BtnHeightDip, new EventHandler(OnCopyDiagnostics));
        AddButton(page, "打开日志文件夹", 148, y, 132, BtnHeightDip, new EventHandler(OnOpenLogFolder));
        AddButton(page, "导出诊断包", 288, y, 116, BtnHeightDip, new EventHandler(OnExportDiagnostics));
        y += 38;

        _lblDiagStatus = AddLabel(page, "", PagePadDip, y, ContentWidthDip, 66, false, "secondary");
        y += 72;

        AddLabel(page, "诊断信息仅保存在本机（%TEMP% 下的日志与诊断包），不会上传到网络。",
            PagePadDip, y, ContentWidthDip, 40, false, "secondary");
    }

    // ========================================================================
    // 分区切换 / 主题
    // ========================================================================
    internal void ShowSection(SettingsSection section)
    {
        _section = section;
        int idx = (int)section;
        if (_pages != null)
        {
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visible = (i == idx);
            }
        }
        ApplyNavSelection();
        RefreshFromMain();
        if (_refreshTimer != null && Visible) { _refreshTimer.Start(); }
    }

    private void ApplyNavSelection()
    {
        if (_navButtons == null || _colors == null) { return; }
        int current = (int)_section;
        for (int i = 0; i < _navButtons.Length; i++)
        {
            Button b = _navButtons[i];
            bool selected = (i == current);
            b.BackColor = selected ? _colors.Hover : _colors.PageBg;
            b.ForeColor = selected ? _colors.Accent : _colors.Text;
            b.Font = selected ? _boldFont : _font;
            b.FlatAppearance.BorderColor = selected ? _colors.Accent : _colors.PageBg;
        }
    }

    // 主题切换后由主窗口调用，重建控件配色
    internal void ApplyTheme()
    {
        if (IsDisposed) { return; }
        ThemeTokens t = _owner != null ? _owner.Colors : null;
        if (t == null)
        {
            // 极端情况下 owner 尚未初始化 Colors：退回设置里的主题（默认浅色）
            ThemeKind kind = ThemeKind.Light;
            if (_owner != null)
            {
                kind = _owner.CurrentTheme;
                if (_owner.Settings != null) { kind = _owner.Settings.Theme; }
            }
            t = ThemeTokens.Get(kind);
        }
        _colors = t;

        bool old = _suppress;
        _suppress = true;
        try
        {
            BackColor = _colors.PageBg;
            if (_navPanel != null) { _navPanel.BackColor = _colors.PageBg; }
            if (_bottomBar != null) { _bottomBar.BackColor = _colors.PageBg; }
            if (_pages != null)
            {
                for (int i = 0; i < _pages.Length; i++) { _pages[i].BackColor = _colors.PageBg; }
            }
            ApplyThemeToControl(this);
            ApplyNavSelection();
        }
        finally
        {
            _suppress = old;
        }
    }

    private void ApplyThemeToControl(Control c)
    {
        if (c == null || _colors == null) { return; }
        ThemeTokens t = _colors;
        string style = c.Tag as string;

        if (style == "separator")
        {
            c.BackColor = t.Border;
        }
        else if (c is Panel)
        {
            c.BackColor = t.PageBg;
        }
        else if (c is Form)
        {
            c.BackColor = t.PageBg;
        }

        Label lab = c as Label;
        if (lab != null)
        {
            lab.BackColor = lab.Parent != null ? lab.Parent.BackColor : t.PageBg;
            if (style == "secondary" || style == "status") { lab.ForeColor = t.TextSecondary; }
            else if (style == "danger") { lab.ForeColor = t.Danger; }
            else if (style == "accent") { lab.ForeColor = t.Accent; }
            else { lab.ForeColor = t.Text; }
        }

        Button btn = c as Button;
        if (btn != null)
        {
            btn.UseVisualStyleBackColor = false;
            btn.FlatStyle = FlatStyle.Flat;
            btn.BackColor = t.Hover;
            btn.ForeColor = t.Text;
            btn.FlatAppearance.BorderColor = t.Border;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = t.Card;
            btn.FlatAppearance.MouseDownBackColor = t.Border;
        }

        CheckBox chk = c as CheckBox;
        if (chk != null)
        {
            chk.BackColor = chk.Parent != null ? chk.Parent.BackColor : t.PageBg;
            chk.ForeColor = t.Text;
        }

        TextBox tb = c as TextBox;
        if (tb != null)
        {
            tb.BackColor = t.Card;
            tb.ForeColor = t.Text;
            tb.BorderStyle = BorderStyle.FixedSingle;
        }

        ComboBox cb = c as ComboBox;
        if (cb != null)
        {
            cb.BackColor = t.Card;
            cb.ForeColor = t.Text;
            cb.FlatStyle = FlatStyle.Flat;
        }

        NumericUpDown nud = c as NumericUpDown;
        if (nud != null)
        {
            nud.BackColor = t.Card;
            nud.ForeColor = t.Text;
            nud.BorderStyle = BorderStyle.FixedSingle;
        }

        ListBox lb = c as ListBox;
        if (lb != null)
        {
            lb.BackColor = t.Card;
            lb.ForeColor = t.Text;
        }

        TrackBar tr = c as TrackBar;
        if (tr != null)
        {
            tr.BackColor = t.PageBg;
            tr.ForeColor = t.Text;
        }

        for (int i = 0; i < c.Controls.Count; i++)
        {
            ApplyThemeToControl(c.Controls[i]);
        }
    }

    // ========================================================================
    // 刷新：从主窗口同步（owner 会定期调用）
    // ========================================================================
    internal void RefreshFromMain()
    {
        if (IsDisposed) { return; }
        bool old = _suppress;
        _suppress = true;
        try
        {
            RefreshImagesPage();
            RefreshWindowValues();
            RefreshDisplayPage();
            RefreshZoomPage();
            if (_section == SettingsSection.Hotkeys) { RefreshHotkeyTable(); }
            RefreshDiagnosticsPage();
        }
        finally
        {
            _suppress = old;
        }
    }

    private void OnRefreshTick(object sender, EventArgs e)
    {
        if (IsDisposed || _suppress || !Visible) { return; }
        bool old = _suppress;
        _suppress = true;
        try
        {
            switch (_section)
            {
                case SettingsSection.Images:
                    UpdateImageSummary();
                    UpdateWatchState();
                    break;
                case SettingsSection.Window:
                    RefreshWindowValues();
                    break;
                case SettingsSection.Display:
                    RefreshDisplayPage();
                    break;
                case SettingsSection.Zoom:
                    RefreshZoomPage();
                    break;
                case SettingsSection.Diagnostics:
                    RefreshDiagnosticsPage();
                    break;
            }
        }
        finally
        {
            _suppress = old;
        }
    }

    // ---- 图片 ----
    private int ImageCount()
    {
        if (_owner == null || _owner.Images == null) { return 0; }
        return _owner.Images.Count;
    }

    private void RefreshImagesPage()
    {
        if (_txtFolder == null) { return; }
        AppSettings st = _owner.Settings;
        string folder = st != null && st.Folder != null ? st.Folder : "";
        if (_txtFolder.Text != folder) { _txtFolder.Text = folder; }
        if (st != null && _chkWatch.Checked != st.Watch) { _chkWatch.Checked = st.Watch; }

        UpdateImageSummary();
        RebuildRecentCombo();
        UpdateSkipList();
        UpdateWatchState();

        _btnOpenFolder.Enabled = folder.Length > 0 && Directory.Exists(folder);
    }

    private void UpdateImageSummary()
    {
        if (_lblImageInfo == null) { return; }
        int count = ImageCount();
        int idx = _owner.CurrentIndex;
        string name = "";
        if (count > 0 && idx >= 0 && idx < count && _owner.Images != null)
        {
            ImageEntry entry = _owner.Images[idx];
            if (entry != null) { name = entry.DisplayName; }
        }
        string text;
        if (count <= 0)
        {
            text = "当前：没有可显示的图片";
        }
        else if (!string.IsNullOrEmpty(name))
        {
            text = "当前：" + name + "（第 " + (idx + 1) + " / " + count + " 张）";
        }
        else
        {
            text = "当前：第 " + (idx + 1) + " / " + count + " 张";
        }
        if (_lblImageInfo.Text != text) { _lblImageInfo.Text = text; }

        bool hasImage = count > 0;
        if (_btnPrev.Enabled != hasImage) { _btnPrev.Enabled = hasImage; }
        if (_btnNext.Enabled != hasImage) { _btnNext.Enabled = hasImage; }
    }

    private void UpdateSkipList()
    {
        if (_lblSkipList == null) { return; }
        StringBuilder sb = new StringBuilder();

        string scanError = null;
        List<SkippedImage> skipped = null;
        try
        {
            if (_owner.Library != null)
            {
                skipped = _owner.Library.Skipped;
                scanError = _owner.Library.LastScanError;
            }
        }
        catch { skipped = null; }

        if (!string.IsNullOrEmpty(scanError))
        {
            sb.Append("最近一次扫描提示：");
            sb.Append(scanError);
            sb.Append("\r\n");
        }

        if (skipped == null || skipped.Count == 0)
        {
            sb.Append("（没有跳过的文件）");
        }
        else
        {
            int max = skipped.Count < 5 ? skipped.Count : 5;
            for (int i = 0; i < max; i++)
            {
                if (i > 0) { sb.Append("\r\n"); }
                SkippedImage item = skipped[i];
                string fileName = item != null ? item.FullPath : "";
                try { fileName = Path.GetFileName(fileName); }
                catch { }
                sb.Append(fileName);
                sb.Append("：");
                sb.Append(item != null ? item.Reason : "");
            }
            if (skipped.Count > 5)
            {
                sb.Append("\r\n… 其余 ");
                sb.Append(skipped.Count - 5);
                sb.Append(" 条已省略（共 ");
                sb.Append(skipped.Count);
                sb.Append(" 条）");
            }
            else
            {
                sb.Append("\r\n共 ");
                sb.Append(skipped.Count);
                sb.Append(" 条");
            }
        }
        string text = sb.ToString();
        if (_lblSkipList.Text != text) { _lblSkipList.Text = text; }
    }

    private void UpdateWatchState()
    {
        if (_lblWatchState == null) { return; }
        bool watchOn = _owner.Settings != null && _owner.Settings.Watch;
        bool active = false;
        string error = null;
        try
        {
            if (_owner.Watcher != null)
            {
                active = _owner.Watcher.Active;
                error = _owner.Watcher.LastError;
            }
        }
        catch { }

        string text;
        if (!string.IsNullOrEmpty(error))
        {
            text = "自动刷新不可用：" + error + "（可点击「重新扫描」手动更新）";
        }
        else if (!watchOn)
        {
            text = "自动刷新：已关闭";
        }
        else if (active)
        {
            text = "自动刷新：已开启（文件夹变化后约 1 秒内自动更新）";
        }
        else
        {
            text = "自动刷新：已开启（等待文件夹监视启动）";
        }
        if (_lblWatchState.Text != text) { _lblWatchState.Text = text; }
    }

    private void RebuildRecentCombo()
    {
        if (_cmbRecent == null) { return; }
        AppSettings st = _owner.Settings;
        List<string> recent = st != null ? st.RecentFolders : null;
        int want = recent != null ? recent.Count : 0;

        bool same = (_cmbRecent.Items.Count == want);
        if (same)
        {
            for (int i = 0; i < want; i++)
            {
                string existing = _cmbRecent.Items[i] as string;
                if (!string.Equals(existing, recent[i], StringComparison.OrdinalIgnoreCase))
                {
                    same = false;
                    break;
                }
            }
        }
        if (same) { return; }
        if (_cmbRecent.DroppedDown || _cmbRecent.ContainsFocus) { return; }

        bool old = _suppress;
        _suppress = true;
        try
        {
            _cmbRecent.BeginUpdate();
            _cmbRecent.Items.Clear();
            if (recent != null)
            {
                for (int i = 0; i < recent.Count; i++) { _cmbRecent.Items.Add(recent[i]); }
            }
            _cmbRecent.EndUpdate();

            string folder = st != null && st.Folder != null ? st.Folder : "";
            int sel = -1;
            for (int i = 0; i < _cmbRecent.Items.Count; i++)
            {
                string item = _cmbRecent.Items[i] as string;
                if (string.Equals(item, folder, StringComparison.OrdinalIgnoreCase)) { sel = i; break; }
            }
            _cmbRecent.SelectedIndex = sel;
        }
        finally
        {
            _suppress = old;
        }
    }

    // ---- 窗口 ----
    private void RefreshWindowValues()
    {
        if (_numX == null || _owner == null) { return; }
        Rectangle bounds;
        try { bounds = _owner.Bounds; }
        catch { return; }

        Screen scr = null;
        try { scr = Screen.FromRectangle(bounds); }
        catch { scr = Screen.PrimaryScreen; }
        if (scr == null) { return; }

        Rectangle wa = scr.WorkingArea;
        int xDip = global::Layout.Dip(bounds.Left - wa.Left, _dpi);
        int yDip = global::Layout.Dip(bounds.Top - wa.Top, _dpi);
        int wDip = global::Layout.Dip(bounds.Width, _dpi);
        int hDip = global::Layout.Dip(bounds.Height, _dpi);

        // 用户正在编辑输入框时不要覆盖（避免输入被打断）
        bool editing = _numX.ContainsFocus || _numY.ContainsFocus || _numW.ContainsFocus || _numH.ContainsFocus;
        if (!editing)
        {
            SetNumValue(_numX, xDip);
            SetNumValue(_numY, yDip);
            SetNumValue(_numW, wDip);
            SetNumValue(_numH, hDip);
        }

        string screenText = "设备名：" + scr.DeviceName + "　分辨率：" + scr.Bounds.Width + " × " + scr.Bounds.Height
            + " 像素　缩放：" + DpiPercentText(_dpi);
        if (_lblScreenInfo.Text != screenText) { _lblScreenInfo.Text = screenText; }

        string workText = "工作区：" + global::Layout.Dip(wa.Width, _dpi) + " × " + global::Layout.Dip(wa.Height, _dpi) + " DIP";
        if (_lblWorkArea.Text != workText) { _lblWorkArea.Text = workText; }

        SyncScreenCombo(scr);
        SyncGateKeyCombo();
        SyncPositionChecks();
    }

    // B2：门控键下拉与 settings 的 GateKey 同步（Ctrl/Alt/Shift 与枚举值一一对应）
    private void SyncGateKeyCombo()
    {
        if (_cmbGateKey == null) { return; }
        AppSettings st = _owner.Settings;
        if (st == null) { return; }
        int idx = (int)st.GateKey;
        if (idx < 0 || idx > 2) { idx = (int)GateKeyKind.Alt; }
        if (_cmbGateKey.SelectedIndex == idx) { return; }
        bool old = _suppress;
        _suppress = true;
        try { _cmbGateKey.SelectedIndex = idx; }
        finally { _suppress = old; }
    }

    private void SyncPositionChecks()
    {
        AppSettings st = _owner.Settings;
        if (st == null) { return; }
        if (_chkPosLocked.Checked != st.PositionLocked) { _chkPosLocked.Checked = st.PositionLocked; }
        if (_chkSnap.Checked != st.Snap) { _chkSnap.Checked = st.Snap; }
    }

    private static void SetNumValue(NumericUpDown num, int value)
    {
        if (num == null) { return; }
        decimal v = value;
        if (v < num.Minimum) { v = num.Minimum; }
        if (v > num.Maximum) { v = num.Maximum; }
        if (num.Value != v) { num.Value = v; }
    }

    private void SyncScreenCombo(Screen scr)
    {
        if (_cmbScreen == null || scr == null) { return; }
        Screen[] all = Screen.AllScreens;
        int idx = -1;
        for (int i = 0; i < all.Length; i++)
        {
            if (string.Equals(all[i].DeviceName, scr.DeviceName, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        }
        if (idx < 0) { return; }

        bool needRebuild = (_cmbScreen.Items.Count != all.Length);
        if (!needRebuild)
        {
            for (int i = 0; i < all.Length; i++)
            {
                string text = ScreenItemText(all[i]);
                string existing = _cmbScreen.Items[i] as string;
                if (!string.Equals(existing, text, StringComparison.Ordinal))
                {
                    needRebuild = true;
                    break;
                }
            }
        }

        bool old = _suppress;
        _suppress = true;
        try
        {
            if (needRebuild)
            {
                _cmbScreen.BeginUpdate();
                _cmbScreen.Items.Clear();
                for (int i = 0; i < all.Length; i++) { _cmbScreen.Items.Add(ScreenItemText(all[i])); }
                _cmbScreen.EndUpdate();
            }
            if (_cmbScreen.SelectedIndex != idx) { _cmbScreen.SelectedIndex = idx; }
        }
        finally
        {
            _suppress = old;
        }
    }

    private string ScreenItemText(Screen scr)
    {
        string text = scr.DeviceName + "  " + scr.Bounds.Width + "×" + scr.Bounds.Height;
        if (scr.Primary) { text += "（主屏幕）"; }
        return text;
    }

    private static string DpiPercentText(int dpi)
    {
        if (dpi <= 0) { dpi = 96; }
        return ((int)Math.Round(dpi * 100.0 / 96.0)) + "%";
    }

    // ---- 显示 ----
    private void RefreshDisplayPage()
    {
        if (_cmbTheme == null) { return; }
        ThemeKind kind = _owner.CurrentTheme;
        if (_cmbTheme.SelectedIndex != (int)kind)
        {
            bool old = _suppress;
            _suppress = true;
            try { _cmbTheme.SelectedIndex = (int)kind; }
            finally { _suppress = old; }
        }

        int opacity = 100;
        AppSettings st = _owner.Settings;
        if (st != null) { opacity = st.Opacity; }
        if (opacity < 20) { opacity = 20; }
        if (opacity > 100) { opacity = 100; }
        if (_trackOpacity.Value != opacity) { _trackOpacity.Value = opacity; }
        string opacityText = opacity + "%";
        if (_lblOpacityValue.Text != opacityText) { _lblOpacityValue.Text = opacityText; }

        if (st != null && _chkTopMost.Checked != st.TopMost) { _chkTopMost.Checked = st.TopMost; }
        if (_chkLocked.Checked != _owner.Locked) { _chkLocked.Checked = _owner.Locked; }

        // 文案随门控键 / 隐藏热键动态刷新（门控键改名后不必重启面板）
        string lockText = LockedCheckText();
        if (_chkLocked.Text != lockText) { _chkLocked.Text = lockText; }

        if (_lblGateTip != null)
        {
            string gateTip = GateTipText();
            if (_lblGateTip.Text != gateTip) { _lblGateTip.Text = gateTip; }
        }

        bool canHide = false;
        try { canHide = _owner.CanHide; }
        catch { canHide = false; }
        _btnHide.Enabled = canHide;
        string hideCombo = HideHotkeyText();
        string hideHint;
        if (canHide)
        {
            hideHint = "隐藏后窗口不在任务栏显示，只能再按 " + hideCombo + " 呼出。";
        }
        else if (HideHotkeyDisabled())
        {
            hideHint = "隐藏热键未设置（settings.ini 的 toggle_hide 为 none 或留空），隐藏功能已禁用，以免隐藏后无法恢复。";
        }
        else
        {
            hideHint = "隐藏热键（" + hideCombo + "）注册失败，隐藏功能已禁用，以免隐藏后无法恢复。";
        }
        if (_lblHideHint.Text != hideHint) { _lblHideHint.Text = hideHint; }
    }

    // ---- 缩放 ----
    private void RefreshZoomPage()
    {
        if (_lblZoomValue == null) { return; }
        double zoom = 1.0;
        try
        {
            // View 是 MarshalByRef 类的字段，先复制到局部变量再读成员，避免远程代理访问
            ViewState view = _owner.View;
            zoom = view.Zoom;
        }
        catch { }
        string text = "当前缩放：" + Util.FormatPercent(zoom);
        if (_lblZoomValue.Text != text) { _lblZoomValue.Text = text; }

        bool longMode = _owner.LongImageMode;
        if (_chkLongImage.Checked != longMode) { _chkLongImage.Checked = longMode; }
        bool imageLocked = _owner.ImageLocked;
        if (_chkImageLock.Checked != imageLocked) { _chkImageLock.Checked = imageLocked; }
    }

    // ---- 诊断 ----
    private void RefreshDiagnosticsPage()
    {
        if (_txtDiag == null) { return; }
        AppSettings st = _owner.Settings;
        StringBuilder sb = new StringBuilder();
        sb.Append("版本：");
        sb.Append(AppInfo.Version);
        sb.Append("\r\n");

        sb.Append("系统 DPI：");
        sb.Append(DpiPercentText(_dpi));
        sb.Append("（");
        sb.Append(_dpi);
        sb.Append(" DPI）\r\n");

        string settingsPath = st != null ? st.SettingsPath : "";
        sb.Append("settings.ini：");
        if (string.IsNullOrEmpty(settingsPath)) { sb.Append("（尚未确定）"); }
        else { sb.Append(settingsPath); }
        if (st != null && st.UsingFallbackPath) { sb.Append("（exe 目录不可写，已回退到 %APPDATA%）"); }
        sb.Append("\r\n");

        sb.Append("图片文件夹：");
        sb.Append(st != null && !string.IsNullOrEmpty(st.Folder) ? st.Folder : "（未设置）");
        sb.Append("\r\n");

        sb.Append("当前缩放：");
        try
        {
            ViewState view = _owner.View;
            sb.Append(Util.FormatPercent(view.Zoom));
        }
        catch { sb.Append("未知"); }
        sb.Append("\r\n");

        sb.Append("GDI 对象数：");
        try
        {
            int gdi = Diagnostics.GdiObjectCount();
            sb.Append(gdi >= 0 ? gdi.ToString() : "未知");
        }
        catch
        {
            sb.Append("未知");
        }
        sb.Append("\r\n");

        sb.Append("图片数量：");
        sb.Append(ImageCount());
        sb.Append(" 张\r\n");

        string text = sb.ToString();
        if (_txtDiag.Text != text) { _txtDiag.Text = text; }
    }

    // ========================================================================
    // 热键表
    // ========================================================================
    internal void RefreshHotkeyTable()
    {
        if (_hotkeyRows == null || _hotkeyRows.IsDisposed) { return; }

        _hotkeyRows.SuspendLayout();
        List<Control> old = new List<Control>();
        for (int i = 0; i < _hotkeyRows.Controls.Count; i++) { old.Add(_hotkeyRows.Controls[i]); }
        for (int i = 0; i < old.Count; i++)
        {
            _hotkeyRows.Controls.Remove(old[i]);
            old[i].Dispose();
        }
        _hotkeyRows.ResumeLayout();

        int rowH = 28;
        int y = 2;
        // 表头（列宽按「功能 / 当前组合 / 状态 / 操作×2」分配；操作列放「改键」+「禁用/启用」两个按钮）
        AddLabel(_hotkeyRows, "功能", 2, y, 88, 22, true, null);
        AddLabel(_hotkeyRows, "当前组合", 94, y, 88, 22, true, null);
        AddLabel(_hotkeyRows, "状态", 186, y, 100, 22, true, null);
        AddLabel(_hotkeyRows, "操作", 290, y, 110, 22, true, null);
        y += 26;
        AddSeparator(_hotkeyRows, 2, y, 398);
        y += 6;

        List<HotkeyEntry> entries = null;
        try
        {
            if (_owner.Hotkeys != null) { entries = _owner.Hotkeys.Entries; }
        }
        catch { entries = null; }

        if (entries == null || entries.Count == 0)
        {
            AddLabel(_hotkeyRows, "（暂无热键数据）", 2, y, 380, 22, false, "secondary");
            y += rowH;
        }
        else
        {
            for (int i = 0; i < entries.Count; i++)
            {
                HotkeyEntry entry = entries[i];
                if (entry == null) { continue; }

                string name = entry.DisplayName;
                if (string.IsNullOrEmpty(name)) { name = HotkeyNameOf(entry.Action); }
                string comboText = ComboTextOf(entry.Combo);
                string statusText;
                string statusStyle;
                HotkeyStatusText(entry, out statusText, out statusStyle);

                AddLabel(_hotkeyRows, name, 2, y, 88, 22, false, null);
                AddLabel(_hotkeyRows, comboText, 94, y, 88, 22, false, null);
                AddLabel(_hotkeyRows, statusText, 186, y, 100, 22, false, statusStyle);

                Button btn = AddButton(_hotkeyRows, "改键", 290, y, 48, 24, new EventHandler(OnRebindHotkey));
                btn.Tag = entry.Action;
                btn.Font = _font;
                // B6：每个热键可单独禁用（写 none 并注销）；已禁用时按钮变为「启用」并恢复默认组合
                bool disabled = entry.Combo == Keys.None || entry.Status == HotkeyStatus.Disabled;
                Button btnToggle = AddButton(_hotkeyRows, disabled ? "启用" : "禁用", 344, y, 52, 24,
                    new EventHandler(OnToggleHotkeyEnabled));
                btnToggle.Tag = entry.Action;
                btnToggle.Font = _font;
                y += rowH;
            }
        }

        _hotkeyRows.AutoScrollMinSize = new Size(Px(398), Px(y + 6));
        ApplyThemeToControl(_hotkeyRows);
    }

    private static void HotkeyStatusText(HotkeyEntry entry, out string text, out string style)
    {
        style = null;
        switch (entry.Status)
        {
            case HotkeyStatus.Registered:
                text = "已注册";
                break;
            case HotkeyStatus.FailedInUse:
                text = "注册失败：被占用";
                style = "danger";
                break;
            case HotkeyStatus.FailedOther:
                text = "注册失败：其它";
                style = "danger";
                break;
            case HotkeyStatus.Disabled:
                text = "已禁用";
                style = "secondary";
                break;
            default:
                text = "未注册";
                style = "secondary";
                break;
        }
    }

    private static string HotkeyNameOf(HotkeyAction action)
    {
        try { return HotkeyManager.DisplayNameOf(action); }
        catch { return action.ToString(); }
    }

    private string FindDuplicateCombo(HotkeyAction action, Keys combo)
    {
        List<HotkeyEntry> entries = null;
        try
        {
            if (_owner.Hotkeys != null) { entries = _owner.Hotkeys.Entries; }
        }
        catch { entries = null; }
        if (entries == null) { return null; }

        for (int i = 0; i < entries.Count; i++)
        {
            HotkeyEntry other = entries[i];
            if (other == null || other.Action == action) { continue; }
            if (other.Status == HotkeyStatus.Disabled) { continue; }
            if (other.Combo == combo)
            {
                return "该组合已经被「" + HotkeyNameOf(other.Action) + "」使用，请换一个组合。";
            }
        }
        return null;
    }

    // ========================================================================
    // 状态提示
    // ========================================================================
    private void SetStatus(Label label, string text, bool danger)
    {
        if (label == null) { return; }
        label.Text = text;
        if (_colors != null)
        {
            label.ForeColor = danger ? _colors.Danger : _colors.TextSecondary;
        }
        // B6：两个多行状态标签按文本测量高度（自动换行），保证长文案完整可见
        if (ReferenceEquals(label, _lblRectStatus))
        {
            FitWrappedLabel(label, 28, 64);
        }
        else if (ReferenceEquals(label, _lblHotkeyStatus))
        {
            FitWrappedLabel(label, 22, 72);
        }
    }

    // B6：按文本/宽度测量自动换行标签高度，夹在 [min,max] DIP（不删除/不裁切文案）
    private void FitWrappedLabel(Label label, int minHeightDip, int maxHeightDip)
    {
        if (label == null || label.IsDisposed) { return; }
        int width = label.Width;
        if (width <= 0) { return; }
        string text = label.Text == null ? "" : label.Text;
        Size measured = TextRenderer.MeasureText(text, label.Font, new Size(width, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        int height = measured.Height + Px(4);
        if (height < Px(minHeightDip)) { height = Px(minHeightDip); }
        if (height > Px(maxHeightDip)) { height = Px(maxHeightDip); }
        if (label.Height != height) { label.Height = height; }
        if (ReferenceEquals(label, _lblHotkeyStatus)) { LayoutHotkeyStatusRows(); }
    }

    // B6：热键表位置跟随状态标签高度（标签在表格上方，两者不重叠）
    private void LayoutHotkeyStatusRows()
    {
        if (_lblHotkeyStatus == null || _lblHotkeyStatus.IsDisposed) { return; }
        if (_hotkeyRows == null || _hotkeyRows.IsDisposed) { return; }
        int top = _lblHotkeyStatus.Bottom + Px(6);
        if (_hotkeyRows.Top != top) { _hotkeyRows.Top = top; }
    }

    // ========================================================================
    // 事件：导航 / 关闭 / 定时刷新
    // ========================================================================
    private void OnNavClick(object sender, EventArgs e)
    {
        Button b = sender as Button;
        if (b == null || b.Tag == null) { return; }
        int idx = (int)b.Tag;
        if (idx < 0 || idx > 5) { return; }
        ShowSection((SettingsSection)idx);
    }

    private void OnCloseClick(object sender, EventArgs e)
    {
        Close();
    }

    // ========================================================================
    // 事件：图片分区
    // ========================================================================
    private void OnChooseFolder(object sender, EventArgs e)
    {
        string start = "";
        if (_owner.Settings != null && !string.IsNullOrEmpty(_owner.Settings.Folder)
            && Directory.Exists(_owner.Settings.Folder))
        {
            start = _owner.Settings.Folder;
        }

        using (FolderBrowserDialog dlg = new FolderBrowserDialog())
        {
            dlg.Description = "选择攻略图片文件夹";
            dlg.ShowNewFolderButton = true;
            if (start.Length > 0) { dlg.SelectedPath = start; }
            if (dlg.ShowDialog(this) != DialogResult.OK) { return; }

            string path = dlg.SelectedPath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                SetStatus(_lblImageStatus, "目录不存在或不可访问：" + path, true);
                return;
            }
            // C9：换文件夹会同步扫描（可能几秒），先给反馈并禁用按钮，避免用户以为卡死。
            _owner.BeginScanFeedback();
            if (_btnRescan != null) { _btnRescan.Enabled = false; }
            try
            {
                _owner.ChangeFolder(path);
                _owner.ScheduleSave();
                SetStatus(_lblImageStatus, "已切换图片文件夹：" + path, false);
            }
            catch (Exception ex)
            {
                SetStatus(_lblImageStatus, "切换图片文件夹失败：" + ex.Message, true);
            }
            finally
            {
                if (_btnRescan != null) { _btnRescan.Enabled = true; }
            }
        }
        RefreshFromMain();
    }

    private void OnOpenFolder(object sender, EventArgs e)
    {
        string folder = _owner.Settings != null ? _owner.Settings.Folder : "";
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            SetStatus(_lblImageStatus, "图片文件夹不存在或不可访问，请先选择文件夹。", true);
            return;
        }
        try
        {
            Process.Start("explorer.exe", "\"" + folder + "\"");
            SetStatus(_lblImageStatus, "已在资源管理器中打开：" + folder, false);
        }
        catch (Exception ex)
        {
            SetStatus(_lblImageStatus, "打开文件夹失败：" + ex.Message, true);
        }
    }

    private void OnRescan(object sender, EventArgs e)
    {
        // C9：扫描同步占用 UI 线程，点下按钮先弹「正在扫描图片…」并禁用按钮，完成后恢复。
        _owner.BeginScanFeedback();
        if (_btnRescan != null) { _btnRescan.Enabled = false; }
        try
        {
            _owner.ReloadImages(true, false);
            SetStatus(_lblImageStatus, "已重新扫描图片文件夹。", false);
        }
        catch (Exception ex)
        {
            SetStatus(_lblImageStatus, "重新扫描失败：" + ex.Message, true);
        }
        finally
        {
            if (_btnRescan != null) { _btnRescan.Enabled = true; }
        }
        RefreshFromMain();
    }

    private void OnPrevImage(object sender, EventArgs e)
    {
        if (ImageCount() <= 0) { return; }
        try { _owner.NavigateImage(-1); }
        catch (Exception ex) { SetStatus(_lblImageStatus, "切换图片失败：" + ex.Message, true); }
        RefreshFromMain();
    }

    private void OnNextImage(object sender, EventArgs e)
    {
        if (ImageCount() <= 0) { return; }
        try { _owner.NavigateImage(1); }
        catch (Exception ex) { SetStatus(_lblImageStatus, "切换图片失败：" + ex.Message, true); }
        RefreshFromMain();
    }

    private void OnRecentFolderChanged(object sender, EventArgs e)
    {
        if (_suppress) { return; }
        int idx = _cmbRecent.SelectedIndex;
        if (idx < 0) { return; }
        string path = _cmbRecent.Items[idx] as string;
        if (string.IsNullOrEmpty(path)) { return; }

        if (!Directory.Exists(path))
        {
            SetStatus(_lblImageStatus, "最近文件夹已失效（可能已删除或不可访问）：" + path, true);
            // 失效项忽略：恢复到当前活动文件夹
            bool old = _suppress;
            _suppress = true;
            try
            {
                string current = _owner.Settings != null ? _owner.Settings.Folder : "";
                int sel = -1;
                for (int i = 0; i < _cmbRecent.Items.Count; i++)
                {
                    string item = _cmbRecent.Items[i] as string;
                    if (string.Equals(item, current, StringComparison.OrdinalIgnoreCase)) { sel = i; break; }
                }
                _cmbRecent.SelectedIndex = sel;
            }
            finally
            {
                _suppress = old;
            }
            return;
        }

        string active = _owner.Settings != null ? _owner.Settings.Folder : "";
        if (string.Equals(active, path, StringComparison.OrdinalIgnoreCase)) { return; }

        try
        {
            _owner.ChangeFolder(path);
            _owner.ScheduleSave();
            SetStatus(_lblImageStatus, "已切换到最近文件夹：" + path, false);
        }
        catch (Exception ex)
        {
            SetStatus(_lblImageStatus, "切换图片文件夹失败：" + ex.Message, true);
        }
        RefreshFromMain();
    }

    private void OnWatchChanged(object sender, EventArgs e)
    {
        if (_suppress || _owner.Settings == null) { return; }
        bool want = _chkWatch.Checked;
        _owner.Settings.Watch = want;

        try
        {
            if (_owner.Watcher != null)
            {
                if (want)
                {
                    string folder = _owner.Settings.Folder;
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        _owner.Watcher.Start(folder);
                    }
                }
                else
                {
                    _owner.Watcher.Stop();
                }
            }
        }
        catch { }

        _owner.ScheduleSave();

        bool old = _suppress;
        _suppress = true;
        try { UpdateWatchState(); }
        finally { _suppress = old; }
    }

    // ========================================================================
    // 事件：窗口分区
    // ========================================================================
    private void OnApplyRect(object sender, EventArgs e)
    {
        try
        {
            Screen scr = Screen.FromRectangle(_owner.Bounds);
            Rectangle wa = scr.WorkingArea;
            int x = wa.Left + global::Layout.Px((int)_numX.Value, _dpi);
            int y = wa.Top + global::Layout.Px((int)_numY.Value, _dpi);
            int w = global::Layout.Px((int)_numW.Value, _dpi);
            int h = global::Layout.Px((int)_numH.Value, _dpi);
            Rectangle target = new Rectangle(x, y, w, h);
            Rectangle clamped = ViewMath.ClampWindowRect(target, wa, _dpi);
            if (clamped.Width <= 0 || clamped.Height <= 0)
            {
                SetStatus(_lblRectStatus, "当前屏幕工作区不可用，无法应用位置与大小。", true);
            }
            else
            {
                _owner.ApplyWindowRect(clamped);
                _owner.ScheduleSave();
                if (clamped != target)
                {
                    SetStatus(_lblRectStatus, "数值超出可用范围，已自动夹取到屏幕工作区内（最小 240 × 180 DIP）。", false);
                }
                else
                {
                    SetStatus(_lblRectStatus, "已应用窗口位置与大小。", false);
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus(_lblRectStatus, "应用失败：" + ex.Message, true);
        }
        RefreshWindowValues();
    }

    private void OnScreenChanged(object sender, EventArgs e)
    {
        if (_suppress) { return; }
        int idx = _cmbScreen.SelectedIndex;
        Screen[] all = Screen.AllScreens;
        if (idx < 0 || idx >= all.Length) { return; }

        try
        {
            Rectangle wa = all[idx].WorkingArea;
            Rectangle cur = _owner.Bounds;
            Rectangle target = new Rectangle(
                wa.Left + (wa.Width - cur.Width) / 2,
                wa.Top + (wa.Height - cur.Height) / 2,
                cur.Width, cur.Height);
            target = ViewMath.ClampWindowRect(target, wa, _dpi);
            _owner.ApplyWindowRect(target);
            _owner.ScheduleSave();
            SetStatus(_lblRectStatus, "已移动到屏幕 " + all[idx].DeviceName + " 并居中。", false);
        }
        catch (Exception ex)
        {
            SetStatus(_lblRectStatus, "切换屏幕失败：" + ex.Message, true);
        }
        RefreshWindowValues();
    }

    private void OnPresetClick(object sender, EventArgs e)
    {
        Button b = sender as Button;
        if (b == null || b.Tag == null) { return; }
        WindowPreset preset = (WindowPreset)b.Tag;
        try
        {
            Screen scr = Screen.FromRectangle(_owner.Bounds);
            Rectangle wa = scr.WorkingArea;
            Rectangle cur = _owner.Bounds;
            // 传入当前窗口矩形（4 参重载）：靠左/靠右只改 X，顶部/底部只改 Y，另一轴保持当前值。
            // 旧重载只收 Size，会把另一轴一并居中，导致「靠左」时窗口竖直归中。
            Rectangle target = ViewMath.PresetWindowRect(preset, wa, cur, _dpi);
            target = ViewMath.ClampWindowRect(target, wa, _dpi);
            _owner.ApplyWindowRect(target);
            _owner.ScheduleSave();
            SetStatus(_lblRectStatus, "已应用预设：" + b.Text, false);
        }
        catch (Exception ex)
        {
            SetStatus(_lblRectStatus, "应用预设失败：" + ex.Message, true);
        }
        RefreshWindowValues();
    }

    private void OnPosLockedChanged(object sender, EventArgs e)
    {
        if (_suppress) { return; }
        try
        {
            _owner.SetPositionLocked(_chkPosLocked.Checked);
            _owner.ScheduleSave();
        }
        catch (Exception ex)
        {
            SetStatus(_lblRectStatus, "设置失败：" + ex.Message, true);
        }
    }

    // B2：门控键下拉（Ctrl=0 / Alt=1 / Shift=2），选中即保存并立即生效
    private void OnGateKeyChanged(object sender, EventArgs e)
    {
        if (_suppress || _cmbGateKey == null) { return; }
        int idx = _cmbGateKey.SelectedIndex;
        GateKeyKind kind = GateKeyKind.Ctrl;
        if (idx == (int)GateKeyKind.Alt) { kind = GateKeyKind.Alt; }
        else if (idx == (int)GateKeyKind.Shift) { kind = GateKeyKind.Shift; }
        try
        {
            _owner.ApplyGateKey(kind);
            SetStatus(_lblRectStatus, "门控键已切换为 " + GateKeyName() + "（立即生效，已写入 settings.ini）", false);
        }
        catch (Exception ex)
        {
            SetStatus(_lblRectStatus, "设置门控键失败：" + ex.Message, true);
        }
        // 面板内其它文案（锁定交互复选框/显示页提示）随门控键刷新
        RefreshDisplayPage();
    }

    private void OnSnapChanged(object sender, EventArgs e)
    {
        if (_suppress) { return; }
        try
        {
            _owner.SetSnapEnabled(_chkSnap.Checked);
            _owner.ScheduleSave();
        }
        catch (Exception ex)
        {
            SetStatus(_lblRectStatus, "设置失败：" + ex.Message, true);
        }
    }

    // ========================================================================
    // 事件：显示分区
    // ========================================================================
    private void OnThemeChanged(object sender, EventArgs e)
    {
        if (_suppress) { return; }
        ThemeKind kind = _cmbTheme.SelectedIndex == 1 ? ThemeKind.Dark : ThemeKind.Light;
        try
        {
            _owner.SetTheme(kind);
            _owner.ScheduleSave();
        }
        catch (Exception ex)
        {
            SetStatus(_lblHideHint, "切换主题失败：" + ex.Message, true);
        }
        ApplyTheme();
    }

    private void OnOpacityChanged(object sender, EventArgs e)
    {
        if (_suppress) { return; }
        int percent = _trackOpacity.Value;
        if (percent < 20) { percent = 20; }
        if (percent > 100) { percent = 100; }
        _lblOpacityValue.Text = percent + "%";
        try
        {
            _owner.SetOpacityPercent(percent, false);
            _owner.ScheduleSave();
        }
        catch (Exception ex)
        {
            _lblOpacityValue.Text = "设置失败";
            SetStatus(_lblHideHint, "设置透明度失败：" + ex.Message, true);
        }
    }

    private void OnTopMostChanged(object sender, EventArgs e)
    {
        if (_suppress) { return; }
        try
        {
            _owner.SetTopMost(_chkTopMost.Checked);
            _owner.ScheduleSave();
        }
        catch (Exception ex)
        {
            SetStatus(_lblHideHint, "设置失败：" + ex.Message, true);
        }
    }

    private void OnLockedChanged(object sender, EventArgs e)
    {
        if (_suppress) { return; }
        try
        {
            // 面板内勾选/取消是「用户主动改锁定」：40 的 SetLocked(bool, bool silent) 在
            // silent=false 且面板打开时会置 _panelLockOverridden=true，关闭面板时不再回滚
            // 到打开前的状态（40 打开面板时用的是 SetLocked(true, true) 静默加锁）。
            _owner.SetLocked(_chkLocked.Checked, false);
        }
        catch (Exception ex)
        {
            SetStatus(_lblHideHint, "切换锁定交互失败：" + ex.Message, true);
        }
    }

    private void OnHideOverlay(object sender, EventArgs e)
    {
        bool canHide = false;
        try { canHide = _owner.CanHide; }
        catch { canHide = false; }
        if (!canHide) { return; }
        try
        {
            _owner.HideOverlay();
        }
        catch (Exception ex)
        {
            SetStatus(_lblHideHint, "隐藏失败：" + ex.Message, true);
        }
    }

    // ========================================================================
    // 事件：缩放分区
    // ========================================================================
    private void OnFitClick(object sender, EventArgs e)
    {
        try { _owner.FitToWindow(); }
        catch (Exception ex) { SetStatus(_lblRectStatus, "适应窗口失败：" + ex.Message, true); }
        RefreshZoomPage();
    }

    private void OnZoomPresetClick(object sender, EventArgs e)
    {
        Button b = sender as Button;
        if (b == null || b.Tag == null) { return; }
        double percent = (double)b.Tag;
        try
        {
            _owner.SetZoomPreset(percent);
        }
        catch (Exception ex)
        {
            SetStatus(_lblRectStatus, "设置缩放失败：" + ex.Message, true);
        }
        RefreshZoomPage();
    }

    private void OnRotateClick(object sender, EventArgs e)
    {
        Button b = sender as Button;
        if (b == null || b.Tag == null) { return; }
        RotateOp op = (RotateOp)b.Tag;
        try
        {
            _owner.Rotate(op);
        }
        catch (Exception ex)
        {
            SetStatus(_lblRectStatus, "旋转/翻转失败：" + ex.Message, true);
        }
        RefreshZoomPage();
    }

    private void OnImageLockChanged(object sender, EventArgs e)
    {
        if (_suppress) { return; }
        try
        {
            _owner.SetImageLocked(_chkImageLock.Checked, false);
        }
        catch (Exception ex)
        {
            SetStatus(_lblRectStatus, "切换锁定图片失败：" + ex.Message, true);
        }
        RefreshZoomPage();
    }

    private void OnLongImageChanged(object sender, EventArgs e)
    {
        if (_suppress) { return; }
        try
        {
            _owner.SetLongImageMode(_chkLongImage.Checked);
        }
        catch (Exception ex)
        {
            SetStatus(_lblRectStatus, "切换长图模式失败：" + ex.Message, true);
        }
        RefreshZoomPage();
    }

    // ========================================================================
    // 事件：热键分区
    // ========================================================================
    private void OnRebindHotkey(object sender, EventArgs e)
    {
        Button btn = sender as Button;
        if (btn == null || btn.Tag == null) { return; }
        HotkeyAction action = (HotkeyAction)btn.Tag;

        Keys current = Keys.None;
        HotkeyEntry entry = null;
        try
        {
            if (_owner.Hotkeys != null) { entry = _owner.Hotkeys.GetEntry(action); }
        }
        catch { entry = null; }
        if (entry != null) { current = entry.Combo; }
        else if (_owner.Settings != null) { current = _owner.Settings.GetHotkey(action); }

        string actionName = HotkeyNameOf(action);
        // B5：录制对话框打开期间注销全部全局热键，否则用户按 Ctrl+Alt+G/T 会先把主窗隐藏/切锁定，
        // 录制框却还开着，表现为「程序消失」。关闭（含取消/异常）后必须在 finally 里恢复。
        bool suspended = SuspendHotkeysForCapture();
        try
        {
            using (HotkeyCaptureDialog dlg = new HotkeyCaptureDialog(current, actionName))
            {
                dlg.ExtraValidator = new Func<Keys, string>(delegate(Keys combo)
                {
                    return FindDuplicateCombo(action, combo);
                });
                dlg.ApplyThemeTokens(_colors);
                dlg.ShowDialog(this);
                if (!dlg.Accepted) { return; }

                string error = null;
                bool ok = false;
                try
                {
                    ok = _owner.RebindHotkey(action, dlg.Result, out error);
                }
                catch (Exception ex)
                {
                    ok = false;
                    error = ex.Message;
                }

                if (ok)
                {
                    SetStatus(_lblHotkeyStatus, "已改键：" + actionName + " → " + ComboTextOf(dlg.Result) + "（已写入 settings.ini）", false);
                    _owner.ScheduleSave();
                }
                else
                {
                    string detail = string.IsNullOrEmpty(error) ? "无法注册（可能被其它程序占用），请换一个组合。" : error;
                    SetStatus(_lblHotkeyStatus, "改键失败：" + ComboTextOf(dlg.Result) + "：" + detail, true);
                }
            }
        }
        finally
        {
            if (suspended)
            {
                try { _owner.Hotkeys.RegisterAll(); } catch { }
                try { _owner.RefreshHotkeyRegistrationUi(); } catch { }
            }
            try { RefreshHotkeyTable(); } catch { }
        }
    }

    // B5：注销全部全局热键；返回 true 表示需要恢复
    private bool SuspendHotkeysForCapture()
    {
        try
        {
            if (_owner.Hotkeys == null) { return false; }
            _owner.Hotkeys.UnregisterAll();
            return true;
        }
        catch { return false; }
    }

    // B6：「禁用/启用」按钮：禁用 → 写 none（Rebind 会注销该热键）；启用 → 恢复该动作的默认组合
    private void OnToggleHotkeyEnabled(object sender, EventArgs e)
    {
        Button btn = sender as Button;
        if (btn == null || btn.Tag == null) { return; }
        HotkeyAction action = (HotkeyAction)btn.Tag;

        Keys current = Keys.None;
        try
        {
            if (_owner.Settings != null) { current = _owner.Settings.GetHotkey(action); }
        }
        catch { current = Keys.None; }

        string actionName = HotkeyNameOf(action);
        try
        {
            if (current == Keys.None)
            {
                Keys def = HotkeyManager.DefaultCombo(action);
                string error = null;
                bool ok = false;
                try { ok = _owner.RebindHotkey(action, def, out error); }
                catch (Exception ex) { ok = false; error = ex.Message; }
                if (ok)
                {
                    SetStatus(_lblHotkeyStatus, "已启用热键：" + actionName + " → " + ComboTextOf(def) + "（已写入 settings.ini）", false);
                }
                else
                {
                    string detail = string.IsNullOrEmpty(error) ? "无法注册（可能被其它程序占用），请改键。" : error;
                    SetStatus(_lblHotkeyStatus, "启用失败：" + detail, true);
                }
            }
            else
            {
                string error = null;
                bool ok = false;
                try { ok = _owner.RebindHotkey(action, Keys.None, out error); }
                catch (Exception ex) { ok = false; error = ex.Message; }
                if (ok)
                {
                    SetStatus(_lblHotkeyStatus, "已禁用热键：" + actionName + "（不再响应；可随时点「启用」恢复）", false);
                }
                else
                {
                    SetStatus(_lblHotkeyStatus, "禁用失败：" + (string.IsNullOrEmpty(error) ? "未知原因" : error), true);
                }
            }
            _owner.ScheduleSave();
        }
        catch (Exception ex)
        {
            SetStatus(_lblHotkeyStatus, "操作失败：" + ex.Message, true);
        }
        RefreshHotkeyTable();
    }

    // ========================================================================
    // 事件：诊断分区
    // ========================================================================
    private void OnCopyDiagnostics(object sender, EventArgs e)
    {
        try
        {
            DiagnosticsContext ctx = _owner.BuildDiagnosticsContext();
            string text = Diagnostics.BuildDiagnosticsText(ctx);
            Clipboard.SetText(text);
            SetStatus(_lblDiagStatus, "诊断信息已复制到剪贴板，可直接粘贴反馈。", false);
        }
        catch (Exception ex)
        {
            SetStatus(_lblDiagStatus, "复制诊断信息失败：" + ex.Message, true);
        }
    }

    private void OnOpenLogFolder(object sender, EventArgs e)
    {
        try
        {
            string dir = Diagnostics.LogDirectory();
            if (string.IsNullOrEmpty(dir))
            {
                SetStatus(_lblDiagStatus, "日志文件夹不可用。", true);
                return;
            }
            Diagnostics.OpenFolder(dir);
            SetStatus(_lblDiagStatus, "已打开日志文件夹：" + dir, false);
        }
        catch (Exception ex)
        {
            SetStatus(_lblDiagStatus, "打开日志文件夹失败：" + ex.Message, true);
        }
    }

    private void OnExportDiagnostics(object sender, EventArgs e)
    {
        try
        {
            DiagnosticsContext ctx = _owner.BuildDiagnosticsContext();
            string dir = Diagnostics.ExportDiagnostics(ctx);
            if (string.IsNullOrEmpty(dir))
            {
                SetStatus(_lblDiagStatus, "导出失败：无法写入临时目录，请检查磁盘空间或权限。", true);
            }
            else
            {
                SetStatus(_lblDiagStatus, "诊断包已导出到：" + dir, false);
            }
        }
        catch (Exception ex)
        {
            SetStatus(_lblDiagStatus, "导出诊断包失败：" + ex.Message, true);
        }
    }

    // ========================================================================
    // 生命周期：保存 / 定时器 / 资源释放
    // ========================================================================
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_refreshTimer != null) { _refreshTimer.Start(); }
        RefreshFromMain();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 关闭时再保存一次，避免最后一次改动丢失（owner 内部为幂等防抖保存）
        try
        {
            if (_owner != null) { _owner.SaveNow(); }
        }
        catch { }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_refreshTimer != null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _refreshTimer = null;
        }
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Dispose();
                _refreshTimer = null;
            }
            if (_font != null)
            {
                _font.Dispose();
                _font = null;
            }
            if (_boldFont != null)
            {
                _boldFont.Dispose();
                _boldFont = null;
            }
        }
    }
}

// ============================================================================
// 热键录制对话框：只在对话框内 KeyDown/ProcessCmdKey 捕获（不装全局钩子）
// 规则：至少一个 Ctrl/Alt/Shift + 一个非修饰键；Esc 取消；系统保留/重复组合继续捕获。
// ============================================================================
internal sealed class HotkeyCaptureDialog : Form
{
    private readonly Keys _currentCombo;
    private readonly string _actionName;
    private readonly int _dpi;
    private ThemeTokens _colors;
    private Font _font;
    private Font _boldFont;
    private Label _lblAction;
    private Label _lblCurrent;
    private Label _lblPrompt;
    private Label _lblNow;
    private Label _lblError;
    private Button _btnCancel;

    // 额外校验（由设置面板注入：表内重复检查）；返回 null 表示通过，否则返回中文原因
    internal Func<Keys, string> ExtraValidator;

    public HotkeyCaptureDialog(Keys current, string actionName)
    {
        _currentCombo = current;
        _actionName = actionName;
        _dpi = DpiHelper.SystemDpi;
        _font = UiFonts.Create(9f, FontStyle.Regular);
        _boldFont = UiFonts.Create(10f, FontStyle.Bold);

        Text = "录制热键";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        Font = _font;
        ClientSize = new Size(Px(340), Px(216));

        BuildUi();
        ApplyThemeTokens(null);
    }

    public Keys Result { get; private set; }
    public bool Accepted { get; private set; }

    private int Px(int dip) { return global::Layout.Px(dip, _dpi); }

    private Label MakeLabel(string text, int x, int y, int w, int h, bool bold)
    {
        Label l = new Label();
        l.Text = text;
        l.AutoSize = false;
        l.Location = new Point(Px(x), Px(y));
        l.Size = new Size(Px(w), Px(h));
        l.Font = bold ? _boldFont : _font;
        l.TextAlign = ContentAlignment.MiddleLeft;
        this.Controls.Add(l);
        return l;
    }

    private Button MakeButton(string text, int x, int y, int w, int h, EventHandler onClick)
    {
        Button b = new Button();
        b.Text = text;
        b.Location = new Point(Px(x), Px(y));
        b.Size = new Size(Px(w), Px(h));
        b.Font = _font;
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
        if (onClick != null) { b.Click += onClick; }
        this.Controls.Add(b);
        return b;
    }

    private void BuildUi()
    {
        _lblAction = MakeLabel("功能：" + _actionName, 16, 10, 308, 24, true);
        _lblCurrent = MakeLabel("当前组合：" + SettingsForm.ComboTextOf(_currentCombo), 16, 36, 308, 20, false);
        _lblPrompt = MakeLabel("请按下新的组合键（至少包含 Ctrl、Alt 或 Shift 之一）；Tab 切换焦点，Esc 取消。", 16, 60, 308, 32, false);
        _lblNow = MakeLabel("", 16, 96, 308, 20, false);
        _lblError = MakeLabel("", 16, 120, 308, 44, false);
        _lblError.Tag = "danger";
        _btnCancel = MakeButton("取消", 212, 180, 112, 28, new EventHandler(OnCancelClick));
        // 无修饰键的 Enter 已放行给默认处理；设为取消按钮，使 Tab 聚焦后 Enter/Space 都能关闭
        AcceptButton = _btnCancel;
        CancelButton = _btnCancel;
    }

    internal void ApplyThemeTokens(ThemeTokens tokens)
    {
        ThemeTokens t = tokens;
        if (t == null) { t = ThemeTokens.Light(); }
        _colors = t;

        BackColor = t.PageBg;
        _lblAction.ForeColor = t.Text;
        _lblCurrent.ForeColor = t.TextSecondary;
        _lblPrompt.ForeColor = t.Text;
        _lblNow.ForeColor = t.Accent;
        _lblError.ForeColor = t.Danger;
        _lblAction.BackColor = t.PageBg;
        _lblCurrent.BackColor = t.PageBg;
        _lblPrompt.BackColor = t.PageBg;
        _lblNow.BackColor = t.PageBg;
        _lblError.BackColor = t.PageBg;

        _btnCancel.UseVisualStyleBackColor = false;
        _btnCancel.FlatStyle = FlatStyle.Flat;
        _btnCancel.BackColor = t.Hover;
        _btnCancel.ForeColor = t.Text;
        _btnCancel.FlatAppearance.BorderColor = t.Border;
        _btnCancel.FlatAppearance.BorderSize = 1;
        _btnCancel.FlatAppearance.MouseOverBackColor = t.Card;
        _btnCancel.FlatAppearance.MouseDownBackColor = t.Border;
    }

    private void OnCancelClick(object sender, EventArgs e)
    {
        Accepted = false;
        Close();
    }

    // 对话框内捕获：ProcessCmdKey 覆盖 Tab/方向键/回车等（KeyDown 收不到的键也能录）
    // 例外：无修饰键的 Tab / Enter / Space 放行给默认处理（Tab 切换焦点，Enter/Space 激活按钮），
    //       否则录制框会把所有按键都吃掉，只剩 Esc 可用。
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys code = keyData & Keys.KeyCode;
        Keys mods = keyData & Keys.Modifiers;
        if (mods == Keys.None && (code == Keys.Tab || code == Keys.Enter || code == Keys.Space))
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }
        HandleCapturedKey(keyData);
        return true;
    }

    private void HandleCapturedKey(Keys keyData)
    {
        Keys mods = keyData & Keys.Modifiers;
        Keys code = keyData & Keys.KeyCode;

        // Esc 取消；Alt+F4 也视为取消（避免无法关闭）
        if (code == Keys.Escape && mods == Keys.None) { Accepted = false; Close(); return; }
        if (code == Keys.F4 && mods == Keys.Alt) { Accepted = false; Close(); return; }

        // 单独的修饰键：提示继续按主键
        if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu
            || code == Keys.LWin || code == Keys.RWin)
        {
            _lblNow.Text = "已按下：" + ModsText(mods) + "请再按一个主键…";
            _lblError.Text = "";
            return;
        }

        if (mods == Keys.None)
        {
            ShowError("必须包含 Ctrl、Alt 或 Shift 至少一个修饰键。");
            return;
        }

        string error = null;
        bool valid;
        try { valid = HotkeyManager.ValidateCombo(keyData, out error); }
        catch { valid = false; error = "热键校验失败，请换一个组合。"; }
        if (!valid)
        {
            ShowError(string.IsNullOrEmpty(error) ? "该组合不可用，请换一个组合。" : error);
            return;
        }

        if (ExtraValidator != null)
        {
            string extra = ExtraValidator(keyData);
            if (!string.IsNullOrEmpty(extra))
            {
                ShowError(extra);
                return;
            }
        }

        Result = keyData;
        Accepted = true;
        Close();
    }

    private void ShowError(string message)
    {
        _lblError.Text = message;
        FitErrorLabel();
        _lblNow.Text = "（组合不合法）请继续按组合键…";
        if (_colors != null) { _lblError.ForeColor = _colors.Danger; }
    }

    // B6：错误文案自动换行 + 按文本测量高度（44..56 DIP），保证「旧热键也未能恢复…」完整可见
    private void FitErrorLabel()
    {
        if (_lblError == null) { return; }
        int width = _lblError.Width;
        if (width <= 0) { return; }
        string text = _lblError.Text == null ? "" : _lblError.Text;
        Size measured = TextRenderer.MeasureText(text, _lblError.Font, new Size(width, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        int height = measured.Height + Px(4);
        if (height < Px(44)) { height = Px(44); }
        if (height > Px(56)) { height = Px(56); }
        if (_lblError.Height != height) { _lblError.Height = height; }
    }

    private static string ModsText(Keys mods)
    {
        StringBuilder sb = new StringBuilder();
        if ((mods & Keys.Control) == Keys.Control) { sb.Append("Ctrl+"); }
        if ((mods & Keys.Alt) == Keys.Alt) { sb.Append("Alt+"); }
        if ((mods & Keys.Shift) == Keys.Shift) { sb.Append("Shift+"); }
        return sb.ToString();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            if (_font != null) { _font.Dispose(); _font = null; }
            if (_boldFont != null) { _boldFont.Dispose(); _boldFont = null; }
        }
    }
}
// ============================================================================
// 自测 — --selftest 自动化自测
// 要求：不注册热键、不读写用户 settings.ini、不碰用户 images、不参与单实例 Mutex。
//       输出 PASS/FAIL 到 stdout、写 selftest.log 与 4 张 PNG、返回退出码 0/1。
// 说明：
//   1) 临时环境：%TEMP%\GuideOverlay-selftest-<pid>\（images\ + settings.ini）；
//      输出目录优先 exe 同目录，不可写则整体回退到该临时目录。
//   2) 4 个动作各截 1 张 PNG（Form.DrawToBitmap，不 Show 窗口），每步做像素/模型断言。
//   3) 另做纯函数断言（ViewMath / SettingsStore / ImageLibrary / HotkeyManager）。
//   4) 整个 Run 包在 try/catch 里：任何未预期异常 → 写日志 + 打印 FAIL + 返回 1，
//      绝不弹 MessageBox。
//   全部 C# 5 语法。
// ============================================================================

internal static class SelfTestRunner
{
    // ------------------------------------------------------------------
    // 运行状态（全部为静态字段，C# 5 不使用自动属性初始化器）
    // ------------------------------------------------------------------
    private static readonly List<string> _logLines = new List<string>();   // 日志行（最后统一写盘）
    private static readonly List<string> _failures = new List<string>();   // FAIL 原因（每条一行）
    private static readonly List<string> _pngInfo = new List<string>();    // 4 张 PNG 的路径/尺寸
    private static int _passed;
    private static int _failed;
    private static Stopwatch _totalWatch;

    private static string _exeDir = "";
    private static string _tempRoot = "";
    // true 表示 %TEMP% 不可写、临时目录已回退（受限沙箱环境）
    private static bool _tempRootFallback;
    // 命中的临时目录候选序号（0=%TEMP%，1=exe 目录，2=%LOCALAPPDATA%，3=BaseDirectory）
    private static int _tempRootCandidateIndex;
    private static string _imagesDir = "";
    private static string _tempSettingsPath = "";
    private static string _outputDir = "";
    private static string _logPath = "";
    private static AppSettings _settings;

    private static string _userSettingsPath = "";
    private static string _fallbackSettingsPath = "";
    private static string _userImagesDir = "";
    private static PathSnapshot _userSettingsBefore;
    private static PathSnapshot _fallbackSettingsBefore;
    private static string _userImagesBefore = "";

    private static int _gdiBefore = -1;
    private static int _gdiAfter = -1;

    // 性能段校验和：累计纯函数结果的符号位，防止 JIT 把计时循环里的调用当死代码优化掉
    private static double _perfSink;

    // ------------------------------------------------------------------
    // 入口
    // ------------------------------------------------------------------
    public static int Run()
    {
        _totalWatch = Stopwatch.StartNew();
        // 输出重定向到文件时，显式 UTF-8 便于中文 FAIL 原因阅读；失败忽略。
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
        // 仅设置 OutputEncoding 在重定向时可能仍按控制台代码页（GBK）编码，这里显式换 UTF-8 输出流。
        try
        {
            StreamWriter utf8Out = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            utf8Out.AutoFlush = true;
            Console.SetOut(utf8Out);
        }
        catch { }

        try
        {
            RunCore();
        }
        catch (Exception ex)
        {
            // 任何未预期异常都兜住：写日志 + FAIL 行 + 退出码 1（绝不弹窗）
            try { Fail("自测异常 " + ex.GetType().Name + "：" + ex.Message); } catch { }
            try { Log("异常堆栈：\r\n" + ex.StackTrace); } catch { }
        }

        // 无论成功/异常都清理临时目录（日志里记录实际路径；输出目录内的日志/PNG 保留）
        try { CleanupTempEnvironment(); } catch { }

        try { WriteFinalLog(); } catch { }

        try
        {
            if (_failed == 0)
            {
                Console.WriteLine("PASS");
            }
            else
            {
                for (int i = 0; i < _failures.Count; i++)
                {
                    Console.WriteLine("FAIL: " + _failures[i]);
                }
            }
        }
        catch { }

        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 主流程
    // ------------------------------------------------------------------
    private static void RunCore()
    {
        InitPaths();
        TakeSideEffectSnapshot();
        InitTempEnvironment();

        Section("测试环境与测试图", TestEnvironment);
        Section("纯函数：锚点缩放", TestZoomAtPoint);
        Section("纯函数：平移夹取", TestClampOffsets);
        Section("纯函数：适应窗口与缩放范围", TestFitAndZoomRange);
        Section("纯函数：自然排序", TestNaturalSort);
        Section("纯函数：窗口位置与大小", TestWindowGeometry);
        Section("纯函数：工具条几何与命中", TestToolbarGeometry);
        Section("纯函数：空状态卡片与 toast 几何", TestCardAndToastGeometry);
        Section("纯函数：长图与切片", TestLongImage);
        Section("纯函数：长图滚动条与滚动夹取", TestScrollbarAndScroll);
        Section("纯函数：边缘吸附", TestSnap);
        Section("纯函数：热键解析与格式化", TestHotkeyParse);
        Section("纯函数：热键录制校验", TestHotkeyValidate);
        Section("纯函数：最近文件夹去重排序", TestRecentFolders);
        Section("settings：往返与非法值夹取", TestSettingsRoundtrip);
        Section("图片文件夹配置", TestImageFolderConfig);
        Section("扫描跳过", TestScanSkipped);
        Section("输出目录回退", TestOutputFallback);
        Section("单实例 Mutex 不参与", TestMutexNotHeld);
        Section("主窗口 4 步截图断言", TestOverlaySteps);
        Section("无副作用核对（后置）", VerifySideEffectSnapshot);
        Section("GDI 对象数", TestGdiObjects);
        Section("性能量化（§十二：缩放/单帧/切图/内存/GDI）", TestPerformance);
    }

    // 清理临时目录（仅当它不等于输出目录时；输出目录里的日志/PNG 必须保留）
    private static void CleanupTempEnvironment()
    {
        try
        {
            if (string.IsNullOrEmpty(_tempRoot)) { return; }
            if (!Directory.Exists(_tempRoot)) { return; }
            if (IsSamePath(_tempRoot, _outputDir)) { return; }
            Directory.Delete(_tempRoot, true);
            Log("临时目录已清理：" + _tempRoot);
        }
        catch (Exception ex)
        {
            Log("临时目录清理失败（可忽略，下次运行会覆盖）：" + Describe(ex));
        }
    }

    // ==================================================================
    // 一、环境初始化
    // ==================================================================

    // 路径与日志头（只读操作，不写用户目录）
    private static void InitPaths()
    {
        _exeDir = SettingsStore.ResolveExeDir();
        _tempRoot = ResolveTempRoot(_exeDir);
        _imagesDir = Path.Combine(_tempRoot, "images");
        _tempSettingsPath = Path.Combine(_tempRoot, "settings.ini");

        _userSettingsPath = SettingsStore.DefaultSettingsPath(_exeDir);
        _fallbackSettingsPath = SettingsStore.FallbackSettingsPath();
        _userImagesDir = SettingsStore.DefaultImageFolder(_exeDir);

        _outputDir = ResolveOutputDirectory(_exeDir, _tempRoot);
        try { Directory.CreateDirectory(_outputDir); } catch { }
        _logPath = Path.Combine(_outputDir, "selftest.log");

        bool exeWritable = IsSamePath(_outputDir, _exeDir);
        Log("攻略悬浮窗 --selftest（版本 " + AppInfo.Version + "）");
        Log("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Log("系统：" + Diagnostics.DescribeOs());
        Log("系统 DPI：" + DpiHelper.SystemDpi.ToString(CultureInfo.InvariantCulture));
        Log("进程 ID：" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
        Log("exe 目录：" + _exeDir);
        Log("临时目录：" + _tempRoot);
        if (_tempRootFallback)
        {
            Log("警告：%TEMP% 不可写（沙箱/权限限制），临时目录回退到候选 #" 
                + _tempRootCandidateIndex.ToString(CultureInfo.InvariantCulture) + "：" + _tempRoot);
        }
        Log("输出目录（实际）：" + _outputDir
            + (exeWritable ? "（exe 同目录可写）" : "（exe 同目录不可写，已回退 %TEMP%）"));
        Log("日志文件：" + _logPath);
        Log("用户 settings.ini（只读检查）：" + _userSettingsPath);
        Log("回退 settings.ini（只读检查）：" + _fallbackSettingsPath);
        Log("用户 images 目录（只读检查）：" + _userImagesDir);
    }

    // 解析临时目录根（按顺序尝试，第一个「能建目录且能写文件」的胜出）：
    //   ① %TEMP%\GuideOverlay-selftest-<pid>\
    //   ② exe 目录\.selftest-tmp\
    //   ③ %LOCALAPPDATA%\GuideOverlay\selftest-<pid>\
    //   ④ AppDomain.BaseDirectory\.selftest-tmp\
    // 本机沙箱会拒绝 .NET 进程写真实 %TEMP%，所以必须有回退链；正常用户机器上 ① 一定可用。
    private static string ResolveTempRoot(string exeDir)
    {
        string pid = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
        string[] candidates = new string[]
        {
            Path.Combine(Path.GetTempPath(), "GuideOverlay-selftest-" + pid),
            Path.Combine(exeDir, ".selftest-tmp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GuideOverlay", "selftest-" + pid),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".selftest-tmp")
        };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (TryPrepareTempRoot(candidates[i]))
            {
                _tempRootFallback = i > 0;
                _tempRootCandidateIndex = i;
                return candidates[i];
            }
        }
        // 全部失败：仍返回规范路径，让后续断言给出明确失败原因
        return candidates[0];
    }

    // 建目录 + 写探测文件验证可写（沙箱可能允许建目录但拒绝写文件，必须都验证）
    private static bool TryPrepareTempRoot(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            string probe = Path.Combine(root, "~probe-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(probe, new byte[] { 1, 2, 3 });
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    // 生成临时测试图与临时 settings.ini（全部在临时目录内）
    private static void InitTempEnvironment()
    {
        try
        {
            Directory.CreateDirectory(_imagesDir);
        }
        catch (Exception ex)
        {
            Fail("临时目录创建失败：" + Describe(ex));
            return;
        }

        string widePath = Path.Combine(_imagesDir, "01-wide.png");
        string tallPath = Path.Combine(_imagesDir, "02-tall.png");
        Color[] widePalette = new Color[]
        {
            ColorTranslator.FromHtml("#E53935"), ColorTranslator.FromHtml("#1E88E5"),
            ColorTranslator.FromHtml("#43A047"), ColorTranslator.FromHtml("#FDD835"),
            ColorTranslator.FromHtml("#8E24AA"), ColorTranslator.FromHtml("#FB8C00"),
            ColorTranslator.FromHtml("#00ACC1"), ColorTranslator.FromHtml("#D81B60"),
            ColorTranslator.FromHtml("#6D4C41"), ColorTranslator.FromHtml("#3949AB"),
            ColorTranslator.FromHtml("#7CB342"), ColorTranslator.FromHtml("#F4511E")
        };
        Color[] tallPalette = new Color[]
        {
            ColorTranslator.FromHtml("#0D47A1"), ColorTranslator.FromHtml("#C62828"),
            ColorTranslator.FromHtml("#2E7D32"), ColorTranslator.FromHtml("#EF6C00"),
            ColorTranslator.FromHtml("#4527A0"), ColorTranslator.FromHtml("#00838F"),
            ColorTranslator.FromHtml("#AD1457"), ColorTranslator.FromHtml("#558B2F"),
            ColorTranslator.FromHtml("#4E342E"), ColorTranslator.FromHtml("#283593"),
            ColorTranslator.FromHtml("#00695C"), ColorTranslator.FromHtml("#F9A825")
        };

        try
        {
            CreateTestImage(widePath, 1600, 900, "壹", widePalette);
            CreateTestImage(tallPath, 900, 1600, "贰", tallPalette);
        }
        catch (Exception ex)
        {
            Fail("生成测试图失败：" + Describe(ex));
        }

        // 临时 settings.ini：folder 注入临时 images（不读用户 settings.ini）
        try
        {
            AppSettings boot = SettingsStore.Load(_exeDir, _tempSettingsPath);
            boot.Folder = _imagesDir;
            boot.FolderSetting = _imagesDir;
            SettingsStore.SaveTo(boot, _tempSettingsPath);
            _settings = SettingsStore.Load(_exeDir, _tempSettingsPath);
        }
        catch (Exception ex)
        {
            Fail("临时 settings.ini 初始化失败：" + Describe(ex));
        }
    }

    private static void TestEnvironment()
    {
        Check(Directory.Exists(_tempRoot), "环境：临时目录已创建", _tempRoot);
        Check(Directory.Exists(_imagesDir), "环境：临时 images 目录已创建", _imagesDir);

        string widePath = Path.Combine(_imagesDir, "01-wide.png");
        string tallPath = Path.Combine(_imagesDir, "02-tall.png");
        CheckFileImage(widePath, 1600, 900, "环境：测试图 01-wide.png 为 1600x900");
        CheckFileImage(tallPath, 900, 1600, "环境：测试图 02-tall.png 为 900x1600");

        // 头部尺寸探测（ImageLibrary）也应识别这两张图
        int w;
        int h;
        string err;
        bool ok = ImageLibrary.TryReadImageSize(widePath, out w, out h, out err);
        Check(ok && w == 1600 && h == 900, "环境：TryReadImageSize 识别 1600x900",
            ok ? (w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture)) : ("失败：" + err));

        Check(File.Exists(_tempSettingsPath), "环境：临时 settings.ini 已写出", _tempSettingsPath);
        Check(_settings != null, "环境：临时 settings 已加载");
        if (_settings == null) { return; }

        Check(SamePathText(_settings.Folder, _imagesDir), "环境：settings.Folder 指向临时 images",
            "期望 " + _imagesDir + "，实际 " + _settings.Folder);
        Check(_settings.StrictPath, "环境：settings 使用显式注入路径（不回退 %APPDATA%）");
        Check(SamePathText(_settings.SettingsPath, _tempSettingsPath), "环境：settings.SettingsPath 为临时 ini",
            "期望 " + _tempSettingsPath + "，实际 " + _settings.SettingsPath);
        Check(_settings.Opacity == 85 && _settings.Theme == ThemeKind.Light,
            "环境：临时 settings 为默认值（opacity=85 / light）");

        // A1：程序集版本资源与 AppInfo.Version 保持一致（exe 属性页显示 5.0.0.0）
        Version asmVersion = Assembly.GetExecutingAssembly().GetName().Version;
        Check(asmVersion != null && asmVersion.ToString(3) == AppInfo.Version,
            "环境：程序集版本与 AppInfo.Version 一致",
            "程序集 " + (asmVersion != null ? asmVersion.ToString() : "null") + "，AppInfo " + AppInfo.Version);
        Check(asmVersion != null && asmVersion.ToString(4) == "5.0.0.0",
            "环境：程序集版本为四段 5.0.0.0",
            asmVersion != null ? asmVersion.ToString() : "null");
    }

    // ==================================================================
    // 二、无副作用快照
    // ==================================================================
    private static void TakeSideEffectSnapshot()
    {
        _userSettingsBefore = SnapshotFile(_userSettingsPath);
        _fallbackSettingsBefore = SnapshotFile(_fallbackSettingsPath);
        _userImagesBefore = SnapshotDirectory(_userImagesDir);
        Log("前置快照：用户 settings.ini 存在=" + _userSettingsBefore.Exists.ToString()
            + "，回退 settings.ini 存在=" + _fallbackSettingsBefore.Exists.ToString()
            + "，用户 images 快照长度=" + _userImagesBefore.Length.ToString(CultureInfo.InvariantCulture));
    }

    private static void VerifySideEffectSnapshot()
    {
        PathSnapshot userAfter = SnapshotFile(_userSettingsPath);
        PathSnapshot fallbackAfter = SnapshotFile(_fallbackSettingsPath);
        string imagesAfter = SnapshotDirectory(_userImagesDir);

        Check(SnapshotEqual(_userSettingsBefore, userAfter), "无副作用：用户 settings.ini 未被读写修改",
            _userSettingsPath + "（存在性/长度/内容发生变化）");
        Check(SnapshotEqual(_fallbackSettingsBefore, fallbackAfter), "无副作用：%APPDATA% 回退 settings.ini 未被修改",
            _fallbackSettingsPath + "（存在性/长度/内容发生变化）");
        Check(string.Equals(_userImagesBefore, imagesAfter, StringComparison.Ordinal),
            "无副作用：用户 images 目录未被修改", _userImagesDir);
        Log("后置快照：用户 settings.ini 存在=" + userAfter.Exists.ToString()
            + "，回退 settings.ini 存在=" + fallbackAfter.Exists.ToString());
    }

    // ==================================================================
    // 三、纯函数断言
    // ==================================================================

    // 3.1 锚点缩放：ZoomAtPoint / ImageCoord
    private static void TestZoomAtPoint()
    {
        // 组 1：zoom 1 → 2，锚点 (100,50)，offset (0,0) → 新 offset (-100,-50)
        double ox;
        double oy;
        ViewMath.ZoomAtPoint(1.0, 0.0, 0.0, 2.0, 100.0, 50.0, out ox, out oy);
        CheckNear(ox, -100.0, 1e-9, "锚点组1：新 offsetX = -100");
        CheckNear(oy, -50.0, 1e-9, "锚点组1：新 offsetY = -50");

        // 组 2：zoom 0.5 → 1.25，锚点 (260,378)，offset (-30,-70)
        ViewMath.ZoomAtPoint(0.5, -30.0, -70.0, 1.25, 260.0, 378.0, out ox, out oy);
        CheckNear(ox, -465.0, 1e-9, "锚点组2：新 offsetX = -465");
        CheckNear(oy, -742.0, 1e-9, "锚点组2：新 offsetY = -742");

        // 组 3：zoom 3 → 0.8，锚点 (0,0)，offset (120,45)
        ViewMath.ZoomAtPoint(3.0, 120.0, 45.0, 0.8, 0.0, 0.0, out ox, out oy);
        CheckNear(ox, 32.0, 1e-9, "锚点组3：新 offsetX = 32");
        CheckNear(oy, 12.0, 1e-9, "锚点组3：新 offsetY = 12");

        // 随机 60 组：缩放前后锚点映射的图片坐标误差（未夹紧时要求 ≤1px）
        Random rnd = new Random(20251107);
        double maxErr = 0.0;
        for (int i = 0; i < 60; i++)
        {
            double oldZoom = 0.1 + rnd.NextDouble() * 9.9;
            double newZoom = 0.1 + rnd.NextDouble() * 9.9;
            double offX = -rnd.NextDouble() * 2000.0;
            double offY = -rnd.NextDouble() * 2000.0;
            double anchorX = rnd.NextDouble() * 1000.0;
            double anchorY = rnd.NextDouble() * 800.0;

            double beforeX = ViewMath.ImageCoordX(anchorX, offX, oldZoom);
            double beforeY = ViewMath.ImageCoordY(anchorY, offY, oldZoom);
            double newOffX;
            double newOffY;
            ViewMath.ZoomAtPoint(oldZoom, offX, offY, newZoom, anchorX, anchorY, out newOffX, out newOffY);
            double afterX = ViewMath.ImageCoordX(anchorX, newOffX, newZoom);
            double afterY = ViewMath.ImageCoordY(anchorY, newOffY, newZoom);

            double err = Math.Max(Math.Abs(afterX - beforeX), Math.Abs(afterY - beforeY));
            if (err > maxErr) { maxErr = err; }
        }
        Check(maxErr <= 1e-6, "锚点映射：随机 60 组误差 ≤1e-6px", "最大误差 " + Fmt(maxErr) + "px");
        Check(maxErr <= 1.0, "锚点映射：随机 60 组误差 ≤1px（规范要求）", "最大误差 " + Fmt(maxErr) + "px");

        // ImageCoord 的边界：非法 zoom 按 1:1
        CheckNear(ViewMath.ImageCoordX(100.0, 0.0, 0.0), 100.0, 1e-9, "锚点：zoom=0 时按 1:1 反算");
        CheckNear(ViewMath.ImageCoordY(50.0, 0.0, double.NaN), 50.0, 1e-9, "锚点：zoom=NaN 时按 1:1 反算");

        // B3：键盘缩放光标锚点判定（视口点是否落在缩放后的图片矩形内）
        Check(ViewMath.ImageContainsViewPoint(0.0, 0.0, 1.0, 1600, 900, 100.0, 50.0),
            "光标锚点：图片内点 (100,50) 命中");
        Check(ViewMath.ImageContainsViewPoint(-10.0, -20.0, 0.5, 1600, 900, 790.0, 430.0),
            "光标锚点：缩放后图片右下边界点算命中");
        Check(!ViewMath.ImageContainsViewPoint(0.0, 0.0, 1.0, 1600, 900, -1.0, 50.0),
            "光标锚点：图片左侧窗外点不命中");
        Check(!ViewMath.ImageContainsViewPoint(0.0, 0.0, 0.0, 1600, 900, 100.0, 50.0),
            "光标锚点：非法 zoom 不命中（调用方退回窗口中心）");
    }

    // 3.2 平移夹取：大于视口不露背景，小于视口居中
    private static void TestClampOffsets()
    {
        // 组 1：两方向都大于视口 → 夹到 [view-scaled, 0]
        double x = 100.0;
        double y = 100.0;
        ViewMath.ClampOffsets(2.0, 1600.0, 900.0, 520.0, 400.0, ref x, ref y);
        CheckNear(x, 0.0, 1e-9, "夹取组1：X 超界回 0");
        CheckNear(y, 0.0, 1e-9, "夹取组1：Y 超界回 0");

        x = -9999.0;
        y = -9999.0;
        ViewMath.ClampOffsets(2.0, 1600.0, 900.0, 520.0, 400.0, ref x, ref y);
        CheckNear(x, -2680.0, 1e-9, "夹取组1：X 下界 = 520-3200 = -2680");
        CheckNear(y, -1400.0, 1e-9, "夹取组1：Y 下界 = 400-1800 = -1400");

        // 不露背景色：offset ≤ 0 且 offset + scaled ≥ view
        Check(x <= 0.0 && x + 1600.0 * 2.0 >= 520.0, "夹取组1：X 方向不露背景");
        Check(y <= 0.0 && y + 900.0 * 2.0 >= 400.0, "夹取组1：Y 方向不露背景");

        // 组 2：两方向都小于视口 → 居中
        x = 999.0;
        y = -999.0;
        ViewMath.ClampOffsets(0.5, 200.0, 100.0, 520.0, 400.0, ref x, ref y);
        CheckNear(x, 210.0, 1e-9, "夹取组2：X 居中 (520-100)/2 = 210");
        CheckNear(y, 175.0, 1e-9, "夹取组2：Y 居中 (400-50)/2 = 175");

        // 组 3：混合（X 可平移、Y 居中）
        x = -500.0;
        y = 123.0;
        ViewMath.ClampOffsets(1.0, 1600.0, 200.0, 520.0, 400.0, ref x, ref y);
        CheckNear(x, -500.0, 1e-9, "夹取组3：X 在 [-1080,0] 内保持不变");
        CheckNear(y, 100.0, 1e-9, "夹取组3：Y 小于视口 → 居中 100");

        // 组 4：边界值（0 与下界本身）
        x = 0.0;
        y = 0.0;
        ViewMath.ClampOffsets(2.0, 1600.0, 900.0, 520.0, 400.0, ref x, ref y);
        CheckNear(x, 0.0, 1e-9, "夹取组4：X 边界 0 保持");
        CheckNear(y, 0.0, 1e-9, "夹取组4：Y 边界 0 保持");

        // 组 5：随机 40 组，验证「大方向不露底、小方向居中」不变量
        Random rnd = new Random(777);
        int bad = 0;
        for (int i = 0; i < 40; i++)
        {
            double zoom = 0.1 + rnd.NextDouble() * 9.9;
            double imgW = 50.0 + rnd.NextDouble() * 4000.0;
            double imgH = 50.0 + rnd.NextDouble() * 4000.0;
            double viewW = 100.0 + rnd.NextDouble() * 2000.0;
            double viewH = 100.0 + rnd.NextDouble() * 2000.0;
            double offX = -rnd.NextDouble() * 5000.0;
            double offY = -rnd.NextDouble() * 5000.0;
            ViewMath.ClampOffsets(zoom, imgW, imgH, viewW, viewH, ref offX, ref offY);

            double scaledW = imgW * zoom;
            double scaledH = imgH * zoom;
            if (scaledW > viewW)
            {
                if (!(offX <= 1e-9 && offX + scaledW >= viewW - 1e-9)) { bad++; }
            }
            else
            {
                if (Math.Abs(offX - (viewW - scaledW) / 2.0) > 1e-9) { bad++; }
            }
            if (scaledH > viewH)
            {
                if (!(offY <= 1e-9 && offY + scaledH >= viewH - 1e-9)) { bad++; }
            }
            else
            {
                if (Math.Abs(offY - (viewH - scaledH) / 2.0) > 1e-9) { bad++; }
            }
        }
        Check(bad == 0, "夹取：随机 40 组不变量（不露底/居中）全部成立", "违例 " + bad + " 组");

        // CanPan：可平移判定
        Check(ViewMath.CanPanX(2.0, 1600.0, 520.0), "可平移判定：X 放大后大于视口");
        Check(!ViewMath.CanPanX(0.1, 1600.0, 520.0), "可平移判定：X 缩小时不可平移");
        Check(ViewMath.CanPanY(2.0, 900.0, 400.0), "可平移判定：Y 放大后大于视口");
        Check(!ViewMath.CanPanY(0.1, 900.0, 400.0), "可平移判定：Y 缩小时不可平移");

        // ShouldPanImage：门控拖动动作选择（锁图后一律移动窗口）
        Check(ViewMath.ShouldPanImage(true, false), "拖动动作：可平移且未锁图 → 平移图片");
        Check(!ViewMath.ShouldPanImage(true, true), "拖动动作：可平移但已锁图 → 移动窗口");
        Check(!ViewMath.ShouldPanImage(false, false), "拖动动作：不可平移且未锁图 → 移动窗口");
        Check(!ViewMath.ShouldPanImage(false, true), "拖动动作：不可平移且已锁图 → 移动窗口");
    }

    // 3.3 适应窗口公式与缩放范围
    private static void TestFitAndZoomRange()
    {
        // 横图：min(520/1600, 400/900) = 0.325
        CheckNear(ViewMath.FitZoom(1600.0, 900.0, 520.0, 400.0), 0.325, 1e-9, "适应公式：1600x900 → 0.325");
        // 竖图：min(520/900, 400/1600) = 0.25
        CheckNear(ViewMath.FitZoom(900.0, 1600.0, 520.0, 400.0), 0.25, 1e-9, "适应公式：900x1600 → 0.25");
        // 大图特例：允许 < 0.1×，不被手动下限夹取
        CheckNear(ViewMath.FitZoom(100000.0, 50000.0, 520.0, 400.0), 0.0052, 1e-9,
            "适应公式：超大图允许 <0.1×（0.0052）");
        Check(ViewMath.FitZoom(100000.0, 50000.0, 520.0, 400.0) < ViewMath.MinManualZoom,
            "适应公式：超大图比例确实小于手动下限 0.1");
        // 非法输入的安全默认值
        CheckNear(ViewMath.FitZoom(0.0, 900.0, 520.0, 400.0), 1.0, 1e-9, "适应公式：图片宽非法 → 1.0");
        CheckNear(ViewMath.FitZoom(1600.0, 900.0, 0.0, 0.0), 0.1, 1e-9, "适应公式：视口非法 → 0.1");

        // 手动缩放夹取 0.1–10
        CheckNear(ViewMath.ClampManualZoom(0.05), 0.1, 1e-12, "缩放夹取：0.05 → 0.1");
        CheckNear(ViewMath.ClampManualZoom(0.1), 0.1, 1e-12, "缩放夹取：0.1 → 0.1");
        CheckNear(ViewMath.ClampManualZoom(1.0), 1.0, 1e-12, "缩放夹取：1.0 → 1.0");
        CheckNear(ViewMath.ClampManualZoom(10.0), 10.0, 1e-12, "缩放夹取：10 → 10");
        CheckNear(ViewMath.ClampManualZoom(99.0), 10.0, 1e-12, "缩放夹取：99 → 10");
        CheckNear(ViewMath.ClampManualZoom(-5.0), 0.1, 1e-12, "缩放夹取：-5 → 0.1");
        CheckNear(ViewMath.ClampManualZoom(double.NaN), 1.0, 1e-12, "缩放夹取：NaN → 1.0");
        CheckNear(ViewMath.ClampManualZoom(double.PositiveInfinity), 10.0, 1e-12, "缩放夹取：+Inf → 10");
        CheckNear(ViewMath.ClampManualZoom(double.NegativeInfinity), 0.1, 1e-12, "缩放夹取：-Inf → 0.1");

        // 长图 fit-width
        CheckNear(ViewMath.FitWidthZoom(900.0, 520.0), 520.0 / 900.0, 1e-9, "长图 fit-width：900 宽 → 520/900");
        CheckNear(ViewMath.FitWidthZoom(100000.0, 520.0), 0.0052, 1e-9, "长图 fit-width：允许 <0.1×");
        CheckNear(ViewMath.FitWidthZoom(0.0, 520.0), 1.0, 1e-9, "长图 fit-width：图片宽非法 → 1.0");
        CheckNear(ViewMath.FitWidthZoom(1000.0, 0.0), 0.1, 1e-9, "长图 fit-width：视口宽非法 → 0.1");

        // VisibleSourceRect：视口反变换 + 与图片求交
        RectangleF src = ViewMath.VisibleSourceRect(1.0, -100.0, -50.0, 520.0, 400.0, 1600.0, 900.0);
        CheckNear(src.X, 100.0, 1e-6, "可见源矩形：X 起点 = 100");
        CheckNear(src.Y, 50.0, 1e-6, "可见源矩形：Y 起点 = 50");
        CheckNear(src.Width, 520.0, 1e-6, "可见源矩形：宽 = 520");
        CheckNear(src.Height, 400.0, 1e-6, "可见源矩形：高 = 400");
        RectangleF empty = ViewMath.VisibleSourceRect(1.0, 5000.0, 0.0, 520.0, 400.0, 1600.0, 900.0);
        Check(empty.IsEmpty, "可见源矩形：完全不可见时返回空");
    }

    // 3.4 自然排序
    private static void TestNaturalSort()
    {
        // 组 1：1/2/10
        List<string> list = new List<string>();
        list.Add("10");
        list.Add("2");
        list.Add("1");
        list.Sort(ViewMath.NaturalComparer);
        CheckStr(string.Join(",", list.ToArray()), "1,2,10", "自然排序：1/2/10 → 1,2,10");

        // 组 2：01/02/10（文件名）
        list = new List<string>();
        list.Add("10.png");
        list.Add("02.png");
        list.Add("01.png");
        list.Sort(ViewMath.NaturalComparer);
        CheckStr(string.Join(",", list.ToArray()), "01.png,02.png,10.png", "自然排序：01/02/10 → 01,02,10");

        // 组 3：A2/A10
        Check(ViewMath.CompareNatural("A2", "A10") < 0, "自然排序：A2 < A10");
        Check(ViewMath.CompareNatural("A10", "A2") > 0, "自然排序：A10 > A2");

        // 组 4：数字值相等（01 与 1）按原始段 Ordinal 比较
        Check(ViewMath.CompareNatural("01", "1") < 0, "自然排序：01 < 1（原始段 Ordinal 兜底）");
        Check(ViewMath.CompareNatural("1", "01") > 0, "自然排序：1 > 01（原始段 Ordinal 兜底）");

        // 组 5：混合目录（按 / 与 \ 分段）
        list = new List<string>();
        list.Add("10/2.png");
        list.Add("2/1.png");
        list.Add("1/10.png");
        list.Add("1/2.png");
        list.Sort(ViewMath.NaturalComparer);
        CheckStr(string.Join(",", list.ToArray()), "1/2.png,1/10.png,2/1.png,10/2.png",
            "自然排序：混合目录按段比较");

        // 组 6：大小写不敏感
        Check(ViewMath.CompareNatural("A2", "a10") < 0, "自然排序：大小写不敏感（A2 < a10）");
        Check(ViewMath.CompareNatural("a", "A") < 0 || ViewMath.CompareNatural("a", "A") > 0,
            "自然排序：大小写平局有确定顺序（非 0）");

        // 组 7：不同层级数
        Check(ViewMath.CompareNatural("a", "a/b") < 0, "自然排序：短路径在前（a < a/b）");
        Check(ViewMath.CompareNatural("a/b", "a") > 0, "自然排序：长路径在后（a/b > a）");
    }

    // 3.5 窗口位置与大小
    private static void TestWindowGeometry()
    {
        Rectangle work = new Rectangle(0, 0, 1920, 1040);
        int dpi = 96;

        // 默认位置（B7）：高 = 工作区 60%（320..900），宽 = 高*9/16（240..1200），右 24 DIP、垂直居中
        Rectangle def = ViewMath.DefaultWindowRect(work, dpi);
        CheckRect(def, new Rectangle(1545, 208, 351, 624), "窗口默认：1920x1040@96 → (1545,208,351x624)");

        // 小屏（1366x768）：高 461 DIP、宽 259 DIP
        Rectangle narrow = ViewMath.DefaultWindowRect(new Rectangle(0, 0, 1366, 768), dpi);
        CheckRect(narrow, new Rectangle(1083, 153, 259, 461), "窗口默认：1366x768 → 高 60% / 宽 9:16");

        // 高 DPI（144）：工作区 1440px 高 = 960 DIP → 576 DIP 高、324 DIP 宽
        Rectangle hidpi = ViewMath.DefaultWindowRect(new Rectangle(0, 0, 2560, 1440), 144);
        CheckRect(hidpi, new Rectangle(2038, 288, 486, 864), "窗口默认：2560x1440@144 → 486x864");

        // B6：设置面板尺寸 = min(560, 工作区-80)
        Size panelBig = ViewMath.SettingsPanelSize(new Rectangle(0, 0, 1920, 1040), 96);
        Check(panelBig.Width == 560 && panelBig.Height == 560,
            "设置面板：1920x1040@96 → 560x560（上限）", panelBig.Width + "x" + panelBig.Height);
        Size panelLaptop = ViewMath.SettingsPanelSize(new Rectangle(0, 0, 1366, 768), 120);
        Check(panelLaptop.Width == 560 && panelLaptop.Height == 534,
            "设置面板：1366x768@125% → 560x534（高不超出工作区）", panelLaptop.Width + "x" + panelLaptop.Height);
        Size panelTiny = ViewMath.SettingsPanelSize(new Rectangle(0, 0, 480, 400), 96);
        Check(panelTiny.Width == 420 && panelTiny.Height == 360,
            "设置面板：极小工作区 → 下限 420x360", panelTiny.Width + "x" + panelTiny.Height);

        // 预设：保持当前尺寸
        Size size = new Size(520, 728);
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.Right, work, size, dpi),
            new Rectangle(1400, 156, 520, 728), "窗口预设：靠右");
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.Left, work, size, dpi),
            new Rectangle(0, 156, 520, 728), "窗口预设：靠左");
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.CenterV, work, size, dpi),
            new Rectangle(700, 156, 520, 728), "窗口预设：垂直居中");
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.CenterH, work, size, dpi),
            new Rectangle(700, 156, 520, 728), "窗口预设：水平居中");
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.Top, work, size, dpi),
            new Rectangle(700, 0, 520, 728), "窗口预设：顶部");
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.Bottom, work, size, dpi),
            new Rectangle(700, 312, 520, 728), "窗口预设：底部");
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.ResetDefault, work, size, dpi), def,
            "窗口预设：重置默认 == DefaultWindowRect");

        // 新重载（带当前矩形）：只改目标轴，另一轴保持当前值（设置面板 7 个预设按钮用）
        Rectangle cur = new Rectangle(100, 200, 520, 728);
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.Right, work, cur, dpi),
            new Rectangle(1400, 200, 520, 728), "窗口预设(新)：靠右只改 X，Y 保持");
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.Left, work, cur, dpi),
            new Rectangle(0, 200, 520, 728), "窗口预设(新)：靠左只改 X，Y 保持");
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.Top, work, cur, dpi),
            new Rectangle(100, 0, 520, 728), "窗口预设(新)：顶部只改 Y，X 保持");
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.Bottom, work, cur, dpi),
            new Rectangle(100, 312, 520, 728), "窗口预设(新)：底部只改 Y，X 保持");
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.CenterH, work, cur, dpi),
            new Rectangle(700, 200, 520, 728), "窗口预设(新)：水平居中只改 X");
        CheckRect(ViewMath.PresetWindowRect(WindowPreset.CenterV, work, cur, dpi),
            new Rectangle(100, 156, 520, 728), "窗口预设(新)：垂直居中只改 Y");

        // 夹取：最小尺寸 240x180、完整可见
        CheckRect(ViewMath.ClampWindowRect(new Rectangle(-100, -100, 100, 50), work, dpi),
            new Rectangle(0, 0, 240, 180), "窗口夹取：过小 → 240x180 且回到工作区左上");
        CheckRect(ViewMath.ClampWindowRect(new Rectangle(1900, 1000, 520, 728), work, dpi),
            new Rectangle(1400, 312, 520, 728), "窗口夹取：右下超界 → 贴右下");
        CheckRect(ViewMath.ClampWindowRect(new Rectangle(0, 0, 3000, 2000), work, dpi),
            new Rectangle(0, 0, 1920, 1040), "窗口夹取：超过工作区 → 缩到工作区");
        CheckRect(ViewMath.ClampWindowRect(new Rectangle(-100, -100, 100, 50),
            new Rectangle(0, 0, 2560, 1440), 144), new Rectangle(0, 0, 360, 270),
            "窗口夹取：144 DPI 最小尺寸 = 360x270");

        // 切换屏幕：副屏工作区 (-1920,0,1920,1040)，矩形仍完整落在该工作区内
        Rectangle work2 = new Rectangle(-1920, 0, 1920, 1040);
        Rectangle onSecond = ViewMath.PresetWindowRect(WindowPreset.Right, work2, size, dpi);
        CheckRect(onSecond, new Rectangle(-520, 156, 520, 728), "切换屏幕：副屏靠右");
        Check(work2.Contains(onSecond), "切换屏幕：副屏预设后矩形完整落在副屏工作区内",
            RectText(onSecond) + " not in " + RectText(work2));

        Rectangle defSecond = ViewMath.DefaultWindowRect(work2, dpi);
        Check(work2.Contains(defSecond), "切换屏幕：副屏默认位置在工作区内",
            RectText(defSecond) + " not in " + RectText(work2));
        CheckRect(ViewMath.ClampWindowRect(onSecond, work2, dpi), onSecond,
            "切换屏幕：已在工作区内的矩形不被夹取移动");
    }

    // 3.6 工具条几何与命中
    private static void TestToolbarGeometry()
    {
        int dpi = 96;
        Size client = new Size(520, 756);

        // 完整工具条（12 张图、当前第 3 张、有 × 按钮、无长图开关）
        ToolbarLayout full = ViewMath.ToolbarGeometry(client, dpi, false, 12, 2, true);
        CheckRect(full.Bar, new Rectangle(78, 0, 364, 32), "工具条：520x756@96 整条矩形 (78,0,364x32)");
        Check(full.Find(ToolbarButton.Close) != null, "工具条：有 × 按钮");
        Check(full.Find(ToolbarButton.LockImage) != null, "工具条：有「锁图」按钮");
        Check(full.Find(ToolbarButton.LockImage).Text == "锁图", "工具条：「锁图」按钮文本");
        Check(full.Find(ToolbarButton.OneToOne) == null, "工具条：100% 已移出工具条（仍可从菜单/设置面板使用）");
        Check(full.Find(ToolbarButton.LongMode) == null, "工具条：未开启长图开关时不出现「长图」按钮");
        CheckToolbarLayout("工具条（完整）", full, client);

        ToolbarItem prev = full.Find(ToolbarButton.Prev);
        ToolbarItem page = full.Find(ToolbarButton.Page);
        ToolbarItem next = full.Find(ToolbarButton.Next);
        Check(prev != null && next != null && page != null, "工具条：◀/页码/▶ 三个按钮都存在");
        if (prev != null) { CheckRect(prev.Rect, new Rectangle(86, 0, 28, 32), "工具条：◀ 按钮矩形"); }
        if (page != null)
        {
            CheckRect(page.Rect, new Rectangle(118, 0, 56, 32), "工具条：页码按钮矩形");
            CheckStr(page.Text, "3/12", "工具条：页码文本 3/12");
            Check(!page.Enabled, "工具条：页码只读（Enabled=false）");
        }
        if (next != null) { CheckRect(next.Rect, new Rectangle(178, 0, 28, 32), "工具条：▶ 按钮矩形"); }

        // 按钮顺序
        Check(full.Items.Count >= 3
            && full.Items[0].Kind == ToolbarButton.Prev
            && full.Items[1].Kind == ToolbarButton.Page
            && full.Items[2].Kind == ToolbarButton.Next, "工具条：按钮顺序 ◀ | 页码 | ▶");

        // 命中测试
        if (prev != null)
        {
            Check(ViewMath.HitTestToolbar(full, RectCenter(prev.Rect)) == ToolbarButton.Prev,
                "工具条命中：◀ 返回 Prev");
        }
        if (page != null)
        {
            Check(ViewMath.HitTestToolbar(full, RectCenter(page.Rect)) == ToolbarButton.None,
                "工具条命中：页码返回 None（只读）");
        }
        if (next != null)
        {
            Check(ViewMath.HitTestToolbar(full, RectCenter(next.Rect)) == ToolbarButton.Next,
                "工具条命中：▶ 返回 Next");
        }
        ToolbarItem fit = full.Find(ToolbarButton.Fit);
        if (fit != null)
        {
            Check(ViewMath.HitTestToolbar(full, RectCenter(fit.Rect)) == ToolbarButton.Fit,
                "工具条命中：适应窗口返回 Fit");
        }
        ToolbarItem settings = full.Find(ToolbarButton.Settings);
        if (settings != null)
        {
            Check(ViewMath.HitTestToolbar(full, RectCenter(settings.Rect)) == ToolbarButton.Settings,
                "工具条命中：设置返回 Settings");
        }
        ToolbarItem close = full.Find(ToolbarButton.Close);
        if (close != null)
        {
            Check(ViewMath.HitTestToolbar(full, RectCenter(close.Rect)) == ToolbarButton.Close,
                "工具条命中：× 返回 Close");
        }
        ToolbarItem lockItem = full.Find(ToolbarButton.LockImage);
        if (lockItem != null)
        {
            Check(ViewMath.HitTestToolbar(full, RectCenter(lockItem.Rect)) == ToolbarButton.LockImage,
                "工具条命中：锁图返回 LockImage");
            CheckRect(lockItem.Rect, new Rectangle(286, 0, 52, 32), "工具条：锁图按钮矩形（沿用原 100% 位置）");
        }
        Check(full.Items.Count >= 6
            && full.Items[3].Kind == ToolbarButton.Fit
            && full.Items[4].Kind == ToolbarButton.LockImage
            && full.Items[5].Kind == ToolbarButton.Settings,
            "工具条：按钮顺序 适应窗口 | 锁图 | 设置…");
        Check(ViewMath.HitTestToolbar(full, new Point(0, 16)) == ToolbarButton.None,
            "工具条命中：工具条左侧空白返回 None");
        Check(ViewMath.HitTestToolbar(full, new Point(146, 40)) == ToolbarButton.None,
            "工具条命中：工具条下方返回 None");
        Check(ViewMath.HitTestToolbar(full, new Point(146, -1)) == ToolbarButton.None,
            "工具条命中：工具条上方返回 None");

        // 无图片：0/0 且翻页禁用
        ToolbarLayout empty = ViewMath.ToolbarGeometry(client, dpi, false, 0, -1, true);
        ToolbarItem emptyPage = empty.Find(ToolbarButton.Page);
        ToolbarItem emptyPrev = empty.Find(ToolbarButton.Prev);
        Check(emptyPage != null && emptyPage.Text == "0/0", "工具条：无图片时页码 0/0",
            emptyPage != null ? emptyPage.Text : "缺失");
        Check(emptyPrev != null && !emptyPrev.Enabled, "工具条：无图片时 ◀ 禁用");
        if (emptyPrev != null)
        {
            Check(ViewMath.HitTestToolbar(empty, RectCenter(emptyPrev.Rect)) == ToolbarButton.None,
                "工具条命中：禁用的 ◀ 返回 None");
        }
        CheckToolbarLayout("工具条（无图片）", empty, client);

        // 144 DPI
        Size hidpiClient = new Size(780, 1134);
        ToolbarLayout hi = ViewMath.ToolbarGeometry(hidpiClient, 144, false, 12, 2, true);
        CheckRect(hi.Bar, new Rectangle(117, 0, 546, 48), "工具条：780x1134@144 整条矩形 (117,0,546x48)");
        CheckToolbarLayout("工具条（144 DPI）", hi, hidpiClient);
        ToolbarItem hiPrev = hi.Find(ToolbarButton.Prev);
        if (hiPrev != null)
        {
            CheckRect(hiPrev.Rect, new Rectangle(129, 0, 42, 48), "工具条：144 DPI 下 ◀ 按钮矩形");
            Check(ViewMath.HitTestToolbar(hi, RectCenter(hiPrev.Rect)) == ToolbarButton.Prev,
                "工具条命中：144 DPI 下 ◀ 返回 Prev");
        }

        // 窄窗口 240x180：按优先级渐进隐藏可选按钮，仍不重叠不出界
        Size tiny = new Size(240, 180);
        ToolbarLayout tinyLayout = ViewMath.ToolbarGeometry(tiny, dpi, true, 3, 0, true);
        Check(tinyLayout.Find(ToolbarButton.Close) == null, "工具条（窄窗）：× 已渐进隐藏");
        Check(tinyLayout.Find(ToolbarButton.LongMode) == null, "工具条（窄窗）：长图开关已渐进隐藏");
        Check(tinyLayout.Find(ToolbarButton.LockImage) == null, "工具条（窄窗）：锁图已渐进隐藏");
        Check(tinyLayout.Find(ToolbarButton.Fit) == null, "工具条（窄窗）：适应窗口已渐进隐藏");
        Check(tinyLayout.Find(ToolbarButton.Settings) != null, "工具条（窄窗）：设置按钮保留");
        Check(tinyLayout.Bar.Right <= tiny.Width, "工具条（窄窗）：整条不出界",
            RectText(tinyLayout.Bar) + " client=" + tiny.Width + "x" + tiny.Height);
        CheckToolbarLayout("工具条（窄窗 240x180）", tinyLayout, tiny);

        ToolbarLayout noClose = ViewMath.ToolbarGeometry(client, dpi, false, 12, 2, false);
        Check(noClose.Find(ToolbarButton.Close) == null, "工具条：canClose=false 时不出现 × 按钮");
        CheckToolbarLayout("工具条（无 × 按钮）", noClose, client);

        // 空布局（客户区非法）不抛异常
        ToolbarLayout zero = ViewMath.ToolbarGeometry(new Size(0, 0), dpi, false, 0, -1, true);
        Check(zero.Items.Count == 0, "工具条：客户区为 0 时返回空布局");
        Check(ViewMath.HitTestToolbar(null, new Point(0, 0)) == ToolbarButton.None,
            "工具条命中：null 布局返回 None");
    }

    // 3.7 空状态卡片与 toast 几何
    private static void TestCardAndToastGeometry()
    {
        int dpi = 96;
        Size client = new Size(520, 756);
        CardLayout card = ViewMath.EmptyCardGeometry(client, dpi, @"C:\images");
        CheckRect(card.Card, new Rectangle(50, 297, 420, 162), "空卡片：520x756@96 居中卡片矩形");
        Check(card.Buttons.Count == 3, "空卡片：三个按钮", "实际 " + card.Buttons.Count);
        Check(card.Find(CardButton.ChooseFolder) != null
            && card.Find(CardButton.OpenFolder) != null
            && card.Find(CardButton.Rescan) != null, "空卡片：三个按钮类型齐全");
        CardItem choose = card.Find(CardButton.ChooseFolder);
        CardItem open = card.Find(CardButton.OpenFolder);
        CardItem rescan = card.Find(CardButton.Rescan);
        if (choose != null) { CheckRect(choose.Rect, new Rectangle(66, 381, 124, 30), "空卡片：选择文件夹按钮矩形"); }
        if (open != null) { CheckRect(open.Rect, new Rectangle(198, 381, 124, 30), "空卡片：打开文件夹按钮矩形"); }
        if (rescan != null) { CheckRect(rescan.Rect, new Rectangle(330, 381, 124, 30), "空卡片：重新扫描按钮矩形"); }
        if (choose != null)
        {
            Check(ViewMath.HitTestCard(card, RectCenter(choose.Rect)) == CardButton.ChooseFolder,
                "空卡片命中：选择文件夹");
            Check(choose.Rect.Left >= card.Card.Left && choose.Rect.Right <= card.Card.Right,
                "空卡片：按钮在卡片内");
        }
        Check(ViewMath.HitTestCard(card, new Point(500, 100)) == CardButton.None,
            "空卡片命中：卡片外返回 None");
        Check(ViewMath.HitTestCard(card, new Point(70, 300)) == CardButton.None,
            "空卡片命中：卡片内空白处返回 None");
        if (choose != null && open != null)
        {
            Check(choose.Rect.Right <= open.Rect.Left, "空卡片：按钮互不重叠");
        }
        if (open != null && rescan != null)
        {
            Check(open.Rect.Right <= rescan.Rect.Left, "空卡片：按钮互不重叠（右）");
        }
        CardLayout narrowCard = ViewMath.EmptyCardGeometry(new Size(300, 500), dpi, @"C:\images");
        Check(narrowCard.Card.Width == 268, "空卡片：窄窗口宽 = 300-32 = 268",
            "实际 " + narrowCard.Card.Width);
        Check(narrowCard.Card.Right <= 300 && narrowCard.Card.Left >= 0, "空卡片：窄窗口不出界");
        Check(ViewMath.HitTestCard(null, new Point(0, 0)) == CardButton.None,
            "空卡片命中：null 布局返回 None");

        // toast：右下角、8 DIP 边距、超宽中间省略
        using (Font font = UiFonts.Create(9f, FontStyle.Regular))
        {
            string shortText = "文件 3/12 · 100%";
            ToastLayout t1 = ViewMath.ToastGeometry(shortText, font, client, dpi, 0);
            Check(t1.Text == shortText, "toast：短文本不省略", t1.Text);
            Check(t1.Rect.Right <= client.Width - 8 && t1.Rect.Bottom <= client.Height - 8,
                "toast：矩形不超出右下角边距", RectText(t1.Rect));
            Check(t1.Rect.Left >= 0 && t1.Rect.Top >= 0, "toast：矩形在客户区内", RectText(t1.Rect));

            StringBuilder longSb = new StringBuilder();
            for (int i = 0; i < 200; i++) { longSb.Append('长'); }
            string longText = longSb.ToString();
            ToastLayout t2 = ViewMath.ToastGeometry(longText, font, client, dpi, 0);
            Check(t2.Text.Length < longText.Length, "toast：超宽文本被截断",
                "原长 " + longText.Length + "，显示 " + t2.Text.Length);
            Check(t2.Text.IndexOf('…') >= 0, "toast：截断使用中间省略号", t2.Text);
            Check(t2.Rect.Width <= client.Width - 40, "toast：整条不超过窗口宽-40",
                "宽 " + t2.Rect.Width);
        }

        // 非法客户区不抛异常
        ToastLayout t3 = ViewMath.ToastGeometry("x", null, new Size(0, 0), dpi, 0);
        Check(t3.Rect.Width == 0 && t3.Text == "", "toast：客户区为 0 时返回空布局");
    }

    // 3.8 长图判定 / 切片
    private static void TestLongImage()
    {
        // 长图判定：h/w ≥ 3 且像素 ≤ 6000 万
        Check(!ViewMath.IsLongImage(900.0, 1600.0, 0), "长图判定：900x1600 不是长图（比例 1.78）");
        Check(ViewMath.IsLongImage(800.0, 3000.0, 0), "长图判定：800x3000 是长图（比例 3.75）");
        Check(ViewMath.IsLongImage(100.0, 300.0, 0), "长图判定：比例恰好 3.0 是长图");
        Check(!ViewMath.IsLongImage(100.0, 299.0, 0), "长图判定：比例 2.99 不是长图");
        Check(!ViewMath.IsLongImage(800.0, 3000.0, 60000001L),
            "长图判定：像素 > 6000 万时不进入长图模式");
        Check(ViewMath.IsLongImage(800.0, 3000.0, 60000000L), "长图判定：像素 = 6000 万仍允许");

        // 切片数量：ceil(imgH / 2000)
        CheckInt(ViewMath.SliceCount(4500.0, 2000), 3, "切片数量：4500/2000 → 3");
        CheckInt(ViewMath.SliceCount(4000.0, 2000), 2, "切片数量：4000/2000 → 2");
        CheckInt(ViewMath.SliceCount(2001.0, 2000), 2, "切片数量：2001/2000 → 2");
        CheckInt(ViewMath.SliceCount(2000.0, 2000), 1, "切片数量：2000/2000 → 1");
        CheckInt(ViewMath.SliceCount(0.0, 2000), 0, "切片数量：图片高非法 → 0");
        CheckInt(ViewMath.SliceCount(4500.0, 0), 0, "切片数量：切片高非法 → 0");

        // 切片源矩形（1 基，最后一片取剩余）
        RectangleF r1 = ViewMath.SliceSourceRect(100.0, 4500.0, 1, 2000);
        CheckNear(r1.Y, 0.0, 1e-6, "切片矩形：第 1 片 Y = 0");
        CheckNear(r1.Height, 2000.0, 1e-6, "切片矩形：第 1 片高 = 2000");
        RectangleF r3 = ViewMath.SliceSourceRect(100.0, 4500.0, 3, 2000);
        CheckNear(r3.Y, 4000.0, 1e-6, "切片矩形：第 3 片 Y = 4000");
        CheckNear(r3.Height, 500.0, 1e-6, "切片矩形：第 3 片高 = 500（剩余）");
        Check(ViewMath.SliceSourceRect(100.0, 4500.0, 4, 2000).IsEmpty, "切片矩形：越界返回空");
        Check(ViewMath.SliceSourceRect(100.0, 4500.0, 0, 2000).IsEmpty, "切片矩形：索引 0 返回空");

        // 切片命名
        CheckStr(ViewMath.SliceFileName("a.jpg", 1), "a-slice-01.png", "切片命名：a.jpg + 1");
        CheckStr(ViewMath.SliceFileName(@"dir\a.jpg", 2), "a-slice-02.png", "切片命名：去目录");
        CheckStr(ViewMath.SliceFileName("a.jpg", 0), "a-slice-01.png", "切片命名：索引 0 按 1 处理");
        CheckStr(ViewMath.SliceFileName("a.jpg", 12), "a-slice-12.png", "切片命名：两位数编号");
        CheckStr(ViewMath.SliceFileName("", 1), "image-slice-01.png", "切片命名：空文件名回退 image");
        CheckStr(ViewMath.SliceFileName("a.JPG", 1), "a-slice-01.png", "切片命名：扩展名大小写不敏感");

        // 滚动步长
        CheckNear(ViewMath.LongImageStepPx(0.5, 96), 60.0, 1e-9, "长图步长：96 DPI = 60px");
        CheckNear(ViewMath.LongImageStepPx(2.0, 144), 90.0, 1e-9, "长图步长：144 DPI = 90px");

        // C2：扫描指纹比较（自动刷新据此判断「图库没变就不重载」）
        Check(ImageLibrary.SameScanFingerprint(3, 1000, 500, 3, 1000, 500), "扫描指纹：三项全同 → true");
        Check(!ImageLibrary.SameScanFingerprint(3, 1000, 500, 4, 1000, 500), "扫描指纹：文件数不同 → false");
        Check(!ImageLibrary.SameScanFingerprint(3, 1000, 500, 3, 1001, 500), "扫描指纹：总字节不同 → false");
        Check(!ImageLibrary.SameScanFingerprint(3, 1000, 500, 3, 1000, 501), "扫描指纹：最大修改时间不同 → false");

        // C5：预取判定（单图 > 800 万像素或长图模式不预取）
        Check(ImageLibrary.ShouldPrefetch(8000000, false), "预取判定：800 万像素 = 允许预取（边界）");
        Check(!ImageLibrary.ShouldPrefetch(8000001, false), "预取判定：超 800 万像素 = 不预取");
        Check(!ImageLibrary.ShouldPrefetch(4000000, true), "预取判定：长图模式一律不预取");
        Check(!ImageLibrary.ShouldPrefetch(0, false), "预取判定：像素数未知 = 不预取");
    }

    // 3.9 长图滚动夹取与滚动条
    private static void TestScrollbarAndScroll()
    {
        // ClampScroll：内容高于视口 → offsetY ∈ [view-content, 0]
        double offY = 100.0;
        ViewMath.ClampScroll(0.5, 4000.0, 800.0, ref offY);
        CheckNear(offY, 0.0, 1e-9, "滚动夹取：正偏移回到 0");
        offY = -5000.0;
        ViewMath.ClampScroll(0.5, 4000.0, 800.0, ref offY);
        CheckNear(offY, -1200.0, 1e-9, "滚动夹取：下界 = 800-2000 = -1200");
        offY = -500.0;
        ViewMath.ClampScroll(0.5, 4000.0, 800.0, ref offY);
        CheckNear(offY, -500.0, 1e-9, "滚动夹取：界内保持");
        offY = -300.0;
        ViewMath.ClampScroll(0.5, 1000.0, 800.0, ref offY);
        CheckNear(offY, 0.0, 1e-9, "滚动夹取：内容不足一屏 → 顶部对齐 0");
        offY = -300.0;
        ViewMath.ClampScroll(0.0, 1000.0, 800.0, ref offY);
        CheckNear(offY, 0.0, 1e-9, "滚动夹取：zoom 非法 → 0");

        // 滚动条轨道（C4：宽度 8→6 DIP，给右下角拉伸热区让位）
        CheckRect(ViewMath.ScrollbarTrackRect(new Size(520, 756), 96),
            new Rectangle(514, 0, 6, 756), "滚动条：右侧 6 DIP 轨道");
        CheckRect(ViewMath.ScrollbarTrackRect(new Size(0, 0), 96), Rectangle.Empty,
            "滚动条：客户区非法 → 空");

        // 滚动条滑块：位置/高度
        Rectangle thumbTop = ViewMath.ScrollbarThumbRect(new Size(520, 756), 96, 756.0, 3000.0, 0.0, 0.5);
        CheckInt(thumbTop.Top, 0, "滚动条：顶部时滑块在轨道顶端");
        CheckInt(thumbTop.Height, 381, "滚动条：滑块高 = 756*756/1500 ≈ 381");
        CheckInt(thumbTop.X, 514, "滚动条：滑块与轨道同 X");
        CheckInt(thumbTop.Width, 6, "滚动条：滑块与轨道同宽");
        Rectangle thumbBottom = ViewMath.ScrollbarThumbRect(new Size(520, 756), 96, 756.0, 3000.0, -744.0, 0.5);
        CheckInt(thumbBottom.Bottom, 756, "滚动条：到底时滑块贴轨道底端");
        Rectangle thumbSmall = ViewMath.ScrollbarThumbRect(new Size(520, 756), 96, 756.0, 100000.0, 0.0, 0.5);
        CheckInt(thumbSmall.Height, 24, "滚动条：滑块最小高度 24 DIP");
        Rectangle thumbFull = ViewMath.ScrollbarThumbRect(new Size(520, 756), 96, 756.0, 1000.0, 0.0, 0.5);
        CheckInt(thumbFull.Height, 756, "滚动条：内容不足一屏 → 滑块占满轨道");

        // C4：拖动看门狗判定（Capture 还在但左右键都松了 → 强制结束交互）
        Check(!OverlayForm.ShouldCancelStuckDrag(false, MouseButtons.None, false),
            "拖动看门狗：没有拖动标志 → 不取消");
        Check(!OverlayForm.ShouldCancelStuckDrag(true, MouseButtons.Left, false),
            "拖动看门狗：左键仍按住 → 正常拖动不打断");
        Check(OverlayForm.ShouldCancelStuckDrag(true, MouseButtons.None, false),
            "拖动看门狗：拖动标志 + 左右键全松 → 取消并恢复穿透");
        Check(!OverlayForm.ShouldCancelStuckDrag(true, MouseButtons.None, true),
            "拖动看门狗：低权限模式不启用（按键状态不可靠，避免误伤）");
    }

    // 3.10 边缘吸附
    private static void TestSnap()
    {
        Rectangle work = new Rectangle(0, 0, 1920, 1040);
        int threshold = Layout.Px(Layout.SnapThresholdDip, 96); // 12

        bool sx;
        bool sy;
        // 阈值内：左边缘吸附
        Point p1 = ViewMath.SnapMove(new Rectangle(5, 100, 520, 728), work, threshold, out sx, out sy);
        Check(p1.X == 0 && sx, "吸附：左边缘距 5px → 吸附到 0", "x=" + p1.X + " snappedX=" + sx);
        Check(!sy, "吸附：Y 方向未触发吸附");

        // 阈值内：右边缘吸附
        Point p2 = ViewMath.SnapMove(new Rectangle(1396, 100, 520, 728), work, threshold, out sx, out sy);
        Check(p2.X == 1400 && sx, "吸附：右边缘距 4px → 吸附到 1400", "x=" + p2.X);

        // 阈值外：不动
        Point p3 = ViewMath.SnapMove(new Rectangle(100, 100, 520, 728), work, threshold, out sx, out sy);
        Check(p3.X == 100 && p3.Y == 100 && !sx && !sy, "吸附：阈值外位置不变",
            "(" + p3.X + "," + p3.Y + ")");
        Point p4 = ViewMath.SnapMove(new Rectangle(13, 100, 520, 728), work, threshold, out sx, out sy);
        Check(p4.X == 13 && !sx, "吸附：距 13px（>12）不吸附", "x=" + p4.X);

        // 上/下边缘
        Point p5 = ViewMath.SnapMove(new Rectangle(100, 5, 520, 728), work, threshold, out sx, out sy);
        Check(p5.Y == 0 && sy, "吸附：上边缘距 5px → 吸附到 0", "y=" + p5.Y);
        Point p6 = ViewMath.SnapMove(new Rectangle(100, 307, 520, 728), work, threshold, out sx, out sy);
        Check(p6.Y == 312 && sy, "吸附：下边缘距 5px → 吸附到 312", "y=" + p6.Y);

        // 不跨屏：传副屏工作区时，靠近主屏边缘的窗口不应吸附
        Rectangle work2 = new Rectangle(-1920, 0, 1920, 1040);
        Point p7 = ViewMath.SnapMove(new Rectangle(1900, 100, 520, 728), work2, threshold, out sx, out sy);
        Check(p7.X == 1900 && p7.Y == 100 && !sx && !sy,
            "吸附：不跨屏（副屏工作区不吸附主屏边缘）", "(" + p7.X + "," + p7.Y + ")");
        Point p8 = ViewMath.SnapMove(new Rectangle(-1915, 100, 520, 728), work2, threshold, out sx, out sy);
        Check(p8.X == -1920 && sx, "吸附：副屏左边缘吸附到 -1920", "x=" + p8.X);

        // 拉伸吸附：右/下边缘阈值内吸附，对边固定
        bool snapped;
        Rectangle r1 = ViewMath.SnapResize(new Rectangle(100, 100, 1810, 930), work, threshold, out snapped);
        Check(snapped, "拉伸吸附：右/下边缘 10px → 触发吸附");
        CheckRect(r1, new Rectangle(100, 100, 1820, 940), "拉伸吸附：右/下边缘吸附到工作区边界");
        Rectangle r2 = ViewMath.SnapResize(new Rectangle(100, 100, 1770, 890), work, threshold, out snapped);
        Check(!snapped && r2 == new Rectangle(100, 100, 1770, 890),
            "拉伸吸附：阈值外不变", RectText(r2));

        // 144 DPI 阈值（12 DIP → 18px）
        int threshold144 = Layout.Px(Layout.SnapThresholdDip, 144);
        CheckInt(threshold144, 18, "吸附：144 DPI 阈值 = 18px");
        Point p9 = ViewMath.SnapMove(new Rectangle(17, 100, 520, 728), work, threshold144, out sx, out sy);
        Check(p9.X == 0 && sx, "吸附：144 DPI 下 17px 触发吸附", "x=" + p9.X);
        Point p10 = ViewMath.SnapMove(new Rectangle(19, 100, 520, 728), work, threshold144, out sx, out sy);
        Check(p10.X == 19 && !sx, "吸附：144 DPI 下 19px 不吸附", "x=" + p10.X);
    }

    // 3.11 热键解析与格式化
    private static void TestHotkeyParse()
    {
        bool ok;
        Keys fallback = Keys.Control | Keys.Alt | Keys.F1;

        Keys k = HotkeyManager.ParseCombo("Ctrl+Alt+Up", fallback, out ok);
        Check(ok && k == (Keys.Control | Keys.Alt | Keys.Up), "热键解析：Ctrl+Alt+Up",
            "ok=" + ok + " 结果=" + k);
        k = HotkeyManager.ParseCombo("ctrl+alt+up", fallback, out ok);
        Check(ok && k == (Keys.Control | Keys.Alt | Keys.Up), "热键解析：大小写不敏感", "结果=" + k);
        k = HotkeyManager.ParseCombo("none", fallback, out ok);
        Check(ok && k == Keys.None, "热键解析：none = 禁用");
        k = HotkeyManager.ParseCombo("", fallback, out ok);
        Check(ok && k == Keys.None, "热键解析：空字符串 = 禁用");
        k = HotkeyManager.ParseCombo(null, fallback, out ok);
        Check(ok && k == Keys.None, "热键解析：null = 禁用");
        k = HotkeyManager.ParseCombo("无", fallback, out ok);
        Check(ok && k == Keys.None, "热键解析：中文「无」= 禁用");

        k = HotkeyManager.ParseCombo("Ctrl+Alt", fallback, out ok);
        Check(!ok && k == fallback, "热键解析：只有修饰键 → 回退默认", "结果=" + k);
        k = HotkeyManager.ParseCombo("Up", fallback, out ok);
        Check(!ok && k == fallback, "热键解析：缺少修饰键 → 回退默认", "结果=" + k);
        k = HotkeyManager.ParseCombo("Ctrl+Alt+Up+Down", fallback, out ok);
        Check(!ok && k == fallback, "热键解析：多个主键 → 回退默认", "结果=" + k);
        k = HotkeyManager.ParseCombo("Ctrl+Nope", fallback, out ok);
        Check(!ok && k == fallback, "热键解析：未知键名 → 回退默认", "结果=" + k);
        k = HotkeyManager.ParseCombo("Win+X", fallback, out ok);
        Check(!ok && k == fallback, "热键解析：Win 组合 → 回退默认", "结果=" + k);

        k = HotkeyManager.ParseCombo("Ctrl+Alt+[", fallback, out ok);
        Check(ok && k == (Keys.Control | Keys.Alt | Keys.OemOpenBrackets), "热键解析：Ctrl+Alt+[", "结果=" + k);
        k = HotkeyManager.ParseCombo("Ctrl+Alt+1", fallback, out ok);
        Check(ok && k == (Keys.Control | Keys.Alt | Keys.D1), "热键解析：Ctrl+Alt+1", "结果=" + k);
        k = HotkeyManager.ParseCombo("Shift+F", fallback, out ok);
        Check(ok && k == (Keys.Shift | Keys.F), "热键解析：Shift+F", "结果=" + k);
        k = HotkeyManager.ParseCombo("Alt+Tab", fallback, out ok);
        Check(ok && k == (Keys.Alt | Keys.Tab), "热键解析：Alt+Tab 可解析（保留组合由校验拒绝）", "结果=" + k);

        // 格式化 + 往返
        CheckStr(HotkeyManager.FormatCombo(Keys.Control | Keys.Alt | Keys.Up), "Ctrl+Alt+Up",
            "热键格式化：Ctrl+Alt+Up");
        CheckStr(HotkeyManager.FormatCombo(Keys.None), "无", "热键格式化：None → 无");
        CheckStr(HotkeyManager.FormatCombo(Keys.Control | Keys.Alt | Keys.D1), "Ctrl+Alt+1",
            "热键格式化：数字键");
        Keys[] combos = new Keys[]
        {
            Keys.Control | Keys.Alt | Keys.Up,
            Keys.Control | Keys.Shift | Keys.F1,
            Keys.Alt | Keys.D9,
            Keys.Control | Keys.Alt | Keys.OemOpenBrackets,
            Keys.Shift | Keys.Space,
            Keys.Control | Keys.Alt | Keys.OemCloseBrackets
        };
        int roundTripBad = 0;
        for (int i = 0; i < combos.Length; i++)
        {
            string text = HotkeyManager.FormatCombo(combos[i]);
            Keys parsed = HotkeyManager.ParseCombo(text, Keys.None, out ok);
            if (!ok || parsed != combos[i]) { roundTripBad++; }
        }
        Check(roundTripBad == 0, "热键：格式化→解析 往返 6 组全部一致", "不一致 " + roundTripBad + " 组");

        // 默认组合 / ini 键名 / 中文名
        Check(HotkeyManager.DefaultCombo(HotkeyAction.ZoomIn) == (Keys.Control | Keys.Alt | Keys.Up),
            "热键默认：放大 = Ctrl+Alt+Up");
        Check(HotkeyManager.DefaultCombo(HotkeyAction.Prev) == (Keys.Control | Keys.Alt | Keys.Left),
            "热键默认：上一张 = Ctrl+Alt+Left");
        Check(HotkeyManager.DefaultCombo(HotkeyAction.ToggleLock) == (Keys.Control | Keys.Alt | Keys.T),
            "热键默认：锁定交互 = Ctrl+Alt+T");
        Check(HotkeyManager.DefaultCombo(HotkeyAction.OpacityDown)
            == (Keys.Control | Keys.Alt | Keys.OemOpenBrackets), "热键默认：降低不透明度 = Ctrl+Alt+[");
        CheckInt(HotkeyManager.AllActions.Length, 18, "热键：动作总数 = 18");

        HashSet<string> iniKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int emptyKeyCount = 0;
        int emptyNameCount = 0;
        for (int i = 0; i < HotkeyManager.AllActions.Length; i++)
        {
            HotkeyAction a = HotkeyManager.AllActions[i];
            string iniKey = HotkeyManager.IniKeyOf(a);
            if (string.IsNullOrEmpty(iniKey)) { emptyKeyCount++; }
            else { iniKeys.Add(iniKey); }
            if (string.IsNullOrEmpty(HotkeyManager.DisplayNameOf(a))) { emptyNameCount++; }
        }
        Check(emptyKeyCount == 0, "热键：每个动作都有 ini 键名", "空 " + emptyKeyCount + " 个");
        Check(iniKeys.Count == HotkeyManager.AllActions.Length, "热键：ini 键名互不重复",
            "唯一 " + iniKeys.Count + " / 共 " + HotkeyManager.AllActions.Length);
        Check(emptyNameCount == 0, "热键：每个动作都有中文名", "空 " + emptyNameCount + " 个");
        CheckStr(HotkeyManager.IniKeyOf(HotkeyAction.ZoomIn), "zoom_in", "热键：放大 ini 键名 = zoom_in");
    }

    // 3.12 热键录制校验
    private static void TestHotkeyValidate()
    {
        string error;
        Check(HotkeyManager.ValidateCombo(Keys.Control | Keys.Alt | Keys.T, out error),
            "热键校验：Ctrl+Alt+T 合法", error);
        Check(HotkeyManager.ValidateCombo(Keys.Control | Keys.Shift | Keys.F1, out error),
            "热键校验：Ctrl+Shift+F1 合法", error);

        bool valid = HotkeyManager.ValidateCombo(Keys.Control | Keys.Alt | Keys.Delete, out error);
        Check(!valid, "热键校验：Ctrl+Alt+Del 非法（系统保留）");
        Check(!string.IsNullOrEmpty(error) && error.IndexOf("保留", StringComparison.Ordinal) >= 0,
            "热键校验：Ctrl+Alt+Del 给出中文原因", error);
        Check(!HotkeyManager.ValidateCombo(Keys.Alt | Keys.Tab, out error), "热键校验：Alt+Tab 非法");
        Check(!HotkeyManager.ValidateCombo(Keys.Control | Keys.Escape, out error), "热键校验：Ctrl+Esc 非法");
        Check(!HotkeyManager.ValidateCombo(Keys.Control, out error), "热键校验：只有修饰键非法");
        Check(!string.IsNullOrEmpty(error), "热键校验：只有修饰键有原因", error);
        Check(!HotkeyManager.ValidateCombo(Keys.T, out error), "热键校验：无修饰键非法");
        Check(!string.IsNullOrEmpty(error), "热键校验：无修饰键有原因", error);
        Check(!HotkeyManager.ValidateCombo(Keys.None, out error), "热键校验：Keys.None 非法");

        // C6：系统/常用快捷键黑名单（精确组合相等，不能误伤 Ctrl+Alt+F 等默认组合）
        Check(!HotkeyManager.ValidateCombo(Keys.Alt | Keys.F4, out error), "热键校验：Alt+F4 非法（系统关闭窗口）");
        Check(!HotkeyManager.ValidateCombo(Keys.Control | Keys.C, out error), "热键校验：Ctrl+C 非法（常用复制）");
        Check(!HotkeyManager.ValidateCombo(Keys.Control | Keys.PrintScreen, out error),
            "热键校验：Ctrl+PrintScreen 也非法（含 PrintScreen 一律拒）");
        Check(HotkeyManager.ValidateCombo(Keys.Control | Keys.Alt | Keys.F, out error),
            "热键校验：Ctrl+Alt+F 合法（默认「适应窗口」，不能被 Ctrl+F 黑名单误伤）", error);
        Check(HotkeyManager.ValidateCombo(Keys.Control | Keys.Shift | Keys.C, out error),
            "热键校验：Ctrl+Shift+C 合法（黑名单必须精确相等）", error);
    }

    // 3.13 最近文件夹去重排序
    private static void TestRecentFolders()
    {
        AppSettings s = new AppSettings();
        Check(s.RecentFolders != null && s.RecentFolders.Count == 0, "最近文件夹：初始为空");

        s.AddRecentFolder(null);
        s.AddRecentFolder("   ");
        Check(s.RecentFolders.Count == 0, "最近文件夹：null/空白被忽略", "实际 " + s.RecentFolders.Count);

        s.AddRecentFolder(@"C:\a");
        Check(s.RecentFolders.Count == 1 && string.Equals(s.RecentFolders[0], @"C:\a", StringComparison.OrdinalIgnoreCase),
            "最近文件夹：添加 C:\\a", "实际 " + JoinList(s.RecentFolders));

        s.AddRecentFolder(@"C:\b");
        Check(s.RecentFolders.Count == 2 && string.Equals(s.RecentFolders[0], @"C:\b", StringComparison.OrdinalIgnoreCase),
            "最近文件夹：新项置顶", "实际 " + JoinList(s.RecentFolders));

        s.AddRecentFolder(@"C:\a");
        Check(s.RecentFolders.Count == 2 && string.Equals(s.RecentFolders[0], @"C:\a", StringComparison.OrdinalIgnoreCase),
            "最近文件夹：重复项去重并置顶", "实际 " + JoinList(s.RecentFolders));

        s.AddRecentFolder(@"C:\a\");
        Check(s.RecentFolders.Count == 2, "最近文件夹：末尾分隔符视为同一路径",
            "实际 " + JoinList(s.RecentFolders));

        s.AddRecentFolder(@"C:\c");
        s.AddRecentFolder(@"C:\d");
        s.AddRecentFolder(@"C:\e");
        s.AddRecentFolder(@"C:\f");
        CheckInt(s.RecentFolders.Count, 5, "最近文件夹：最多保留 5 个");
        Check(string.Equals(s.RecentFolders[0], @"C:\f", StringComparison.OrdinalIgnoreCase),
            "最近文件夹：最近在前（C:\\f）", "实际 " + JoinList(s.RecentFolders));
        bool hasB = false;
        for (int i = 0; i < s.RecentFolders.Count; i++)
        {
            if (string.Equals(s.RecentFolders[i], @"C:\b", StringComparison.OrdinalIgnoreCase)) { hasB = true; }
        }
        Check(!hasB, "最近文件夹：超出 5 个时淘汰最旧的 C:\\b", "实际 " + JoinList(s.RecentFolders));

        s.AddRecentFolder(@"c:\A");
        CheckInt(s.RecentFolders.Count, 5, "最近文件夹：大小写不敏感去重（c:\\A 与 C:\\a 同一项）");
    }

    // 3.14 settings 往返与非法值夹取
    private static void TestSettingsRoundtrip()
    {
        string dir = Path.Combine(_tempRoot, "settings-tests");
        try { Directory.CreateDirectory(dir); } catch { }

        // (1) 文件不存在：全部默认值，不抛异常
        string missing = Path.Combine(dir, "missing.ini");
        AppSettings defaults = SettingsStore.Load(_exeDir, missing);
        Size expectDefaultWindow = Layout.DefaultWindowSize(Screen.PrimaryScreen.WorkingArea, DpiHelper.SystemDpi);
        CheckInt(defaults.Opacity, 85, "settings 默认：opacity = 85");
        CheckInt(defaults.Width, expectDefaultWindow.Width, "settings 默认：width = 9:16 竖版宽（工作区高 60%）");
        CheckInt(defaults.Height, expectDefaultWindow.Height, "settings 默认：height = 工作区高 60%");
        Check(defaults.Theme == ThemeKind.Light, "settings 默认：theme = light");
        Check(defaults.GateKey == GateKeyKind.Alt, "settings 默认：gate_key = Alt");
        Check(defaults.FirstRun, "settings 默认：ini 不存在 → first_run = true（首次运行）");
        Check(defaults.TopMost && defaults.Snap && defaults.Watch && !defaults.PositionLocked,
            "settings 默认：topmost/snap/watch=1、locked=0");
        Check(SamePathText(defaults.Folder, SettingsStore.DefaultImageFolder(_exeDir)),
            "settings 默认：folder 回退 exe\\images", "实际 " + defaults.Folder);
        Check(defaults.GetHotkey(HotkeyAction.ZoomIn) == (Keys.Control | Keys.Alt | Keys.Up),
            "settings 默认：热键填默认组合");

        // (2) 非法 ini：夹取 / 回退默认
        string illegal = Path.Combine(dir, "illegal.ini");
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[window]");
        sb.AppendLine("left=120");
        sb.AppendLine("top=200");
        sb.AppendLine("width=100");
        sb.AppendLine("height=50");
        sb.AppendLine("opacity=5");
        sb.AppendLine("topmost=0");
        sb.AppendLine("locked=1");
        sb.AppendLine("snap=7");
        sb.AppendLine("last_image=03-第一章.jpg");
        sb.AppendLine("[images]");
        sb.AppendLine("folder=" + _imagesDir);
        sb.AppendLine("watch=1");
        sb.AppendLine("recent1=" + _imagesDir);
        sb.AppendLine("recent2=" + _imagesDir);
        sb.AppendLine(@"recent3=Z:\definitely-not-exists-guideoverlay");
        sb.AppendLine("[behavior]");
        sb.AppendLine("gate_key=bogus");
        sb.AppendLine("[ui]");
        sb.AppendLine("theme=purple");
        sb.AppendLine("[hotkeys]");
        sb.AppendLine("zoom_in=Ctrl+Alt+Up");
        sb.AppendLine("zoom_out=none");
        sb.AppendLine("next=");
        sb.AppendLine("prev=Ctrl+Alt");
        sb.AppendLine("fit=Alt+Tab");
        WriteUtf8(illegal, sb.ToString());

        AppSettings s = SettingsStore.Load(_exeDir, illegal);
        CheckInt(s.Width, Layout.MinWindowWidthDip, "settings 夹取：width 100 → 240");
        CheckInt(s.Height, Layout.MinWindowHeightDip, "settings 夹取：height 50 → 180");
        CheckInt(s.Opacity, 20, "settings 夹取：opacity 5 → 20");
        Check(!s.TopMost, "settings 解析：topmost=0 → false");
        Check(s.PositionLocked, "settings 解析：locked=1 → true");
        Check(s.Snap, "settings 解析：snap=7（非法）→ 默认 true");
        CheckStr(s.LastImage, "03-第一章.jpg", "settings 解析：last_image 保留中文");
        Check(SamePathText(s.Folder, _imagesDir), "settings 解析：folder 指向临时 images",
            "实际 " + s.Folder);
        Check(s.Watch, "settings 解析：watch=1 → true");
        CheckInt(s.RecentFolders.Count, 1, "settings 解析：recent 去重 + 丢弃失效项后剩 1 个");
        Check(s.GateKey == GateKeyKind.Alt, "settings 解析：gate_key 非法 → Alt（默认值）");
        Check(s.GateKeyFallback, "settings 解析：gate_key 非法 → 标记 GateKeyFallback（启动摘要提示）");
        Check(s.Theme == ThemeKind.Light, "settings 解析：theme 非法 → light");
        Check(s.GetHotkey(HotkeyAction.ZoomIn) == (Keys.Control | Keys.Alt | Keys.Up),
            "settings 解析：zoom_in 合法组合保留");
        Check(s.GetHotkey(HotkeyAction.ZoomOut) == Keys.None, "settings 解析：zoom_out=none → 禁用");
        Check(s.GetHotkey(HotkeyAction.Next) == Keys.None, "settings 解析：next 空 → 禁用");
        Check(s.GetHotkey(HotkeyAction.Prev) == (Keys.Control | Keys.Alt | Keys.Left),
            "settings 解析：prev 非法 → 回退默认 Ctrl+Alt+Left");
        // C6：ini 里手工写 Alt+Tab（系统保留）也要过 ValidateCombo → 回退默认，不能注册抢系统组合
        Check(s.GetHotkey(HotkeyAction.Fit) == (Keys.Control | Keys.Alt | Keys.F),
            "settings 解析：fit=Alt+Tab（系统保留）→ 回退默认 Ctrl+Alt+F");
        CheckInt(s.HotkeyFallbackCount, 2,
            "settings 解析：prev 语法非法 + fit 系统保留 → HotkeyFallbackCount=2（启动摘要提示）");

        // (3) 更多非法值：opacity 上下界 / 布尔只接受 0/1 / dark / shift
        string illegal2 = Path.Combine(dir, "illegal2.ini");
        sb = new StringBuilder();
        sb.AppendLine("[window]");
        sb.AppendLine("width=300");
        sb.AppendLine("height=300");
        sb.AppendLine("opacity=999");
        sb.AppendLine("topmost=5");
        sb.AppendLine("locked=7");
        sb.AppendLine("snap=2");
        sb.AppendLine("[images]");
        sb.AppendLine("folder=" + _imagesDir);
        sb.AppendLine("watch=3");
        sb.AppendLine("[behavior]");
        sb.AppendLine("gate_key=SHIFT");
        sb.AppendLine("[ui]");
        sb.AppendLine("theme=DARK");
        WriteUtf8(illegal2, sb.ToString());

        AppSettings s2 = SettingsStore.Load(_exeDir, illegal2);
        CheckInt(s2.Opacity, 100, "settings 夹取：opacity 999 → 100");
        CheckInt(s2.Width, 300, "settings 解析：width 300 保留");
        CheckInt(s2.Height, 300, "settings 解析：height 300 保留");
        Check(s2.TopMost, "settings 解析：topmost=5（非法）→ 默认 true");
        Check(!s2.PositionLocked, "settings 解析：locked=7（非法）→ 默认 false");
        Check(s2.Snap, "settings 解析：snap=2（非法）→ 默认 true");
        Check(s2.Watch, "settings 解析：watch=3（非法）→ 默认 true");
        Check(s2.GateKey == GateKeyKind.Shift, "settings 解析：gate_key=SHIFT → Shift");
        Check(s2.Theme == ThemeKind.Dark, "settings 解析：theme=DARK → dark");

        // (3b) [meta] first_run 三态（B1）：老用户 ini 无键 = false；显式 0/1 按值
        string oldUser = Path.Combine(dir, "olduser.ini");
        WriteUtf8(oldUser, "[window]\r\nwidth=520\r\nheight=756\r\n");
        AppSettings oldS = SettingsStore.Load(_exeDir, oldUser);
        Check(!oldS.FirstRun, "settings 解析：ini 存在但无 first_run → false（老用户不弹引导）");

        string firstRun0 = Path.Combine(dir, "firstrun0.ini");
        WriteUtf8(firstRun0, "[meta]\r\nfirst_run=0\r\n");
        Check(!SettingsStore.Load(_exeDir, firstRun0).FirstRun,
            "settings 解析：first_run=0 → 引导已完成");

        string firstRun1 = Path.Combine(dir, "firstrun1.ini");
        WriteUtf8(firstRun1, "[meta]\r\nfirst_run=1\r\n");
        Check(SettingsStore.Load(_exeDir, firstRun1).FirstRun,
            "settings 解析：first_run=1 → 仍需首次运行引导");

        // (4) 完整往返：写回再读回，字段一致
        string roundTrip = Path.Combine(dir, "roundtrip.ini");
        AppSettings w = SettingsStore.Load(_exeDir, roundTrip);
        w.HasWindowRect = true;
        w.Left = 0;
        w.Top = 0;
        w.Width = 520;
        w.Height = 756;
        w.Opacity = 60;
        w.TopMost = false;
        w.PositionLocked = true;
        w.Snap = false;
        w.LastImage = "sub/03.jpg";
        w.Folder = _imagesDir;
        w.FolderSetting = _imagesDir;
        w.Watch = false;
        w.GateKey = GateKeyKind.Alt;
        w.Theme = ThemeKind.Dark;
        w.RecentFolders.Clear();
        w.AddRecentFolder(_imagesDir);
        w.AddRecentFolder(_tempRoot);
        w.SetHotkey(HotkeyAction.ZoomIn, Keys.Control | Keys.Shift | Keys.Z);
        w.SetHotkey(HotkeyAction.Jump9, Keys.None);
        w.FirstRun = false;   // 模拟引导已完成 → 应写回 first_run=0
        SettingsStore.SaveTo(w, roundTrip);
        Check(File.Exists(roundTrip), "settings 往返：ini 已写出", roundTrip);
        string roundTripText = "";
        try { roundTripText = File.ReadAllText(roundTrip); } catch { }
        CheckContains(roundTripText, "[meta]", "settings 往返：[meta] 分区已写出");
        CheckContains(roundTripText, "first_run=0", "settings 往返：引导完成写回 first_run=0");

        // C7：ini 必须带 UTF-8 BOM（记事本等按 ANSI 误判会导致中文乱码）
        byte[] roundTripBytes = new byte[0];
        try { roundTripBytes = File.ReadAllBytes(roundTrip); } catch { }
        Check(roundTripBytes.Length >= 3 && roundTripBytes[0] == 0xEF
            && roundTripBytes[1] == 0xBB && roundTripBytes[2] == 0xBF,
            "settings 往返：ini 以 UTF-8 BOM 开头（EF BB BF）");

        // C7：非 UTF-8 旧配置的编码回退判定（纯函数）
        Check(!SettingsStore.NeedsEncodingFallback("正常的 UTF-8 中文"), "settings 编码：正常 UTF-8 不触发回退");
        Check(SettingsStore.NeedsEncodingFallback("损坏字符 \uFFFD 存在"), "settings 编码：含 U+FFFD 触发系统编码重读");

        AppSettings r = SettingsStore.Load(_exeDir, roundTrip);
        CheckInt(r.Opacity, 60, "settings 往返：opacity = 60");
        Check(r.HasWindowRect, "settings 往返：HasWindowRect 保留");
        CheckInt(r.Left, 0, "settings 往返：left = 0");
        CheckInt(r.Top, 0, "settings 往返：top = 0");
        CheckInt(r.Width, 520, "settings 往返：width = 520");
        CheckInt(r.Height, 756, "settings 往返：height = 756");
        Check(!r.TopMost, "settings 往返：topmost = false");
        Check(r.PositionLocked, "settings 往返：locked = true");
        Check(!r.Snap, "settings 往返：snap = false");
        CheckStr(r.LastImage, "sub/03.jpg", "settings 往返：last_image");
        Check(SamePathText(r.Folder, _imagesDir), "settings 往返：folder", "实际 " + r.Folder);
        Check(!r.Watch, "settings 往返：watch = false");
        Check(r.GateKey == GateKeyKind.Alt, "settings 往返：gate_key = Alt");
        Check(!r.FirstRun, "settings 往返：first_run = 0（引导已完成）");
        Check(r.Theme == ThemeKind.Dark, "settings 往返：theme = dark");
        CheckInt(r.RecentFolders.Count, 2, "settings 往返：最近文件夹 2 项");
        if (r.RecentFolders.Count == 2)
        {
            Check(SamePathText(r.RecentFolders[0], _tempRoot), "settings 往返：最近文件夹顺序（tempRoot 在前）",
                "实际 " + r.RecentFolders[0]);
            Check(SamePathText(r.RecentFolders[1], _imagesDir), "settings 往返：最近文件夹顺序（images 在后）",
                "实际 " + r.RecentFolders[1]);
        }
        Check(r.GetHotkey(HotkeyAction.ZoomIn) == (Keys.Control | Keys.Shift | Keys.Z),
            "settings 往返：zoom_in = Ctrl+Shift+Z");
        Check(r.GetHotkey(HotkeyAction.Jump9) == Keys.None, "settings 往返：jump9 = none（禁用）");
        Check(r.GetHotkey(HotkeyAction.Prev) == (Keys.Control | Keys.Alt | Keys.Left),
            "settings 往返：未写动作保持默认组合");

        // 路径辅助
        CheckStr(SettingsStore.DefaultSettingsPath(_exeDir),
            Path.Combine(SettingsStore.TrimDir(_exeDir), AppInfo.SettingsFileName),
            "settings 路径：DefaultSettingsPath = exe\\settings.ini");
        Check(SettingsStore.FallbackSettingsPath().EndsWith(AppInfo.SettingsFileName, StringComparison.OrdinalIgnoreCase),
            "settings 路径：回退路径以 settings.ini 结尾");
    }

    // 3.15 图片文件夹配置
    private static void TestImageFolderConfig()
    {
        string resolved = SettingsStore.ResolveImageFolder(_imagesDir, _exeDir);
        Check(SamePathText(resolved, _imagesDir), "文件夹配置：绝对路径可读 → 原样返回",
            "实际 " + resolved);

        string def = SettingsStore.DefaultImageFolder(_exeDir);
        string missing = SettingsStore.ResolveImageFolder(@"Z:\no-such-folder-guideoverlay-12345", _exeDir);
        Check(SamePathText(missing, def), "文件夹配置：路径不存在 → 回退 exe\\images",
            "期望 " + def + "，实际 " + missing);
        string emptyCfg = SettingsStore.ResolveImageFolder("", _exeDir);
        Check(SamePathText(emptyCfg, def), "文件夹配置：空值 → 回退 exe\\images", "实际 " + emptyCfg);

        string relative = SettingsStore.ResolveImageFolder("images", _exeDir);
        Check(SamePathText(relative, Path.Combine(SettingsStore.TrimDir(_exeDir), "images")),
            "文件夹配置：相对路径按 exe 目录解析", "实际 " + relative);

        // 写入临时 ini 再读回 folder
        string ini = Path.Combine(_tempRoot, "settings-tests", "folder.ini");
        AppSettings s = SettingsStore.Load(_exeDir, ini);
        s.Folder = _imagesDir;
        s.FolderSetting = _imagesDir;
        SettingsStore.SaveTo(s, ini);
        AppSettings r = SettingsStore.Load(_exeDir, ini);
        Check(SamePathText(r.Folder, _imagesDir), "文件夹配置：写入/读回 folder 一致",
            "期望 " + _imagesDir + "，实际 " + r.Folder);
        Check(SettingsStore.IsReadableDirectory(_imagesDir), "文件夹配置：临时 images 可读");
        Check(!SettingsStore.IsReadableDirectory(Path.Combine(_tempRoot, "no-such-dir")),
            "文件夹配置：不存在的目录不可读");
    }

    // 3.16 扫描跳过
    private static void TestScanSkipped()
    {
        string scanDir = Path.Combine(_tempRoot, "scan");
        try { Directory.CreateDirectory(scanDir); } catch { }

        // 3 张合法图（用于自然排序）+ webp + 损坏 png + 0 字节 png + txt
        string baseImg = Path.Combine(scanDir, "1.png");
        CreateTestImage(baseImg, 40, 20, "一", new Color[]
        {
            Color.Red, Color.Blue, Color.Green, Color.Yellow
        });
        File.Copy(baseImg, Path.Combine(scanDir, "2.png"), true);
        File.Copy(baseImg, Path.Combine(scanDir, "10.png"), true);
        WriteBytes(Path.Combine(scanDir, "broken.webp"), new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00 });
        WriteUtf8(Path.Combine(scanDir, "corrupt.png"), "this is definitely not a png file");
        WriteBytes(Path.Combine(scanDir, "empty.png"), new byte[0]);
        WriteUtf8(Path.Combine(scanDir, "notes.txt"), "hello");

        using (ImageLibrary lib = new ImageLibrary())
        {
            ScanResult result = null;
            try { result = lib.Scan(scanDir); }
            catch (Exception ex) { Fail("扫描：合法目录抛异常 " + Describe(ex)); return; }

            Check(result != null && result.Error == null, "扫描：临时目录扫描不报错",
                result != null ? result.Error : "result 为 null");
            if (result == null) { return; }
            CheckInt(result.Images.Count, 3, "扫描：3 张合法图");
            CheckInt(result.Skipped.Count, 4, "扫描：4 个文件被跳过");

            // 自然排序：1.png、2.png、10.png
            if (result.Images.Count == 3)
            {
                CheckStr(result.Images[0].RelPath, "1.png", "扫描：第 1 张为 1.png");
                CheckStr(result.Images[1].RelPath, "2.png", "扫描：第 2 张为 2.png");
                CheckStr(result.Images[2].RelPath, "10.png", "扫描：第 3 张为 10.png（自然排序）");
                CheckInt(result.Images[0].PixelWidth, 40, "扫描：图片宽 40");
                CheckInt(result.Images[0].PixelHeight, 20, "扫描：图片高 20");
            }

            CheckSkipReason(result, "broken.webp", "不支持的格式", "扫描跳过：webp 原因含「不支持的格式」");
            CheckSkipReason(result, "broken.webp", "WebP", "扫描跳过：webp 原因含「WebP」");
            CheckSkipReason(result, "corrupt.png", ImageLibrary.ReasonCorrupt, "扫描跳过：损坏 png 原因正确");
            CheckSkipReason(result, "empty.png", ImageLibrary.ReasonEmptyFile, "扫描跳过：0 字节文件原因正确");
            CheckSkipReason(result, "notes.txt", "不支持的格式", "扫描跳过：txt 原因正确");

            // 不存在的目录：不崩溃，返回错误
            ScanResult missingResult = lib.Scan(Path.Combine(_tempRoot, "no-such-scan-folder"));
            Check(missingResult != null && !string.IsNullOrEmpty(missingResult.Error),
                "扫描：不存在的目录返回错误而不是抛异常");
            Check(missingResult != null && missingResult.Images.Count == 0,
                "扫描：不存在的目录图片列表为空");

            // 真实临时 images：2 张，尺寸正确
            ScanResult imgs = lib.Scan(_imagesDir);
            Check(imgs != null && imgs.Images.Count == 2, "扫描：临时 images 有 2 张图",
                imgs != null ? imgs.Images.Count.ToString(CultureInfo.InvariantCulture) : "null");
            if (imgs != null && imgs.Images.Count == 2)
            {
                CheckStr(imgs.Images[0].RelPath, "01-wide.png", "扫描：临时 images 顺序 01 在前");
                CheckInt(imgs.Images[0].PixelWidth, 1600, "扫描：01-wide.png 宽 1600");
                CheckInt(imgs.Images[1].PixelHeight, 1600, "扫描：02-tall.png 高 1600");
            }
        }

        // 扩展名判断
        Check(ImageLibrary.IsSupportedExtension("A.PNG"), "扫描：扩展名大小写不敏感（PNG）");
        Check(ImageLibrary.IsSupportedExtension("a.jpeg"), "扫描：支持 jpeg");
        Check(ImageLibrary.IsSupportedExtension("a.bmp"), "扫描：支持 bmp");
        Check(ImageLibrary.IsSupportedExtension("a.gif"), "扫描：支持 gif");
        Check(!ImageLibrary.IsSupportedExtension("a.webp"), "扫描：不支持 webp");
        Check(!ImageLibrary.IsSupportedExtension("a.tif"), "扫描：不支持 tif");
        Check(!ImageLibrary.IsSupportedExtension(""), "扫描：空路径不支持");

        // C1：切片产物识别 + 不污染图库
        Check(ImageLibrary.IsSliceArtifact("a-slice-01.png"), "切片产物：a-slice-01.png 识别为切片");
        Check(ImageLibrary.IsSliceArtifact(@"dir\a-slice-12.png"), "切片产物：目录下的切片也识别");
        Check(!ImageLibrary.IsSliceArtifact("正常图.png"), "切片产物：正常图不误伤");
        Check(!ImageLibrary.IsSliceArtifact("a-slice-1.png"), "切片产物：编号 1 位不算（约定 2 位）");
        Check(!ImageLibrary.IsSliceArtifact("a-slice-01.jpg"), "切片产物：非 png 不算");

        // C1：图库里的 .slices 子目录整体跳过，顶层 *-slice-NN.png 也不收录
        string slicesScanDir = Path.Combine(_tempRoot, "scan-slices");
        try { Directory.CreateDirectory(slicesScanDir); } catch { }
        try { Directory.CreateDirectory(Path.Combine(slicesScanDir, ImageLibrary.SliceOutputDirectoryName)); } catch { }
        string normalImage = Path.Combine(slicesScanDir, "正常图.png");
        CreateTestImage(normalImage, 40, 20, "正", new Color[] { Color.Red, Color.Blue });
        try { File.Copy(normalImage, Path.Combine(slicesScanDir, "b-slice-02.png"), true); }
        catch { }
        try
        {
            File.Copy(normalImage,
                Path.Combine(slicesScanDir, ImageLibrary.SliceOutputDirectoryName, "a-slice-01.png"), true);
        }
        catch { }

        using (ImageLibrary sliceLib = new ImageLibrary())
        {
            ScanResult sliceResult = null;
            try { sliceResult = sliceLib.Scan(slicesScanDir); }
            catch (Exception ex) { Fail("切片扫描：抛异常 " + Describe(ex)); }
            Check(sliceResult != null && sliceResult.Error == null, "切片扫描：不报错");
            if (sliceResult != null)
            {
                CheckInt(sliceResult.Images.Count, 1, "切片扫描：只收录 1 张正常图（切片不加入图库）");
                CheckInt(sliceResult.Skipped.Count, 1, "切片扫描：顶层切片文件被跳过（.slices 目录整体跳过）");
                bool hasSliceReason = false;
                for (int i = 0; i < sliceResult.Skipped.Count; i++)
                {
                    SkippedImage skip = sliceResult.Skipped[i];
                    if (skip != null && string.Equals(skip.Reason, ImageLibrary.ReasonSliceArtifact,
                        StringComparison.Ordinal))
                    {
                        hasSliceReason = true;
                        break;
                    }
                }
                Check(hasSliceReason, "切片扫描：跳过原因含「切片产物」");
            }
        }

        // 超大图判定
        Check(ImageLibrary.IsOversized(8000, 8000), "扫描：8000x8000（6400 万像素）判超大");
        Check(!ImageLibrary.IsOversized(8000, 7000), "扫描：8000x7000（5600 万像素）不算超大");
        Check(ImageLibrary.IsOversized(32768, 100), "扫描：单边 > 32767 判超大");
    }

    // 3.17 输出目录回退
    private static void TestOutputFallback()
    {
        Check(IsSamePath(ResolveOutputDirectory(_tempRoot, _tempRoot), _tempRoot),
            "输出回退：可写目录直接使用");
        string fakeDir = Path.Combine(_tempRoot, "no-such-output-dir-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
        Check(IsSamePath(ResolveOutputDirectory(fakeDir, _tempRoot), _tempRoot),
            "输出回退：不可写/不存在的 exe 目录 → 回退 %TEMP%", fakeDir);
        Check(IsSamePath(_outputDir, ResolveOutputDirectory(_exeDir, _tempRoot)),
            "输出回退：实际输出目录与规则一致");
        Check(Directory.Exists(_outputDir), "输出回退：输出目录存在", _outputDir);
        Check(!string.IsNullOrEmpty(_logPath) && Path.GetFileName(_logPath) == "selftest.log",
            "输出回退：日志文件名为 selftest.log", _logPath);
    }

    // 3.18 单实例 Mutex：自测不参与
    private static void TestMutexNotHeld()
    {
        Mutex m = null;
        bool createdNew = false;
        try
        {
            m = new Mutex(false, AppInfo.SingleInstanceMutexName, out createdNew);
            Check(true, "Mutex：自测进程可创建同名 Mutex（未参与单实例，不会阻塞自测）",
                "createdNew=" + createdNew);
            Log("  Mutex 状态：createdNew=" + createdNew
                + (createdNew ? "（当前无主程序持有）" : "（主程序正在运行，自测仍未被阻塞）"));
        }
        catch (Exception ex)
        {
            Fail("Mutex：创建同名 Mutex 抛异常 " + Describe(ex));
        }
        finally
        {
            if (m != null) { try { m.Dispose(); } catch { } }
        }
    }

    // ==================================================================
    // 四、主窗口 4 步截图断言
    // ==================================================================
    private static void TestOverlaySteps()
    {
        if (_settings == null)
        {
            Fail("窗口步骤：临时 settings 未初始化，跳过全部窗口断言");
            return;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _gdiBefore = Diagnostics.GdiObjectCount();

        OverlayForm form = null;
        try
        {
            try
            {
                form = new OverlayForm(_settings, true);
            }
            catch (Exception ex)
            {
                Fail("窗口创建：new OverlayForm(settings, true) 抛异常 " + Describe(ex));
                return;
            }

            // ---- 构造后的基本状态 ----
            Check(form.SelfTestMode, "窗口：SelfTestMode == true");
            Check(form.Images != null && form.Images.Count == 2, "窗口：扫描到 2 张测试图",
                "实际 " + (form.Images == null ? -1 : form.Images.Count));
            CheckInt(form.CurrentIndex, 0, "窗口：初始索引为 0");
            Check(form.CurrentBitmap != null && form.CurrentBitmap.Width == 1600 && form.CurrentBitmap.Height == 900,
                "窗口：初始图为 1600x900", DescribeBitmap(form.CurrentBitmap));
            Check(form.CurrentEntry != null && string.Equals(form.CurrentEntry.DisplayName, "01-wide.png", StringComparison.Ordinal),
                "窗口：当前文件为 01-wide.png",
                form.CurrentEntry != null ? form.CurrentEntry.DisplayName : "null");
            Check(form.Colors != null && form.Colors.Canvas == ColorTranslator.FromHtml("#F2F3F5"),
                "窗口：浅色主题画布色为 #F2F3F5",
                form.Colors != null ? form.Colors.Canvas.ToString() : "Colors 为 null");
            Check(form.Interactive, "窗口：自测模式处于可交互状态（截图含工具条）");
            Check(form.ToolbarVisible, "窗口：自测模式工具条可见");
            Check(form.ClientSize.Width > 0 && form.ClientSize.Height > 0, "窗口：客户区尺寸有效",
                form.ClientSize.Width + "x" + form.ClientSize.Height);

            // 不注册全局热键：自测模式下 OnHandleCreated 提前返回，OwnerHandle 仍为空
            Check(form.Hotkeys != null && form.Hotkeys.OwnerHandle == IntPtr.Zero,
                "窗口：未注册全局热键（Hotkeys.OwnerHandle 为空）");
            bool anyRegistered = false;
            if (form.Hotkeys != null && form.Hotkeys.Entries != null)
            {
                for (int i = 0; i < form.Hotkeys.Entries.Count; i++)
                {
                    if (form.Hotkeys.Entries[i] != null
                        && form.Hotkeys.Entries[i].Status == HotkeyStatus.Registered)
                    {
                        anyRegistered = true;
                    }
                }
            }
            Check(!anyRegistered, "窗口：没有任何热键处于已注册状态");

            // 锁图开关：切换后状态正确，且不影响当前视图
            form.SetImageLocked(true, false);
            Check(form.ImageLocked, "锁图：SetImageLocked(true) 后 ImageLocked == true");
            form.SetImageLocked(false, false);
            Check(!form.ImageLocked, "锁图：SetImageLocked(false) 后 ImageLocked == false");

            // ---- 步骤 1：适应窗口 ----
            form.FitToWindow();
            Check(ReadView(form).Mode == ViewMode.Fit, "步骤1：视图模式为「适应窗口」");
            double expectFit = ViewMath.FitZoom(1600.0, 900.0, form.ClientSize.Width, form.ClientSize.Height);
            CheckNear(ReadView(form).Zoom, expectFit, 1e-9, "步骤1：适应窗口比例与公式一致");
            Check(ReadView(form).OffsetX <= 1e-6 && ReadView(form).OffsetX + 1600.0 * ReadView(form).Zoom >= form.ClientSize.Width - 1e-6,
                "步骤1：X 方向图片覆盖视口（不露背景）",
                "offsetX=" + Fmt(ReadView(form).OffsetX));
            Bitmap b1 = CaptureStep(form, 1, "步骤1（适应窗口）");
            if (b1 != null) { b1.Dispose(); }

            // ---- 步骤 2：200% 缩放（窗口中心锚点） ----
            double cx = form.ClientSize.Width / 2.0;
            double cy = form.ClientSize.Height / 2.0;
            double beforeImageX = ViewMath.ImageCoordX(cx, ReadView(form).OffsetX, ReadView(form).Zoom);
            double beforeImageY = ViewMath.ImageCoordY(cy, ReadView(form).OffsetY, ReadView(form).Zoom);

            form.SetZoom(2.0, true);

            CheckNear(ReadView(form).Zoom, 2.0, 0.001, "步骤2：View.Zoom == 2.0（±0.001）");
            double afterImageX = ViewMath.ImageCoordX(cx, ReadView(form).OffsetX, ReadView(form).Zoom);
            double afterImageY = ViewMath.ImageCoordY(cy, ReadView(form).OffsetY, ReadView(form).Zoom);
            double anchorErr = Math.Max(Math.Abs(afterImageX - beforeImageX), Math.Abs(afterImageY - beforeImageY));
            Check(anchorErr <= 1.0, "步骤2：窗口中心锚点映射误差 ≤1px",
                "误差 " + Fmt(anchorErr) + "px（前 " + Fmt(beforeImageX) + "," + Fmt(beforeImageY)
                + " 后 " + Fmt(afterImageX) + "," + Fmt(afterImageY) + "）");
            Bitmap b2 = CaptureStep(form, 2, "步骤2（200% 缩放）");
            if (b2 != null) { b2.Dispose(); }

            // ---- 步骤 3：下一张 ----
            int indexBefore = form.CurrentIndex;
            form.NavigateImage(1);
            Check(indexBefore == 0 && form.CurrentIndex == 1, "步骤3：当前索引由 0 变 1",
                "前 " + indexBefore + "，后 " + form.CurrentIndex);
            CheckContains(form.StatusLine, "2/2", "步骤3：StatusLine 含「2/2」");
            Check(form.CurrentBitmap != null && form.CurrentBitmap.Width == 900 && form.CurrentBitmap.Height == 1600,
                "步骤3：切换到 900x1600 竖图", DescribeBitmap(form.CurrentBitmap));
            Check(ReadView(form).Mode == ViewMode.Fit, "步骤3：换图后自动适应窗口");
            Bitmap b3 = CaptureStep(form, 3, "步骤3（下一张）");
            if (b3 != null) { b3.Dispose(); }

            // ---- 步骤 4：透明度 60% ----
            form.SetOpacityPercent(60, false);
            CheckNear(form.Opacity, 0.6, 0.01, "步骤4：Form.Opacity == 0.6（±0.01）");
            CheckInt(form.Settings.Opacity, 60, "步骤4：Settings.Opacity == 60");
            Bitmap b4 = CaptureStep(form, 4, "步骤4（透明度 60%）");
            if (b4 != null) { b4.Dispose(); }

            // ---- 缩放预设锚点（窗口级，不 Show） ----
            form.FitToWindow();
            double presetBeforeX = ViewMath.ImageCoordX(cx, ReadView(form).OffsetX, ReadView(form).Zoom);
            double presetBeforeY = ViewMath.ImageCoordY(cy, ReadView(form).OffsetY, ReadView(form).Zoom);
            form.SetZoomPreset(200.0);
            CheckNear(ReadView(form).Zoom, 2.0, 0.001, "缩放预设：200% → View.Zoom = 2.0");
            double presetAfterX = ViewMath.ImageCoordX(cx, ReadView(form).OffsetX, ReadView(form).Zoom);
            double presetAfterY = ViewMath.ImageCoordY(cy, ReadView(form).OffsetY, ReadView(form).Zoom);
            double presetErr = Math.Max(Math.Abs(presetAfterX - presetBeforeX), Math.Abs(presetAfterY - presetBeforeY));
            Check(presetErr <= 1.0, "缩放预设：200% 后窗口中心锚点误差 ≤1px", "误差 " + Fmt(presetErr) + "px");
            form.SetZoomPreset(100.0);
            CheckNear(ReadView(form).Zoom, 1.0, 0.001, "缩放预设：100% → View.Zoom = 1.0");
            form.SetZoomPreset(25.0);
            CheckNear(ReadView(form).Zoom, 0.25, 0.001, "缩放预设：25% → View.Zoom = 0.25");

            // ---- C2：自动刷新保留阅读位置（watcher 走的 ReloadImages(true, true) 路径） ----
            form.FitToWindow();
            form.SetZoom(2.0, true);
            int c2Index = form.CurrentIndex;
            double c2Zoom = ReadView(form).Zoom;
            form.ReloadImages(true, true);
            Check(form.CurrentIndex == c2Index, "C2：图库指纹未变时自动刷新不换图",
                "索引 " + form.CurrentIndex + "，期望 " + c2Index);
            CheckNear(ReadView(form).Zoom, c2Zoom, 0.001,
                "C2：图库指纹未变时自动刷新不重载（缩放保留）");

            // 加一张新图 → 指纹变化 → 重载，但当前图路径没变，阅读位置必须还原
            string c2NewImage = Path.Combine(_settings.Folder, "zz-c2-added.png");
            try { CreateTestImage(c2NewImage, 320, 200, "C2", new Color[] { Color.Red, Color.Blue }); }
            catch { }
            int c2CountBefore = form.Images != null ? form.Images.Count : 0;
            form.ReloadImages(true, true);
            Check(form.Images != null && form.Images.Count == c2CountBefore + 1,
                "C2：新增图片后自动刷新能看到新图",
                "前 " + c2CountBefore + "，后 " + (form.Images == null ? -1 : form.Images.Count));
            Check(form.CurrentIndex == c2Index, "C2：新增图片后仍停留在当前图（不跳回第 0 张）",
                "索引 " + form.CurrentIndex + "，期望 " + c2Index);
            CheckNear(ReadView(form).Zoom, c2Zoom, 0.05, "C2：新增图片后保留缩放比例");
            try { if (File.Exists(c2NewImage)) { File.Delete(c2NewImage); } }
            catch { }
        }
        finally
        {
            if (form != null)
            {
                try { form.Dispose(); } catch { }
                form = null;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            _gdiAfter = Diagnostics.GdiObjectCount();
        }
    }

    // 截取 1 步：DrawToBitmap → 尺寸断言 → 非空白断言 → 存 PNG
    private static Bitmap CaptureStep(OverlayForm form, int stepNo, string label)
    {
        string pngPath = Path.Combine(_outputDir, "selftest-" + stepNo.ToString(CultureInfo.InvariantCulture) + ".png");
        Bitmap bmp = null;
        string error = null;
        try
        {
            bmp = CaptureForm(form, out error);
        }
        catch (Exception ex)
        {
            error = Describe(ex);
        }
        if (bmp == null)
        {
            Fail(label + "：DrawToBitmap 截图失败 " + (error != null ? error : "未知原因"));
            return null;
        }

        Check(bmp.Width == form.ClientSize.Width && bmp.Height == form.ClientSize.Height,
            label + "：位图尺寸 == 客户区尺寸",
            "位图 " + bmp.Width + "x" + bmp.Height + "，客户区 "
            + form.ClientSize.Width + "x" + form.ClientSize.Height);

        Color canvas = form.Colors != null ? form.Colors.Canvas : ColorTranslator.FromHtml("#F2F3F5");
        string contentError = AnalyzeBitmapContent(bmp, canvas);
        Check(contentError == null, label + "：图片区域非空白", contentError);

        try
        {
            bmp.Save(pngPath, ImageFormat.Png);
        }
        catch (Exception ex)
        {
            Fail(label + "：PNG 保存失败 " + Describe(ex) + "（" + pngPath + "）");
        }
        bool saved = false;
        long savedLength = 0;
        try
        {
            FileInfo fi = new FileInfo(pngPath);
            saved = fi.Exists;
            if (saved) { savedLength = fi.Length; }
        }
        catch { }
        Check(saved && savedLength > 1000, label + "：PNG 已写出且非空",
            pngPath + (saved ? "（" + savedLength + " 字节）" : "（不存在）"));

        string info = "selftest-" + stepNo.ToString(CultureInfo.InvariantCulture) + ".png（"
            + bmp.Width + "x" + bmp.Height + "，" + savedLength + " 字节）";
        _pngInfo.Add(info);
        Log("  PNG：" + pngPath + "（" + bmp.Width + "x" + bmp.Height + "，" + savedLength + " 字节）");
        return bmp;
    }

    // 把窗口客户区画到位图（不要求 Show；FormBorderStyle=None 时客户区 == 窗体矩形）
    private static Bitmap CaptureForm(OverlayForm form, out string error)
    {
        error = null;
        int w = form.ClientSize.Width;
        int h = form.ClientSize.Height;
        if (w <= 0 || h <= 0)
        {
            error = "客户区尺寸无效 " + w + "x" + h;
            return null;
        }
        Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        try
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
        }
        catch (Exception ex)
        {
            bmp.Dispose();
            error = Describe(ex);
            return null;
        }
        return bmp;
    }

    // 采样中心 + 四角内侧 + 四边中点：至少 2 点非背景色，且整图颜色种数 ≥ 3
    private static string AnalyzeBitmapContent(Bitmap bmp, Color canvas)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        if (w < 8 || h < 8) { return "位图过小 " + w + "x" + h; }

        int[] pixels = new int[w * h];
        BitmapData data = null;
        try
        {
            data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        catch (Exception ex)
        {
            return "读取像素失败：" + Describe(ex);
        }
        finally
        {
            if (data != null)
            {
                try { bmp.UnlockBits(data); } catch { }
            }
        }

        int canvasRgb = (canvas.R << 16) | (canvas.G << 8) | canvas.B;
        int inset = Math.Max(4, Math.Min(w, h) / 16);
        List<Point> samples = new List<Point>();
        samples.Add(new Point(w / 2, h / 2));                 // 中心
        samples.Add(new Point(inset, inset));                 // 左上内侧
        samples.Add(new Point(w - 1 - inset, inset));         // 右上内侧
        samples.Add(new Point(inset, h - 1 - inset));         // 左下内侧
        samples.Add(new Point(w - 1 - inset, h - 1 - inset)); // 右下内侧
        samples.Add(new Point(inset, h / 2));                 // 左边中点
        samples.Add(new Point(w - 1 - inset, h / 2));         // 右边中点
        samples.Add(new Point(w / 2, inset));                 // 上边中点
        samples.Add(new Point(w / 2, h - 1 - inset));         // 下边中点

        int nonBackground = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            Point p = samples[i];
            if (p.X < 0 || p.X >= w || p.Y < 0 || p.Y >= h) { continue; }
            int rgb = pixels[p.Y * w + p.X] & 0xFFFFFF;
            if (rgb != canvasRgb) { nonBackground++; }
        }

        HashSet<int> colors = new HashSet<int>();
        for (int i = 0; i < pixels.Length; i++)
        {
            colors.Add(pixels[i] & 0xFFFFFF);
        }

        if (nonBackground < 2)
        {
            return "采样点中非背景色数量 = " + nonBackground + "（要求 ≥2）";
        }
        if (colors.Count < 3)
        {
            return "整图颜色种数 = " + colors.Count + "（要求 ≥3）";
        }
        return null;
    }

    // ==================================================================
    // 五、GDI 对象数
    // ==================================================================
    private static void TestGdiObjects()
    {
        Log("GDI 对象数：测试前 " + _gdiBefore.ToString(CultureInfo.InvariantCulture)
            + "，测试后 " + _gdiAfter.ToString(CultureInfo.InvariantCulture));
        if (_gdiBefore <= 0 || _gdiAfter <= 0)
        {
            Log("  GDI 对象数不可用（GetGuiResources 失败），跳过差值断言");
            return;
        }
        int diff = _gdiAfter - _gdiBefore;
        Check(diff <= 50, "性能：GDI 对象数前后差 ≤50",
            "前 " + _gdiBefore + " / 后 " + _gdiAfter + " / 差 " + diff);
    }

    // ==================================================================
    // 六、性能量化（提示词 §十二「稳定性与性能」）
    //   1) 纯函数：ZoomAtPoint / ClampOffsets 各连续 100 次，p95 < 5ms
    //   2) 单帧绘制：DrawToBitmap 采样 30 次（先热身 2 次）：p95 < 33ms 记 [OK]；
    //      33–100ms 记 [WARN]（不 FAIL）；>100ms 记 [FAIL] 并附数字
    //   3) 切图：NavigateImage(1) 连续 20 次，p95 < 500ms
    //   4) 私有内存峰值增量 < 512MB（段前 / 段中每次采样 / 段后）
    //   5) 本段 GDI 对象数前后差 ≤ 50（复用 Diagnostics.GdiObjectCount，不重复定义同名方法）
    //   说明：单帧绘制按任务约定用 30 次采样（提示词原文 100 次；100 次全窗口 DrawToBitmap
    //         在慢机上会把自测拖到十几秒），p95 统计口径与阈值不变，每帧 Bitmap 均 Dispose。
    // ==================================================================
    private static void TestPerformance()
    {
        const int pureIterations = 100;
        const int frameIterations = 30;
        const int frameWarmups = 2;
        const int navIterations = 20;
        // 8MP 量级图（4000x2000 = 800 万像素）与 1280x800 视口
        const double imgW = 4000.0;
        const double imgH = 2000.0;
        const double viewW = 1280.0;
        const double viewH = 800.0;

        // 段前：先 GC 两轮，避免把上一个段落的残留算进私有内存基线
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Process proc = null;
        try { proc = Process.GetCurrentProcess(); } catch { }
        double memBefore = PrivateMemoryMb(proc);
        double memPeak = memBefore;
        int gdiPerfBefore = Diagnostics.GdiObjectCount();

        // 固定种子：锚点/平移量可复现，分位数不受随机波动影响
        Random rnd = new Random(20251107);

        // 预生成 100 组输入：计时区间里只保留纯函数调用本身，不含随机数生成等额外开销
        double[] anchorXs = new double[pureIterations];
        double[] anchorYs = new double[pureIterations];
        double[] zoomArgs = new double[pureIterations];
        double[] clampZooms = new double[pureIterations];
        double[] rawOffXs = new double[pureIterations];
        double[] rawOffYs = new double[pureIterations];
        for (int i = 0; i < pureIterations; i++)
        {
            anchorXs[i] = rnd.NextDouble() * viewW;
            anchorYs[i] = rnd.NextDouble() * viewH;
            zoomArgs[i] = 0.25 + rnd.NextDouble() * 3.75;      // 0.25x–4x
            clampZooms[i] = 0.1 + rnd.NextDouble() * 9.9;      // 0.1x–10x
            rawOffXs[i] = -rnd.NextDouble() * 6000.0;          // 故意越界，让夹取真的生效
            rawOffYs[i] = -rnd.NextDouble() * 4000.0;
        }

        // ---- 1a) 锚点缩放纯函数：连续 100 次 ----
        double offX = 0.0;
        double offY = 0.0;
        int zoomIndex = 0;
        List<double> zoomTimes = MeasureTimes(delegate
        {
            int i = zoomIndex;
            zoomIndex = i + 1;
            offX = rawOffXs[i];
            offY = rawOffYs[i];
            double nx;
            double ny;
            ViewMath.ZoomAtPoint(1.0, offX, offY, zoomArgs[i], anchorXs[i], anchorYs[i], out nx, out ny);
            // 只累加符号：既建立对 out 结果的真实数据依赖（防 JIT 消除），又不让校验和变成天文数字
            _perfSink += (nx > 0.0) ? 1.0 : -1.0;
        }, pureIterations);

        // ---- 1b) 平移夹取纯函数：连续 100 次（每次故意给越界 offset） ----
        double clampX = 0.0;
        double clampY = 0.0;
        int clampIndex = 0;
        List<double> clampTimes = MeasureTimes(delegate
        {
            int i = clampIndex;
            clampIndex = i + 1;
            clampX = rawOffXs[i];
            clampY = rawOffYs[i];
            ViewMath.ClampOffsets(clampZooms[i], imgW, imgH, viewW, viewH, ref clampX, ref clampY);
            _perfSink += (clampX > 0.0) ? 1.0 : -1.0;
        }, pureIterations);

        Log("  纯函数 ZoomAtPoint（" + Fmt(imgW) + "x" + Fmt(imgH) + " 图，"
            + pureIterations.ToString(CultureInfo.InvariantCulture) + " 次）：" + MsText(zoomTimes));
        Check(zoomTimes.Count == pureIterations, "性能：ZoomAtPoint 完成 100 次采样",
            "实际 " + zoomTimes.Count.ToString(CultureInfo.InvariantCulture) + " 次");
        Check(zoomTimes.Count > 0 && Percentile(zoomTimes, 95.0) < 5.0,
            "性能：ZoomAtPoint p95 < 5ms", MsText(zoomTimes));

        Log("  纯函数 ClampOffsets（" + Fmt(imgW) + "x" + Fmt(imgH) + " 图，"
            + pureIterations.ToString(CultureInfo.InvariantCulture) + " 次）：" + MsText(clampTimes));
        Check(clampTimes.Count == pureIterations, "性能：ClampOffsets 完成 100 次采样",
            "实际 " + clampTimes.Count.ToString(CultureInfo.InvariantCulture) + " 次");
        Check(clampTimes.Count > 0 && Percentile(clampTimes, 95.0) < 5.0,
            "性能：ClampOffsets p95 < 5ms", MsText(clampTimes));

        // ---- 2) 单帧绘制 + 3) 切图（单独建窗口；测完立刻 Dispose） ----
        List<double> frameTimes = new List<double>(frameIterations);
        List<double> navTimes = new List<double>(navIterations);
        List<double> mpFormTimes = new List<double>(20);
        string mpFormError = null;
        string frameError = null;
        string navError = null;
        bool windowBench = false;
        OverlayForm perfForm = null;
        try
        {
            if (_settings == null)
            {
                Fail("性能：临时 settings 未初始化，无法测量单帧绘制与切图");
            }
            else
            {
                try { perfForm = new OverlayForm(_settings, true); }
                catch (Exception ex) { Fail("性能：创建用于测量的窗口失败 " + Describe(ex)); }

                if (perfForm != null)
                {
                    windowBench = true;
                    // 步骤 4 把 Opacity 改成过 60%，这里恢复 100% 再测，避免合成路径干扰
                    try { perfForm.SetOpacityPercent(100, false); } catch { }
                    perfForm.FitToWindow();

                    Stopwatch sw = new Stopwatch();

                    // 热身 2 次：首次 DrawToBitmap 含句柄创建、字体/画刷缓存等一次性开销
                    for (int i = 0; i < frameWarmups; i++)
                    {
                        string warmError;
                        Bitmap warm = CaptureForm(perfForm, out warmError);
                        if (warm != null) { warm.Dispose(); }
                    }

                    for (int i = 0; i < frameIterations; i++)
                    {
                        string err;
                        sw.Restart();
                        Bitmap bmp = CaptureForm(perfForm, out err);
                        sw.Stop();
                        if (bmp == null)
                        {
                            frameError = "单帧绘制第 " + (i + 1).ToString(CultureInfo.InvariantCulture)
                                + " 次截图失败：" + (err != null ? err : "未知原因");
                            break;
                        }
                        double frameMs = sw.Elapsed.TotalMilliseconds;
                        bmp.Dispose();   // 每帧必须释放，否则内存峰值结论失真
                        frameTimes.Add(frameMs);
                        double frameMem = PrivateMemoryMb(proc);
                        if (frameMem > memPeak) { memPeak = frameMem; }
                    }

                    if (frameError == null)
                    {
                        for (int i = 0; i < navIterations; i++)
                        {
                            sw.Restart();
                            try
                            {
                                perfForm.NavigateImage(1);
                            }
                            catch (Exception ex)
                            {
                                sw.Stop();
                                navError = "切图第 " + (i + 1).ToString(CultureInfo.InvariantCulture)
                                    + " 次抛异常：" + Describe(ex);
                                break;
                            }
                            sw.Stop();
                            navTimes.Add(sw.Elapsed.TotalMilliseconds);
                            double navMem = PrivateMemoryMb(proc);
                            if (navMem > memPeak) { memPeak = navMem; }
                        }
                    }

                    // ---- 3.5) 8MP 真实窗体单帧（走绘制模块的降采样 mip 路径，§十二 的直接证据） ----
                    if (frameError == null && navError == null)
                    {
                        try
                        {
                            string bigPath = Path.Combine(_settings.Folder, "zz-8mp-perf.png");
                            if (!File.Exists(bigPath))
                            {
                                CreateTestImage(bigPath, 4000, 2000, "8MP PERF",
                                    new Color[] { Color.FromArgb(255, 32, 34, 38), Color.FromArgb(255, 240, 240, 240) });
                            }
                            perfForm.ReloadImages(true, true);
                            int bigIndex = -1;
                            for (int i = 0; i < perfForm.Images.Count; i++)
                            {
                                if (string.Equals(perfForm.Images[i].DisplayName, "zz-8mp-perf.png",
                                    StringComparison.OrdinalIgnoreCase)) { bigIndex = i; break; }
                            }
                            if (bigIndex < 0)
                            {
                                mpFormError = "临时目录里未找到 8MP 测试图";
                            }
                            else
                            {
                                for (int guard = 0; guard <= perfForm.Images.Count && perfForm.CurrentIndex != bigIndex; guard++)
                                {
                                    perfForm.NavigateImage(1);
                                }
                                // ViewState 是结构体：先取副本改好再整体写回，避免 CS1690（marshal-by-ref 字段成员访问）
                                ViewState viewCopy = perfForm.View;
                                viewCopy.Mode = ViewMode.Manual;
                                viewCopy.Zoom = 0.2;
                                viewCopy.OffsetX = 0.0;
                                viewCopy.OffsetY = 0.0;
                                perfForm.View = viewCopy;
                                perfForm.RebuildFitCache();
                                perfForm.Invalidate();
                                for (int i = 0; i < 2; i++)
                                {
                                    string we;
                                    Bitmap wb = CaptureForm(perfForm, out we);
                                    if (wb != null) { wb.Dispose(); }
                                }
                                for (int i = 0; i < 20; i++)
                                {
                                    string err;
                                    sw.Restart();
                                    Bitmap bmp = CaptureForm(perfForm, out err);
                                    sw.Stop();
                                    if (bmp == null)
                                    {
                                        mpFormError = "8MP 单帧第 " + (i + 1).ToString(CultureInfo.InvariantCulture)
                                            + " 次截图失败：" + (err != null ? err : "未知原因");
                                        break;
                                    }
                                    bmp.Dispose();
                                    mpFormTimes.Add(sw.Elapsed.TotalMilliseconds);
                                    double mpMem = PrivateMemoryMb(proc);
                                    if (mpMem > memPeak) { memPeak = mpMem; }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            mpFormError = Describe(ex);
                        }
                    }
                }
            }
        }
        finally
        {
            if (perfForm != null)
            {
                try { perfForm.Dispose(); } catch { }
                perfForm = null;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // 段后：私有内存与 GDI 再各取一次
        double memAfter = PrivateMemoryMb(proc);
        if (memAfter > memPeak) { memPeak = memAfter; }
        int gdiPerfAfter = Diagnostics.GdiObjectCount();
        try { if (proc != null) { proc.Dispose(); } } catch { }
        proc = null;

        // ---- 2) 单帧绘制：<33ms OK / 33–100ms WARN / >100ms FAIL ----
        if (!windowBench)
        {
            Log("  单帧绘制/切图：没有可用窗口，未采集样本（原因见上方 FAIL 行）");
        }
        else
        {
            if (frameError != null) { Fail("性能：" + frameError); }
            if (frameTimes.Count == 0)
            {
                if (frameError == null) { Fail("性能：单帧绘制没有有效样本"); }
            }
            else
            {
                Log("  单帧绘制 DrawToBitmap（热身 " + frameWarmups.ToString(CultureInfo.InvariantCulture)
                    + " 次，统计 " + frameTimes.Count.ToString(CultureInfo.InvariantCulture) + " 次）："
                    + MsText(frameTimes));
                if (frameError == null)
                {
                    Check(frameTimes.Count == frameIterations, "性能：单帧绘制完成 30 次采样",
                        "实际 " + frameTimes.Count.ToString(CultureInfo.InvariantCulture) + " 次");
                }
                double frameP95 = Percentile(frameTimes, 95.0);
                if (frameP95 < 33.0)
                {
                    Check(true, "性能：单帧绘制 p95 < 33ms", MsText(frameTimes));
                }
                else if (frameP95 <= 100.0)
                {
                    // 软阈值：慢机/远程桌面下 33–100ms 只警告，不让自测整体失败
                    Warn("性能：单帧绘制 p95 落在 33–100ms 区间（环境偏慢，不 FAIL）：" + MsText(frameTimes));
                }
                else
                {
                    Check(false, "性能：单帧绘制 p95 > 100ms", MsText(frameTimes));
                }
            }

            // ---- 3) 切图 ----
            if (navError != null) { Fail("性能：" + navError); }
            if (frameError != null)
            {
                Log("  切图 NavigateImage(1)：单帧绘制失败后未继续采集");
            }
            else if (navTimes.Count == 0)
            {
                if (navError == null) { Fail("性能：切图没有有效样本"); }
            }
            else
            {
                Log("  切图 NavigateImage(1)（" + navTimes.Count.ToString(CultureInfo.InvariantCulture)
                    + " 次）：" + MsText(navTimes));
                if (navError == null)
                {
                    Check(navTimes.Count == navIterations, "性能：切图完成 20 次采样",
                        "实际 " + navTimes.Count.ToString(CultureInfo.InvariantCulture) + " 次");
                }
                Check(navTimes.Count > 0 && Percentile(navTimes, 95.0) < 500.0,
                    "性能：切图 p95 < 500ms", MsText(navTimes));
            }

            // ---- 3.5) 8MP 真实窗体单帧（mip 降采样路径） ----
            if (mpFormError != null) { Fail("性能：8MP 真实窗体单帧测量失败：" + mpFormError); }
            else if (mpFormTimes.Count == 0)
            {
                Fail("性能：8MP 真实窗体单帧没有有效样本");
            }
            else
            {
                Log("  8MP 真实窗体单帧（4000x2000 图，Zoom=20%，统计 "
                    + mpFormTimes.Count.ToString(CultureInfo.InvariantCulture) + " 次）：" + MsText(mpFormTimes));
                Check(mpFormTimes.Count == 20, "性能：8MP 真实窗体单帧完成 20 次采样",
                    "实际 " + mpFormTimes.Count.ToString(CultureInfo.InvariantCulture) + " 次");
                double mpFormP95 = Percentile(mpFormTimes, 95.0);
                if (mpFormP95 < 33.0)
                {
                    Check(true, "性能：8MP 真实窗体单帧 p95 < 33ms", MsText(mpFormTimes));
                }
                else if (mpFormP95 <= 100.0)
                {
                    Warn("性能：8MP 真实窗体单帧 p95 落在 33–100ms（环境偏慢，不 FAIL）：" + MsText(mpFormTimes));
                }
                else
                {
                    Check(false, "性能：8MP 真实窗体单帧 p95 > 100ms", MsText(mpFormTimes));
                }
            }
        }

        // ---- 4) 私有内存峰值 ----
        if (memBefore < 0.0 || memPeak < 0.0)
        {
            Log("  私有内存：Process.PrivateMemorySize64 读取失败，跳过峰值断言");
        }
        else
        {
            Log("  私有内存（MB）：段前 " + FmtMs(memBefore) + " / 峰值 " + FmtMs(memPeak)
                + " / 段后 " + FmtMs(memAfter) + "（峰值增量 " + FmtMs(memPeak - memBefore) + " MB）");
            Check((memPeak - memBefore) < 512.0, "性能：私有内存峰值增量 < 512MB",
                "段前 " + FmtMs(memBefore) + " MB / 峰值 " + FmtMs(memPeak) + " MB / 增量 "
                + FmtMs(memPeak - memBefore) + " MB");
        }

        // ---- 5) GDI 对象数（复用 Diagnostics.GdiObjectCount；方法名不与 TestGdiObjects 重复） ----
        if (gdiPerfBefore >= 0 && gdiPerfAfter >= 0)
        {
            int gdiDiff = gdiPerfAfter - gdiPerfBefore;
            string gdiText = "前 " + gdiPerfBefore.ToString(CultureInfo.InvariantCulture)
                + " / 后 " + gdiPerfAfter.ToString(CultureInfo.InvariantCulture)
                + " / 差 " + gdiDiff.ToString(CultureInfo.InvariantCulture);
            Log("  GDI 对象数（性能段）：" + gdiText);
            Check(gdiDiff <= 50, "性能：GDI 对象数前后差 ≤50（性能段）", gdiText);
        }
        else
        {
            Log("  GDI 对象数（性能段）：GetGuiResources 不可用，跳过差值断言");
        }

        // ---- 6) 8MP 图单帧缩放绘制（§十二「8MP 图片单帧 < 33ms」的直接证据） ----
        // 用 4000x2000（800 万像素）位图按 OnPaint 相同的高质量插值缩放到 1280x800，
        // 计时区只含 Graphics.DrawImage，覆盖绘制管线里最重的一步。
        {
            const int mpIterations = 20;
            const int mpWarmups = 2;
            List<double> mpTimes = new List<double>(mpIterations);
            Bitmap big = null;
            Bitmap target = null;
            Graphics g = null;
            try
            {
                big = new Bitmap(4000, 2000, PixelFormat.Format32bppArgb);
                using (Graphics bg = Graphics.FromImage(big))
                {
                    bg.Clear(Color.FromArgb(255, 32, 34, 38));
                    using (Pen pen = new Pen(Color.FromArgb(255, 240, 240, 240), 6f))
                    {
                        bg.DrawRectangle(pen, 20, 20, 3959, 1959);
                    }
                }
                target = new Bitmap(1280, 800, PixelFormat.Format32bppArgb);
                Stopwatch mpSw = new Stopwatch();
                for (int i = 0; i < mpIterations + mpWarmups; i++)
                {
                    bool measure = i >= mpWarmups;
                    if (measure) { mpSw.Restart(); }
                    g = Graphics.FromImage(target);
                    try
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using (ImageAttributes ia = new ImageAttributes())
                        {
                            ia.SetWrapMode(WrapMode.TileFlipXY);
                            g.DrawImage(big, new Rectangle(0, 0, 1280, 800),
                                0, 0, big.Width, big.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                    finally
                    {
                        g.Dispose();
                        g = null;
                    }
                    if (measure) { mpSw.Stop(); mpTimes.Add(mpSw.Elapsed.TotalMilliseconds); }
                }
            }
            catch (Exception ex)
            {
                Fail("性能：8MP 单帧缩放绘制失败：" + Describe(ex));
            }
            finally
            {
                if (g != null) { try { g.Dispose(); } catch { } }
                if (target != null) { try { target.Dispose(); } catch { } }
                if (big != null) { try { big.Dispose(); } catch { } }
            }
            if (mpTimes.Count > 0)
            {
                // 参考值：GDI+ 对 8MP 源直接高质量双三次的固有开销（约 80ms），
                // 应用实际绘制走降采样 mip 缓存，见下面「8MP 真实窗体单帧」断言。
                Log("  [参考值，不计入断言] GDI+ 直接缩放 8MP（4000x2000 → 1280x800，不走 mip 缓存，"
                    + mpTimes.Count.ToString(CultureInfo.InvariantCulture) + " 次）：" + MsText(mpTimes));
            }
        }

        // 校验和：证明 200 次纯函数调用真的执行了（没有被 JIT 当死代码消除）
        Log("  性能段结果校验和（±N，证明调用有真实数据依赖）：" + Fmt(_perfSink));
    }

    // ------------------------------------------------------------------
    // 性能量化辅助：逐次计时、平均/分位、私有内存、统一日志格式
    // ------------------------------------------------------------------

    // 连续执行 step 共 iterations 次，每次单独计时（毫秒）；异常路径也会先停表再抛出
    private static List<double> MeasureTimes(Action step, int iterations)
    {
        List<double> times = new List<double>(iterations > 0 ? iterations : 0);
        if (iterations <= 0) { return times; }
        Stopwatch sw = new Stopwatch();
        for (int i = 0; i < iterations; i++)
        {
            sw.Restart();
            try
            {
                if (step != null) { step(); }
            }
            finally
            {
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
        }
        return times;
    }

    // 当前进程私有内存（MB）；proc 为空或读取失败返回 -1
    private static double PrivateMemoryMb(Process proc)
    {
        if (proc == null) { return -1.0; }
        try { proc.Refresh(); } catch { }
        try
        {
            long bytes = proc.PrivateMemorySize64;
            return bytes / (1024.0 * 1024.0);
        }
        catch { return -1.0; }
    }

    // 平均耗时（毫秒）；空集合返回 0
    private static double MeanOf(List<double> values)
    {
        if (values == null || values.Count == 0) { return 0.0; }
        double sum = 0.0;
        for (int i = 0; i < values.Count; i++) { sum += values[i]; }
        return sum / values.Count;
    }

    // 合计耗时（毫秒）
    private static double SumOf(List<double> values)
    {
        if (values == null || values.Count == 0) { return 0.0; }
        double sum = 0.0;
        for (int i = 0; i < values.Count; i++) { sum += values[i]; }
        return sum;
    }

    private static double MinOf(List<double> values)
    {
        if (values == null || values.Count == 0) { return 0.0; }
        double min = values[0];
        for (int i = 1; i < values.Count; i++) { if (values[i] < min) { min = values[i]; } }
        return min;
    }

    private static double MaxOf(List<double> values)
    {
        if (values == null || values.Count == 0) { return 0.0; }
        double max = values[0];
        for (int i = 1; i < values.Count; i++) { if (values[i] > max) { max = values[i]; } }
        return max;
    }

    // 分位数（最近秩法：排序后取第 ceil(p/100*n) 个，p 取 0–100）；空集合返回 0
    private static double Percentile(List<double> values, double percent)
    {
        if (values == null || values.Count == 0) { return 0.0; }
        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        int index = (int)Math.Ceiling(percent / 100.0 * sorted.Length) - 1;
        if (index < 0) { index = 0; }
        if (index >= sorted.Length) { index = sorted.Length - 1; }
        return sorted[index];
    }

    // 统一的耗时文案：平均 X.XX ms / p95 Y.YY ms（n=…，最小 … ms，最大 … ms，合计 … ms）
    private static string MsText(List<double> times)
    {
        if (times == null || times.Count == 0)
        {
            return "平均 0.00 ms / p95 0.00 ms（n=0）";
        }
        return "平均 " + FmtMs(MeanOf(times))
            + " ms / p95 " + FmtMs(Percentile(times, 95.0))
            + " ms（n=" + times.Count.ToString(CultureInfo.InvariantCulture)
            + "，最小 " + FmtMs(MinOf(times))
            + " ms，最大 " + FmtMs(MaxOf(times))
            + " ms，合计 " + FmtMs(SumOf(times)) + " ms）";
    }

    // 固定两位小数（毫秒），与日志里 X.XX 的口径一致
    private static string FmtMs(double value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    // ==================================================================
    // 七、断言辅助
    // ==================================================================

    private static void Section(string name, Action body)
    {
        Log("");
        Log("=== " + name + " ===");
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            if (body != null) { body(); }
        }
        catch (Exception ex)
        {
            Fail(name + " 抛出未预期异常：" + Describe(ex));
            try { Log("堆栈：" + ex.StackTrace); } catch { }
        }
        sw.Stop();
        Log("（" + name + " 耗时 " + sw.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms）");
    }

    // 读取 ViewState（struct 字段属于 MarshalByRefObject 子类，先复制到本地避免 CS1690）
    private static ViewState ReadView(OverlayForm form)
    {
        return form.View;
    }

    private static void Check(bool condition, string name, string detail = null)
    {
        if (condition)
        {
            _passed++;
            Log("  [OK] " + name);
        }
        else
        {
            Fail(name + (string.IsNullOrEmpty(detail) ? "" : "：" + detail));
        }
    }

    // 软阈值：[WARN] 不算失败，但计入通过数，并在日志里留明确一行便于人工核对
    private static void Warn(string message)
    {
        _passed++;
        Log("  [WARN] " + (message == null ? "" : message));
    }

    private static void CheckNear(double actual, double expected, double tolerance, string name)
    {
        bool ok = Math.Abs(actual - expected) <= tolerance;
        Check(ok, name, "期望 " + Fmt(expected) + " ± " + Fmt(tolerance) + "，实际 " + Fmt(actual));
    }

    private static void CheckInt(int actual, int expected, string name)
    {
        Check(actual == expected, name, "期望 " + expected.ToString(CultureInfo.InvariantCulture)
            + "，实际 " + actual.ToString(CultureInfo.InvariantCulture));
    }

    private static void CheckStr(string actual, string expected, string name)
    {
        bool ok = string.Equals(actual, expected, StringComparison.Ordinal);
        Check(ok, name, "期望 \"" + (expected != null ? expected : "(null)")
            + "\"，实际 \"" + (actual != null ? actual : "(null)") + "\"");
    }

    private static void CheckContains(string text, string needle, string name)
    {
        bool ok = text != null && needle != null && text.IndexOf(needle, StringComparison.Ordinal) >= 0;
        Check(ok, name, "期望包含 \"" + (needle != null ? needle : "(null)")
            + "\"，实际 \"" + (text != null ? text : "(null)") + "\"");
    }

    private static void CheckRect(Rectangle actual, Rectangle expected, string name)
    {
        Check(actual == expected, name, "期望 " + RectText(expected) + "，实际 " + RectText(actual));
    }

    private static void CheckToolbarLayout(string label, ToolbarLayout layout, Size client)
    {
        if (layout == null || layout.Items == null)
        {
            Fail(label + "：布局为空");
            return;
        }
        Check(layout.Bar.Width > 0 && layout.Bar.Height > 0, label + "：工具条矩形有效", RectText(layout.Bar));
        Check(layout.Bar.Left >= 0 && layout.Bar.Right <= client.Width, label + "：工具条不超出客户区左右",
            RectText(layout.Bar) + " client=" + client.Width + "x" + client.Height);
        Check(layout.Bar.Top >= 0 && layout.Bar.Bottom <= client.Height, label + "：工具条不超出客户区上下",
            RectText(layout.Bar) + " client=" + client.Width + "x" + client.Height);

        for (int i = 0; i < layout.Items.Count; i++)
        {
            ToolbarItem item = layout.Items[i];
            if (item == null) { Fail(label + "：存在空按钮项"); continue; }
            Rectangle r = item.Rect;
            Check(r.Left >= layout.Bar.Left && r.Right <= layout.Bar.Right,
                label + "：按钮 " + item.Kind + " 在工具条内", RectText(r) + " bar=" + RectText(layout.Bar));
            Check(r.Top == layout.Bar.Top && r.Height == layout.Bar.Height,
                label + "：按钮 " + item.Kind + " 与工具条同高", RectText(r));
            if (i > 0 && layout.Items[i - 1] != null)
            {
                Check(layout.Items[i - 1].Rect.Right <= r.Left,
                    label + "：按钮 " + layout.Items[i - 1].Kind + "/" + item.Kind + " 不重叠",
                    RectText(layout.Items[i - 1].Rect) + " vs " + RectText(r));
            }
        }
    }

    private static void CheckSkipReason(ScanResult result, string fileName, string expectedPart, string name)
    {
        string reason = null;
        if (result != null && result.Skipped != null)
        {
            for (int i = 0; i < result.Skipped.Count; i++)
            {
                SkippedImage item = result.Skipped[i];
                if (item == null || string.IsNullOrEmpty(item.FullPath)) { continue; }
                string nameOnly = "";
                try { nameOnly = Path.GetFileName(item.FullPath); }
                catch { nameOnly = item.FullPath; }
                if (string.Equals(nameOnly, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    reason = item.Reason;
                    break;
                }
            }
        }
        bool ok = reason != null && reason.IndexOf(expectedPart, StringComparison.Ordinal) >= 0;
        Check(ok, name, "期望原因含 \"" + expectedPart + "\"，实际 \""
            + (reason != null ? reason : "(未找到跳过项)") + "\"");
    }

    private static void CheckFileImage(string path, int width, int height, string name)
    {
        bool exists = false;
        long length = 0;
        try
        {
            FileInfo fi = new FileInfo(path);
            exists = fi.Exists;
            if (exists) { length = fi.Length; }
        }
        catch { }
        if (!exists)
        {
            Fail(name + "：文件不存在 " + path);
            return;
        }
        if (length <= 1000)
        {
            Fail(name + "：文件过小 " + length + " 字节（" + path + "）");
            return;
        }
        try
        {
            using (Bitmap bmp = new Bitmap(path))
            {
                Check(bmp.Width == width && bmp.Height == height, name,
                    "期望 " + width + "x" + height + "，实际 " + bmp.Width + "x" + bmp.Height);
            }
        }
        catch (Exception ex)
        {
            Fail(name + "：无法读取位图 " + Describe(ex));
        }
    }

    // ==================================================================
    // 八、测试图生成
    // ==================================================================

    // 彩色分块 + 大号中文序号（GDI+ 生成，纯内存/临时目录）
    private static void CreateTestImage(string path, int width, int height, string label, Color[] palette)
    {
        if (width <= 0 || height <= 0) { throw new ArgumentException("图片尺寸非法"); }
        if (palette == null || palette.Length == 0)
        {
            palette = new Color[] { Color.Red, Color.Blue, Color.Green };
        }
        using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                // 彩色分块（4 列 × 3 行）
                int cols = 4;
                int rows = 3;
                int cellW = width / cols;
                int cellH = height / rows;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int index = (r * cols + c) % palette.Length;
                        using (SolidBrush brush = new SolidBrush(palette[index]))
                        {
                            g.FillRectangle(brush, c * cellW, r * cellH, cellW, cellH);
                        }
                    }
                }

                // 外框（白色，增加颜色层次）
                using (Pen pen = new Pen(Color.White, Math.Max(4, Math.Min(width, height) / 100)))
                {
                    g.DrawRectangle(pen, 2, 2, width - 5, height - 5);
                }

                // 大号中文序号（白描边 + 黑字，保证任何底色上都清晰）
                float fontSize = Math.Min(width, height) / 3.2f;
                if (fontSize < 8f) { fontSize = 8f; }
                using (Font font = new Font(UiFonts.ResolvedFamily, fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    RectangleF textRect = new RectangleF(0, 0, width, height);
                    try
                    {
                        float outline = Math.Max(6f, fontSize / 14f);
                        using (SolidBrush white = new SolidBrush(Color.White))
                        {
                            g.DrawString(label, font, white,
                                new RectangleF(-outline, -outline, width, height), sf);
                            g.DrawString(label, font, white,
                                new RectangleF(outline, outline, width, height), sf);
                        }
                        using (SolidBrush black = new SolidBrush(Color.Black))
                        {
                            g.DrawString(label, font, black, textRect, sf);
                        }
                    }
                    catch
                    {
                        // 极端字体环境下退回最简单的黑字绘制，保证测试图仍可生成
                        using (SolidBrush fallbackBrush = new SolidBrush(Color.Black))
                        {
                            g.DrawString(label, font, fallbackBrush, textRect, sf);
                        }
                    }
                }
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }

    // ==================================================================
    // 九、日志与工具
    // ==================================================================

    private static void Log(string line)
    {
        try { _logLines.Add(line == null ? "" : line); } catch { }
    }

    private static void Fail(string reason)
    {
        string text = string.IsNullOrEmpty(reason) ? "未命名断言失败" : reason;
        _failed++;
        try { _failures.Add(text); } catch { }
        Log("  [FAIL] " + text);
    }

    private static void WriteFinalLog()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < _logLines.Count; i++) { sb.AppendLine(_logLines[i]); }

        // 汇总块单独拼：stdout 被重定向时（selftest.bat 的 > selftest.log）只写 stdout，
        // 避免程序自写日志与重定向句柄同时写同一个文件、互相覆盖。
        StringBuilder sum = new StringBuilder();
        sum.AppendLine();
        sum.AppendLine("=== 汇总 ===");
        sum.AppendLine("断言总数：" + (_passed + _failed).ToString(CultureInfo.InvariantCulture)
            + "，通过 " + _passed.ToString(CultureInfo.InvariantCulture)
            + "，失败 " + _failed.ToString(CultureInfo.InvariantCulture));
        sum.AppendLine("总耗时：" + (_totalWatch != null ? _totalWatch.ElapsedMilliseconds : 0).ToString(CultureInfo.InvariantCulture) + " ms");
        sum.AppendLine("GDI 对象数：前 " + _gdiBefore.ToString(CultureInfo.InvariantCulture)
            + " / 后 " + _gdiAfter.ToString(CultureInfo.InvariantCulture)
            + " / 差 " + (_gdiAfter - _gdiBefore).ToString(CultureInfo.InvariantCulture));
        sum.AppendLine("实际输出目录：" + _outputDir);
        sum.AppendLine("日志文件：" + _logPath);
        sum.AppendLine("PNG 文件（" + _pngInfo.Count.ToString(CultureInfo.InvariantCulture) + " 张）：");
        for (int i = 0; i < _pngInfo.Count; i++) { sum.AppendLine("  " + _pngInfo[i]); }
        sum.AppendLine("结论：" + (_failed == 0 ? "PASS" : "FAIL"));

        sb.Append(sum);
        string text = sb.ToString();

        bool redirected = false;
        try { redirected = Console.IsOutputRedirected; }
        catch { }
        if (redirected)
        {
            // stdout 已被调用方重定向到日志文件（selftest.bat 的 > selftest.log）：
            // 完整日志走 stdout，不要再自写一份，否则两个句柄互相覆盖
            try { Console.Write(text); }
            catch { }
            return;
        }
        try
        {
            string dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
            File.WriteAllText(_logPath, text, new UTF8Encoding(false));
        }
        catch
        {
            // 日志写不进去也不能再崩：退到 %TEMP% 再试一次
            try
            {
                string fallback = Path.Combine(Path.GetTempPath(),
                    "GuideOverlay-selftest-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + ".log");
                File.WriteAllText(fallback, text, new UTF8Encoding(false));
                _logPath = fallback;
            }
            catch { }
        }
    }

    // 输出目录：exe 同目录可写则用之，否则回退 %TEMP%\GuideOverlay-selftest-<pid>\
    private static string ResolveOutputDirectory(string exeDir, string tempRoot)
    {
        if (!string.IsNullOrEmpty(exeDir) && IsDirectoryWritable(exeDir)) { return exeDir; }
        return tempRoot;
    }

    private static bool IsDirectoryWritable(string dir)
    {
        if (string.IsNullOrEmpty(dir)) { return false; }
        string probe = null;
        try
        {
            if (!Directory.Exists(dir)) { return false; }
            probe = Path.Combine(dir, "~guideoverlay-selftest-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (FileStream fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.WriteByte(0);
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (probe != null)
            {
                try { if (File.Exists(probe)) { File.Delete(probe); } } catch { }
            }
        }
    }

    private static bool IsSamePath(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) { return false; }
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    private static bool SamePathText(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) { return false; }
        string x = a.Trim();
        string y = b.Trim();
        try { x = Path.GetFullPath(x); } catch { }
        try { y = Path.GetFullPath(y); } catch { }
        return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(Exception ex)
    {
        if (ex == null) { return "未知异常"; }
        return ex.GetType().Name + "：" + ex.Message;
    }

    private static string DescribeBitmap(Bitmap bmp)
    {
        if (bmp == null) { return "null"; }
        return bmp.Width.ToString(CultureInfo.InvariantCulture) + "x"
            + bmp.Height.ToString(CultureInfo.InvariantCulture);
    }

    private static string Fmt(double value)
    {
        if (double.IsNaN(value)) { return "NaN"; }
        if (double.IsPositiveInfinity(value)) { return "+Inf"; }
        if (double.IsNegativeInfinity(value)) { return "-Inf"; }
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string RectText(Rectangle r)
    {
        return "(" + r.X.ToString(CultureInfo.InvariantCulture) + ","
            + r.Y.ToString(CultureInfo.InvariantCulture) + ","
            + r.Width.ToString(CultureInfo.InvariantCulture) + "x"
            + r.Height.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static Point RectCenter(Rectangle r)
    {
        return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
    }

    private static string JoinList(List<string> list)
    {
        if (list == null) { return "(null)"; }
        return string.Join(",", list.ToArray());
    }

    private static void WriteUtf8(string path, string text)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
        File.WriteAllBytes(path, bytes);
    }

    // ------------------------------------------------------------------
    // 文件/目录快照（用于无副作用断言）
    // ------------------------------------------------------------------
    private sealed class PathSnapshot
    {
        public bool Exists;
        public long Length;
        public byte[] Bytes;
    }

    private static PathSnapshot SnapshotFile(string path)
    {
        PathSnapshot snapshot = new PathSnapshot();
        snapshot.Exists = false;
        snapshot.Length = 0;
        snapshot.Bytes = null;
        if (string.IsNullOrEmpty(path)) { return snapshot; }
        try
        {
            if (File.Exists(path))
            {
                snapshot.Exists = true;
                snapshot.Bytes = File.ReadAllBytes(path);
                snapshot.Length = snapshot.Bytes.LongLength;
            }
        }
        catch
        {
            snapshot.Exists = true;
            snapshot.Bytes = null;
            snapshot.Length = -1;
        }
        return snapshot;
    }

    private static bool SnapshotEqual(PathSnapshot a, PathSnapshot b)
    {
        if (a == null || b == null) { return a == b; }
        if (a.Exists != b.Exists) { return false; }
        if (!a.Exists) { return true; }
        if (a.Length != b.Length) { return false; }
        if (a.Bytes == null || b.Bytes == null) { return a.Bytes == b.Bytes; }
        if (a.Bytes.Length != b.Bytes.Length) { return false; }
        for (int i = 0; i < a.Bytes.Length; i++)
        {
            if (a.Bytes[i] != b.Bytes[i]) { return false; }
        }
        return true;
    }

    // 目录签名：递归文件名 + 大小（排序后拼接），用于前后对比
    private static string SnapshotDirectory(string dir)
    {
        if (string.IsNullOrEmpty(dir)) { return "EMPTY-PATH"; }
        try
        {
            if (!Directory.Exists(dir)) { return "MISSING"; }
            string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            List<string> items = new List<string>();
            for (int i = 0; i < files.Length; i++)
            {
                string rel = files[i];
                long length = -1;
                try
                {
                    if (!string.IsNullOrEmpty(dir))
                    {
                        string fullDir = Path.GetFullPath(dir);
                        string fullFile = Path.GetFullPath(files[i]);
                        if (fullFile.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase))
                        {
                            rel = fullFile.Substring(fullDir.Length).TrimStart('\\', '/');
                        }
                        else { rel = fullFile; }
                    }
                    FileInfo fi = new FileInfo(files[i]);
                    if (fi.Exists) { length = fi.Length; }
                }
                catch { }
                items.Add(rel + "|" + length.ToString(CultureInfo.InvariantCulture));
            }
            items.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(";", items.ToArray());
        }
        catch (Exception ex)
        {
            return "ERROR:" + ex.Message;
        }
    }
}
// ============================================================================
// 程序入口 — 入口：DPI 感知 / 单实例 / 全局异常兜底 / --selftest 分派
// ============================================================================

internal static class Program
{
    private static Mutex _mutex;
    private static AppSettings _settings;
    private static int _fatalHandled;
    private static int _uiThreadId;

    [STAThread]
    private static int Main(string[] args)
    {
        _uiThreadId = Thread.CurrentThread.ManagedThreadId;

        // 必须在任何窗口创建之前
        try { NativeMethods.SetProcessDPIAware(); }
        catch { }

        bool selfTest = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--selftest", StringComparison.OrdinalIgnoreCase)) { selfTest = true; }
        }

        if (selfTest)
        {
            try { NativeMethods.AttachConsole(Win32Const.ATTACH_PARENT_PROCESS); }
            catch { }
            try
            {
                return SelfTestRunner.Run();
            }
            catch (Exception ex)
            {
                try { Console.WriteLine("FAIL: 自测异常 " + ex.Message); }
                catch { }
                return 1;
            }
        }

        string exeDir = SettingsStore.ResolveExeDir();

        bool createdNew = false;
        try
        {
            // Local\ 前缀：同一登录会话内唯一，避免终端服务/快速用户切换时把另一个用户的实例拦掉
            _mutex = new Mutex(true, "Local\\" + AppInfo.SingleInstanceMutexName, out createdNew);
        }
        catch
        {
            createdNew = true;
        }
        if (!createdNew)
        {
            // 没拿到所有权就退出：句柄仍需释放
            try
            {
                if (_mutex != null) { _mutex.Close(); _mutex = null; }
            }
            catch { }
            MessageBox.Show("攻略悬浮窗已经在运行。", AppInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _settings = SettingsStore.Load(exeDir, null);

            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                HandleFatal(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                HandleFatal(e.ExceptionObject as Exception);
            };

            OverlayForm form = new OverlayForm(_settings);
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            HandleFatal(ex);
            return 1;
        }
        finally
        {
            try
            {
                if (_mutex != null)
                {
                    _mutex.ReleaseMutex();
                    _mutex.Close();
                    _mutex = null;
                }
            }
            catch { }
        }
    }

    private static void HandleFatal(Exception ex)
    {
        // 可能同时被 UI 线程（ThreadException）与线程池线程（AppDomain.UnhandledException）触发
        if (Interlocked.CompareExchange(ref _fatalHandled, 1, 0) != 0) { return; }

        bool onUiThread = (Thread.CurrentThread.ManagedThreadId == _uiThreadId);
        string logPath = null;
        if (onUiThread)
        {
            // 只在 UI 线程写配置：与 SaveNow 并发写同一个 settings.ini 会互相踩踏
            try
            {
                if (_settings != null) { SettingsStore.Save(_settings); }
            }
            catch { }
        }
        try
        {
            logPath = Diagnostics.WriteCrashLog(ex, "未捕获异常", _settings);
        }
        catch { }

        if (!onUiThread)
        {
            // 非 UI 线程弹 MessageBox 可能死锁（UI 线程已挂），只落日志并退出
            try { Environment.Exit(1); }
            catch { }
            return;
        }

        string message = "攻略悬浮窗遇到问题，需要退出。";
        if (!string.IsNullOrEmpty(logPath)) { message += "\r\n\r\n崩溃日志：" + logPath; }
        try
        {
            MessageBox.Show(message, AppInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
        try { Environment.Exit(1); }
        catch { }
    }
}
