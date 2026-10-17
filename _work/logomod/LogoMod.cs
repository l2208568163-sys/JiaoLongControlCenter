using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace JiaoLongLogoMod;

/// <summary>
/// 主页 Logo 三连击自定义：
/// 由 dnlib 在 HomePage/HomePageForUMA 构造函数末尾注入 LogoMod.Init(this)。
/// 三连击（0.9 秒窗口）弹出文件选择器，选中的图片复制进 LocalState 并
/// 写 LocalSettings，下次启动时自动恢复。
///
/// v2：三连击挂在页面上（冒泡），点在按钮/滑杆/输入框等交互控件上的按下
/// 不计数；命中目标不再决定能否触发，只决定恢复时替换哪张图。选中逻辑
/// 排除铺满整页的背景/覆盖层 Image（如 UpLayer）。首次加载时把可视树
/// Image 清单转储到 %ProgramData%\JiaoLongLogoMod\log.txt 供定位。
/// </summary>
public static class LogoMod
{
    private const string SettingKey = "CustomLogoPath";
    private const string CopiedName = "custom-logo";
    private static readonly TimeSpan MultiClickWindow = TimeSpan.FromMilliseconds(900);
    private const uint RedirectTimeoutMs = 8000;

    private static int _clickCount;
    private static DateTime _lastClick;
    private static DateTime _lastCountedPress;
    private static bool _picking;
    private static Image _logo;
    private static Microsoft.UI.Xaml.Input.PointerRoutedEventArgs _lastArgs;
    private static IntPtr _redirectEventHandle;

    // 触发阈值：以 Tapped 计数。WinUI 把三连击的第 2、3 下识别为双击（Tapped 只发
    // 一次、DoubleTapped 一次），且本宿主投影缺 DoubleTappedEventHandler，所以
    // 3 次物理点击 = 2 次 Tapped；阈值 2 等效于"快速三连击"。
    private const int TriggerThreshold = 2;

    public static void Init(object pageObj)
    {
        try
        {
            Log($"init v9: ctor-time Init called with {pageObj?.GetType().FullName ?? "null"}");
            if (pageObj is not Page page)
                return;

            // 构造函数时机就挂页面级触发（不依赖 Loaded）。
            page.AddHandler(
                UIElement.PointerPressedEvent,
                new Microsoft.UI.Xaml.Input.PointerEventHandler(OnPagePointerPressed),
                true);

            // Loaded 事件在本应用不可靠（桥接在线的启动流程里从不触发），改用
            // 定时器轮询可视树：Logo 出现即认为树就绪，做全部挂载 + 命中测试探针。
            var timer = page.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(800);
            timer.IsRepeating = true;
            var ticks = 0;
            timer.Tick += (s, e) =>
            {
                try
                {
                    ticks++;
                    if (page.Tag as string == "JiaoLongLogoMod")
                    {
                        timer.Stop();
                        return;
                    }
                    var root = page.Content;
                    var children = root is null ? 0 : VisualTreeHelper.GetChildrenCount(root);
                    if (ticks is 1 or 5 or 15)
                        Log($"poll #{ticks}: content={root?.GetType().Name ?? "null"} children={children} pageW={page.ActualWidth:F0}");
                    if (children == 0)
                        return;
                    _logo = FindLogoImage(page);
                    if (_logo is null)
                        return;
                    timer.Stop();
                    Log($"tree ready after {ticks} poll(s)");
                    OnTreeReady(page);
                }
                catch (Exception tex)
                {
                    Log("poll: " + tex.Message);
                    timer.Stop();
                }
            };
            timer.Start();
            Log("init v9: bootstrap timer started");

            page.Loaded += OnPageLoaded;

            // 主页监控卡控制项（齿轮面板 + MQTT），与本 Logo 功能同一注入点
            try { HomeMod.Init(page); }
            catch (Exception hex) { Log("home init: " + hex.Message); }
        }
        catch (Exception ex)
        {
            Log("init: " + ex.Message);
        }
    }

