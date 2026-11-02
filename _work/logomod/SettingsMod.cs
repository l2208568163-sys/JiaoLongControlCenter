using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace JiaoLongLogoMod;

/// <summary>
/// 设置页定制（由 dnlib 在 WinUIGallery.Pages.SettingsPage 构造函数末尾注入 Init(this)）：
///   1) 「关于」卡片里两枚微信二维码（会员中心 / 售后服务）隐藏；
///   2) 三行官方入口原地改写文案与跳转目标，**保留原有布局/样式/结构**：
///        机械革命官方网站 → 关于作者      → 作者主页
///        驱动下载         → 检查更新      → GitHub Releases
///        机械革命官方售后 → 开源地址      → GitHub 仓库
///
/// 为什么运行时做：官方 XAML 编译进 resources.pri 的 XBF，源文件改不了。
/// 行结构（实测转储）：卡片是 Grid，每行是它的一个子 Grid（右侧那枚 43x33 的
/// HyperlinkButton 才是跳转按钮，整行不是 ButtonBase，行文案是同级的 TextBlock）。
/// 跳转改写：不碰 HyperlinkButton 内部（NavigateUri 可能被投影裁剪），而是在卡片
/// 的同一 Grid 行上盖一层透明点击层 —— 视觉零改动，点击整行都走我们的目标。
/// </summary>
public static class SettingsMod
{
    private const string DoneTag = "JiaoLongSettingsMod";

    private const string UrlAuthor = "https://github.com/l2208568163-sys";
    private const string UrlRepo = "https://github.com/l2208568163-sys/JiaoLongControlCenter";
    private const string UrlReleases = "https://github.com/l2208568163-sys/JiaoLongControlCenter/releases";

    /// <summary>注入点（SettingsPage ctor 尾部）。任何异常都吞掉，绝不拖垮页面。</summary>
    public static void Init(object pageObj)
    {
        try
        {
            InitCore(pageObj);
        }
        catch (Exception ex)
        {
            try { LogoMod.Log("settings: init failed: " + ex.Message); } catch { }
        }
    }

    private static void InitCore(object pageObj)
    {
        if (pageObj is not FrameworkElement page)
            return;

        if (page.Tag as string == DoneTag)
            return;
        page.Tag = DoneTag;

        LogoMod.Log("settings: init (qr removal + entry rewrite v5)");
        _hidden = 0;
        _rewritten = 0;
        _aboutDone = false;
        Handled.Clear();
        Poll(page, 0, 0);
    }

    private static int _hidden;
    private static int _rewritten;
    private static bool _aboutDone;
    private static bool _treeDumped;

    /// <summary>轮询等可视树成型（宿主里 Loaded 不可靠，DispatcherQueueTimer 也不稳）。</summary>
    private static void Poll(FrameworkElement page, int round, int idleRounds)
    {
        int hits;
        try
        {
            hits = ScanAndHide(page);
        }
        catch (Exception ex)
        {
            try { LogoMod.Log("settings: scan error: " + ex.Message); } catch { }
            hits = 0;
        }

        if (hits > 0)
        {
            _hidden += hits;
            idleRounds = 0;
            try { LogoMod.Log($"settings: hidden {hits} qr element(s) at round {round} (total {_hidden})"); } catch { }
        }
        else
        {
            idleRounds++;
        }

        if (!_aboutDone)
        {
            try { _aboutDone = TryRewriteEntries(page); }
            catch (Exception ex) { try { LogoMod.Log("settings: entry rewrite error: " + ex.Message); } catch { } }
        }

        if (!_treeDumped && round == 1)
        {
            _treeDumped = true;
            try { DumpTree(page); } catch (Exception ex) { LogoMod.Log("settings: dump failed: " + ex.Message); }
        }

        var enough = _hidden >= 2 && _aboutDone && idleRounds >= 2;
        if (enough || idleRounds >= 10 || round >= 40)
        {
            try { LogoMod.Log($"settings: poll end at round {round}, qr hidden={_hidden}, entries={_rewritten}"); } catch { }
            return;
        }

        var queue = page.DispatcherQueue;
        if (queue is null)
            return;
        Task.Delay(250).ContinueWith(_ =>
        {
            try { queue.TryEnqueue(() => Poll(page, round + 1, idleRounds)); } catch { }
        });
    }

