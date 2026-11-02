using System.Runtime.InteropServices;

namespace JiaoLongCCUBridge;

/// <summary>
/// 电池类驱动直读（对齐官方 BatteryModel/Win32.cs 的实现路径）。
///
/// 为什么不用 WMI：本机 root\wmi 下 <c>BatteryStaticData</c> 类不存在，
/// <c>Win32_Battery.FullChargeCapacity</c> 也为空，拿不到设计容量与真实的
/// 充放电速率（Rate）。而电池类驱动（battc.sys）的 IOCTL 接口能一次拿全：
/// 设计容量 / 满充容量 / 循环次数 / 电压 / 带符号速率。
///
/// 路径：SetupDiGetClassDevs(GUID_DEVCLASS_BATTERY) → 枚举接口 →
///       CreateFile(devicePath) → IOCTL_BATTERY_QUERY_TAG / _INFORMATION / _STATUS
/// </summary>
public static class BatteryIo
{
    // ---------------- P/Invoke ----------------

    private static readonly Guid GuidDevClassBattery = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");

    private const uint DigcfPresent = 0x02;
    private const uint DigcfDeviceInterface = 0x10;

    // CTL_CODE(FILE_DEVICE_BATTERY=0x29, fn, METHOD_BUFFERED, FILE_READ_ACCESS)
    private const uint IoctlBatteryQueryTag = 0x294040;   // 2703424
    private const uint IoctlBatteryQueryInformation = 0x294044; // 2703428
    private const uint IoctlBatteryQueryStatus = 0x29404C;   // 2703436

    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryQueryInformation
    {
        public uint BatteryTag;
        public int InformationLevel; // BATTERY_QUERY_INFORMATION_LEVEL, BatteryInformation = 0
        public int AtRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation
    {
        public int Capabilities;
        public byte Technology;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] Chemistry;
        public int DesignedCapacity;
        public int FullChargedCapacity;
        public int DefaultAlert1;
        public int DefaultAlert2;
        public int CriticalBias;
        public int CycleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryWaitStatus
    {
        public uint BatteryTag;
        public uint Timeout;
        public uint PowerState;
        public uint LowCapacity;
        public uint HighCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryStatus
    {
        public uint PowerState;
        public uint Capacity;
        public uint Voltage;
        public int Rate; // 带符号：>0 充电，<0 放电，0 静置
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr handle, uint controlCode, IntPtr inBuffer,
        int inBufferSize, IntPtr outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private static readonly IntPtr InvalidHandle = new(-1);

    // ---------------- 结果模型 ----------------

    /// <summary>一次电池快照。字段语义与 BATTERY_* 结构一一对应。</summary>
    public sealed class Snapshot
    {
        public bool Present { get; init; }
        public int DesignedCapacity { get; init; }     // mWh
        public int FullChargedCapacity { get; init; }  // mWh
        public int CycleCount { get; init; }
        public uint Capacity { get; init; }            // mWh，当前剩余
        public uint Voltage { get; init; }             // mV
        public int Rate { get; init; }                 // mW，带符号
        public bool PowerOnline { get; init; }
        public bool Charging { get; init; }
        public bool Discharging { get; init; }
        public bool Critical { get; init; }
        public string Chemistry { get; init; } = "";

        /// <summary>健康度（满充/设计 ×100），无法计算时为 -1。</summary>
        public int HealthPercent =>
            DesignedCapacity > 0 && FullChargedCapacity > 0
                ? (int)Math.Round(FullChargedCapacity * 100.0 / DesignedCapacity)
                : -1;

        /// <summary>电量百分比：优先按 容量/满充容量，退化到系统值。</summary>
        public int PercentOr(int fallback)
        {
            if (FullChargedCapacity > 0 && Capacity > 0)
            {
                int p = (int)Math.Round(Capacity * 100.0 / FullChargedCapacity);
                return Math.Clamp(p, 0, 100);
            }
            return fallback;
        }

        /// <summary>剩余放电秒数；未在放电时为 -1。</summary>
        public long SecondsRemaining()
        {
            if (!Discharging || Rate >= 0 || Capacity == 0) return -1;
            double mw = -Rate;                       // 放电时 Rate 为负
            if (mw < 1) return -1;
            return (long)Math.Round(Capacity / mw * 3600.0);
        }

        /// <summary>充满剩余秒数；未在充电或已满时为 -1。</summary>
        public long SecondsToFull()
        {
            if (!Charging || Rate <= 0 || FullChargedCapacity <= 0) return -1;
            if (Capacity >= (uint)FullChargedCapacity) return -1;
            return (long)Math.Round((FullChargedCapacity - Capacity) / (double)Rate * 3600.0);
        }
    }

    // ---------------- 读取 ----------------

    private static readonly object Gate = new();

    /// <summary>读取第一块电池的快照。失败返回 Present=false（调用方降级到系统 API）。</summary>
    public static Snapshot TryRead()
    {
        lock (Gate)
        {
            try
            {
                return ReadCore() ?? new Snapshot { Present = false };
            }
            catch
            {
                return new Snapshot { Present = false };
            }
        }
    }

    private static Snapshot? ReadCore()
    {
        Guid guid = GuidDevClassBattery;
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == InvalidHandle) return null;

        try
        {
            var did = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint index = 0; ; index++)
            {
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref did))
                {
                    if (Marshal.GetLastWin32Error() == ErrorNoMoreItems) break;
                    break;
                }

                string? path = GetInterfacePath(set, ref did);
                if (string.IsNullOrEmpty(path)) continue;

                // 设备路径形如 \\?\ACPI#PNP0C0A#1#{72631e54-...}
                IntPtr h = CreateFile(path, 0x80000000 /*GENERIC_READ*/ | 0x40000000 /*GENERIC_WRITE*/,
                    0x1 | 0x2 /*FILE_SHARE_READ|WRITE*/, IntPtr.Zero, 3 /*OPEN_EXISTING*/, 0, IntPtr.Zero);
                if (h == InvalidHandle) continue;

                try
                {
                    var snap = Query(h);
                    if (snap != null) return snap; // 第一块可用电池即为结果（本机只有一块）
                }
                finally
                {
                    CloseHandle(h);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return null;
    }

    private static string? GetInterfacePath(IntPtr set, ref SpDeviceInterfaceData did)
    {
        // cbSize 必须是 SP_DEVICE_INTERFACE_DETAIL_DATA 的"结构大小"（x64 为 8），
        // 但缓冲区要足够装下路径。手动分配 + 固定 cbSize，避免 512 字符数组把 cbSize 撑大。
        const int bufferSize = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            Marshal.WriteInt32(buffer, 0, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref did, buffer, bufferSize, out int required, IntPtr.Zero)
                && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            {
                return null;
            }
            // DevicePath 紧跟在 DWORD cbSize 之后（WCHAR 对齐 2，x64 下 offset = 4）
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static Snapshot? Query(IntPtr handle)
    {
        // 1) QUERY_TAG：拿电池标签，后续所有查询都以它为句柄
        IntPtr outTag = Marshal.AllocHGlobal(4);
        try
        {
            if (!DeviceIoControl(handle, IoctlBatteryQueryTag, IntPtr.Zero, 0, outTag, 4, out _, IntPtr.Zero))
                return null;
            uint tag = (uint)Marshal.ReadInt32(outTag);
            if (tag == 0) return null;

            var info = QueryInformation(handle, tag);
            var status = QueryStatus(handle, tag);
            if (info == null && status == null) return null;

            var i = info ?? new BatteryInformation
            {
                Reserved = new byte[3], Chemistry = new byte[4],
            };
            var s = status ?? default;

            string chemistry = i.Chemistry is { Length: >= 4 }
                ? System.Text.Encoding.ASCII.GetString(i.Chemistry).TrimEnd('\0')
                : "";

            const uint powerOnline = 0x1, discharging = 0x2, charging = 0x4, critical = 0x8;

            return new Snapshot
            {
                Present = true,
                DesignedCapacity = i.DesignedCapacity,
                FullChargedCapacity = i.FullChargedCapacity,
                CycleCount = i.CycleCount,
                Capacity = s.Capacity,
                Voltage = s.Voltage,
                Rate = s.Rate,
                PowerOnline = (s.PowerState & powerOnline) != 0,
                Discharging = (s.PowerState & discharging) != 0,
                Charging = (s.PowerState & charging) != 0,
                Critical = (s.PowerState & critical) != 0,
                Chemistry = chemistry,
            };
        }
        finally
        {
            Marshal.FreeHGlobal(outTag);
        }
    }

    private static BatteryInformation? QueryInformation(IntPtr handle, uint tag)
    {
        var query = new BatteryQueryInformation { BatteryTag = tag, InformationLevel = 0, AtRate = 0 };
        int inSize = Marshal.SizeOf<BatteryQueryInformation>();
        int outSize = Marshal.SizeOf<BatteryInformation>();
        IntPtr inPtr = Marshal.AllocHGlobal(inSize);
        IntPtr outPtr = Marshal.AllocHGlobal(outSize);
        try
        {
            Marshal.StructureToPtr(query, inPtr, false);
            if (!DeviceIoControl(handle, IoctlBatteryQueryInformation, inPtr, inSize, outPtr, outSize, out _, IntPtr.Zero))
                return null;
            var info = Marshal.PtrToStructure<BatteryInformation>(outPtr);
            // ByValArray 字段在 PtrToStructure 后可能为 null，补齐避免后续 NullReference
            info.Reserved ??= new byte[3];
            info.Chemistry ??= new byte[4];
            return info;
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
            Marshal.FreeHGlobal(outPtr);
        }
    }

    private static BatteryStatus? QueryStatus(IntPtr handle, uint tag)
    {
        var wait = new BatteryWaitStatus { BatteryTag = tag };
        int inSize = Marshal.SizeOf<BatteryWaitStatus>();
        int outSize = Marshal.SizeOf<BatteryStatus>();
        IntPtr inPtr = Marshal.AllocHGlobal(inSize);
        IntPtr outPtr = Marshal.AllocHGlobal(outSize);
        try
        {
            Marshal.StructureToPtr(wait, inPtr, false);
            if (!DeviceIoControl(handle, IoctlBatteryQueryStatus, inPtr, inSize, outPtr, outSize, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStructure<BatteryStatus>(outPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
            Marshal.FreeHGlobal(outPtr);
        }
    }
}
