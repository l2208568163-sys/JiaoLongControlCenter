using System.Runtime.InteropServices;

namespace JiaoLongCCUBridge;

/// <summary>
/// WinRing0 底层访问（复用第三方蛟龙控制中心同款驱动，本机已验证可用）：
/// - ITE EC 扩展寻址（端口 0x4E/0x4F，命令 0x2E：0x11 设高址 / 0x10 设低址 / 0x12 读写数据）
///   风扇占空比：0xC83C=CPU 0xC83D=GPU，手动模式位：0xB20 bit1
/// - AMD SMU（SMN 0xB8/0xBC 转发，RSMU set-coall 等）
/// </summary>
public sealed class EcAccess : IDisposable
{
    private const ushort EcIndexPort = 0x4E;
    private const ushort EcDataPort = 0x4F;
    private const ushort EcStatusPort = 0x66;   // EC 状态端口（IBF=bit1 输入缓冲满）
    public const ushort EcRegFanCpu = 0xC83C;
    public const ushort EcRegFanGpu = 0xC83D;
    public const ushort EcRegFanManual = 0xB20;

    // SMU（DragonRange / Socket_AM5_V1，来自旧程序反编译）
    private const uint SmnPciAddr = 0;              // bus 0, dev 0, func 0
    private const byte SmnAddrReg = 0xB8;
    private const byte SmnDataReg = 0xBC;
    private const uint RsmuAddrMsg = 0x3B10524;
    private const uint RsmuAddrRsp = 0x3B10570;
    private const uint RsmuAddrArg = 0x3B10A40;
    private const uint MsgSetCoall = 7;             // 全核 Curve Optimizer 偏移

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint LoadLibrary(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddress(nint module, string name);

    private delegate int InitializeOlsDelegate();
    private delegate byte ReadIoPortByteDelegate(ushort port);
    private delegate void WriteIoPortByteDelegate(ushort port, byte value);
    private delegate uint ReadPciConfigDwordDelegate(uint pciAddress, byte regAddress);
    private delegate void WritePciConfigDwordDelegate(uint pciAddress, byte regAddress, uint value);

    private InitializeOlsDelegate? _init;
    private ReadIoPortByteDelegate? _readPort;
    private WriteIoPortByteDelegate? _writePort;
    private ReadPciConfigDwordDelegate? _readPci;
    private WritePciConfigDwordDelegate? _writePci;

    public bool Available { get; private set; }
    private readonly object _lock = new();

    public bool Init()
    {
        try
        {
            var module = LoadLibrary("WinRing0x64.dll");
            if (module == nint.Zero)
            {
                Log.Error("WinRing0x64.dll 加载失败（驱动可能被拦截）");
                return false;
            }
            _init = GetDelegate<InitializeOlsDelegate>(module, "InitializeOls");
            _readPort = GetDelegate<ReadIoPortByteDelegate>(module, "ReadIoPortByte");
            _writePort = GetDelegate<WriteIoPortByteDelegate>(module, "WriteIoPortByte");
            _readPci = GetDelegate<ReadPciConfigDwordDelegate>(module, "ReadPciConfigDword");
            _writePci = GetDelegate<WritePciConfigDwordDelegate>(module, "WritePciConfigDword");
            if (_init == null || _readPort == null || _writePort == null || _readPci == null || _writePci == null)
            {
                Log.Error("WinRing0 导出函数缺失");
                return false;
            }
            if (_init() == 0)
            {
                Log.Error("WinRing0 InitializeOls 失败（需要管理员，驱动可能被安全策略阻止）");
                return false;
            }
            Available = true;
            Log.Info("WinRing0 EC/SMU 后端就绪");
            // ITE EC 扩展寻址使能（老程序 ECRW_Tool.Fan_Init → Write_EC_Init：
            // 读 0x2000 芯片 ID，ITE(0x55) 时对 0x1060 |= 0x80 打开扩展寻址。
            // 缺这一步，0xC83C 等扩展地址的读写全是垃圾值）。
            try
            {
                var chipId = ReadEc(0x2000);
                if (chipId == 0x55)
                {
                    var ver = ReadEc(0x1060);
                    WriteEc(0x1060, (byte)(ver | 0x80));
                    Log.Info($"ITE EC init: chip=0x{chipId:X2} ver 0x{ver:X2}->0x{(byte)(ver | 0x80):X2}");
                }
                else
                {
                    Log.Info($"ITE EC init: chip id 0x{chipId:X2} (非 ITE，跳过使能)");
                }
            }
            catch (Exception ex)
            {
                Log.Error("ITE EC init: " + ex.Message);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("WinRing0 init: " + ex.Message);
            return false;
        }
    }

    private static T? GetDelegate<T>(nint module, string name) where T : class
    {
        var addr = GetProcAddress(module, name);
        return addr == nint.Zero ? null : Marshal.GetDelegateForFunctionPointer(addr, typeof(T)) as T;
    }

    // ---------- ITE EC 扩展寻址（与旧程序协议一致） ----------

    /// <summary>等 EC 输入缓冲清空（老程序 ECStatus(EC_IBF)：轮询 0x66 的 bit1）。
    /// 之前轮询 0x4E 的 bit1 是错误端口，EC 忙时写入被丢弃、序列损坏。</summary>
    private void EcWaitIdle()
    {
        for (int i = 0; i < 500; i++)
        {
            if ((_readPort!(EcStatusPort) & 0x02) == 0) return;
        }
    }

    public byte ReadEc(ushort address)
    {
        lock (_lock)
        {
            EcWaitIdle();
            _writePort!(EcIndexPort, 0x2E); _writePort(EcDataPort, 0x11);   // 高址选择
            EcWaitIdle();
            _writePort(EcIndexPort, 0x2F); _writePort(EcDataPort, (byte)(address >> 8));
            EcWaitIdle();
            _writePort(EcIndexPort, 0x2E); _writePort(EcDataPort, 0x10);   // 低址选择
            EcWaitIdle();
            _writePort(EcIndexPort, 0x2F); _writePort(EcDataPort, (byte)(address & 0xFF));
            EcWaitIdle();
            _writePort(EcIndexPort, 0x2E); _writePort(EcDataPort, 0x12);   // 数据读写
            EcWaitIdle();
            return _readPort!(EcDataPort);
        }
    }

    public void WriteEc(ushort address, byte value)
    {
        lock (_lock)
        {
            EcWaitIdle();
            _writePort!(EcIndexPort, 0x2E); _writePort(EcDataPort, 0x11);
            EcWaitIdle();
            _writePort(EcIndexPort, 0x2F); _writePort(EcDataPort, (byte)(address >> 8));
            EcWaitIdle();
            _writePort(EcIndexPort, 0x2E); _writePort(EcDataPort, 0x10);
            EcWaitIdle();
            _writePort(EcIndexPort, 0x2F); _writePort(EcDataPort, (byte)(address & 0xFF));
            EcWaitIdle();
            _writePort(EcIndexPort, 0x2E); _writePort(EcDataPort, 0x12);
            EcWaitIdle();
            _writePort(EcIndexPort, 0x2F); _writePort(EcDataPort, value);
        }
    }

    // ---------- EC 扩展寻址变体 B：经 0x66/0x62 命令对（报告 §2 记载的老路径） ----------

    private void WaitIbf66()
    {
        for (int i = 0; i < 500; i++)
        {
            if ((_readPort!(EcStatusPort) & 0x02) == 0) return;
        }
    }

    private byte ReadEc66(ushort address)
    {
        lock (_lock)
        {
            WaitIbf66();
            _writePort!(EcStatusPort, 0x2E); _writePort(0x62, 0x11);
            WaitIbf66();
            _writePort(EcStatusPort, 0x2F); _writePort(0x62, (byte)(address >> 8));
            WaitIbf66();
            _writePort(EcStatusPort, 0x2E); _writePort(0x62, 0x10);
            WaitIbf66();
            _writePort(EcStatusPort, 0x2F); _writePort(0x62, (byte)(address & 0xFF));
            WaitIbf66();
            _writePort(EcStatusPort, 0x2E); _writePort(0x62, 0x12);
            WaitIbf66();
            _writePort(EcStatusPort, 0x2F);
            return _readPort!(0x62);
        }
    }

    private void WriteEc66(ushort address, byte value)
    {
        lock (_lock)
        {
            WaitIbf66();
            _writePort!(EcStatusPort, 0x2E); _writePort(0x62, 0x11);
            WaitIbf66();
            _writePort(EcStatusPort, 0x2F); _writePort(0x62, (byte)(address >> 8));
            WaitIbf66();
            _writePort(EcStatusPort, 0x2E); _writePort(0x62, 0x10);
            WaitIbf66();
            _writePort(EcStatusPort, 0x2F); _writePort(0x62, (byte)(address & 0xFF));
            WaitIbf66();
            _writePort(EcStatusPort, 0x2E); _writePort(0x62, 0x12);
            WaitIbf66();
            _writePort(EcStatusPort, 0x2F); _writePort(0x62, value);
        }
    }

    /// <summary>在桥接进程内探测两种 EC 接口变体（EC_PROBE 诊断命令用）。</summary>
    public string Probe()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            byte chipA = ReadEc(0x2000);
            byte fanA = ReadEc(EcRegFanCpu);
            sb.Append($"A(0x4E/0x4F): chip@0x2000=0x{chipA:X2} fan@0xC83C=0x{fanA:X2}; ");
        }
        catch (Exception ex) { sb.Append("A failed: " + ex.Message + "; "); }
        try
        {
            byte chipB = ReadEc66(0x2000);
            var verB = ReadEc66(0x1060);
            sb.Append($"B(0x66/0x62): chip@0x2000=0x{chipB:X2} ver@0x1060=0x{verB:X2}; ");
            WriteEc66(0x1060, (byte)(verB | 0x80));
            var ver2 = ReadEc66(0x1060);
            sb.Append($"ver2=0x{ver2:X2}; ");
            // 关键回读环：写 duty 100 → 立即回读 → 置手动位 → 再回读
            var before = ReadEc66(0xC83C);
            WriteEc66(0xC83C, 100);
            WriteEc66(0xC83D, 100);
            var back = ReadEc66(0xC83C);
            byte man = ReadEc66(0xB20);
            WriteEc66(0xB20, (byte)(man | 0x02));
            var man2 = ReadEc66(0xB20);
            var after = ReadEc66(0xC83C);
            sb.Append($"fan: before=0x{before:X2} write100 back=0x{back:X2} man 0x{man:X2}->0x{man2:X2} after=0x{after:X2}");
        }
        catch (Exception ex) { sb.Append("B failed: " + ex.Message); }
        return sb.ToString();
    }