    /// <summary>Loaded 兜底路径（部分环境会触发；Tag 防重入）。</summary>
    private static void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Page page && page.Tag as string != "JiaoLongLogoMod")
            {
                Log("loaded fallback fired");
                _logo = FindLogoImage(page);
                OnTreeReady(page);
            }
        }
        catch (Exception ex)
        {
            Log("OnPageLoaded: " + ex.Message);
        }
    }

    /// <summary>可视树就绪后的全部挂载（由轮询或 Loaded 驱动，Tag 防重入）。</summary>
    private static void OnTreeReady(Page page)
    {
        page.Tag = "JiaoLongLogoMod";

        DumpImages(page);

        if (_logo is not null)
        {
            ToolTipService.SetToolTip(_logo, "连点 3 次：自定义图标");
            // 主路径：直接挂 Logo 图片自身——PointerPressed 在命中元素上最先触发，
            // WinUI3 里 ScrollViewer 等容器会捕获/吞掉向上冒泡的路由（挂根收不到，
            // v2-v5 全军覆没的原因）。每个钩子独立容错，一个失败不影响其余。
            try { _logo.PointerPressed -= OnPagePointerPressed; _logo.PointerPressed += OnPagePointerPressed; }
            catch (Exception ex) { Log("hook pointerpressed: " + ex.Message); }
            try { _logo.Tapped -= OnLogoTapped; _logo.Tapped += OnLogoTapped; }
            catch (Exception ex) { Log("hook tapped: " + ex.Message); }
            // 双击按 +2 计数（Tapped 对双击的第二下不触发，3 连击 = Tapped1 + DoubleTapped2）。
            // 危险引用隔离：DoubleTappedEventHandler 在宿主投影里可能不存在，
            // 引用必须放在独立小方法里，否则整个 OnTreeReady 无法 JIT。
            try { HookDoubleTappedSafe(_logo); }
            catch (Exception ex) { Log("hook doubletapped: " + ex.Message); }
            Restore(page, _logo);
            Log($"chosen logo: {_logo.ActualWidth:F0}x{_logo.ActualHeight:F0} (direct+gesture handlers attached)");

            // 祖先链逐级挂钩：本次物理点击究竟命中谁、被哪层吞掉，日志会给出身份；
            // 任何一层收到 PointerPressed 都会计数（args 引用去重防重复）。
            try
            {
                var anc = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(_logo);
                while (anc is not null && !ReferenceEquals(anc, page))
                {
                    if (anc is UIElement ue)
                    {
                        var captured = anc;
                        ue.PointerPressed -= OnPagePointerPressed;
                        ue.PointerPressed += OnPagePointerPressed;
                        Log($"ancestor hook: {anc.GetType().Name}");
                    }
                    anc = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(anc);
                }
            }
            catch (Exception aex)
            {
                Log("ancestor hooks: " + aex.Message);
            }
        }
        else
        {
            Log("no logo image found; root fallback only");
        }

        // 兜底：窗口内容根上加 handledEventsToo 处理器（部分表面可能直达根）。
        var root = page.XamlRoot?.Content;
        Log($"xamlroot content: {root?.GetType().FullName ?? "null"}");
        if (root is UIElement rootUi && (root as FrameworkElement)?.Tag as string != "JiaoLongLogoMod")
        {
            if (root is FrameworkElement fe) fe.Tag = "JiaoLongLogoMod";
            rootUi.AddHandler(
                UIElement.PointerPressedEvent,
                new Microsoft.UI.Xaml.Input.PointerEventHandler(OnPagePointerPressed),
                true);
        }
    }

    /// <summary>隔离 DoubleTapped 挂钩（宿主投影可能没有该类型）。</summary>
    private static void HookDoubleTappedSafe(Image logo)
    {
        logo.DoubleTapped -= OnLogoDoubleTapped;
        logo.DoubleTapped += OnLogoDoubleTapped;
    }

    /// <summary>Logo 手势备用路径：Tapped（抬手无拖动）。</summary>
    private static void OnLogoDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        => OnLogoTappedCore(2);

    private static void OnLogoTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => OnLogoTappedCore(1);

    private static void OnLogoTappedCore(int weight)
    {
        try
        {
            Log($"tapped on logo (weight {weight})");
            CountPressAndMaybeTrigger(weight);
        }
        catch (Exception ex)
        {
            Log("tapped handler: " + ex.Message);
        }
    }

    private static void CountPressAndMaybeTrigger(int weight = 1)
    {
        var now = DateTime.UtcNow;
        _clickCount = (now - _lastClick) <= MultiClickWindow ? _clickCount + weight : Math.Min(weight, TriggerThreshold);
        _lastClick = now;
        _lastCountedPress = now;
        if (_clickCount >= TriggerThreshold && !_picking)
        {
            _clickCount = 0;
            _picking = true;
            // 兜底：若对话框流程异常退出，5 秒后解除触发锁
            _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => { _picking = false; });
            Log("TRIGGER picker");
            PickAndApplyBlocking(_logo);
        }
    }

    private static void OnPagePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            // 去重：同一次按下的路由（Logo 直连 + 根兜底）共享同一 args 对象，只计一次
            if (ReferenceEquals(e, _lastArgs))
                return;
            _lastArgs = e;

            var src = e.OriginalSource as DependencyObject;
            var filtered = IsInteractiveSource(src, sender as DependencyObject);
            Log($"press: src={src?.GetType().Name ?? "null"} filtered={filtered} count_was={_clickCount} picking={_picking}");
            if (filtered)
                return;

            CountPressAndMaybeTrigger();
        }
        catch (Exception ex)
        {
            Log("press handler: " + ex.Message);
            _picking = false;
        }
    }

    /// <summary>按下点是否落在交互控件上（沿可视树向上找，到根为止）。</summary>
    private static bool IsInteractiveSource(DependencyObject node, DependencyObject root)
    {
        try
        {
            while (node is not null && !ReferenceEquals(node, root))
            {
                if (node is ButtonBase || node is ComboBox || node is Slider ||
                    node is TextBox || node is ToggleSwitch ||
                    node is ListViewItem || node is GridViewItem || node is ComboBoxItem ||
                    node is Microsoft.UI.Xaml.Controls.NavigationViewItem)
                    return true;
                node = VisualTreeHelper.GetParent(node);
            }
        }
        catch
        {
        }
        return false;
    }

    /// <summary>
    /// 选"恢复目标"图：Source 非空、宽 ≥100、面积 ≥10000，且不是铺满整页
    /// （宽高都 ≥ 页面 80%）的背景/覆盖层；在剩余里取面积最大的。
    /// </summary>
    private static Image FindLogoImage(Page page)
    {
        var pw = page.ActualWidth;
        var ph = page.ActualHeight;
        Image best = null;
        double bestArea = 0;
        Visit(page.Content);
        return best;

        void Visit(DependencyObject node)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(node, i) is not { } child)
                    continue;
                if (child is Image img && img.Source is not null)
                {
                    var w = img.ActualWidth;
                    var h = img.ActualHeight;
                    var full = pw > 0 && ph > 0 && w >= pw * 0.8 && h >= ph * 0.8;
                    Log($"image {w:F0}x{h:F0} at ({Canvas.GetLeft(img):F0},{Canvas.GetTop(img):F0}) full={full} src={DescribeSource(img)}");
                    if (!full && w >= 100 && w * h >= 10000 && w * h > bestArea)
                    {
                        bestArea = w * h;
                        best = img;
                    }
                }
                Visit(child);
            }
        }
    }

    private static string DescribeSource(Image img)
    {
        try
        {
            return img.Source switch
            {
                BitmapImage b => b.UriSource?.ToString() ?? "(bitmap)",
                { } s => s.GetType().Name,
                _ => "null"
            };
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>把页面可视树里的所有 Image 转储到日志文件（每次启动覆盖）。</summary>
    private static void DumpImages(Page page)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} page={page.GetType().FullName} size={page.ActualWidth:F0}x{page.ActualHeight:F0} ===");
            void Visit(DependencyObject node, int depth)
            {
                var count = VisualTreeHelper.GetChildrenCount(node);
                for (var i = 0; i < count; i++)
                {
                    if (VisualTreeHelper.GetChild(node, i) is not { } child)
                        continue;
                    if (child is Image img && img.Source is not null)
                        sb.AppendLine($"{new string(' ', depth * 2)}Image {img.ActualWidth:F0}x{img.ActualHeight:F0} name={((img as FrameworkElement)?.Name ?? "")} src={DescribeSource(img)}");
                    else if (child is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
                        sb.AppendLine($"{new string(' ', depth * 2)}{child.GetType().Name} '{fe.Name}' {fe.ActualWidth:F0}x{fe.ActualHeight:F0}");
                    Visit(child, depth + 1);
                }
            }
            Visit(page.Content, 0);
            Log(sb.ToString());
        }
        catch (Exception ex)
        {
            Log("DumpImages: " + ex.Message);
        }
    }

    internal static void Log(string text)
    {
        // 双路写：ProgramData 失败（权限/被占用）时回落到用户 LocalAppData，
        // 保证诊断日志永远有去处。
        var lines = text + Environment.NewLine;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "JiaoLongLogoMod");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "log.txt"), lines);
            return;
        }
        catch { }
        try
        {
            var dir2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JiaoLongLogoMod");
            Directory.CreateDirectory(dir2);
            File.AppendAllText(Path.Combine(dir2, "log.txt"), lines);
        }
        catch { }
    }

    /// <summary>状态目录：ProgramData 下的 JiaoLongLogoMod（日志已证明该目录可写）。</summary>
    private static string StateDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "JiaoLongLogoMod");
    }

    /// <summary>
    /// 选图 + 应用（阻塞版）：宿主的 WinRT 投影是按需精简的，FileOpenPicker 全家
    /// 都不存在（应用自己从不选文件），所以用 comdlg32 的 GetOpenFileNameW ——
    /// 纯 Win32，必然可用。模态对话框自带消息泵，在 UI 线程调用安全。
    /// 宿主经 IL 裁剪后 File.Copy 等成员不存在，因此不复制文件——直接记录
    /// 原图路径并让 Logo 引用它（仅用宿主实测存在的 API：WriteAllText/Exists）。
    /// </summary>
    private static void PickAndApplyBlocking(Image logo)
    {
        try
        {
            var file = ShowOpenFileDialog();
            Log($"picker: returned {(string.IsNullOrEmpty(file) ? "null(取消)" : file)}");
            if (string.IsNullOrEmpty(file))
                return;

            try { SaveCustomPathReg(file); Log("picker: path saved to registry"); }
            catch (Exception rex) { Log("picker: registry save failed: " + rex.Message); }

            if (logo is not null)
                ApplyLogoSource(logo, file);
        }
        catch (Exception ex)
        {
            Log("PickAndApply: " + ex.Message);
        }
        finally
        {
            _picking = false;
        }
    }

    private static void ApplyLogoSource(Image logo, string path)
    {
        try
        {
            logo.Source = new BitmapImage(new Uri(path));
            Log("apply: logo replaced OK");
        }
        catch (Exception ex)
        {
            Log("apply: " + ex.Message);
        }
    }

    private static void Restore(Page page, Image logo)
    {
        try
        {
            string path = null;
            try { path = LoadCustomPathReg(); }
            catch (Exception rex) { Log("restore: registry read failed: " + rex.Message); }
            if (string.IsNullOrEmpty(path))
                return;
            if (!File.Exists(path))
            {
                Log("restore: file missing, skip: " + path);
                return;
            }
            ApplyLogoSource(logo, path);
            Log("restore: applied " + path);
        }
        catch (Exception ex)
        {
            Log("restore: " + ex.Message);
        }
    }

    /// <summary>
    /// 持久化用原生 advapi32 P/Invoke：宿主投影连 RegistryKey.SetValue 都被裁掉了
    /// （应用只读注册表），而系统 DLL 的导出函数永远不会缺。
    /// </summary>
    private static void SaveCustomPathReg(string path)
    {
        var hkey = IntPtr.Zero;
        try
        {
            // KEY_CREATE_SUB_KEY... 用 KEY_ALL_ACCESS 简化；HKCU 下创建子键
            if (RegCreateKeyExW(HKEY_CURRENT_USER, "Software\\JiaoLongLogoMod", 0, null, 0,
                    0x000F003F /*KEY_ALL_ACCESS*/, 0, out hkey, out _) != 0)
                throw new Exception("RegCreateKeyExW failed");
            var data = (path + "\0").ToCharArray();
            var cb = data.Length * 2;
            var bytes = new byte[cb];
            for (var i = 0; i < data.Length; i++)
            {
                bytes[i * 2] = (byte)data[i];
                bytes[i * 2 + 1] = (byte)(data[i] >> 8);
            }
            if (RegSetValueExW(hkey, "CustomLogoPath", 0, 1 /*REG_SZ*/, bytes, cb) != 0)
                throw new Exception("RegSetValueExW failed");
        }
        finally
        {
            if (hkey != IntPtr.Zero) RegCloseKey(hkey);
        }
    }

    private static string LoadCustomPathReg()
    {
        var cb = 1024;
        var bytes = new byte[cb];
        var type = 0;
        var rc = RegGetValueW(HKEY_CURRENT_USER, "Software\\JiaoLongLogoMod", "CustomLogoPath",
            0x00000002 /*RRF_RT_REG_SZ*/, out type, bytes, ref cb);
        if (rc != 0 || cb < 2)
            return null;
        // UTF-16LE → string（手工解码，避开可能被裁的 Encoding/Text 成员）
        var chars = new char[cb / 2];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = (char)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
        var s = new string(chars);
        var z = s.IndexOf('\0');
        return z >= 0 ? s.Substring(0, z) : s;
    }

    private static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegCreateKeyExW(IntPtr hKey, string subKey, int reserved, string cls, int options, int samDesired, IntPtr securityAttributes, out IntPtr hResult, out int disposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegSetValueExW(IntPtr hKey, string valueName, int reserved, int type, byte[] data, int cbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegGetValueW(IntPtr hKey, string subKey, string valueName, int flags, out int type, byte[] data, ref int cbData);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr hKey);

    // ---------- comdlg32 文件对话框 ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAMEW
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAMEW ofn);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static string ShowOpenFileDialog()
    {
        var owner = GetForegroundWindow();
        Log($"picker: owner hwnd=0x{owner.ToInt64():X}");
        var filter = "图片文件\0*.png;*.jpg;*.jpeg;*.bmp;*.webp\0所有文件\0*.*\0";
        var fileBuf = new string('\0', 520);
        var ofn = new OPENFILENAMEW
        {
            lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
            hwndOwner = owner,
            lpstrFilter = filter,
            lpstrFile = fileBuf,
            nMaxFile = 520,
            lpstrTitle = "选择 Logo 图片",
            Flags = 0x1000 | 0x800 | 0x4 // OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY
        };
        var ok = GetOpenFileNameW(ref ofn);
        if (!ok)
            return null;
        return ofn.lpstrFile;
    }

    // ---------- 单实例重定向（替代 Program.DecideRedirection） ----------

    /// <summary>返回 true 表示已把激活交给现有实例，本进程应直接退出。</summary>
    public static bool DecideRedirectionHook()
    {
        try
        {
            var appInstance = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent();
            var args = appInstance.GetActivatedEventArgs();
            var instance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("MySingleInstanceApp");
            if (instance.IsCurrent)
            {
                instance.Activated += OnRedirectedActivation;
                return false;
            }

            var pid = (int)instance.ProcessId;

            // 关键防线：残留僵尸进程连一个顶层窗口都没有（隐藏窗口也算有）。
            // 对僵尸调用 RedirectActivationToAsync 会抛 stowed exception 直接杀掉本进程，
            // 所以先看目标进程有没有窗口——一个都没有就自己接管运行。
            bool targetHasWindow = HasAnyTopLevelWindow(pid);
            if (!targetHasWindow)
            {
                return false;
            }

            _redirectEventHandle = CreateEvent(IntPtr.Zero, true, false, null);
            var redirected = false;
            Task.Run(() =>
            {
                try { instance.RedirectActivationToAsync(args).AsTask().Wait(); redirected = true; }
                catch { }
                try { SetEvent(_redirectEventHandle); } catch { }
            });

            var wait = CoWaitForMultipleObjects(0, RedirectTimeoutMs, 1, new IntPtr[] { _redirectEventHandle }, out _);
            if (wait != 0 || !redirected) // 超时或重定向失败 —— 自己接管
            {
                return false;
            }

            // 重定向成功：把现有实例的窗口拉到前台
            try
            {
                var h = Process.GetProcessById(pid).MainWindowHandle;
                if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
            }
            catch { }
            return true;
        }
        catch
        {
            return false; // 任何异常都宁可启动新实例，绝不静默退出
        }
    }

    private static void OnRedirectedActivation(object sender, Microsoft.Windows.AppLifecycle.AppActivationArguments args)
    {
        try
        {
            var h = Process.GetCurrentProcess().MainWindowHandle;
            if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
        }
        catch { }
    }

    /// <summary>目标进程是否拥有至少一个顶层窗口（含隐藏窗口）。僵尸进程一个都没有。</summary>
    private static bool HasAnyTopLevelWindow(int pid)
    {
        try
        {
            _enumTargetPid = (uint)pid;
            _enumFound = false;
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var owner);
                if (owner == _enumTargetPid) { _enumFound = true; return false; }
                return true;
            }, IntPtr.Zero);
            return _enumFound;
        }
        catch
        {
            return true; // 查不到就按"有窗口"处理，走原有重定向路径
        }
    }

    private static uint _enumTargetPid;
    private static bool _enumFound;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr attrs, bool manualReset, bool initialState, string name);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr h);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint flags, uint timeoutMs, ulong handles, IntPtr[] pHandles, out uint index);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr h, int cmd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr h);
}
