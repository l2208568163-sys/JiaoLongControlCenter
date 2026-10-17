using System.Management;

namespace JiaoLongCCUBridge;

/// <summary>
/// 机械革命 MIFS 固件接口（root\WMI → MICommonInterface，实例 ACPI\PNP0C14\MIFS_0）。
/// InData[1]=0xFA(GET)/0xFB(SET)，InData[3]=功能号，InData[4..]=参数；
/// OutData[4]=单字节返回值；fn13 双转速按 (OutData[5]<<8)+OutData[4] 解析。
/// 功能号表来自第三方蛟龙控制中心反编译代码，已在本机实测验证（温度/双转速读取）。
/// </summary>
public sealed class Mifs
{
    private const byte TypeGet = 250; // WMIMethodType.Get
    private const byte TypeSet = 251; // WMIMethodType.Set

    public const byte FnSystemPerMode = 8;        // 0=游戏(Gaming) 1=狂暴(Turbo) 2=办公(Office)
    public const byte FnGpuMode = 9;              // 0=混合 1=独显直连
    public const byte FnRgbKeyboardStatus = 10;
    public const byte FnFnLock = 11;
    public const byte FnTplLock = 12;
    public const byte FnCpuGpuFanSpeed = 13;      // 读 CPU/GPU 风扇转速
    public const byte FnAmbientlight = 15;
    public const byte FnRgbKeyboardMode = 16;     // 0=关 1=内置流光 2=固定颜色
    public const byte FnRgbKeyboardColor = 17;    // R,G,B 三字节
    public const byte FnRgbKeyboardBrightness = 18; // 0-3
    public const byte FnSystemAcType = 19;        // 电源状态
    public const byte FnMaxFanSpeedSwitch = 20;   // 自定义风扇转速开关
    public const byte FnMaxFanSpeed = 21;         // 自定义风扇转速值(%)
    public const byte FnCpuThermometer = 22;      // CPU 温度
    public const byte FnCpuPower = 23;            // 子功能: [0]=关闭 [2,w]=SPL [3,w]=SPPT [4,t]=温度墙

    private ManagementObject? _mi;
    private readonly object _lock = new();
    public bool Available { get; private set; }
    public string LastError { get; private set; } = "";

    public bool Init()
    {
        try
        {
            _mi = new ManagementObject("root\\WMI", "MICommonInterface.InstanceName='ACPI\\PNP0C14\\MIFS_0'", null);
            _mi.Get();
            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            LastError = ex.Message;
        }
        return Available;
    }

    public byte[] Invoke(byte type, byte fn, params byte[] payload)
    {
        if (!Available) throw new InvalidOperationException("MIFS 不可用: " + LastError);
        lock (_lock)
        {
            var input = new byte[32];
            input[1] = type;
            input[3] = fn;
            for (int i = 0; i < payload.Length && 4 + i < 32; i++) input[4 + i] = payload[i];
            var args = _mi!.GetMethodParameters("MiInterface");
            args["InData"] = input;
            var result = _mi.InvokeMethod("MiInterface", args, null);
            var outData = (byte[])result!["OutData"];
            return outData;
        }
    }

    /// <summary>GET：返回 OutData[4] 单字节。</summary>
    public byte GetByte(byte fn)
    {
        var outData = Invoke(TypeGet, fn);
        return outData.Length > 4 ? outData[4] : (byte)0;
    }

    public void Set(byte fn, params byte[] payload) => Invoke(TypeSet, fn, payload);

    public byte GetCpuTemperature() => GetByte(FnCpuThermometer);

    public (int cpuRpm, int gpuRpm) GetFanSpeeds()
    {
        var outData = Invoke(TypeGet, FnCpuGpuFanSpeed);
        int cpu = outData.Length > 5 ? (outData[5] << 8) + outData[4] : 0;
        int gpu = outData.Length > 7 ? (outData[7] << 8) + outData[6] : 0;
        return (cpu, gpu);
    }

