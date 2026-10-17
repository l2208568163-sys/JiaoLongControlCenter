namespace JiaoLongCCUBridge;

/// <summary>
/// 自定义曲线控制环 v2：
/// - FanControlRespective=false：单曲线，CPU/GPU 取高者，双风扇同步（MIFS fn21 或 EC 双写）
/// - FanControlRespective=true：CPU/GPU 独立曲线，经 WinRing0 分别写 EC 0xC83C/0xC83D
/// </summary>
public sealed class FanController
{
    private readonly Mifs _mifs;
    private readonly EcAccess _ec;
    private readonly StateStore _state;
    private readonly Func<uint> _gpuTempProvider;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public FanController(Mifs mifs, EcAccess ec, StateStore state, Func<uint> gpuTempProvider)
    {
        _mifs = mifs;
        _ec = ec;
        _state = state;
        _gpuTempProvider = gpuTempProvider;
    }

    public void StartIfActive()
    {
        if (_state.CustomLoopActive) EnsureLoop();
    }

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
        bool manualSet = false;
        var ticks = 0;
        while (!token.IsCancellationRequested)
        {
            ticks++;
            try
            {
                double cpuTemp = _mifs.GetCpuTemperature();
                uint gpuTemp = _gpuTempProvider();

                byte cpuDuty, gpuDuty;
                if (_state.FanControlRespective && _ec.Available)
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

                if (cpuDuty != lastCpu || gpuDuty != lastGpu || !manualSet)
                {
                    if (_ec.Available)
                    {
                        _ec.SetFanDuty(cpuDuty, gpuDuty);
                    }
                    else
                    {
                        _mifs.SetFanSpeedSwitch(true);
                        _mifs.SetFanSpeed(cpuDuty); // 回退路径：双风扇同步
                    }
                    LastCpuDuty = cpuDuty;
                    LastGpuDuty = gpuDuty;
                    lastCpu = cpuDuty;
                    lastGpu = gpuDuty;
                    manualSet = true;
                }
                if (ticks % 8 == 1)
                {
                    byte rC = 0, rG = 0;
                    try { rC = _ec.ReadEc(0xC83C); rG = _ec.ReadEc(0xC83D); } catch { }
                    Log.Info($"fan loop: cpu={cpuTemp:F0}C gpu={gpuTemp}C duty={cpuDuty}/{gpuDuty} ec_read={rC}/{rG} respective={_state.FanControlRespective}");
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
