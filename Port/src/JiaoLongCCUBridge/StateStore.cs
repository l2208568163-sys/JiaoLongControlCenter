using System.Text.Json;

namespace JiaoLongCCUBridge;

/// <summary>桥接持久化状态：风扇曲线、当前模式、用户在 UI 里调的参数。</summary>
public sealed class StateStore
{
    public int OperatingMode { get; set; } = 1;          // CCX 语义: 0=办公 1=游戏 2=狂暴 3=自定义
    public bool FanBoost { get; set; }
    public bool CustomLoopActive { get; set; }
    public string CustomProfileName { get; set; } = "Profile1";
    public bool FanControlRespective { get; set; }
    public int SmartBalance { get; set; }
    public int GameWhitelist { get; set; }
    public string? CustomOsdName { get; set; }

    // —— 键盘灯效（MIFS fn16/17/18）——
    public bool KeyboardOn { get; set; } = true;
    public byte KeyboardBrightness { get; set; } = 3;
    public byte KeyboardMode { get; set; } = 2;          // 0=关 1=流光 2=固定色
    public string KeyboardColorHex { get; set; } = "FF0072";
    public string? KeyboardTimerDeadline { get; set; }   // ISO 时间，定时关灯

    // —— GPU 超频（KaronOC P0 偏移）——
    public bool GpuOverClockingSwitch { get; set; }
    public int GpuCoreOffset { get; set; }
    public int GpuMemOffset { get; set; }

    // —— 独显直连（fn9 设置后重启生效；GET 值不可信，以本地状态为准）——
    public bool DgpuDirectOn { get; set; }

    // —— CPU 电压偏移（SMU set-coall，负向步数）——
    public int CpuUndervoltSteps { get; set; }
    public string CpuTccOffsetSwitch { get; set; } = "0"; // UI 温度墙开关回显

    // —— 主页监控卡控制项（JiaoLong/Home* 协议，HomeMod UI 注入）——
    /// <summary>关闭睿频（电源计划 PERFBOOSTMODE=0）。恢复时写回 TurboBoostPrevValue。</summary>
    public bool TurboOff { get; set; }
    public int? TurboBoostPrevValue { get; set; }

    /// <summary>GPU 锁频（NVML SetGpuLockedClocks，min=max=固定频率）。</summary>
    public bool GpuLockOn { get; set; }
    public int GpuLockMhz { get; set; }

    /// <summary>自定义风扇转速上限（0-7000；本机固件 fn20/21 与 EC 直写实测均无效，
    /// 此值仅状态记忆与 UI 回显，写入通道保留待固件突破）。>5800 为超转风险区。</summary>
    public int FanMaxRpm { get; set; }
    /// <summary>最大转速开关（旧程序 MAX_FAN Switch 通道，固件无效仅状态记忆）。</summary>
    public bool FanMaxSwitch { get; set; }

    // —— 显示 ——
    public bool ColorCalibrationOn { get; set; }

    // —— 电池页 ——
    /// <summary>电池保护模式：0=长效(Performance) 1=平衡(Balanced) 2=工作站(Healthy)。
    /// 本机固件无充电阈值接口（fn0-7/24-40 全空），此值仅状态记忆与回显。</summary>
    public int BatteryHealthMode { get; set; } = 1;

    /// <summary>高性能电源模式：真实生效 = 切到 Windows「高性能」电源计划。</summary>
    public bool HighPerformancePowerMode { get; set; }

    // —— 电池页其余开关（BIOS/固件级功能，本机无接口，仅状态记忆）——
    public string UsbChargerState { get; set; } = "OFF";
    public string DeepSleepState { get; set; } = "OFF";
    public int DeepSleepSeconds { get; set; }
    public string AcRecoveryState { get; set; } = "OFF";
    public string BatteryLogoState { get; set; } = "OFF";

    // —— 开关回显（无固件后端，仅状态记忆）——
    public string WinKeyState { get; set; } = "UNLOCK";
    public string OsdState { get; set; } = "ON";
    public string NumPadState { get; set; } = "UNLOCK";

    // UI 里设置的参数（v1 保存并回显；SPL/SPPT/温度墙通过 MIFS fn23 写固件）
    public int? CpuAmdSPL { get; set; }
    public int? CpuAmdSPPT { get; set; }
    public int? CpuAmdFPPT { get; set; }
    public int? CpuAmdTccTarget { get; set; }

    public FanCurve CpuCurve { get; set; } = FanCurve.DefaultCpu();
    public FanCurve GpuCurve { get; set; } = FanCurve.DefaultGpu();

    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "JiaoLongCCUBridge");
    private static string FilePath => Path.Combine(Dir, "state.json");

    public static StateStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<StateStore>(File.ReadAllText(FilePath)) ?? new StateStore();
        }
        catch
        {
            // 损坏则用默认状态
        }
        return new StateStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不致命
        }
    }
}

public sealed class FanCurve
{
    public bool Activated { get; set; }
    public string Name { get; set; } = "Profile1";
    public bool FanControlRespective { get; set; }
    public int Level { get; set; } = 11;
    public List<int> Duty { get; set; } = new();

    public static FanCurve DefaultCpu() => new() { Duty = DefaultDuty() };
    public static FanCurve DefaultGpu() => new() { Duty = DefaultDuty() };

    /// <summary>默认曲线：40℃ 30% 起步，90℃ 拉满（对齐官方默认风扇表的形状）。</summary>
    public static List<int> DefaultDuty() => new() { 30, 30, 32, 35, 40, 45, 50, 58, 70, 85, 100 };

    /// <summary>把曲线展开成 UI 需要的 16 个点（T0..T15 = 10℃ 起步、(Level-1) 步长到 90℃）。</summary>
    public List<int> ExpandTo16()
    {
        var list = new List<int>(Duty);
        while (list.Count < 16) list.Add(list.Count > 0 ? list[^1] : 50);
        if (list.Count > 16) list = list.Take(16).ToList();
        return list;
    }

    /// <summary>按温度求占空比（温度轴：10℃ 起、步长 80/(Level-1)、封顶 90℃）。</summary>
    public int DutyForTemp(double tempC)
    {
        var duty = Duty;
        if (duty.Count == 0) return 50;
        int last = duty.Count - 1;
        double step = last > 0 ? 80.0 / last : 80.0;
        double pos = (tempC - 10.0) / step;
        int idx = (int)Math.Floor(pos);
        if (idx <= 0) return duty[0];
        if (idx >= last) return duty[last];
        double frac = pos - idx;
        return (int)Math.Round(duty[idx] + (duty[idx + 1] - duty[idx]) * frac);
    }
}
