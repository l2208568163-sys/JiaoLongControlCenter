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
}