    private static readonly HashSet<string> Logged = new HashSet<string>();
    private static readonly List<object> Handled = new List<object>();

    // ============================ 1. 二维码隐藏 ============================

    /// <summary>遍历树，隐藏所有 wechat-*.png / store.png；返回本轮新命中数量。</summary>
    private static int ScanAndHide(FrameworkElement page)
    {
        var hits = new List<DependencyObject>();
        Visit(page, 0);
        foreach (var target in hits)
            HideOne(target);
        return hits.Count;

        void Visit(DependencyObject node, int depth)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(node, i) is not { } child)
                    continue;

                // 已经处理过的不再重复计数（不用 Visibility getter：宿主可能裁了）
                if (IsHandled(child))
                    continue;

                if (child is Image image)
                {
                    var uri = UriOf(image.Source);
                    if (uri is not null)
                    {
                        if (Logged.Add(uri))
                            LogoMod.Log($"settings: image {image.ActualWidth:F0}x{image.ActualHeight:F0} {uri}");
                        if (IsQrAsset(uri))
                            hits.Add(child);
                    }
                }

                Visit(child, depth + 1);
            }
        }
    }

    private static bool IsHandled(object node)
    {
        for (var i = 0; i < Handled.Count; i++)
        {
            if (ReferenceEquals(Handled[i], node))
                return true;
        }
        return false;
    }

    private static string UriOf(ImageSource source)
    {
        try
        {
            return source is BitmapImage bmp && bmp.UriSource is not null
                ? bmp.UriSource.OriginalString
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsQrAsset(string uri)
    {
        // 售后服务 = wechat-service-account.png
        // 会员中心 = wechat-subscription-account.png；本机实际用的是 store.png（同一张小程序码）
        return uri.IndexOf("wechat-service-account", StringComparison.OrdinalIgnoreCase) >= 0
            || uri.IndexOf("wechat-subscription-account", StringComparison.OrdinalIgnoreCase) >= 0
            || uri.EndsWith("/store.png", StringComparison.OrdinalIgnoreCase)
            || uri.EndsWith("\\store.png", StringComparison.OrdinalIgnoreCase);
    }

    private static void HideOne(DependencyObject target)
    {
        Handled.Add(target);
        if (TryCollapseContainer(target))
            return;

        try
        {
            if (target is UIElement ui)
            {
                ui.Visibility = Visibility.Collapsed;
                LogoMod.Log("settings: collapsed self only (no safe container)");
            }
        }
        catch (Exception ex)
        {
            try { LogoMod.Log("settings: collapse self failed: " + ex.Message); } catch { }
        }
    }

    /// <summary>从命中元素往上最多 4 层，找最内层的「纯展示」容器并整体 Collapsed。</summary>
    private static bool TryCollapseContainer(DependencyObject target)
    {
        DependencyObject node;
        try { node = VisualTreeHelper.GetParent(target); }
        catch { return false; }

        for (var level = 1; level <= 4 && node is not null; level++)
        {
            var children = DirectChildren(node);
            if (node is FrameworkElement fe && children <= 8 && IsDisplayOnly(node))
            {
                try
                {
                    fe.Visibility = Visibility.Collapsed;
                    LogoMod.Log($"settings: collapsed container level {level} ({node.GetType().Name}, children={children})");
                    return true;
                }
                catch (Exception ex)
                {
                    try { LogoMod.Log("settings: container collapse failed: " + ex.Message); } catch { }
                    return false;
                }
            }
            try { node = VisualTreeHelper.GetParent(node); }
            catch { return false; }
        }
        return false;
    }

    private static int DirectChildren(DependencyObject node)
    {
        try { return VisualTreeHelper.GetChildrenCount(node); }
        catch { return int.MaxValue; }
    }

    private static bool IsDisplayOnly(DependencyObject node)
    {
        var count = DirectChildren(node);
        if (count == int.MaxValue)
            return false;
        for (var i = 0; i < count; i++)
        {
            DependencyObject child;
            try { child = VisualTreeHelper.GetChild(node, i); }
            catch { return false; }
            if (child is null)
                continue;

            if (child is ButtonBase || child is HyperlinkButton || child is ComboBox ||
                child is Slider || child is TextBox || child is ToggleSwitch ||
                child is ListView || child is ScrollViewer || child is Frame ||
                child is NavigationView)
                return false;

            if (child is Image || child is TextBlock || child is Border ||
                child is Microsoft.UI.Xaml.Shapes.Rectangle)
                continue;

            if (child is Panel || child is ContentControl || child is ContentPresenter)
            {
                if (!IsDisplayOnly(child))
                    return false;
                continue;
            }

            return false;
        }
        return true;
    }

    // ============================ 2. 三行入口原地改写 ============================

    private static bool TryRewriteEntries(FrameworkElement page)
    {
        var labels = new List<TextBlock>();
        CollectLabels(page, labels);
        if (labels.Count == 0)
            return false;

        for (var i = 0; i < labels.Count; i++)
        {
            string text;
            try { text = labels[i].Text ?? ""; } catch { continue; }

            string newText, url;
            if (!TryMap(text, out newText, out url))
                continue;

            // 先确认这是「一整行入口」（宽度/高度像行）；二维码下面那枚「售后服务」
            // 标签虽然文案撞词，但找不到行容器，直接跳过，不误改。
            var row = FindRowContainer(labels[i]);
            if (row is null)
                continue;

            // 文案替换（只改这一处文字，其余样式/字号/对齐完全不动）
            try { labels[i].Text = newText; }
            catch (Exception ex) { LogoMod.Log("settings: set text failed: " + ex.Message); continue; }

            var covered = false;
            try { covered = TryCoverRow(row, url, newText); }
            catch (Exception ex) { LogoMod.Log("settings: cover row error: " + ex.Message); }
            _rewritten++;
            LogoMod.Log($"settings: entry '{text}' -> '{newText}' ({url}) covered={covered}");
        }

        // 三行都换掉了就收工；否则继续等布局（文案换成新值后不会再命中，故用计数判断）
        return _rewritten >= 3;
    }

    /// <summary>原文案 → 新文案 + 跳转目标。</summary>
    private static bool TryMap(string text, out string newText, out string url)
    {
        newText = null;
        url = null;
        if (string.IsNullOrEmpty(text))
            return false;

        if (text.IndexOf("官方网站", StringComparison.Ordinal) >= 0
            || text.IndexOf("官网商城", StringComparison.Ordinal) >= 0
            || text.IndexOf("Official", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            newText = "关于作者";
            url = UrlAuthor;
            return true;
        }

        if (text.IndexOf("驱动下载", StringComparison.Ordinal) >= 0
            || text.IndexOf("驱动", StringComparison.Ordinal) >= 0
            || text.IndexOf("Driver", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            newText = "检查更新";
            url = UrlReleases;
            return true;
        }

        if (text.IndexOf("官方售后", StringComparison.Ordinal) >= 0
            || text.IndexOf("售后服务", StringComparison.Ordinal) >= 0
            || text.IndexOf("After-sales", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            newText = "开源地址";
            url = UrlRepo;
            return true;
        }

        return false;
    }

    private static void CollectLabels(DependencyObject node, List<TextBlock> labels)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is not { } child)
                continue;
            if (child is TextBlock tb)
                labels.Add(tb);
            else
                CollectLabels(child, labels);
        }
    }

    /// <summary>从文案往上找「整行」容器：宽度接近卡片宽度、高度是一行的量级。</summary>
    private static DependencyObject FindRowContainer(TextBlock label)
    {
        DependencyObject node;
        try { node = VisualTreeHelper.GetParent(label); }
        catch { return null; }

        for (var level = 0; level < 6 && node is not null; level++)
        {
            if (node is FrameworkElement fe)
            {
                var w = fe.ActualWidth;
                var h = fe.ActualHeight;
                if (w >= 300 && h > 20 && h < 110)
                    return node;
            }
            try { node = VisualTreeHelper.GetParent(node); }
            catch { return null; }
        }
        return null;
    }

    /// <summary>
    /// 在卡片（Grid）的同一行上盖一层透明点击层：视觉零改动，点击整行（含右侧箭头）
    /// 都走我们的目标，且 e.Handled 阻止原跳转按钮再响应。
    /// </summary>
    private static bool TryCoverRow(DependencyObject row, string url, string tag)
    {
        try
        {
            var parent = VisualTreeHelper.GetParent(row);
            if (parent is not Grid card)
            {
                LogoMod.Log("settings: row parent is " + (parent?.GetType().Name ?? "null") + ", skip shield");
                return false;
            }

            var gridRow = 0;
            try { gridRow = Grid.GetRow((FrameworkElement)row); }
            catch { }

            var shield = new Microsoft.UI.Xaml.Shapes.Rectangle();
            try { shield.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)); }
            catch (Exception ex) { LogoMod.Log("settings: shield brush failed: " + ex.Message); return false; }

            var target = url;
            var name = tag;
            shield.Tapped += (s, e) =>
            {
                try { e.Handled = true; } catch { }
                LogoMod.Log($"settings: entry tapped '{name}' -> {target}");
                OpenUrl(target);
            };
            try { ToolTipService.SetToolTip(shield, url); } catch { }

            card.Children.Add(shield);
            // 卡片是单列 Grid（各行子元素实测都是整卡宽度），SetRow 即可铺满整行；
            // Grid.SetColumnSpan 在宿主投影里被裁（实测 MethodNotFound），不可用。
            try { Grid.SetRow(shield, gridRow); }
            catch (Exception ex) { LogoMod.Log("settings: shield SetRow failed: " + ex.Message); }
        }
        catch (Exception ex)
        {
            LogoMod.Log("settings: shield failed: " + ex.Message);
            return false;
        }

        // 跳转按钮的 NavigateUri 顺手也改掉（键盘激活路径）；引用可能被裁剪，
        // 单独 try 隔离，失败不影响上面已经盖好的点击层。
        try { TryRetargetHyperlinks(row, url); } catch { }
        return true;
    }

    /// <summary>顺手把行内跳转按钮的 NavigateUri 也改掉（键盘激活时生效）；被裁剪就只记日志。</summary>
    private static void TryRetargetHyperlinks(DependencyObject row, string url)
    {
        var buttons = new List<HyperlinkButton>();
        CollectHyperlinks(row, buttons);
        foreach (var btn in buttons)
        {
            try
            {
                var before = btn.NavigateUri is null ? "(null)" : btn.NavigateUri.ToString();
                btn.NavigateUri = new Uri(url);
                var after = btn.NavigateUri is null ? "(null)" : btn.NavigateUri.ToString();
                LogoMod.Log($"settings: hyperlink {before} -> {after}");
            }
            catch (Exception ex)
            {
                LogoMod.Log("settings: retarget hyperlink skipped: " + ex.Message);
            }
        }
    }

    private static void CollectHyperlinks(DependencyObject node, List<HyperlinkButton> buttons)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is not { } child)
                continue;
            if (child is HyperlinkButton hb)
                buttons.Add(hb);
            else
                CollectHyperlinks(child, buttons);
        }
    }

    /// <summary>隔离：shell32 ShellExecuteW（永远存在，不依赖被裁剪的 Process.Start）。</summary>
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr ShellExecuteW(IntPtr hwnd, string op, string file, string parameters, string directory, int showCmd);

    private static void OpenUrl(string url)
    {
        try
        {
            var rc = ShellExecuteW(IntPtr.Zero, "open", url, null, null, 5);
            LogoMod.Log("settings: ShellExecute -> " + rc.ToInt64());
        }
        catch (Exception ex)
        {
            LogoMod.Log("settings: ShellExecute failed: " + ex.Message);
        }
    }

    // ============================ 诊断：可视树转储 ============================

    /// <summary>一次性把设置页可视树写进日志（类型/名字/尺寸/文案），排查结构用。</summary>
    private static void DumpTree(FrameworkElement page)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("settings: === tree dump ===");
        Walk(page, 0);
        LogoMod.Log(sb.ToString());

        void Walk(DependencyObject node, int depth)
        {
            if (depth > 22) return;
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(node, i) is not { } child)
                    continue;
                var indent = new string('.', depth);
                var fe = child as FrameworkElement;
                var name = fe?.Name ?? "";
                var w = fe?.ActualWidth ?? 0;
                var h = fe?.ActualHeight ?? 0;
                var text = "";
                if (child is TextBlock tb)
                {
                    try { text = tb.Text ?? ""; } catch { }
                    if (text.Length > 28) text = text.Substring(0, 28);
                }
                sb.AppendLine($"settings: {indent}{child.GetType().Name} '{name}' {w:F0}x{h:F0} {(text.Length > 0 ? "T=" + text : "")}");
                Walk(child, depth + 1);
            }
        }
    }
}