    /// <summary>经 0x66/0x62 变体写风扇占空比并置手动位（若变体 B 可用）。</summary>
    public void SetFanDuty66(byte cpuDuty, byte gpuDuty)
    {
        lock (_lock)
        {
            WriteEc66(EcRegFanCpu, Math.Clamp(cpuDuty, (byte)0, (byte)100));
            WriteEc66(EcRegFanGpu, Math.Clamp(gpuDuty, (byte)0, (byte)100));
            byte v = ReadEc66(EcRegFanManual);
            WriteEc66(EcRegFanManual, (byte)(v | 0x02));
        }
    }

    /// <summary>进入手动风扇模式（0xB20 |= 0x02）。</summary>
    public void EnableFanManual()
    {
        byte v = ReadEc(EcRegFanManual);
        if ((v & 0x02) == 0) WriteEc(EcRegFanManual, (byte)(v | 0x02));
    }

    /// <summary>退出手动风扇模式并清占空比，交还固件自动控制。</summary>
    public void ReleaseFanManual()
    {
        try
        {
            WriteEc(EcRegFanCpu, 0);
            WriteEc(EcRegFanGpu, 0);
            byte v = ReadEc(EcRegFanManual);
            WriteEc(EcRegFanManual, (byte)(v & ~0x02));
        }
        catch { }
    }

