using System.Runtime.InteropServices;

namespace JiaoLongCCUBridge;

/// <summary>
/// 设备开关（WiFi/蓝牙/摄像头）：SetupAPI 按 类+FriendlyName 匹配设备后
/// DICS_ENABLE/DICS_DISABLE 启用/禁用（官方 CCU 的 Command\*.ps1 同语义）。
/// </summary>
public static class DeviceToggles
{
    private const uint DIGCF_PRESENT = 0x2;
    private const uint DIGCF_ALLCLASSES = 0x4;
    private const uint SPDRP_DEVICEDESC = 0x0;
    private const uint SPDRP_CLASS = 0x6;
    private const uint SPDRP_FRIENDLYNAME = 0xC;
    private const uint DICS_FLAG_CONFIGSPECIFIC = 2;
    private const uint DICS_ENABLE = 1;
    private const uint DICS_DISABLE = 2;
    private const uint DICS_FLAG_GLOBAL = 1;
    private const uint DIF_PROPERTYCHANGE = 0x12;
    private const int CM_PROB_DISABLED = 22;
    private const uint DN_HAS_PROBLEM = 0x400;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_CLASSINSTALL_HEADER
    {
        public int cbSize;
        public uint InstallFunction;
        public IntPtr hwndParent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_PROPCHANGE_PARAMS
    {
        public SP_CLASSINSTALL_HEADER ClassInstallHeader;
        public uint StateChange;
        public uint Scope;
        public uint HwProfile;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, IntPtr enumerator, IntPtr hwnd, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr h, uint index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr h, ref SP_DEVINFO_DATA data, uint property, out uint regType, byte[] buffer, uint size, out uint required);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiSetClassInstallParamsW(IntPtr h, ref SP_DEVINFO_DATA data, IntPtr paramsBuf, int size);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr h, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Disable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Enable_DevNode(uint devInst, uint flags);

    private static readonly Guid GuidNet = new("4D36E972-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid GuidBluetooth = new("E0CBF06C-CD8B-4647-BB8A-263B43F0F974");
    private static readonly Guid GuidCamera = new("CA3E7AB9-B4C3-4AE6-8251-579EF933890F");
    private static readonly Guid GuidImage = new("6BDD1FC6-810F-11D0-BEC7-08002BE2092F");

    public sealed class DevInfo
    {
        public string Name = "";
        public string Class = "";
        public uint DevInst;
        public bool Disabled;
    }

    private static string? ReadString(IntPtr h, ref SP_DEVINFO_DATA data, uint property)
    {
        // 两段式：先查所需大小
        if (!SetupDiGetDeviceRegistryPropertyW(h, ref data, property, out _, Array.Empty<byte>(), 0, out uint needed))
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 234 && err != 122) // 非 INSUFFICIENT_BUFFER 则真失败
                return null;
        }
        if (needed == 0) return null;
        var buf = new byte[needed];
        if (!SetupDiGetDeviceRegistryPropertyW(h, ref data, property, out _, buf, needed, out _))
        {
            Log.Error($"ReadString prop={property} err={Marshal.GetLastWin32Error()}");
            return null;
        }
        string s = System.Text.Encoding.Unicode.GetString(buf, 0, (int)needed);
        return s.TrimEnd('\0');
    }

    /// <summary>调试：列出前 N 台设备（类名 + 名称）。</summary>
    public static List<DevInfo> DebugList(int max)
    {
        var result = new List<DevInfo>();
        IntPtr h = SetupDiGetClassDevsW(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (h == IntPtr.Zero || h == new IntPtr(-1))
        {
            Log.Error($"DebugList: SetupDiGetClassDevsW failed err={Marshal.GetLastWin32Error()}");
            return result;
        }
        try
        {
            for (uint i = 0; i < (uint)max; i++)
            {
                var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiEnumDeviceInfo(h, i, ref data))
                {
                    Log.Info($"DebugList: enumerate stopped at {i}, err={Marshal.GetLastWin32Error()}");
                    break;
                }
                string cls = ReadString(h, ref data, SPDRP_CLASS) ?? "(null)";
                string name = ReadString(h, ref data, SPDRP_FRIENDLYNAME) ?? "(null)";
                result.Add(new DevInfo { Name = name, Class = cls, DevInst = data.DevInst });
            }
        }
        finally { SetupDiDestroyDeviceInfoList(h); }
        return result;
    }

