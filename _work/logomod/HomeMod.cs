using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using Windows.Foundation;

namespace JiaoLongLogoMod;

/// <summary>
/// 主页监控卡控制项注入 v9（2026-10-13）：控制面板内联挂载于监控列表下方
///（CPU 卡：温度等监控项之后；GPU 卡：显存等监控项之后），无齿轮、无弹窗。
/// 宿主 CCUWinUI 经 IL 裁剪，大量 WinUI API 在运行时不存在（ToggleSwitch..ctor /
/// StackPanel.Spacing / Control.Padding / Button.Flyout / ExceptionDispatchInfo 均已实测缺失）。
/// 因此本文件只用「反编译 C# 代码里实证调用过」的 API：
///   TextBlock(全套属性)/Grid/StackPanel(Orientation)/Canvas(SetLeft/SetTop)/Border/
///   Path+PathGeometry+PathFigure+LineSegment/ContentDialog/Button(ctor+Width+Height)/
///   M2Mqtt(官方 UI 同款客户端)。
/// 富控件（Slider/ToggleSwitch/ComboBox）逐个隔离尝试，失败自动降级为
/// 点击/步进 TextBlock——全部功能不依赖任何一个未实证 API。
/// 每个危险引用独立小方法 + 调用点 try/catch（JIT 失败杀不死整个方法）。
/// </summary>
public static class HomeMod
{
    // ============================ 状态 ============================

    private sealed class HomeState
    {
        public bool TurboOff;
        public bool PowerPlanHigh = true;
        public int UndervoltSteps;
        public int Spl = 85, Sppt = 120;
        public int CpuCores = 8, CpuThreads = 16;
        public bool GpuOcOn;
        public int GpuCoreOffset, GpuMemOffset;
        public bool GpuLockOn;
        public int GpuLockMhz = 2100;
        public bool GpuLockSupported;
        public int GpuLockMin = 210, GpuLockMax = 3105;
        public int FanMaxRpm;
        public bool FanMaxSwitch;
        public int TempWall = 95;
        // v9j：每项启用开关（关 = 该项恢复默认/不下发）
        public bool Pl1En = true, Pl2En = true, UvEn = true, CoreEn = true, MemEn = true, LockEn = true, FanRpmEn = true, TempWallEn = true;
        public string FanCpuRpm = "-", FanGpuRpm = "-", FanCpuDuty = "-", FanGpuDuty = "-";
    }

    private static readonly HomeState State = new();
    private static readonly object StateLock = new();
    private static readonly List<double> CpuFreqHistory = new();
    private static readonly List<double> GpuFreqHistory = new();
    private const int HistoryLen = 60;

    private static bool _started;
    private static bool _mqttStarted;
    private static MqttClient? _mqtt;
    private static readonly HashSet<string> _seenTopics = new(StringComparer.Ordinal);
    private static Page? _page;
    private static bool _suppressEvents;
    private static int _refreshQueued;          // TryEnqueue 刷新去重
    private static bool _enqueueLogged;

    // UI 引用
    private static readonly List<Action> Syncs = new();          // UI tick 回填
    private static readonly Dictionary<string, (DateTime At, string Json)> Pending = new(); // 滑块防抖提交
    private static Path? _cpuChartPath, _gpuChartPath;
    private static TextBlock? _fanCpuRpmText, _fanGpuRpmText, _fanCpuDutyText, _fanGpuDutyText;

    // ============================ 入口 ============================

