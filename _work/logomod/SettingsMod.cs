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
/// 设置页「关于」卡片里的两枚微信二维码（会员中心 / 售后服务）隐藏。
/// 由 dnlib 在 WinUIGallery.Pages.SettingsPage 构造函数末尾注入 SettingsMod.Init(this)。
///
/// 为什么运行时做：官方 XAML 编译进 resources.pri 的 XBF，源文件不可改；
/// 而二维码是 Assets/Image/wechat-*.png，靠 BitmapImage.UriSource 即可精确识别。
/// 隐藏策略：从命中元素往上找最内层的「安全容器」（子树只有 Image/TextBlock/
/// Border/Grid/StackPanel/Rectangle），整个容器 Collapsed —— 二维码和它下面的
/// 名字一起消失，行高自动塌陷，不留空洞。
/// 诊断：每次扫描把可视树里所有 Image / ImageBrush 的 URI 写进 log.txt。
/// </summary>
public static class SettingsMod
{
    private const string DoneTag = "JiaoLongSettingsMod";

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

        LogoMod.Log("settings: init (qr removal + about rewrite v4)");
        _hidden = 0;
        _aboutDone = false;
        Handled.Clear();
        Poll(page, 0, 0);
    }

    private static int _hidden;

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

        // 关于卡片：删三个官网入口、插入 GitHub 仓库入口（一次成功后置位）
        if (!_aboutDone)
        {
            try { _aboutDone = TryRewriteAbout(page); }
            catch (Exception ex) { try { LogoMod.Log("settings: about rewrite error: " + ex.Message); } catch { } }
        }

        // 两枚二维码 + 关于卡片都处理完（或连续 10 轮没进展 / 满 40 轮）就收工
        var enough = _hidden >= 2 && _aboutDone && idleRounds >= 2;
        if (enough || idleRounds >= 10 || round >= 40)
        {
            try { LogoMod.Log($"settings: poll end at round {round}, qr hidden={_hidden}, aboutDone={_aboutDone}"); } catch { }
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

    // ============================ 关于卡片重写 ============================
    // 官方「关于」卡片里三行入口（官方网站 / 驱动下载 / 官方售后）在 XBF 里写死，
    // 运行时按可见文本命中后整体 Collapsed，并在原位置插入我们自己的 GitHub 行。

    private const string RepoUrl = "https://github.com/l2208568163-sys/JiaoLongControlCenter";
    private static bool _aboutDone;
    private static bool _aboutDumped;

    private static bool TryRewriteAbout(FrameworkElement page)
    {
        var rows = new List<DependencyObject>();
        var texts = new List<string>();
        CollectRows(page, rows, texts);
        if (rows.Count == 0)
            return false; // 树还没成型，等下一轮

        if (!_aboutDumped)
        {
            _aboutDumped = true;
            for (var i = 0; i < rows.Count; i++)
            {
                var t = texts[i] ?? "";
                if (t.Length > 40) t = t.Substring(0, 40);
                LogoMod.Log($"settings: about clickable[{i}] {rows[i].GetType().Name} '{t}'");
            }
        }

        DependencyObject firstRow = null;
        var matched = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!IsOfficialLink(texts[i]))
                continue;
            matched++;
            if (firstRow is null)
                firstRow = rows[i];
            CollapseNode(rows[i]);
        }

        if (firstRow is null)
        {
            if (!_aboutDone)
                LogoMod.Log("settings: about: no official link row matched yet");
            return false;
        }

        LogoMod.Log($"settings: about: collapsed {matched} official link row(s)");

        try
        {
            var parent = VisualTreeHelper.GetParent(firstRow);
            if (parent is not Panel panel)
            {
                LogoMod.Log("settings: about: parent is " + (parent?.GetType().Name ?? "null"));
                return true;
            }

            var index = 0;
            try
            {
                var found = panel.Children.IndexOf((UIElement)firstRow);
                if (found >= 0) index = found;
            }
            catch { }

            var row = BuildRepoRow();
            if (row is null)
                return true;
            panel.Children.Insert(index, row);
            LogoMod.Log($"settings: about: github row inserted at {index}/{panel.Children.Count} of {panel.GetType().Name}");
        }
        catch (Exception ex)
        {
            LogoMod.Log("settings: about insert failed: " + ex.Message);
        }
        return true;
    }

    /// <summary>收集页面里所有可点击行（ButtonBase 派生：Button / HyperlinkButton）。</summary>
    private static void CollectRows(DependencyObject node, List<DependencyObject> rows, List<string> texts)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is not { } child)
                continue;
            if (child is ButtonBase)
            {
                rows.Add(child);
                texts.Add(TextOf(child, 0));
            }
            else
            {
                CollectRows(child, rows, texts);
            }
        }
    }

    private static string TextOf(DependencyObject node, int depth)
    {
        if (depth > 12)
            return "";
        var sb = new System.Text.StringBuilder();
        Append(node, 0);
        return sb.ToString();

        void Append(DependencyObject n, int d)
        {
            if (d > 12) return;
            if (n is TextBlock tb)
            {
                try { sb.Append(tb.Text); } catch { }
                return;
            }
            var count = VisualTreeHelper.GetChildrenCount(n);
            for (var i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(n, i) is { } c)
                    Append(c, d + 1);
            }
        }
    }

    private static bool IsOfficialLink(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        return text.IndexOf("官方网站", StringComparison.Ordinal) >= 0
            || text.IndexOf("官网商城", StringComparison.Ordinal) >= 0
            || text.IndexOf("驱动下载", StringComparison.Ordinal) >= 0
            || text.IndexOf("官方售后", StringComparison.Ordinal) >= 0
            || text.IndexOf("售后服务", StringComparison.Ordinal) >= 0
            || text.IndexOf("Official", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("Driver", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("After-sales", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void CollapseNode(DependencyObject node)
    {
        try
        {
            if (node is UIElement ui)
                ui.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            LogoMod.Log("settings: collapse row failed: " + ex.Message);
        }
    }

    /// <summary>GitHub 入口行：紫底描边卡片 + 标题 + 仓库地址 + 打开提示，整块可点。</summary>
    private static FrameworkElement BuildRepoRow()
    {
        try
        {
            var root = new Grid();
            try { root.Margin = new Thickness(0, 2, 0, 10); } catch { }

            var bg = new Microsoft.UI.Xaml.Shapes.Rectangle();
            try { bg.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0x38, 0x8B, 0x5C, 0xF6)); } catch { }
            try { bg.Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 0xB1, 0x8C, 0xFF)); } catch { }
            try { bg.StrokeThickness = 1; } catch { }
            try { bg.RadiusX = 10; bg.RadiusY = 10; } catch { }
            try { root.Children.Add(bg); } catch { }

            var stack = new StackPanel();
            try { stack.Orientation = Orientation.Vertical; } catch { }
            try { stack.Margin = new Thickness(14, 9, 14, 10); } catch { }

            var title = new TextBlock { Text = "GitHub 开源仓库（JiaoLongControlCenter）", FontSize = 15 };
            try { title.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xD3, 0xC2, 0xFF)); } catch { }
            try { stack.Children.Add(title); } catch { }

            var url = new TextBlock { Text = RepoUrl, FontSize = 12 };
            try { url.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x9E, 0xE8, 0xFF)); } catch { }
            try { url.Margin = new Thickness(0, 3, 0, 0); } catch { }
            try { stack.Children.Add(url); } catch { }

            var hint = new TextBlock { Text = "点击在浏览器中打开 ↗", FontSize = 12 };
            try { hint.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)); } catch { }
            try { hint.Margin = new Thickness(0, 2, 0, 0); } catch { }
            try { stack.Children.Add(hint); } catch { }

            try { root.Children.Add(stack); } catch { }

            try { root.Tapped += (s, e) => { LogoMod.Log("settings: github row tapped"); OpenUrl(RepoUrl); }; } catch { }
            try { ToolTipService.SetToolTip(root, RepoUrl); } catch { }
            return root;
        }
        catch (Exception ex)
        {
            LogoMod.Log("settings: build repo row failed: " + ex.Message);
            return null;
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

    /// <summary>遍历树，隐藏所有 wechat-*.png；返回本轮新命中数量。</summary>
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
                        if (IsWeChatQr(uri))
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

    private static bool IsWeChatQr(string uri)
    {
        // 售后服务 = wechat-service-account.png
        // 会员中心 = wechat-subscription-account.png；本机上实际用的是 store.png（同一张小程序码）
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

        // 兜底：只塌自己
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

    /// <summary>子树里只允许纯展示元素（没有按钮/输入/链接），否则视为不能整体塌。</summary>
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

            // 布局容器继续下钻；其他类型一律保守放弃
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
}