    /// <summary>枚举匹配的设备。kind: wifi | bt | cam</summary>
    /// <remarks>
    /// 摄像头优先只匹配物理设备：Class=Camera 的「Virtual Camera」（OBS/直播软件注册）
    /// 会被一并禁用，导致用户开摄像头开关时误伤虚拟摄像头。仅当物理摄像头一台都找不到时，
    /// 才回退到包含虚拟设备的集合，避免"有摄像头但开关无效"。
    /// </remarks>
    public static List<DevInfo> Enumerate(string kind)
    {
        var result = new List<DevInfo>();
        var virtualCam = new List<DevInfo>();
        IntPtr h = SetupDiGetClassDevsW(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return result;
        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiEnumDeviceInfo(h, i, ref data)) break;
                string cls = ReadString(h, ref data, SPDRP_CLASS) ?? "";
                string name = ReadString(h, ref data, SPDRP_FRIENDLYNAME)
                    ?? ReadString(h, ref data, SPDRP_DEVICEDESC) ?? "";
                if (name.Length == 0) continue;

                bool isVirtual = name.Contains("virtual", StringComparison.OrdinalIgnoreCase);

                // 类匹配用 ClassGuid（SPDRP_CLASS 属性对部分设备读不出，ClassGuid 枚举时自带）
                bool match = kind switch
                {
                    // WiFi：Net 类且名字含 Wi-Fi/Wireless/WLAN，排除虚拟/直连适配器
                    "wifi" => data.ClassGuid == GuidNet &&
                              (name.Contains("wi-fi", StringComparison.OrdinalIgnoreCase) ||
                               name.Contains("wifi", StringComparison.OrdinalIgnoreCase) ||
                               name.Contains("wireless", StringComparison.OrdinalIgnoreCase) ||
                               name.Contains("wlan", StringComparison.OrdinalIgnoreCase)) &&
                              !isVirtual &&
                              !name.Contains("direct", StringComparison.OrdinalIgnoreCase),
                    // 蓝牙：蓝牙类的无线电适配器（排除枚举器/协议子节点/外设）
                    "bt" => data.ClassGuid == GuidBluetooth &&
                            name.Contains("adapter", StringComparison.OrdinalIgnoreCase) &&
                            !isVirtual,
                    // 摄像头：Camera 类全部 + Image 类的相机（物理优先，虚拟兜底）
                    "cam" => (data.ClassGuid == GuidCamera) ||
                             (data.ClassGuid == GuidImage && name.Contains("cam", StringComparison.OrdinalIgnoreCase)),
                    _ => false,
                };
                if (!match) continue;

                CM_Get_DevNode_Status(out uint status, out uint problem, data.DevInst, 0);
                bool disabled = (status & DN_HAS_PROBLEM) != 0 && problem == CM_PROB_DISABLED;
                var info = new DevInfo { Name = name, Class = cls, DevInst = data.DevInst, Disabled = disabled };

                if (kind == "cam" && isVirtual) virtualCam.Add(info);
                else result.Add(info);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(h);
        }

        if (kind == "cam" && result.Count == 0) return virtualCam;
        return result;
    }

    /// <summary>查询开关状态（true=启用）。无匹配设备时视为启用。</summary>
    public static bool QueryEnabled(string kind)
    {
        var devs = Enumerate(kind);
        if (devs.Count == 0) return true;
        return devs.Any(d => !d.Disabled);
    }

    /// <summary>启用/禁用一类设备，返回是否成功。</summary>
    public static bool SetEnabled(string kind, bool enable) => SetEnabledEx(kind, enable).Ok;

    /// <summary>设备开关的详细结果（供日志与错误主题使用）。</summary>
    public sealed class ToggleResult
    {
        public bool Ok;
        public int Total;
        public int Changed;
        /// <summary>cfgMgr32 错误码（0 = 成功）。23 = CR_REMOVE_VETOED。</summary>
        public int ErrorCode;
        public string Detail = "";
        public override string ToString() => $"ok={Ok} total={Total} changed={Changed} cr={ErrorCode} ({Detail})";
    }

    /// <summary>启用/禁用一类设备，返回可诊断的结果。</summary>
    public static ToggleResult SetEnabledEx(string kind, bool enable)
    {
        var r = new ToggleResult();
        List<DevInfo> all;
        try { all = Enumerate(kind); }
        catch (Exception ex) { r.Detail = "枚举失败: " + ex.Message; return r; }

        r.Total = all.Count;
        if (all.Count == 0)
        {
            r.Detail = "未找到匹配设备";
            Log.Error($"device toggle {kind}: 未找到匹配设备");
            return r;
        }

        var todo = all.Where(d => enable ? d.Disabled : !d.Disabled).ToList();
        if (todo.Count == 0)
        {
            r.Ok = true;
            r.Detail = $"已处于目标状态（{all.Count} 台）";
            return r;
        }

        if (!SystemCaps.IsElevated)
        {
            r.Detail = "权限不足：需要管理员/SYSTEM";
            Log.Error($"device toggle {kind}: {r.Detail}");
            return r;
        }

        bool allOk = true;
        int lastCr = 0;
        foreach (var dev in todo)
        {
            int cr = SetDeviceState(dev.DevInst, enable);
            if (cr != 0) { allOk = false; lastCr = cr; }
        }

        r.Changed = todo.Count;
        r.Ok = allOk;
        r.ErrorCode = lastCr;
        r.Detail = string.Join("; ", todo.Select(d => d.Name));
        Log.Info($"device toggle {kind} -> {(enable ? "enable" : "disable")} ({todo.Count}/{all.Count} 台): {(allOk ? "OK" : $"FAILED cr={lastCr}")} [{r.Detail}]");
        return r;
    }

    private static int SetDeviceState(uint devInst, bool enable)
    {
        int rc = enable ? CM_Enable_DevNode(devInst, 0) : CM_Disable_DevNode(devInst, 0);
        if (rc != 0) Log.Error($"CM_{(enable ? "Enable" : "Disable")}_DevNode failed cr={rc}");
        return rc;
    }
}