    public static void Init(object pageObj)
    {
        try
        {
            if (_started) return;
            _started = true;
            if (pageObj is not Page page) return;
            _page = page;
            LogoMod.Log("home: init v9k2 (path switch ui)");

            var timer = page.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(700);
            timer.IsRepeating = true;
            var ticks = 0;
            timer.Tick += (s, e) =>
            {
                try
                {
                    ticks++;
                    var cpu = FindByName(page, "cpuRegion");
                    var gpu = FindByName(page, "gpuRegion");
                    var fan = FindByName(page, "fanRegion");
                    if (cpu is null || gpu is null || fan is null)
                    {
                        if (ticks % 10 == 1)
                            LogoMod.Log($"home poll #{ticks}: cpu={(cpu is null ? "-" : "+")} gpu={(gpu is null ? "-" : "+")} fan={(fan is null ? "-" : "+")}");
                        return;
                    }
                    timer.Stop();
                    LogoMod.Log($"home: cards found after {ticks} poll(s)");
                    AttachCard(cpu, "cpu");
                    AttachCard(gpu, "gpu");
                    AttachCard(fan, "fan");
                    StartMqtt();
                    StartUiTimer(page);
                }
                catch (Exception tex)
                {
                    LogoMod.Log("home poll: " + tex.Message);
                }
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            LogoMod.Log("home init: " + ex.Message);
        }
    }

    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        try
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(root, i) is not { } child) continue;
                if (child is FrameworkElement fe && fe.Name == name) return fe;
                var found = FindByName(child, name);
                if (found is not null) return found;
            }
        }
        catch { }
        return null;
    }

    // ============================ 卡片挂载 ============================

    private static void AttachCard(FrameworkElement card, string kind)
    {
        try { BuildPanel(kind); }
        catch (Exception ex) { LogoMod.Log($"home panel {kind}: " + ex.Message); }
        try { AttachPanelInline(card, kind); }
        catch (Exception ex) { LogoMod.Log($"home inline {kind}: " + ex.Message); }
    }

    /// <summary>v9：废弃齿轮+ContentDialog，控制面板作为 Grid 新行内联挂载在
    /// 监控列表（温度/显存等）下方。宿主 UpdatePlaceholderItems 自身调用
    /// new GridLength(0.0, GridUnitType.Pixel)，构造器实证可用；
    /// RowDefinition.Add 万一失败则降级为占用最后一个已有行（占位行）。</summary>
    /// <summary>v9b：控制面板挂进卡片 Grid 已有的占位填充行（监控列表下方的空白行）。
    /// v9 教训：RowDefinitions.Add 会破坏宿主页面布局导致整窗黑屏（无异常、无崩溃）；
    /// 而向既有行添加子元素是安全的（v8 Canvas 覆盖层实测）。占位行高度由宿主
    /// UpdatePlaceholderItems 按剩余空间计算，内容过高会被裁剪——面板尽量紧凑。</summary>
    private static void AttachPanelInline(FrameworkElement card, string kind)
    {
        if (card is not Grid grid)
        {
            LogoMod.Log($"home: {kind} card is {card.GetType().Name}, not Grid");
            return;
        }
        StackPanel? panel = kind switch
        {
            "cpu" => _cpuPanel,
            "gpu" => _gpuPanel,
            _ => _fanPanel
        };
        if (panel is null) { LogoMod.Log($"inline: panel {kind} not built"); return; }

        int row = grid.RowDefinitions.Count - 1;
        if (row < 0) { LogoMod.Log($"inline: {kind} grid has no rows"); return; }
        try { panel.Margin = new Thickness(10, 4, 0, 2); } catch { } // v9e: 左缩进与监控文字对齐
        // v9f：CPU 卡监控只有 4 行（显卡卡 5 行），宿主列表行高固定导致 CPU 卡面板上方
        // 多出一行空白。TranslateTransform 运行时类型找不到（CsWinRT 投影），
        // 改用负 Margin 上拉（Thickness 支持负值，纯布局无新 API）。
        if (kind == "cpu")
        {
            try { panel.Margin = new Thickness(10, -44, 0, 2); LogoMod.Log("inline: cpu negative top margin -44"); }
            catch (Exception ex) { LogoMod.Log("inline: neg margin failed " + ex.Message); }
        }
        else if (kind == "gpu")
        {
            try { panel.Margin = new Thickness(10, -14, 0, 2); LogoMod.Log("inline: gpu negative top margin -14"); }
            catch (Exception ex) { LogoMod.Log("inline: neg margin failed " + ex.Message); }
        }
        try { panel.VerticalAlignment = VerticalAlignment.Top; } catch { }
        grid.Children.Add(panel);
        Grid.SetRow(panel, row);
        LogoMod.Log($"home: panel attached to {kind} existing row {row}");
    }

    /// <summary>隔离：RenderTransform + TranslateTransform 引用单独成方法，
    /// JIT 失败只废本方法不拖垮 AttachPanelInline（v4 教训：JIT 失败发生在调用边界）。</summary>
    // SetTranslateY 已废弃：TranslateTransform 运行时类型在 CsWinRT 投影下找不到
    // （asm.GetType 返回 null），改用负 Margin（v9f）。

    /// <summary>隔离：只含 RowDefinition/Add 引用。GridLength 用宿主实证的构造器。</summary>
    // v9 实测：RowDefinitions.Add 触发宿主布局死循环（整窗黑屏无异常），该路径已废弃。

    // ============================ 面板 ============================

    private static StackPanel? _cpuPanel, _gpuPanel, _fanPanel;
    private static readonly Windows.UI.Color CpuLine = Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xD9, 0x64);
    private static readonly Windows.UI.Color GpuLine = Windows.UI.Color.FromArgb(0xFF, 0x9D, 0x7C, 0xFF);

    private static StackPanel NewPanel(string title, List<FrameworkElement> rows)
    {
        var panel = new StackPanel();
        // v9c：不设固定宽度——卡片实际宽度不足 350 时固定宽会把右侧 +/− 挤出可点区域
        // v9e：去掉面板自带标题与分隔横线，功能行直接跟在监控列表下方；
        // 左边距与上方监控文字（使用率/频率）垂直对齐（截图比对校准）
        try { panel.Margin = new Thickness(10, 0, 0, 0); } catch { }
        foreach (var r in rows) panel.Children.Add(r);
        return panel;
    }

    /// <summary>行容器：水平 StackPanel（Orientation 在 MEZone 系列实证可用）。
    /// v9g：行底距 10，与监控列表的行节奏一致。</summary>
    private static StackPanel NewRow()
    {
        var row = new StackPanel();
        try { row.Orientation = Orientation.Horizontal; } catch { }
        try { row.Margin = new Thickness(0, 0, 0, 4); } catch { }
        return row;
    }

    private static TextBlock NewLabel(string text)
    {
        // v9g：字号 14 + 上下 Padding 5，与监控列表（使用率/频率）字号一致
        return new TextBlock { Text = text, FontSize = 14, Padding = new Thickness(2, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
    }

    private static TextBlock NewTapText(string text, double fontSize = 14)
    {
        var tb = new TextBlock { Text = text, FontSize = fontSize, Padding = new Thickness(6, 4, 6, 4) };
        return tb;
    }

    private static void BuildPanel(string kind)
    {
        BuildPanelsOnce();
    }

    private static bool _panelsBuilt;

    /// <summary>三个面板只建一次（控件引用供 UI tick 回填；页面重建时复用）。</summary>
    private static void BuildPanelsOnce()
    {
        if (_panelsBuilt) return;
        _panelsBuilt = true;
        try { _cpuPanel = BuildCpuPanel(); LogoMod.Log("cpu panel built"); } catch (Exception ex) { LogoMod.Log("cpu panel: " + ex.Message); }
        try { _gpuPanel = BuildGpuPanel(); LogoMod.Log("gpu panel built"); } catch (Exception ex) { LogoMod.Log("gpu panel: " + ex.Message); }
        try { _fanPanel = BuildFanPanel(); LogoMod.Log("fan panel built"); } catch (Exception ex) { LogoMod.Log("fan panel: " + ex.Message); }
    }

    // ---------- CPU 面板 ----------

    private static StackPanel BuildCpuPanel()
    {
        var rows = new List<FrameworkElement>();
        Syncs.Clear();

        // v9h：核心/线程信息行移除（用户要求，无实际用处）

        // 关闭睿频（开关）
        rows.Add(ToggleRow("关闭睿频", () => State.TurboOff, on => Publish("TURBO", $",\"On\":{(!on ? "true" : "false")}")));

        // 电源管理（循环）
        rows.Add(CycleRow("电源管理", new[] { "高性能", "平衡" },
            () => State.PowerPlanHigh ? 0 : 1,
            i => Publish("POWER_PLAN", $",\"High\":{(i == 0 ? "true" : "false")}")));

        // 功耗墙/降压（步进）
        rows.Add(StepperRow("长时功耗 PL1", 15, 120, 1, () => State.Spl,
            v => Publish("SPL", $",\"Watts\":{v},\"Enabled\":{B(State.Pl1En)}"), "W",
            () => State.Pl1En, on => { State.Pl1En = on; Publish("SPL", $",\"Watts\":{State.Spl},\"Enabled\":{B(on)}"); }));
        rows.Add(StepperRow("短时功耗 PL2", 15, 130, 1, () => State.Sppt,
            v => Publish("SPPT", $",\"Watts\":{v},\"Enabled\":{B(State.Pl2En)}"), "W",
            () => State.Pl2En, on => { State.Pl2En = on; Publish("SPPT", $",\"Watts\":{State.Sppt},\"Enabled\":{B(on)}"); }));
        rows.Add(StepperRow("CPU 降压步数", 0, 50, 1, () => State.UndervoltSteps,
            v => Publish("UNDERVOLT", $",\"Steps\":{v},\"Enabled\":{B(State.UvEn)}"), "",
            () => State.UvEn, on => { State.UvEn = on; Publish("UNDERVOLT", $",\"Steps\":{State.UndervoltSteps},\"Enabled\":{B(on)}"); }));

        // v9g：频率曲线行移除——字号/行距放大后卡片高度不够，曲线会被底边裁掉；
        // 如需恢复：rows.Add(caption) + rows.Add(ChartBox(_cpuChartPath))
        _cpuChartPath = null;

        return NewPanel("处理器", rows);
    }

    // ---------- GPU 面板 ----------

    private static StackPanel BuildGpuPanel()
    {
        var rows = new List<FrameworkElement>();

        rows.Add(ToggleRow("显卡超频", () => State.GpuOcOn, on => Publish("GPU_OC", $",\"On\":{(on ? "true" : "false")}")));
        rows.Add(StepperRow("核心偏移", 0, 250, 10, () => State.GpuCoreOffset,
            v => Publish("GPU_CORE_OFFSET", $",\"Mhz\":{v},\"Enabled\":{B(State.CoreEn)}"), "MHz",
            () => State.CoreEn, on => { State.CoreEn = on; Publish("GPU_CORE_OFFSET", $",\"Mhz\":{State.GpuCoreOffset},\"Enabled\":{B(on)}"); }));
        rows.Add(StepperRow("显存偏移", 0, 1000, 50, () => State.GpuMemOffset,
            v => Publish("GPU_MEM_OFFSET", $",\"Mhz\":{v},\"Enabled\":{B(State.MemEn)}"), "MHz",
            () => State.MemEn, on => { State.MemEn = on; Publish("GPU_MEM_OFFSET", $",\"Mhz\":{State.GpuMemOffset},\"Enabled\":{B(on)}"); }));
        rows.Add(ToggleRow("锁定频率", () => State.GpuLockOn, on => Publish("GPU_LOCK", $",\"On\":{(on ? "true" : "false")}")));
        rows.Add(StepperRow("锁定频率值", 210, 3105, 15, () => State.GpuLockMhz,
            v => Publish("GPU_LOCK_MHZ", $",\"Mhz\":{v},\"Enabled\":{B(State.LockEn)}"), "MHz",
            () => State.LockEn, on => { State.LockEn = on; Publish("GPU_LOCK_MHZ", $",\"Mhz\":{State.GpuLockMhz},\"Enabled\":{B(on)}"); }));

        // v9g：频率曲线行移除（同 CPU，卡片高度不够）
        _gpuChartPath = null;

        return NewPanel("显卡", rows);
    }

    // ---------- 风扇面板（v9i：自定义转速 + 最大转速开关 + 温度墙；强冷由控制台承担已删） ----------

    private static TextBlock? _fanWarnText;

    private static StackPanel BuildFanPanel()
    {
        var rows = new List<FrameworkElement>();

        // 自定义风扇转速（0-7000，步进 100；>5800 走风险提示）
        rows.Add(StepperRow("自定义风扇转速", 0, 7000, 100,
            () => State.FanMaxRpm, v => Publish("MAX_FAN", $",\"Value\":{v},\"Enabled\":{B(State.FanRpmEn)}"), "RPM",
            () => State.FanRpmEn, on => { State.FanRpmEn = on; Publish("MAX_FAN", $",\"Value\":{State.FanMaxRpm},\"Enabled\":{B(on)}"); }));

        // 超转风险提示（>5800 显示）
        _fanWarnText = new TextBlock
        {
            Text = "⚠ 超过系统上限 5800 RPM，风扇超转有损坏风险",
            FontSize = 12,
            Opacity = 0,
        };
        try { _fanWarnText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6A, 0x4C)); } catch { }
        rows.Add(_fanWarnText);
        Syncs.Add(() => { if (_fanWarnText != null) TrySetOpacity(_fanWarnText, State.FanMaxRpm > 5800 ? 1.0 : 0.0); });

        // 最大转速开关（对应旧程序 MAX_FAN Switch）
        rows.Add(ToggleRow("最大转速", () => State.FanMaxSwitch,
            on => Publish("MAX_FAN", $",\"Switch\":{on.ToString().ToLower()}")));

        // 温度墙（温度曲线上限，MIFS fn23 子功能 4）
        rows.Add(StepperRow("温度墙 (温度曲线)", 40, 110, 1,
            () => State.TempWall, v => Publish("TEMP_WALL", $",\"Value\":{v},\"Enabled\":{B(State.TempWallEn)}"), "°C",
            () => State.TempWallEn, on => { State.TempWallEn = on; Publish("TEMP_WALL", $",\"Value\":{State.TempWall},\"Enabled\":{B(on)}"); }));

        return NewPanel("风扇", rows);
    }

    private static void TrySetOpacity(FrameworkElement el, double o)
    {
        try { el.Opacity = o; } catch { }
    }

    private static StackPanel WrapRow(params FrameworkElement[] children)
    {
        var row = NewRow();
        foreach (var c in children) row.Children.Add(c);
        return row;
    }

    // ---------- 控件：开关 / 循环 / 步进（富控件尝试 + TextBlock 降级） ----------

    /// <summary>开关行：优先 ToggleSwitch；ctor 缺失（实测）则降级为点击 TextBlock。
    /// 注意：富控件构造必须在独立方法里——宿主投影缺成员时整个方法无法 JIT，
    /// 且 try/catch 必须包在「调用点」（v4 实测教训：JIT 失败发生在调用边界）。</summary>
    private static FrameworkElement ToggleRow(string label, Func<bool> get, Action<bool> set)
    {
        var row = NewRow();
        row.Children.Add(NewLabel(label));

        bool ok = false;
        try { ok = TryAddToggleSwitch(row, get, set); } catch (Exception ex) { LogoMod.Log("control: ToggleSwitch JIT 失败: " + ex.Message); ok = false; }
        if (ok) return row;

        // 降级：点击切换（本方法不引用 ToggleSwitch）
        var fallback = NewTapText("");
        TrySetW(fallback, 110);
        fallback.Tapped += (s, e) =>
        {
            try { LogoMod.Log("tap toggle: " + label); set(!get()); } catch (Exception ex) { LogoMod.Log("tap toggle err: " + ex.Message); }
        };
        Syncs.Add(() => TrySetText(fallback, get() ? "已开启" : "已关闭"));
        row.Children.Add(fallback);
        LogoMod.Log("control: 降级-点击开关 (" + label + ")");
        return row;
    }

    /// <summary>隔离：只含 ToggleSwitch 引用。JIT 失败返回 false（调用方走降级）。</summary>
    private static bool TryAddToggleSwitch(StackPanel row, Func<bool> get, Action<bool> set)
    {
        try
        {
            var ts = new ToggleSwitch { OnContent = "开", OffContent = "关", FontSize = 11 };
            ts.Toggled += (s, e) =>
            {
                if (_suppressEvents) return;
                set(ts.IsOn);
            };
            Syncs.Add(() =>
            {
                _suppressEvents = true;
                try { ts.IsOn = get(); } catch { }
                _suppressEvents = false;
            });
            row.Children.Add(ts);
            LogoMod.Log("control: ToggleSwitch OK");
            return true;
        }
        catch (Exception ex)
        {
            LogoMod.Log("control: ToggleSwitch 不可用，走降级: " + ex.Message);
            return false;
        }
    }

    /// <summary>循环行：优先 ComboBox；缺失则降级为点击切换 TextBlock。</summary>
    private static FrameworkElement CycleRow(string label, string[] options, Func<int> get, Action<int> set)
    {
        var row = NewRow();
        row.Children.Add(NewLabel(label));

        bool ok = false;
        try { ok = TryAddComboBox(row, options, get, set); } catch (Exception ex) { LogoMod.Log("control: ComboBox JIT 失败: " + ex.Message); ok = false; }
        if (ok) return row;

        var fallback = NewTapText("");
        TrySetW(fallback, 90);
        fallback.Tapped += (s, e) =>
        {
            try { LogoMod.Log("tap cycle: " + label); set((get() + 1) % options.Length); } catch (Exception ex) { LogoMod.Log("tap cycle err: " + ex.Message); }
        };
        Syncs.Add(() => TrySetText(fallback, options[Math.Clamp(get(), 0, options.Length - 1)]));
        row.Children.Add(fallback);
        LogoMod.Log("control: 降级-点击循环 (" + label + ")");
        return row;
    }

    /// <summary>隔离：只含 ComboBox 引用。</summary>
    private static bool TryAddComboBox(StackPanel row, string[] options, Func<int> get, Action<int> set)
    {
        try
        {
            var combo = new ComboBox();
            TrySetW(combo, 110);
            foreach (var o in options) combo.Items.Add(o);
            combo.SelectionChanged += (s, e) =>
            {
                if (_suppressEvents) return;
                if (combo.SelectedIndex >= 0) set(combo.SelectedIndex);
            };
            Syncs.Add(() =>
            {
                _suppressEvents = true;
                try { combo.SelectedIndex = Math.Clamp(get(), 0, options.Length - 1); } catch { }
                _suppressEvents = false;
            });
            row.Children.Add(combo);
            LogoMod.Log("control: ComboBox OK");
            return true;
        }
        catch (Exception ex)
        {
            LogoMod.Log("control: ComboBox 不可用，走降级: " + ex.Message);
            return false;
        }
    }

    /// <summary>步进行：优先 Slider（ValueChanged 防抖提交，本应用无 PointerReleased）；
    /// 缺失则降级为 −/+ 点击 TextBlock。</summary>
    private static FrameworkElement StepperRow(string label, int min, int max, int step,
        Func<int> get, Action<int> set, string unit, Func<bool>? getEn = null, Action<bool>? setEn = null)
    {
        var row = NewRow();
        row.Children.Add(NewLabel(label));

        bool ok = false;
        try { ok = TryAddSlider(row, label, min, max, step, get, set, unit); } catch (Exception ex) { LogoMod.Log("control: Slider JIT 失败 (" + label + "): " + ex.Message); ok = false; }
        if (ok) { AddEnableSwitch(row, getEn, setEn); return row; }

        var fallbackVal = NewTapText("");
        TrySetW(fallbackVal, 70);
        var minus = NewTapText("−", 14);
        var plus = NewTapText("+", 14);
        try { minus.Padding = new Thickness(10, 6, 10, 6); plus.Padding = new Thickness(10, 6, 10, 6); } catch { }
        minus.Tapped += (s, e) => { try { if (getEn != null && !getEn()) return; LogoMod.Log("tap minus: " + label); set(Math.Max(min, get() - step)); } catch (Exception ex) { LogoMod.Log("tap minus err: " + ex.Message); } };
        plus.Tapped += (s, e) => { try { if (getEn != null && !getEn()) return; LogoMod.Log("tap plus: " + label); set(Math.Min(max, get() + step)); } catch (Exception ex) { LogoMod.Log("tap plus err: " + ex.Message); } };
        AttachTapEdit(fallbackVal, get, set, min, max, getEn); // v9i：点数值直接编辑
        Syncs.Add(() =>
        {
            TrySetText(fallbackVal, FormatVal(get(), unit));
            if (getEn != null) { try { TrySetOpacity(fallbackVal, getEn() ? 1.0 : 0.45); } catch { } }
        });
        row.Children.Add(minus);
        row.Children.Add(fallbackVal);
        row.Children.Add(plus);
        AddEnableSwitch(row, getEn, setEn);
        LogoMod.Log("control: 降级-步进 (" + label + (getEn != null ? " +开关" : "") + ")");
        return row;
    }

    private static string B(bool v) => v ? "true" : "false";

    /// <summary>行尾启用开关（v9k）：完整开关外观 —— 圆角轨道 + 圆形滑块。
    /// 宿主投影裁掉了 ToggleSwitch，故用 Canvas+Border 自绘；Border 若被裁则降级为 ● 文字点。</summary>
    private static void AddEnableSwitch(StackPanel row, Func<bool>? getEn, Action<bool>? setEn)
    {
        if (getEn == null || setEn == null) return;

        SwitchUi? ui = null;
        try { ui = BuildSwitchUi(); }
        catch (Exception ex) { LogoMod.Log("switch: 自绘开关不可用，降级文字点: " + ex.Message); ui = null; }

        if (ui == null)
        {
            // 降级：●/○ 文字点
            var dot = NewTapText("", 14);
            try { dot.Padding = new Thickness(8, 2, 4, 2); } catch { }
            dot.Tapped += (s2, e) =>
            {
                try { bool on = !getEn(); LogoMod.Log("tap enable -> " + (on ? "on" : "off")); setEn(on); }
                catch (Exception ex) { LogoMod.Log("tap enable err: " + ex.Message); }
            };
            Syncs.Add(() =>
            {
                bool on = getEn();
                TrySetText(dot, on ? "●" : "○");
                try
                {
                    dot.Foreground = on
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2E, 0xC5, 0xFF))
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
                }
                catch { }
            });
            row.Children.Add(dot);
            return;
        }

        var root = ui.Root;
        root.Tapped += (s2, e) =>
        {
            try
            {
                bool on = !getEn();
                LogoMod.Log("tap enable -> " + (on ? "on" : "off"));
                ui.Apply(on);      // 立即视觉反馈（不等遥测回传）
                setEn(on);
            }
            catch (Exception ex) { LogoMod.Log("tap enable err: " + ex.Message); }
        };
        Syncs.Add(() => ui.Apply(getEn()));
        ui.Apply(getEn());
        row.Children.Add(root);
        LogoMod.Log("control: 开关已挂载（Canvas 自绘）");
    }

    private sealed class SwitchUi
    {
        public FrameworkElement Root = null!;
        public Action<bool> Apply = _ => { };
    }

    /// <summary>圆角矩形采样点（每角 seg 段折线拟合，屏幕坐标 y 向下）。</summary>
    private static List<Point> RoundRectPoints(double x, double y, double w, double h, double r, int seg)
    {
        var pts = new List<Point>();
        double[,] corners =
        {
            { x + w - r, y + r, -90 },      // 右上
            { x + w - r, y + h - r, 0 },    // 右下
            { x + r, y + h - r, 90 },       // 左下
            { x + r, y + r, 180 }           // 左上
        };
        for (int c = 0; c < 4; c++)
        {
            for (int i = 0; i <= seg; i++)
            {
                double ang = (corners[c, 2] + 90.0 * i / seg) * Math.PI / 180.0;
                pts.Add(new Point(corners[c, 0] + r * Math.Cos(ang), corners[c, 1] + r * Math.Sin(ang)));
            }
        }
        return pts;
    }

    /// <summary>隔离：用 Path+PathGeometry+LineSegment（白名单 API）画圆角胶囊/圆形。</summary>
    private static Path? TryMakePill(double w, double h, Windows.UI.Color color)
    {
        try
        {
            var fig = new PathFigure();
            var pts = RoundRectPoints(0, 0, w, h, Math.Min(h / 2.0, w / 2.0), 6);
            fig.StartPoint = pts[0];
            for (int i = 1; i < pts.Count; i++)
                fig.Segments.Add(new LineSegment { Point = pts[i] });
            try { fig.IsClosed = true; } catch { }
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            var path = new Path { Data = geo };
            TrySetFill(path, color);
            return path;
        }
        catch (Exception ex)
        {
            LogoMod.Log("switch: Path 开关不可用: " + ex.Message);
            return null;
        }
    }

    /// <summary>隔离：Path.Fill 赋值（Fill setter 若被裁只丢颜色不炸）。</summary>
    private static void TrySetFill(Path path, Windows.UI.Color color)
    {
        try { path.Fill = new SolidColorBrush(color); } catch { }
    }

    /// <summary>隔离：自绘开关外观。轨道 40x22 胶囊 + 滑块 16 圆点。</summary>
    private static SwitchUi BuildSwitchUi()
    {
        const double W = 44, H = 26;
        var canvas = new Canvas();
        TrySetW(canvas, W);
        try { canvas.Height = H; } catch { }

        var track = TryMakePill(40, 22, Windows.UI.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        if (track == null) throw new InvalidOperationException("track null");
        var knob = TryMakePill(16, 16, Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        if (knob == null) throw new InvalidOperationException("knob null");

        try { Canvas.SetLeft(track, 2); Canvas.SetTop(track, 2); } catch { }
        try { Canvas.SetTop(knob, 5); Canvas.SetLeft(knob, 4); } catch { }
        canvas.Children.Add(track);
        canvas.Children.Add(knob);

        var cOn = Windows.UI.Color.FromArgb(0xFF, 0x2E, 0xC5, 0xFF);
        var cOff = Windows.UI.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);
        var kOn = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        var kOff = Windows.UI.Color.FromArgb(0xFF, 0xBF, 0xBF, 0xBF);

        var ui = new SwitchUi { Root = canvas };
        ui.Apply = on =>
        {
            TrySetFill(track, on ? cOn : cOff);
            TrySetFill(knob, on ? kOn : kOff);
            try { Canvas.SetLeft(knob, on ? 24 : 4); } catch { }
        };
        return ui;
    }

    /// <summary>隔离：只含 Slider 引用。</summary>
    private static bool TryAddSlider(StackPanel row, string label, int min, int max, int step,
        Func<int> get, Action<int> set, string unit)
    {
        var fallbackVal = NewTapText("");
        TrySetW(fallbackVal, 70);
        string key = label;
        try
        {
            var slider = new Slider();
            TrySetW(slider, 120);
            slider.Minimum = min;
            slider.Maximum = max;
            try { slider.StepFrequency = step; } catch { }
            slider.Value = get();
            slider.ValueChanged += (s, e) =>
            {
                if (_suppressEvents) return; // 程序回填不触发提交
                TrySetText(fallbackVal, FormatVal(slider.Value, unit));
                // 防抖：停止拖动 350ms 后提交（拖动中不写固件）
                Pending[key] = (DateTime.UtcNow, $"{{\"Action\":\"{ActionOf(label)}\",\"{FieldOf(label)}\":{(int)slider.Value}}}");
            };
            row.Children.Add(slider);
            row.Children.Add(fallbackVal);
            AttachTapEdit(fallbackVal, get, set, min, max); // v9i：点数值直接编辑
            Syncs.Add(() =>
            {
                if (Pending.TryGetValue(key, out var p) && (DateTime.UtcNow - p.At).TotalMilliseconds > 350)
                {
                    Pending.Remove(key);
                    PublishRaw(p.Json);
                }
                // 回填（用户没在拖时）；_suppressEvents 防止 ValueChanged 回环发布
                _suppressEvents = true;
                try { slider.Value = Math.Clamp(get(), min, max); } catch { }
                _suppressEvents = false;
                TrySetText(fallbackVal, FormatVal(get(), unit));
            });
            LogoMod.Log("control: Slider OK (" + label + ")");
            return true;
        }
        catch (Exception ex)
        {
            LogoMod.Log("control: Slider 不可用，走降级 (" + label + "): " + ex.Message);
            return false;
        }
    }

    // ---------- v9i：数值点击编辑（tap 值 → 内联 TextBox → 失焦提交） ----------

    /// <summary>点数值文本：原位换成 TextBox，失焦时解析并提交（Clamp min/max）。
    /// TextBox/FocusState 若被宿主投影裁剪，仅本编辑功能不可用，不影响其它控制。</summary>
    private static void AttachTapEdit(TextBlock val, Func<int> get, Action<int> set, int min, int max, Func<bool>? getEn = null)
    {
        val.Tapped += (s, e) =>
        {
            try { if (getEn != null && !getEn()) return; ShowInlineEditor(val, get, set, min, max); }
            catch (Exception ex) { LogoMod.Log("edit open err: " + ex.Message); }
        };
    }

    /// <summary>隔离：全反射操作 TextBox（设值/宽度/焦点/失焦提交），编译期无 TextBox 引用。</summary>
    private static void ShowInlineEditor(TextBlock val, Func<int> get, Action<int> set, int min, int max)
    {
        if (val.Parent is not StackPanel row) return;
        int idx = row.Children.IndexOf(val);
        if (idx < 0) return;
        var box = TryCreateTextBox();
        if (box is null) return;
        var bt = box.GetType();
        bt.GetProperty("Text")!.SetValue(box, get().ToString());
        bt.GetProperty("Width")!.SetValue(box, 70.0);
        row.Children.RemoveAt(idx);
        row.Children.Insert(idx, (UIElement)box);
        LogoMod.Log("edit open");

        // 提交：失焦时解析并写回，再把原 TextBlock 换回来
        Action commitAction = () =>
        {
            try
            {
                var text = (string)(bt.GetProperty("Text")!.GetValue(box) ?? "");
                if (int.TryParse(text.Trim(), out var n)) set(Math.Clamp(n, min, max));
                else LogoMod.Log("edit: invalid '" + text + "'");
            }
            catch (Exception ex) { LogoMod.Log("edit commit err: " + ex.Message); }
            try
            {
                int i = row.Children.IndexOf((UIElement)box);
                if (i >= 0) row.Children.RemoveAt(i);
                if (!row.Children.Contains(val)) row.Children.Insert(Math.Min(i, row.Children.Count), val);
            }
            catch (Exception ex) { LogoMod.Log("edit swapback err: " + ex.Message); }
        };

        // LostFocus += commit：WinRT 事件是 2 参 TypedEventHandler，
        // 用静态 (object,object) 处理器 + 路由表转发（Action 直接 CreateDelegate 签名不符）
        try
        {
            CommitMap[box] = commitAction;
            var evt = bt.GetEvent("LostFocus");
            var mi = typeof(HomeMod).GetMethod("EditLostFocusHandler",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var handler = Delegate.CreateDelegate(evt.EventHandlerType, null, mi);
            evt.AddEventHandler(box, handler);
        }
        catch (Exception ex) { LogoMod.Log("edit: LostFocus hook " + ex.Message); }

        // 拉起键盘焦点
        try
        {
            // Focus 是方法不是属性；先找带一个参数的重载，再退到无参
            var mi = (System.Reflection.MethodInfo?)null;
            foreach (var m in bt.GetMethods())
                if (m.Name == "Focus" && m.GetParameters().Length == 1) { mi = m; break; }
            if (mi == null) mi = bt.GetMethod("Focus", Type.EmptyTypes);
            if (mi != null)
            {
                var p0 = mi.GetParameters()[0];
                var fsEnum = Enum.Parse(p0.ParameterType, "Programmatic");
                mi.Invoke(box, new object[] { fsEnum });
            }
        }
        catch (Exception ex) { LogoMod.Log("edit: focus " + ex.Message); }
    }

    /// <summary>隔离：反射创建 TextBox（宿主投影裁掉 Windows.UI.Xaml.Controls 编译期命名空间）。
    /// 失败返回 null（编辑功能不可用，不影响其它控制）。</summary>
    private static object? TryCreateTextBox()
    {
        try
        {
            Type abi = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types = null;
                try { types = asm.GetTypes(); }
                catch (Exception ex2)
                {
                    // 不能引用 ReflectionTypeLoadException 类型本身（JIT 解析失败），反射取 Types
                    try { types = ex2.GetType().GetProperty("Types")?.GetValue(ex2) as Type[]; } catch { }
                }
                if (types == null) continue;
                foreach (var ty in types)
                {
                    if (ty == null || ty.Name != "TextBox") continue;
                    if (ty.Namespace != null && ty.Namespace.StartsWith("ABI.")) { abi = ty; break; }
                }
                if (abi != null) break;
            }
            if (abi == null) { LogoMod.Log("edit: ABI TextBox missing"); return null; }
            // 投影类型无公共 ctor、宿主 WinRT.Runtime 也无托管 ActivateInstance
            // → Win32 RoGetActivationFactory + IActivationFactory vtable slot6 激活
            var instPtr = RoActivate("Microsoft.UI.Xaml.Controls.TextBox");
            if (instPtr == IntPtr.Zero) { LogoMod.Log("edit: RoActivate failed"); return null; }
            foreach (var m in abi.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                if (m.Name == "FromAbi" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(IntPtr))
                {
                    var boxed = m.Invoke(null, new object[] { instPtr });
                    LogoMod.Log("edit: FromAbi OK -> " + (boxed == null ? "null" : boxed.GetType().FullName));
                    return boxed;
                }
            LogoMod.Log("edit: FromAbi not found");
            return null;
        }
        catch (Exception ex) { LogoMod.Log("edit: TextBox scan " + ex.Message); return null; }
    }

    [System.Runtime.InteropServices.DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int WindowsCreateString(string src, int length, out IntPtr hstring);

    [System.Runtime.InteropServices.DllImport("combase.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);


    /// <summary>RoGetActivationFactory + IActivationFactory::ActivateInstance（vtable 槽 6）。
    /// 宿主裁掉了 Marshal.ReadIntPtr/GetDelegateForFunctionPointer，改用 unsafe 函数指针。</summary>
    private static unsafe IntPtr RoActivate(string className)
    {
        try
        {
            if (WindowsCreateString(className, className.Length, out var h) != 0)
            {
                LogoMod.Log("edit: WindowsCreateString failed");
                return IntPtr.Zero;
            }
            // 必须请求 IActivationFactory：请求 IInspectable 拿到的是 IInspectable 接口指针，
            // 其 vtable 槽 6 不是 ActivateInstance（踩内存返回指针值）
            var iid = new Guid(0x00000035, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46); // WinRT.Runtime3 的 IID_IActivationFactory
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType("WinRT.ActivationFactory");
                    var vft = t != null ? t.GetNestedType("Vftbl") : null;
                    if (vft == null) continue;
                    var ffi = vft.GetField("PIID", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (ffi != null) { iid = (Guid)ffi.GetValue(null); LogoMod.Log("edit: PIID=" + iid); break; }
                    var ppi = vft.GetProperty("PIID", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (ppi != null) { iid = (Guid)ppi.GetValue(null); LogoMod.Log("edit: PIID=" + iid); break; }
                }
            }
            catch { }
            int hr = RoGetActivationFactory(h, ref iid, out var pf);
            if (hr != 0 || pf == IntPtr.Zero)
            {
                LogoMod.Log("edit: RoGetActivationFactory hr=0x" + hr.ToString("X"));
                return IntPtr.Zero;
            }
            IntPtr vtbl = *(IntPtr*)pf;          // vtable 指针
            IntPtr slot = ((IntPtr*)vtbl)[6];    // ActivateInstance
            LogoMod.Log("edit: pf=0x" + ((long)pf).ToString("X") + " vtbl=0x" + ((long)vtbl).ToString("X") + " slot6=0x" + ((long)slot).ToString("X"));
            var act = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)slot;
            IntPtr inst = IntPtr.Zero;
            hr = act(pf, &inst);
            LogoMod.Log("edit: ActivateInstance hr=0x" + hr.ToString("X") + " inst=0x" + ((long)inst).ToString("X"));
            return hr == 0 ? inst : IntPtr.Zero;
        }
        catch (Exception ex) { LogoMod.Log("edit: RoActivate exc " + ex.Message); return IntPtr.Zero; }
    }

    private static readonly System.Collections.Generic.Dictionary<object, Action> CommitMap
        = new System.Collections.Generic.Dictionary<object, Action>();

    private static void EditLostFocusHandler(object sender, object e)
    {
        try
        {
            if (sender != null && CommitMap.TryGetValue(sender, out var a))
            {
                CommitMap.Remove(sender);
                a();
            }
        }
        catch (Exception ex) { LogoMod.Log("edit: route " + ex.Message); }
    }

    private static string ActionOf(string label) => label switch
    {
        "长时功耗 PL1" => "SPL",
        "短时功耗 PL2" => "SPPT",
        "CPU 降压步数" => "UNDERVOLT",
        "核心偏移" => "GPU_CORE_OFFSET",
        "显存偏移" => "GPU_MEM_OFFSET",
        "锁定频率值" => "GPU_LOCK_MHZ",
        "自定义风扇转速" => "MAX_FAN",
        "温度墙 (温度曲线)" => "TEMP_WALL",
        _ => "NOOP"
    };

    private static string FieldOf(string label) => label switch
    {
        "CPU 降压步数" => "Steps",
        "自定义风扇转速" => "Value",
        "温度墙 (温度曲线)" => "Value",
        _ => "Watts"
    };

    private static string FormatVal(double v, string unit) =>
        unit == "W" ? $"{v:0} W" : unit == "MHz" ? $"{v:0} MHz" : $"{v:0}";

    // ---------- 图表（RadarMap 同款画法） ----------

    private static Path NewChart(Windows.UI.Color color)
    {
        var p = new Path
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 1.5,
            Height = 48,
            Data = new PathGeometry()
        };
        TrySetW(p, 260);
        p.Margin = new Thickness(4);
        return p;
    }

    /// <summary>图表行：Path 直接入列（Border.Child setter 也被裁剪，v5 实测——不套 Border）。</summary>
    private static FrameworkElement ChartBox(Path chart)
    {
        return chart;
    }

    private static void DrawChart(Path? path, List<double> data)
    {
        try
        {
            if (path is null || data.Count < 2) return;
            const double w = 252, h = 44;
            // LINQ Min/Max 也被裁剪（实测）——手写循环
            double min = data[0], max = data[0];
            for (int i = 1; i < data.Count; i++)
            {
                if (data[i] < min) min = data[i];
                if (data[i] > max) max = data[i];
            }
            if (max - min < 1) { min -= 1; max += 1; }
            var fig = new PathFigure { StartPoint = new Point(0, h - (data[0] - min) / (max - min) * h) };
            for (int i = 1; i < data.Count; i++)
            {
                double x = i / (double)(HistoryLen - 1) * w;
                double y = h - (data[i] - min) / (max - min) * h;
                fig.Segments.Add(new LineSegment { Point = new Point(x, y) });
            }
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            path.Data = geo;
        }
        catch (Exception ex) { LogoMod.Log("chart: " + ex.Message); }
    }

    // ============================ 小工具（每个危险 setter 独立方法，JIT 失败可隔离） ============================

    private static void TrySetW(FrameworkElement e, double w) { try { e.Width = w; } catch { } }
    private static void TrySetText(TextBlock tb, string text) { try { tb.Text = text; } catch { } }

    // ============================ MQTT（M2Mqtt，官方 UI 同款） ============================

    private static void StartMqtt()
    {
        if (_mqttStarted) return;
        _mqttStarted = true;
        _ = Task.Run(() => { while (true) { try { MqttLoop(); } catch (Exception ex) { LogoMod.Log("mqtt loop: " + ex.Message); } System.Threading.Thread.Sleep(5000); } });
    }

    /// <summary>v9h：断线自动重连（桥接服务重启会杀连接，旧版只连一次）。每 5 秒重建客户端。</summary>
    private static void MqttLoop()
    {
        var client = new MqttClient("127.0.0.1", 13688, false, null, null, MqttSslProtocols.None);
        _mqtt = client;
        client.MqttMsgPublishReceived += (s, e) =>
        {
            try
            {
                // 只记每个 topic 的首条（遥测每秒 3 条，全记会把日志撑爆——v7 实测教训）
                if (_seenTopics.Add(e.Topic)) LogoMod.Log("mqtt rx: " + e.Topic + " (first)");
                OnMessage(e.Topic, Encoding.UTF8.GetString(e.Message));
            }
            catch (Exception ex) { LogoMod.Log("mqtt rx err: " + ex.Message); }
        };
        client.MqttMsgSubscribed += (s, e) =>
        {
            try { LogoMod.Log("mqtt suback received"); } catch { }
        };
        client.ConnectionClosed += (s, e) =>
        {
            try { LogoMod.Log("mqtt closed: " + e); } catch { }
        };
        client.Connect("HomeMod_" + Guid.NewGuid().ToString("N"),
            "UWPClient_User_5", "UWPClient_Pwd888881772688_5", false, 1);
        client.Subscribe(
            new[] { "JiaoLong/HomeStatus", "System/CpuInfo", "System/GpuInfo", "System/FanInfo" },
            new byte[] { 1, 1, 1, 1 });
        PublishRaw("""{"Action":"GETSTATUS"}""");
        LogoMod.Log("mqtt: connected + subscribed");
        // 阻塞至断开，然后由外层循环重连
        while (client.IsConnected) System.Threading.Thread.Sleep(1000);
        LogoMod.Log("mqtt: disconnected, reconnect in 5s");
    }

    private static void PublishRaw(string json)
    {
        try
        {
            if (_mqtt?.IsConnected != true) { LogoMod.Log("pub skip (not connected)"); return; }
            _mqtt.Publish("JiaoLong/HomeControl", Encoding.UTF8.GetBytes(json), 2, false);
            LogoMod.Log("pub " + json);
        }
        catch (Exception ex) { LogoMod.Log("pub failed: " + ex.Message); }
    }

    private static void Publish(string action, string extra = "")
    {
        PublishRaw("{\"Action\":\"" + action + "\"" + extra + "}");
    }

    private static void OnMessage(string topic, string json)
    {
        try
        {
            lock (StateLock)
            {
                switch (topic)
                {
                    case "JiaoLong/HomeStatus":
                        State.TurboOff = GetBool(json, "TurboOff");
                        State.PowerPlanHigh = GetBool(json, "PowerPlanHigh", true);
                        State.UndervoltSteps = GetInt(json, "UndervoltSteps");
                        State.Spl = GetInt(json, "Spl", 85);
                        State.Sppt = GetInt(json, "Sppt", 120);
                        State.CpuCores = GetInt(json, "CpuCores", 8);
                        State.CpuThreads = GetInt(json, "CpuThreads", 16);
                        State.GpuOcOn = GetBool(json, "GpuOcOn");
                        State.GpuCoreOffset = GetInt(json, "GpuCoreOffset");
                        State.GpuMemOffset = GetInt(json, "GpuMemOffset");
                        State.GpuLockOn = GetBool(json, "GpuLockOn");
                        State.GpuLockMhz = GetInt(json, "GpuLockMhz", 2100);
                        State.GpuLockSupported = GetBool(json, "GpuLockSupported");
                        State.FanMaxRpm = GetInt(json, "FanMaxRpm", 0);
                        State.FanMaxSwitch = GetBool(json, "FanMaxSwitch");
                        State.TempWall = GetInt(json, "TempWall", 95);
                        State.Pl1En = GetBool(json, "Pl1Enabled", true);
                        State.Pl2En = GetBool(json, "Pl2Enabled", true);
                        State.UvEn = GetBool(json, "UndervoltEnabled", true);
                        State.CoreEn = GetBool(json, "GpuCoreOffsetEnabled", true);
                        State.MemEn = GetBool(json, "GpuMemOffsetEnabled", true);
                        State.LockEn = GetBool(json, "GpuLockMhzEnabled", true);
                        State.FanRpmEn = GetBool(json, "FanMaxRpmEnabled", true);
                        State.TempWallEn = GetBool(json, "TempWallEnabled", true);
                        var range = GetArrayRange(json, "GpuLockClocks");
                        if (range.HasValue)
                        {
                            State.GpuLockMin = range.Value.Min;
                            State.GpuLockMax = range.Value.Max;
                        }
                        LogoMod.Log("mqtt: HomeStatus applied (turboOff=" + State.TurboOff + " spl=" + State.Spl + " lock=" + State.GpuLockSupported + ")");
                        break;
                    case "System/CpuInfo":
                        {
                            var f = GetStr(json, "CpuFrequency");
                            if (double.TryParse(f, NumberStyles.Any, CultureInfo.InvariantCulture, out var cf) && cf > 0)
                            {
                                CpuFreqHistory.Add(cf);
                                if (CpuFreqHistory.Count > HistoryLen) CpuFreqHistory.RemoveAt(0);
                            }
                        }
                        break;
                    case "System/GpuInfo":
                        {
                            var f = GetStr(json, "GpuCoreFreq");
                            if (double.TryParse(f, NumberStyles.Any, CultureInfo.InvariantCulture, out var gf) && gf > 0)
                            {
                                GpuFreqHistory.Add(gf);
                                if (GpuFreqHistory.Count > HistoryLen) GpuFreqHistory.RemoveAt(0);
                            }
                        }
                        break;
                    case "System/FanInfo":
                        State.FanCpuRpm = GetStr(json, "CpuFanRpm");
                        State.FanGpuRpm = GetStr(json, "GpuFanRpm");
                        State.FanCpuDuty = GetStr(json, "CpuFanDuty");
                        State.FanGpuDuty = GetStr(json, "GpuFanDuty");
                        break;
                }
            }
        }
        catch (Exception ex) { LogoMod.Log("onmsg " + topic + ": " + ex.Message); }

        // v9d：遥测/状态到达即调度 UI 刷新。实测 DispatcherQueueTimer.Tick 不触发
        //（标签全是冻结值），而 TryEnqueue 在白名单内；遥测每秒 4 条足够流畅。
        TryEnqueueRefresh();
    }

    private static void TryEnqueueRefresh()
    {
        try
        {
            var page = _page;
            if (page is null) return;
            if (System.Threading.Interlocked.CompareExchange(ref _refreshQueued, 1, 0) != 0) return;
            bool ok = page.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    System.Threading.Interlocked.Exchange(ref _refreshQueued, 0);
                    RefreshUi();
                }
                catch { }
            });
            if (ok && !_enqueueLogged)
            {
                _enqueueLogged = true;
                LogoMod.Log("refresh: TryEnqueue path active");
            }
            if (!ok) System.Threading.Interlocked.Exchange(ref _refreshQueued, 0);
        }
        catch { }
    }

    // ---------- 手写扁平 JSON 取值 ----------
    // 宿主裁剪了 System.Text.Json 的 JsonElement.TryGetProperty（v4 实测），
    // 而载荷全是扁平对象——自己扫描，零依赖零风险。

    private static int JsonValueStart(string json, string key)
    {
        var pat = "\"" + key + "\"";
        int i = json.IndexOf(pat, StringComparison.Ordinal);
        if (i < 0) return -1;
        i = json.IndexOf(':', i + pat.Length);
        return i < 0 ? -1 : i + 1;
    }

    private static string JsonValueAt(string json, int vstart)
    {
        while (vstart < json.Length && char.IsWhiteSpace(json[vstart])) vstart++;
        if (vstart >= json.Length) return "";
        if (json[vstart] == '"')
        {
            int end = json.IndexOf('"', vstart + 1);
            return end < 0 ? "" : json.Substring(vstart + 1, end - vstart - 1);
        }
        int e = vstart;
        while (e < json.Length && json[e] != ',' && json[e] != '}')
            e++;
        return json.Substring(vstart, e - vstart).Trim();
    }

    private static bool GetBool(string json, string key, bool fallback = false)
    {
        int i = JsonValueStart(json, key);
        if (i < 0) return fallback;
        var v = JsonValueAt(json, i);
        return v == "true" || v == "True" || v == "1";
    }

    private static int GetInt(string json, string key, int fallback = 0)
    {
        int i = JsonValueStart(json, key);
        if (i < 0) return fallback;
        var v = JsonValueAt(json, i);
        return int.TryParse(v, out var n) ? n : fallback;
    }

    private static string GetStr(string json, string key)
    {
        int i = JsonValueStart(json, key);
        if (i < 0) return "-";
        var v = JsonValueAt(json, i);
        return v.Length == 0 ? "-" : v;
    }

    /// <summary>取数字数组的首尾元素（用于 GpuLockClocks 支持频率范围）。</summary>
    private static (int Min, int Max)? GetArrayRange(string json, string key)
    {
        try
        {
            int i = JsonValueStart(json, key);
            if (i < 0) return null;
            int lb = json.IndexOf('[', i);
            if (lb < 0) return null;
            int rb = json.IndexOf(']', lb);
            if (rb < 0) return null;
            var inner = json.Substring(lb + 1, rb - lb - 1);
            if (inner.Length == 0) return null;
            var parts = inner.Split(',');
            if (!int.TryParse(parts[0].Trim(), out var first)) return null;
            if (!int.TryParse(parts[parts.Length - 1].Trim(), out var last)) return null;
            return (first, last);
        }
        catch { return null; }
    }

    // ============================ UI 定时器 ============================

    private static void StartUiTimer(Page page)
    {
        var timer = page.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.IsRepeating = true;
        timer.Tick += (s, e) =>
        {
            try { RefreshUi(); }
            catch (Exception ex) { LogoMod.Log("ui tick: " + ex.Message); }
        };
        timer.Start();
    }

    private static void RefreshUi()
    {
        // 控件回填
        foreach (var sync in Syncs)
        {
            try { sync(); } catch { }
        }

        // 风扇信息
        string fcr, fgr, fcd, fgd;
        lock (StateLock)
        {
            fcr = State.FanCpuRpm; fgr = State.FanGpuRpm; fcd = State.FanCpuDuty; fgd = State.FanGpuDuty;
        }
        if (_fanCpuRpmText is not null) TrySetText(_fanCpuRpmText, fcr + " RPM");
        if (_fanGpuRpmText is not null) TrySetText(_fanGpuRpmText, fgr + " RPM");
        if (_fanCpuDutyText is not null) TrySetText(_fanCpuDutyText, fcd + " %");
        if (_fanGpuDutyText is not null) TrySetText(_fanGpuDutyText, fgd + " %");

        // 图表
        List<double> cpuHist, gpuHist;
        lock (StateLock)
        {
            cpuHist = new List<double>(CpuFreqHistory);
            gpuHist = new List<double>(GpuFreqHistory);
        }
        DrawChart(_cpuChartPath, cpuHist);
        DrawChart(_gpuChartPath, gpuHist);
    }
}