    /// <summary>性能模式：0=游戏 1=狂暴 2=办公（固件枚举 BalanceMode/PerformanceMode/QuietMode）。</summary>
    public byte GetPerformanceMode() => GetByte(FnSystemPerMode);

    public void SetPerformanceMode(byte mode) => Set(FnSystemPerMode, mode);

    public bool GetFanSpeedSwitch() => GetByte(FnMaxFanSpeedSwitch) != 0;

    public void SetFanSpeedSwitch(bool on) => Set(FnMaxFanSpeedSwitch, (byte)(on ? 1 : 0));

    public byte GetFanSpeedValue() => GetByte(FnMaxFanSpeed);

    public void SetFanSpeed(byte percent) => Set(FnMaxFanSpeed, Math.Clamp(percent, (byte)0, (byte)100));

    public bool IsOnAc()
    {
        try
        {
            return GetByte(FnSystemAcType) != 0;
        }
        catch
        {
            return WinPower.IsOnAc();
        }
    }

    /// <summary>设置 AMD SPL/SPPT/温度墙（固件 CPUPower 子功能，单位 W/℃）。</summary>
    public void SetCpuPower(byte subState, byte value) => Set(FnCpuPower, subState, value);

    /// <summary>
    /// 读取当前 SPL(2)/SPPT(3)/温度墙(4)（GET fn23 + 子功能号，OutData[4..5] 小端字）。
    /// 固件若不支持读取会返回 0，由调用方兜底用本地状态/默认值。
    /// </summary>
    public int GetCpuPower(byte subState)
    {
        try
        {
            var outData = Invoke(TypeGet, FnCpuPower, subState);
            if (outData.Length < 6) return 0;
            return outData[4] | (outData[5] << 8);
        }
        catch { return 0; }
    }

    public void CloseCustomCpuPower() => Set(FnCpuPower, (byte)0, (byte)0);

    // ---------- 独显直连 / Fn / 触摸板 / 键盘灯（探测确认可用） ----------

    /// <summary>fn9：0=混合 1=独显直连（切换后需重启生效）。</summary>
    public byte GetGpuMode() => GetByte(FnGpuMode);

    public void SetGpuMode(byte mode) => Set(FnGpuMode, mode);

    /// <summary>fn11：0=未锁 1=锁定。</summary>
    public bool GetFnLock() => GetByte(FnFnLock) != 0;

    public void SetFnLock(bool on) => Set(FnFnLock, (byte)(on ? 1 : 0));

    /// <summary>fn12：0=触摸板开 1=触摸板锁定。</summary>
    public bool GetTouchpadLocked() => GetByte(FnTplLock) != 0;

    public void SetTouchpadLocked(bool locked) => Set(FnTplLock, (byte)(locked ? 1 : 0));

    /// <summary>fn16 键盘灯模式：0=关 1=内置流光 2=固定颜色。</summary>
    public byte GetKeyboardMode() => GetByte(FnRgbKeyboardMode);

    public void SetKeyboardMode(byte mode) => Set(FnRgbKeyboardMode, mode);

    /// <summary>fn17 键盘灯颜色 R,G,B（先切固定模式，延时后写颜色）。</summary>
    public (byte r, byte g, byte b) GetKeyboardColor()
    {
        var outData = Invoke(TypeGet, FnRgbKeyboardColor);
        return (outData[4], outData[5], outData[6]);
    }

    public void SetKeyboardColor(byte r, byte g, byte b) => Set(FnRgbKeyboardColor, r, g, b);

    /// <summary>fn18 键盘灯亮度 0-3。</summary>
    public byte GetKeyboardBrightness() => Math.Clamp(GetByte(FnRgbKeyboardBrightness), (byte)0, (byte)3);

    public void SetKeyboardBrightness(byte level) => Set(FnRgbKeyboardBrightness, Math.Clamp(level, (byte)0, (byte)3));
}
