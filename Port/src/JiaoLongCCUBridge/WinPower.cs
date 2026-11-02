using System.Runtime.InteropServices;

namespace JiaoLongCCUBridge;

/// <summary>电源状态查询（kernel32 GetSystemPowerStatus，避免引入 WinForms）。</summary>
public static class WinPower
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;   // 0=电池 1=外接 255=未知
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved0;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern bool GetSystemPowerStatus(ref SystemPowerStatus status);

    public static bool IsOnAc()
    {
        try
        {
            var s = new SystemPowerStatus();
            return GetSystemPowerStatus(ref s) && s.ACLineStatus == 1;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>电量百分比（0-100，255=未知时返回 -1）。</summary>
    public static int BatteryPercent()
    {
        try
        {
            var s = new SystemPowerStatus();
            if (!GetSystemPowerStatus(ref s)) return -1;
            return s.BatteryLifePercent is > 0 and < 255 ? s.BatteryLifePercent : -1;
        }
        catch { return -1; }
    }

    /// <summary>剩余续航秒数（0xFFFFFFFF=未知返回 -1；0=充满/接电）。</summary>
    public static long BatteryLifeSeconds()
    {
        try
        {
            var s = new SystemPowerStatus();
            if (!GetSystemPowerStatus(ref s)) return -1;
            return s.BatteryLifeTime is > 0 and < 0xFFFFFFFF ? s.BatteryLifeTime : -1;
        }
        catch { return -1; }
    }

    /// <summary>是否在充电（电池标志位：charging=8）。</summary>
    public static bool IsCharging()
    {
        try
        {
            var s = new SystemPowerStatus();
            if (!GetSystemPowerStatus(ref s)) return false;
            return (s.BatteryFlag & 8) != 0;
        }
        catch { return false; }
    }

    // ============================ 电源计划（电池页「高性能电源模式」的真实后端） ============================

    public static readonly Guid PlanHigh = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");     // 高性能
    public static readonly Guid PlanBalanced = new("381b4222-f694-41f0-9685-ff5bb260df2e"); // 平衡

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid schemeGuid);

    // 注意：读活动计划的真实导出是 PowerGetActiveScheme（powrprof 自动 LocalAlloc，需 LocalFree 释放）。
    // 之前误写成不存在的 PowerReadActiveScheme，EntryPointNotFoundException 直接把服务启动打崩。
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr activePolicyGuid, ref uint bufferSize);

    /// <summary>当前激活的电源计划 GUID（读取失败返回 Empty）。</summary>
    public static Guid ActivePlan()
    {
        try
        {
            uint size = 0;
            uint err = PowerGetActiveScheme(IntPtr.Zero, out var buf, ref size);
            if (err == 122 /*ERROR_INSUFFICIENT_BUFFER*/)
                err = PowerGetActiveScheme(IntPtr.Zero, out buf, ref size);
            if (err != 0 || buf == IntPtr.Zero) return Guid.Empty;
            try
            {
                var bytes = new byte[16];
                Marshal.Copy(buf, bytes, 0, 16);
                return new Guid(bytes);
            }
            finally { Marshal.FreeHGlobal(buf); } // LocalAlloc/LocalFree 配对
        }
        catch
        {
            return Guid.Empty;
        }
    }

    /// <summary>激活电源计划（powrprof 直调，服务 Session 0 可用；成功返回 true）。</summary>
    public static bool SetActivePlan(Guid plan)
    {
        try
        {
            var g = plan;
            return PowerSetActiveScheme(IntPtr.Zero, ref g) == 0;
        }
        catch { return false; }
    }

    // ============================ 睿频开关（主页 CPU 卡「关闭睿频」后端） ============================
    // 走电源计划隐藏设置 PERFBOOSTMODE（SUB_PROCESSOR 子组），0=Disabled。
    // 关闭前先读当前值存起来，恢复时写回原值——不猜系统默认值。

    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subGroupGuid, ref Guid powerSettingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subGroupGuid, ref Guid powerSettingGuid, uint value);

    /// <summary>当前活动电源计划的 PERFBOOSTMODE 值（读取失败返回 -1）。</summary>
    public static int GetBoostMode()
    {
        try
        {
            var scheme = ActivePlan();
            if (scheme == Guid.Empty) return -1;
            var sub = SubProcessor;
            var setting = PerfBoostMode;
            uint err = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out uint v);
            return err == 0 ? (int)v : -1;
        }
        catch { return -1; }
    }

    /// <summary>
    /// 关闭/恢复睿频。off=true 写 0（Disabled）；off=false 写 restoreValue。
    /// 写完后重新激活当前计划使设置生效。成功返回 true。
    /// </summary>
    public static bool SetTurbo(bool off, int restoreValue)
    {
        try
        {
            var scheme = ActivePlan();
            if (scheme == Guid.Empty) return false;
            var sub = SubProcessor;
            var setting = PerfBoostMode;
            uint value = off ? 0u : (uint)Math.Max(restoreValue, 1);
            uint err = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value);
            if (err != 0) return false;
            return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
        }
        catch { return false; }
    }

    // ============================ 限制最高频率（主页 CPU 卡「限制频率」后端） ============================
    // 双通道写入，谁被固件认就用谁：
    //   ① GUID_PROCESSOR_FREQUENCY_MAX（MHz，"Processor frequency"，Win10 1809+ / CPPC 机型）
    //   ② GUID_PROCESSOR_THROTTLE_MAX（%，"Maximum processor state"，老通道兜底，按标称频率折算）
    // 两者都是「上限」语义：只会压低、不会抬高频率；0/100 = 不限。

    private static readonly Guid FreqMaxMhz = new("75b0ae3f-bce0-45a7-8c89-c9611c25e100");
    private static readonly Guid ThrottleMaxPct = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subGroupGuid, ref Guid powerSettingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subGroupGuid, ref Guid powerSettingGuid, uint value);

    /// <summary>标称（基频）MHz：注册表 ~MHz，读不到按 3600 估（7745HX 基频）。</summary>
    public static int NominalMhz()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (k?.GetValue("~MHz") is int mhz && mhz > 500 && mhz < 6000)
                return mhz;
        }
        catch { }
        return 3600;
    }

    /// <summary>当前生效的频率上限（MHz）；-1 = 未限制。</summary>
    public static int GetMaxFreqMhz()
    {
        try
        {
            var scheme = ActivePlan();
            if (scheme == Guid.Empty) return -1;
            var sub = SubProcessor;
            var s1 = FreqMaxMhz;
            if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s1, out uint mhz) == 0 && mhz > 0)
                return (int)mhz;
            var s2 = ThrottleMaxPct;
            if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s2, out uint pct) == 0 && pct is > 0 and < 100)
                return NominalMhz() * (int)pct / 100;
            return -1;
        }
        catch { return -1; }
    }

    /// <summary>
    /// 设置最高频率（MHz）；mhz &lt;= 0 表示取消限制（写回 0 / 100）。
    /// AC 与 DC 都写（笔记本电池下同样生效），写完后重新激活计划令其立即生效。
    /// </summary>
    public static bool SetMaxFreqMhz(int mhz)
    {
        try
        {
            var scheme = ActivePlan();
            if (scheme == Guid.Empty) return false;
            var sub = SubProcessor;

            uint mhzValue = mhz > 0 ? (uint)mhz : 0u;
            uint pctValue = mhz > 0
                ? (uint)Math.Clamp((int)Math.Round(mhz * 100.0 / Math.Max(NominalMhz(), 1)), 5, 100)
                : 100u;

            var s1 = FreqMaxMhz;
            uint e1 = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s1, mhzValue);
            PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s1, mhzValue);

            var s2 = ThrottleMaxPct;
            uint e2 = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s2, pctValue);
            PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref s2, pctValue);

            bool reactivated = PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
            return reactivated && (e1 == 0 || e2 == 0);
        }
        catch { return false; }
    }

    // ============================ 电源模式覆盖层（overlay） ============================
    // Win10 1709+ 的「电源模式」（节能/平衡/最佳性能）与「电源计划」是两套机制：
    // 计划决定处理器/设备策略，overlay 决定计划之上的系统性能档。官方
    // PowerOptionAPI.PowerSetActiveOverlayScheme 走的就是这条通道。
    // 只切计划不切 overlay 时，Win11 设置页会显示与实际相反的档位（两处状态打架）。

    private static readonly Guid OverlayBestPerformance = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid OverlayBalanced = Guid.Empty; // 全零 = 平衡（推荐）

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

    /// <summary>把系统「电源模式」覆盖层设为最佳性能。false = 系统不支持（不视为失败）。</summary>
    public static bool SetOverlayBestPerformance()
    {
        try { return PowerSetActiveOverlayScheme(OverlayBestPerformance) == 0; }
        catch { return false; }
    }

    /// <summary>把覆盖层恢复为「平衡（推荐）」。</summary>
    public static bool SetOverlayBalanced()
    {
        try { return PowerSetActiveOverlayScheme(OverlayBalanced) == 0; }
        catch { return false; }
    }

    // ============================ 休眠空闲超时（深度睡眠的可用近似） ============================
    // 本机 powercfg /a 显示 S1/S2/S3 全部「固件不支持」，官方 BIOS 级「深度睡眠」无从复刻。
    // 可落地的等价语义：空闲 N 秒后进入休眠（HIBERNATEIDLE）——比 S0ix 更省电，且不需要 S3。

    private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid HibernateIdle = new("9d7815a6-7ee4-497e-8888-515a05f02364");

    /// <summary>当前 AC 下的休眠空闲秒数（0 = 从不）。读取失败返回 -1。</summary>
    public static int GetHibernateIdleSeconds()
    {
        try
        {
            var scheme = ActivePlan();
            if (scheme == Guid.Empty) return -1;
            var sub = SubSleep;
            var setting = HibernateIdle;
            if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out uint v) != 0) return -1;
            return v > int.MaxValue ? -1 : (int)v;
        }
        catch { return -1; }
    }

    /// <summary>
    /// 设置 AC/DC 休眠空闲秒数（0 = 从不休眠），写完后重新激活当前计划令其立即生效。
    /// 注意：本机 HIBERNATEIDLE 原值为 0，恢复时写回 0 即代表"从不"，无需猜测系统默认值。
    /// </summary>
    public static bool SetHibernateIdleSeconds(int seconds)
    {
        try
        {
            var scheme = ActivePlan();
            if (scheme == Guid.Empty) return false;
            var sub = SubSleep;
            var setting = HibernateIdle;
            uint value = (uint)Math.Max(0, seconds);
            uint e1 = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value);
            PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value);
            bool reactivated = PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
            return reactivated && e1 == 0;
        }
        catch { return false; }
    }
}
