namespace JiaoLongCCUBridge;

/// <summary>
/// 自定义风扇曲线/固定转速控制环：
/// - FanControlRespective=false：单曲线，CPU/GPU 取高者，双风扇同步
/// - FanControlRespective=true 且 EC 可用：CPU/GPU 独立曲线，分别写 EC 0xC83C/0xC83D
/// - 固定转速模式（主页「自定义风扇转速」开启）：EC 占空比闭环 —— 因 EC 读数是坏的，
///   反馈用 MIFS fn13 实测转速：duty 从 100 起步，每 2s 按误差步进收敛到目标 RPM。
/// </summary>
public sealed class FanController
{
    private readonly Mifs _mifs;
    private readonly EcAccess _ec;
    private readonly StateStore _state;
    private readonly Func<uint> _gpuTempProvider;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>占空比→RPM 标定：duty100 → 实测 ~5800RPM（厂商上限，2026-10-18 验证）。</summary>
    public static byte RpmToDuty(int rpm) => (byte)Math.Clamp((int)Math.Round(rpm * 100.0 / 5800.0), 0, 100);

    public FanController(Mifs mifs, EcAccess ec, StateStore state, Func<uint> gpuTempProvider)
    {
        _mifs = mifs;
        _ec = ec;
        _state = state;
        _gpuTempProvider = gpuTempProvider;
    }

    public void StartIfActive()
    {
        if (_state.CustomLoopActive || FixedRpmActive) EnsureLoop();
    }

    /// <summary>固定转速模式是否激活（主页「自定义风扇转速」开启且目标 > 0）。</summary>
    public bool FixedRpmActive => _state.FanMaxRpmEnabled && _state.FanMaxRpm > 0;

    public void EnsureLoop()
    {
        if (_loop != null && !_loop.IsCompleted) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void StopLoop()
    {
        try { _cts?.Cancel(); } catch { }
        _loop = null;
    }

    public byte LastCpuDuty { get; private set; }
    public byte LastGpuDuty { get; private set; }

    private async Task LoopAsync(CancellationToken token)
    {
        byte lastCpu = 0xFF, lastGpu = 0xFF;
        var ticks = 0;
        int fixedDuty = 100, lastTarget = -1;
        while (!token.IsCancellationRequested)
        {
            ticks++;
            try
            {
                double cpuTemp = _mifs.GetCpuTemperature();
                uint gpuTemp = _gpuTempProvider();

                byte cpuDuty, gpuDuty;
                if (FixedRpmActive)
                {
                    // 固定转速闭环：EC 读数是坏的，用 MIFS fn13 实测转速做反馈，
                    // duty 从 100 起步向下/向上收敛（步幅 ±4%/2s，死区 120RPM）。
                    int target = _state.FanMaxRpm;
                    if (target != lastTarget)
                    {
                        fixedDuty = RpmToDuty(target);
                        lastTarget = target;
                    }
                    try
                    {
                        var (rC, rG) = _mifs.GetFanSpeeds();
                        int actual = Math.Max(rC, rG);
                        int err = target - actual;
                        if (Math.Abs(err) > 120)
                            fixedDuty = Math.Clamp(fixedDuty + Math.Clamp(err / 150, -4, 4), 8, 100);
                    }
                    catch { }
                    cpuDuty = gpuDuty = (byte)fixedDuty;
                }
                else if (_state.FanControlRespective && _ec.FanEcSafe)
                {
                    cpuDuty = (byte)Math.Clamp(_state.CpuCurve.DutyForTemp(cpuTemp), 0, 100);
                    var gpuCurve = _state.GpuCurve;
                    gpuDuty = (byte)Math.Clamp(gpuCurve.DutyForTemp(gpuTemp), 0, 100);
                }
                else
                {
                    double effective = Math.Max(cpuTemp, gpuTemp);
                    cpuDuty = gpuDuty = (byte)Math.Clamp(_state.CpuCurve.DutyForTemp(effective), 0, 100);
                }

                if (cpuDuty != lastCpu || gpuDuty != lastGpu)
                {
                    if (_ec.FanEcSafe)
                    {
                        // EC 直写占空比 + 手动位 —— 本机实测有效（2026-10-18）。
                        // SetFanDuty 内部含旧程序 Fan_Speed 同款 RMW 手动位序列。
                        _ec.SetFanDuty(cpuDuty, gpuDuty);
                    }
                    else
                    {
                        // 无 EC 时的回退：固件自定义转速通道
                        try
                        {
                            _mifs.SetFanSpeedSwitch(true);
                            _mifs.SetFanSpeed(cpuDuty);
                        }
                        catch { }
                    }
                    LastCpuDuty = cpuDuty;
                    LastGpuDuty = gpuDuty;
                    lastCpu = cpuDuty;
                    lastGpu = gpuDuty;
                }
                if (ticks % 8 == 1)
                {
                    string extra = FixedRpmActive ? $" target={_state.FanMaxRpm} dutyNow={fixedDuty}%" : $" respective={_state.FanControlRespective}";
                    Log.Info($"fan loop: cpu={cpuTemp:F0}C gpu={gpuTemp}C duty={cpuDuty}/{gpuDuty}{extra}");
                }
            }
            catch
            {
                // 硬件采样偶发失败，下一轮重试
            }
            try { await Task.Delay(2000, token); } catch (TaskCanceledException) { break; }
        }
    }
}