    public void SetFanDuty(byte cpuDuty, byte gpuDuty)
    {
        // 顺序与老程序 ECRW_Tool.Fan_Speed 一致：先写双风扇占空比，再置手动位
        WriteEc(EcRegFanCpu, Math.Clamp(cpuDuty, (byte)0, (byte)100));
        WriteEc(EcRegFanGpu, Math.Clamp(gpuDuty, (byte)0, (byte)100));
        byte v = ReadEc(EcRegFanManual);
        if ((v & 0x02) == 0) WriteEc(EcRegFanManual, (byte)(v | 0x02));
    }

    // ---------- AMD SMU（SMN 转发，仅 set-coall 用） ----------

    private uint ReadSmn(uint address)
    {
        _writePci!(SmnPciAddr, SmnAddrReg, address);
        return _readPci!(SmnPciAddr, SmnDataReg);
    }

    private void WriteSmn(uint address, uint value)
    {
        _writePci!(SmnPciAddr, SmnAddrReg, address);
        _writePci!(SmnPciAddr, SmnDataReg, value);
    }

    /// <summary>RSMU 消息（参照旧程序 RyzenSmu.SendMsg：RSP=0 → ARG → MSG → 轮询 RSP）。</summary>
    private bool SmuSend(uint msgId, uint arg0)
    {
        lock (_lock)
        {
            try
            {
                WriteSmn(RsmuAddrRsp, 0);
                for (int i = 0; i < 6; i++) WriteSmn(RsmuAddrArg + (uint)(i * 4), i == 0 ? arg0 : 0);
                WriteSmn(RsmuAddrMsg, msgId);
                for (int i = 0; i < 8192; i++)
                {
                    if (ReadSmn(RsmuAddrRsp) == 1) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>全核 Curve Optimizer 偏移。offset 为负向降压步数（0 = 关闭），协议 coall = 0x100000 - N。</summary>
    public bool SetCpuCoAll(int undervoltSteps)
    {
        if (!Available) return false;
        uint arg = (uint)(0x100000 - Math.Clamp(undervoltSteps, 0, 0x100000));
        return SmuSend(MsgSetCoall, arg);
    }

    public void Dispose()
    {
        // WinRing0 驱动保持加载（进程退出自动清理），不做 EC 破坏性操作
    }
}
