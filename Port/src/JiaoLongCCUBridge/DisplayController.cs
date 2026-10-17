using System.Runtime.InteropServices;
using System.Text;

namespace JiaoLongCCUBridge;

/// <summary>
/// 显示控制器：刷新率枚举/切换（user32）+ sRGB ICC 校色关联（mscms）。
/// 多屏环境（MuMu/GameViewer 等虚拟显示器）下自动定位物理屏：
/// 枚举适配器，排除镜像驱动与名称含 Virtual 的虚拟显卡，选物理输出。
/// </summary>
public static class DisplayController
{
    // ---------- DISPLAY_DEVICE / DEVMODE ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevicesW(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsExW(string? deviceName, int modeNum, ref DEVMODE devMode, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsExW(string? deviceName, ref DEVMODE devMode, nint hwnd, uint flags, nint lParam);

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint DM_DISPLAYFREQUENCY = 0x400000;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DISPLAY_ATTACHED_TO_DESKTOP = 0x1;
    private const int DISPLAY_MIRRORING_DRIVER = 0x8;

    /// <summary>定位物理屏的设备名（\\.\DISPLAYx）。跳过虚拟显示器/镜像驱动。每次重查（拓扑会变）。</summary>
    public static string? GetPhysicalDeviceName()
    {
        // 1) 常规枚举（用户会话可用；服务 Session 0 下会返回空）
        string? best = null;
        for (uint i = 0; i < 32; i++)
        {
            var dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;
            bool attached = (dd.StateFlags & DISPLAY_ATTACHED_TO_DESKTOP) != 0;
            bool mirror = (dd.StateFlags & DISPLAY_MIRRORING_DRIVER) != 0;
            bool virtualAdapter = dd.DeviceString.Contains("Virtual", StringComparison.OrdinalIgnoreCase);
            if (!attached || mirror || virtualAdapter) continue;
            best ??= dd.DeviceName;
        }
        if (best != null) return best;

        // 2) Session 0 回退：直接扫描 DISPLAY1-9 的模式数据库，取最高刷新率的设备
        //    （物理 240Hz 屏必然胜过 60Hz 虚拟屏）
        string? bestByHz = null;
        int bestHz = 0;
        for (int n = 1; n <= 9; n++)
        {
            string dev = $"\\\\.\\DISPLAY{n}";
            var cur = NewDevMode();
            if (!EnumDisplaySettingsExW(dev, ENUM_CURRENT_SETTINGS, ref cur, 0)) continue;
            int maxHz = 0;
            for (int i = 0; ; i++)
            {
                var m = NewDevMode();
                if (!EnumDisplaySettingsExW(dev, i, ref m, 0)) break;
                if (m.dmPelsWidth == cur.dmPelsWidth && m.dmPelsHeight == cur.dmPelsHeight &&
                    m.dmBitsPerPel == cur.dmBitsPerPel && m.dmDisplayFrequency > maxHz)
                    maxHz = (int)m.dmDisplayFrequency;
            }
            Log.Info($"mode-scan {dev}: {cur.dmPelsWidth}x{cur.dmPelsHeight}@{cur.dmDisplayFrequency} max={maxHz}");
            if (maxHz > bestHz) { bestHz = maxHz; bestByHz = dev; }
        }
        Log.Info($"physical display (mode-scan): {bestByHz ?? "(none)"} bestHz={bestHz}");
        return bestByHz;
    }

    /// <summary>当前刷新率（物理屏）。</summary>
    public static int CurrentHz()
    {
        var dm = NewDevMode();
        var dev = GetPhysicalDeviceName();
        if (EnumDisplaySettingsExW(dev, ENUM_CURRENT_SETTINGS, ref dm, 0))
            return (int)dm.dmDisplayFrequency;
        return 0;
    }

    /// <summary>枚举物理屏在当前分辨率下的去重刷新率列表（降序）。</summary>
    public static List<int> SupportedHz()
    {
        var set = new SortedSet<int>();
        var dev = GetPhysicalDeviceName();
        var current = NewDevMode();
        if (!EnumDisplaySettingsExW(dev, ENUM_CURRENT_SETTINGS, ref current, 0)) return new List<int>();
        for (int i = 0; ; i++)
        {
            var dm = NewDevMode();
            if (!EnumDisplaySettingsExW(dev, i, ref dm, 0)) break;
            if (dm.dmPelsWidth == current.dmPelsWidth && dm.dmPelsHeight == current.dmPelsHeight &&
                dm.dmBitsPerPel == current.dmBitsPerPel && dm.dmDisplayFrequency >= 50)
                set.Add((int)dm.dmDisplayFrequency);
        }
        return set.Reverse().ToList();
    }

    /// <summary>切换物理屏刷新率。</summary>
    public static bool SetHz(int hz)
    {
        var dev = GetPhysicalDeviceName();
        var dm = NewDevMode();
        if (!EnumDisplaySettingsExW(dev, ENUM_CURRENT_SETTINGS, ref dm, 0)) return false;
        dm.dmFields |= DM_DISPLAYFREQUENCY;
        dm.dmDisplayFrequency = (uint)hz;
        bool ok = ChangeDisplaySettingsExW(dev, ref dm, nint.Zero, 0, nint.Zero) == DISP_CHANGE_SUCCESSFUL;
        Log.Info($"SetHz({hz}) on {dev ?? "primary"}: {ok}");
        return ok;
    }

    private static DEVMODE NewDevMode()
    {
        var dm = new DEVMODE();
        dm.dmDeviceName = new string('\0', 32);
        dm.dmFormName = new string('\0', 32);
        dm.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
        return dm;
    }

    // ---------- ICC 校色（sRGB 关联） ----------

    [DllImport("mscms.dll", CharSet = CharSet.Unicode)]
    private static extern bool InstallColorProfileW(string machineName, string profileFileName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode)]
    private static extern bool WcsAssociateColorProfileWithDeviceW(string machineName, string profileFileName, string deviceName);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode)]
    private static extern bool WcsDisassociateColorProfileFromDeviceW(string machineName, string profileFileName, string deviceName);

    private static string SrgbProfilePath => Path.Combine(Environment.SystemDirectory, @"spool\drivers\color\sRGB Color Space Profile.icm");

    public static bool EnableSrgb()
    {
        try
        {
            string profile = SrgbProfilePath;
            if (!File.Exists(profile)) return false;
            InstallColorProfileW(null, profile);
            return WcsAssociateColorProfileWithDeviceW(null, profile, GetPrimaryGdiDeviceName());
        }
        catch (Exception ex)
        {
            Log.Error("sRGB 关联失败: " + ex.Message);
            return false;
        }
    }

    public static bool DisableSrgb()
    {
        try
        {
            string profile = SrgbProfilePath;
            if (!File.Exists(profile)) return false;
            return WcsDisassociateColorProfileFromDeviceW(null, profile, GetPrimaryGdiDeviceName());
        }
        catch (Exception ex)
        {
            Log.Error("sRGB 解除失败: " + ex.Message);
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetICMProfileW(nint hdc, ref uint size, StringBuilder fileName);

    private static string GetPrimaryGdiDeviceName()
    {
        var dc = GetDC(nint.Zero);
        try
        {
            var sb = new StringBuilder(260);
            uint size = 260;
            if (GetICMProfileW(dc, ref size, sb) != 0) return sb.ToString();
            return "\\\\.\\DISPLAY1";
        }
        finally
        {
            ReleaseDC(nint.Zero, dc);
        }
    }
}
