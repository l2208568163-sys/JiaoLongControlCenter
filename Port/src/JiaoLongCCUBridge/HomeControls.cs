using System.Management;
using System.Text.Json;

namespace JiaoLongCCUBridge;

/// <summary>
/// 主页监控卡控制项（2026-10-13 新增）。
/// 官方 CCU.WinUI 没有这套协议——由 HomeMod（UI 注入库，随 LogoMod 一同注入 HomePage）
/// 消费：UI 发 <see cref="HandleHomeControlAsync"/>，桥接回 <see cref="SendHomeStatusAsync"/>（retained）。
/// 与官方自定义设置页共享 StateStore 字段（SPL/SPPT/电压/GPU OC），两处调节保持同步。
/// </summary>
public sealed partial class ProtocolHandlers
{
    // ============================ 主页控制项协议 ============================

    /// <summary>发布主页控制项当前状态（retained，UI 启动即收）。</summary>
    public async Task SendHomeStatusAsync()
    {
        var payload = new Dictionary<string, object?>
        {
            // CPU 卡
            ["TurboOff"] = _state.TurboOff,
            ["BoostMode"] = WinPower.GetBoostMode(),
            ["PowerPlanHigh"] = _state.HighPerformancePowerMode,
            ["UndervoltSteps"] = _state.CpuUndervoltSteps,
            ["UndervoltRange"] = new[] { 0, 50 },
            ["Spl"] = ReadCpuPowerLimit(2, _state.CpuAmdSPL, 85),
            ["Sppt"] = ReadCpuPowerLimit(3, _state.CpuAmdSPPT, 120),
            ["SplRange"] = new[] { 15, 120 },
            ["SpptRange"] = new[] { 15, 130 },
            ["CpuCores"] = CpuCoreCount(),
            ["CpuThreads"] = Environment.ProcessorCount,
            ["CpuNominalMhz"] = WinPower.NominalMhz(),
            ["FreqLimit"] = WinPower.GetMaxFreqMhz(),          // -1 = 不限
            ["FreqLimitRange"] = new[] { 1200, 5200 },

            // GPU 卡
            ["GpuOcOn"] = _state.GpuOverClockingSwitch,
            ["GpuCoreOffset"] = _state.GpuCoreOffset,
            ["GpuMemOffset"] = _state.GpuMemOffset,
            ["GpuCoreOffsetRange"] = new[] { 0, 250 },
            ["GpuMemOffsetRange"] = new[] { 0, 1000 },
            ["GpuLockOn"] = _state.GpuLockOn,
            ["GpuLockMhz"] = _state.GpuLockMhz,
            ["GpuLockSupported"] = _nvml?.Available == true,
            ["GpuLockClocks"] = _nvml?.SupportedGraphicsClocks() ?? new List<uint>(),
            ["GpuMaxClock"] = _nvml?.MaxGraphicsClock() ?? 0,

            // 风扇卡：自定义转速（固定占空比模式，FanController 控制环落地）
            ["FanMaxRpm"] = _state.FanMaxRpm,
            ["TempWall"] = ReadCpuPowerLimit(4, _state.CpuAmdTccTarget, 95),

            // 各项启用开关（v9j）
            ["Pl1Enabled"] = _state.Pl1Enabled,
            ["Pl2Enabled"] = _state.Pl2Enabled,
            ["UndervoltEnabled"] = _state.UndervoltEnabled,
            ["GpuCoreOffsetEnabled"] = _state.GpuCoreOffsetEnabled,
            ["GpuMemOffsetEnabled"] = _state.GpuMemOffsetEnabled,
            ["GpuLockMhzEnabled"] = _state.GpuLockMhzEnabled,
            ["FanMaxRpmEnabled"] = _state.FanMaxRpmEnabled,
            ["TempWallEnabled"] = _state.TempWallEnabled,
            ["FreqLimitEnabled"] = _state.FreqLimitEnabled,
        };
        await PublishAsync("JiaoLong/HomeStatus", payload, retain: true);
    }

    /// <summary>SPL/SPPT 当前值：优先读固件，读不到用本地状态，再不行用默认。</summary>
    private int ReadCpuPowerLimit(byte subState, int? stateValue, int fallback)
    {
        try
        {
            int fw = _mifs.GetCpuPower(subState);
            if (fw > 0) return fw;
        }
        catch { }
        return stateValue ?? fallback;
    }

    private int? _cpuCores;

    /// <summary>物理核心数（WMI，Environment.ProcessorCount 在服务进程可能只见亲和性子集）。</summary>
    private int CpuCoreCount()
    {
        if (_cpuCores is int n) return n;
        int cores = 0;
        try
        {
            using var s = new ManagementObjectSearcher("root\\CIMV2", "SELECT NumberOfCores FROM Win32_Processor");
            foreach (var mo in s.Get()) cores += Convert.ToInt32(mo["NumberOfCores"]);
        }
        catch { }
        _cpuCores = cores > 0 ? cores : Environment.ProcessorCount;
        return _cpuCores.Value;
    }

    public async Task HandleHomeControlAsync(string? action, JsonElement root)
    {
        switch (action)
        {
            case "GETSTATUS":
                await SendHomeStatusAsync();
                break;

            // ---------- CPU 卡 ----------
            case "TURBO":
                ApplyTurbo(GetBool(root, "On", false));
                break;
            case "POWER_PLAN":
                ApplyPowerPlan(GetBool(root, "High", false));
                break;
            case "UNDERVOLT":
            {
                int steps = GetInt(root, "Steps");
                bool en = GetBool(root, "Enabled", _state.UndervoltEnabled);
                _state.UndervoltEnabled = en;
                _state.CpuUndervoltSteps = Math.Clamp(steps, 0, 50);
                int applyUv = en ? _state.CpuUndervoltSteps : 0;
                if (_ec.Available)
                {
                    bool ok = _ec.SetCpuCoAll(applyUv);
                    Log.Info($"home undervolt steps={applyUv} ({(en ? "on" : "off->0")}): {(ok ? "OK" : "FAILED")}");
                }
                _state.Save();
                break;
            }
            case "SPL":
            {
                int w = GetInt(root, "Watts");
                bool en = GetBool(root, "Enabled", _state.Pl1Enabled);
                _state.Pl1Enabled = en;
                _state.CpuAmdSPL = w;
                int applyW = en ? w : 85;
                try { _mifs.SetCpuPower(2, (byte)Math.Clamp(applyW, 0, 200)); } catch { }
                Log.Info($"home SPL = {applyW}W ({(en ? "on" : "off->default 85")})");
                _state.Save();
                break;
            }
            case "SPPT":
            {
                int w = GetInt(root, "Watts");
                bool en = GetBool(root, "Enabled", _state.Pl2Enabled);
                _state.Pl2Enabled = en;
                _state.CpuAmdSPPT = w;
                int applyW = en ? w : 120;
                try { _mifs.SetCpuPower(3, (byte)Math.Clamp(applyW, 0, 200)); } catch { }
                Log.Info($"home SPPT = {applyW}W ({(en ? "on" : "off->default 120")})");
                _state.Save();
                break;
            }

            // ---------- GPU 卡 ----------
            case "GPU_OC":
                _state.GpuOverClockingSwitch = GetBool(root, "On", false);
                ApplyGpuOverclock();
                _state.Save();
                break;
            case "GPU_CORE_OFFSET":
                _state.GpuCoreOffset = GetInt(root, "Mhz");
                _state.GpuCoreOffsetEnabled = GetBool(root, "Enabled", _state.GpuCoreOffsetEnabled);
                Log.Info($"home GPU core offset={_state.GpuCoreOffset}MHz ({( _state.GpuCoreOffsetEnabled ? "on" : "off->0")})");
                if (_state.GpuOverClockingSwitch) ApplyGpuOverclock();
                _state.Save();
                break;
            case "GPU_MEM_OFFSET":
                _state.GpuMemOffset = GetInt(root, "Mhz");
                _state.GpuMemOffsetEnabled = GetBool(root, "Enabled", _state.GpuMemOffsetEnabled);
                Log.Info($"home GPU mem offset={_state.GpuMemOffset}MHz ({( _state.GpuMemOffsetEnabled ? "on" : "off->0")})");
                if (_state.GpuOverClockingSwitch) ApplyGpuOverclock();
                _state.Save();
                break;
            case "GPU_LOCK":
            {
                bool on = GetBool(root, "On", false);
                _state.GpuLockOn = on;
                bool ok;
                if (on && _state.GpuLockMhz > 0)
                    ok = _nvml?.SetLockedClocks((uint)_state.GpuLockMhz) ?? false;
                else
                    ok = _nvml?.ResetLockedClocks() ?? false;
                Log.Info($"home GPU lock on={on} mhz={_state.GpuLockMhz}: {(ok ? "OK" : "FAILED")}");
                _state.Save();
                break;
            }
            case "GPU_LOCK_MHZ":
            {
                _state.GpuLockMhz = GetInt(root, "Mhz");
                _state.GpuLockMhzEnabled = GetBool(root, "Enabled", _state.GpuLockMhzEnabled);
                bool applyLock = _state.GpuLockOn && _state.GpuLockMhzEnabled && _state.GpuLockMhz > 0;
                bool ok = applyLock
                    ? (_nvml?.SetLockedClocks((uint)_state.GpuLockMhz) ?? false)
                    : (_nvml?.ResetLockedClocks() ?? false);
                Log.Info($"home GPU lock mhz={_state.GpuLockMhz} ({(applyLock ? "on" : "off->unlock")}): {(ok ? "OK" : "FAILED")}");
                _state.Save();
                break;
            }

            // ---------- 风扇卡 ----------
            case "MAX_FAN":
            {
                int rpm = Math.Clamp(GetInt(root, "Value"), 0, 7000);
                bool en = GetBool(root, "Enabled", _state.FanMaxRpmEnabled);
                _state.FanMaxRpm = rpm;
                _state.FanMaxRpmEnabled = en;

                if (en && rpm > 0)
                {
                    // 实测有效路径（2026-10-18）：EC 占空比闭环，MIFS fn13 实测转速反馈，
                    // 收敛到目标 RPM（上限=厂商规格 ~5800）。
                    _fan.StopLoop();
                    _fan.EnsureLoop();
                    Log.Info($"home MAX_FAN fixed={rpm}RPM (EC closed-loop, start duty {FanController.RpmToDuty(rpm)}%)");
                }
                else
                {
                    // 关闭 = 恢复固件自动风扇
                    try { _mifs.SetFanSpeedSwitch(false); } catch { }
                    if (!_state.CustomLoopActive)
                    {
                        if (_ec.FanEcSafe) _ec.ReleaseFanManual();
                        _fan.StopLoop();
                    }
                    Log.Info("home MAX_FAN off->auto");
                }
                _state.Save();
                break;
            }
            case "TEMP_WALL":
            {
                int t = Math.Clamp(GetInt(root, "Value"), 40, 110);
                bool en = GetBool(root, "Enabled", _state.TempWallEnabled);
                _state.TempWallEnabled = en;
                _state.CpuAmdTccTarget = t;
                int applyT = en ? t : 95;
                try { _mifs.SetCpuPower(4, (byte)applyT); } catch { }
                Log.Info($"home TEMP_WALL = {applyT}C ({(en ? "on" : "off->default 95")})");
                _state.Save();
                break;
            }

            // 限制最高频率（取代原 UNDERVOLT 降压步数）：powrprof 写 FREQUENCY_MAX(MHz)
            // + THROTTLE_MAX(%)，cap 到指定 MHz；关闭 = 取消限制（写回 0 / 100）。
            case "FREQ_LIMIT":
            {
                int mhz = Math.Clamp(GetInt(root, "Mhz"), 0, 6000);
                bool en = GetBool(root, "Enabled", _state.FreqLimitEnabled);
                _state.FreqLimitEnabled = en;
                _state.CpuFreqLimit = mhz;
                int applyMhz = en && mhz > 0 ? mhz : 0;
                bool ok = WinPower.SetMaxFreqMhz(applyMhz);
                Log.Info($"home FREQ_LIMIT = {(applyMhz > 0 ? applyMhz + "MHz" : "不限")} " +
                         $"({(en ? "on" : "off->unlimited")}): {(ok ? "OK" : "FAILED")} " +
                         $"(生效值 {WinPower.GetMaxFreqMhz()}MHz)");
                _state.Save();
                break;
            }

            default:
                Log.Info($"home action {action} acknowledged (no-op)");
                break;
        }

        // 任何写入后回发状态（GETSTATUS 自身会回发，这里跳过重复）
        if (action != "GETSTATUS")
            await SendHomeStatusAsync();
    }

    private static bool GetBool(JsonElement root, string name, bool fallback)
    {
        if (root.TryGetProperty(name, out var e))
        {
            if (e.ValueKind == JsonValueKind.True) return true;
            if (e.ValueKind == JsonValueKind.False) return false;
            if (e.ValueKind == JsonValueKind.String && bool.TryParse(e.GetString(), out var b)) return b;
            if (e.ValueKind == JsonValueKind.Number) return e.GetInt32() != 0;
        }
        return fallback;
    }

    private static int GetInt(JsonElement root, string name, int fallback = 0)
    {
        if (root.TryGetProperty(name, out var e))
        {
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n)) return n;
            if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var s)) return s;
        }
        return fallback;
    }

    /// <summary>关闭/恢复睿频（电源计划 PERFBOOSTMODE）。</summary>
    private void ApplyTurbo(bool on)
    {
        bool ok;
        if (on)
        {
            int prev = _state.TurboBoostPrevValue ?? 2; // 无记录时按 Aggressive 恢复
            ok = WinPower.SetTurbo(false, prev);
            Log.Info($"home turbo ON (restore boost mode {prev}): {(ok ? "OK" : "FAILED")}");
        }
        else
        {
            int cur = WinPower.GetBoostMode();
            if (cur > 0) _state.TurboBoostPrevValue = cur; // 记住原值供恢复
            ok = WinPower.SetTurbo(true, 0);
            Log.Info($"home turbo OFF (prev={_state.TurboBoostPrevValue}): {(ok ? "OK" : "FAILED")}");
        }
        _state.TurboOff = !on;
        _state.Save();
    }

    /// <summary>高性能/平衡电源计划（与电池页「高性能电源模式」同一后端与状态）。</summary>
    private void ApplyPowerPlan(bool high)
    {
        _state.HighPerformancePowerMode = high;
        bool ok = WinPower.SetActivePlan(high ? WinPower.PlanHigh : WinPower.PlanBalanced);
        // PowerSetActiveScheme 会用计划存储值覆盖隐藏设置 → 睿频关闭态需补写
        if (_state.TurboOff)
        {
            bool t = WinPower.SetTurbo(true, 0);
            Log.Info($"home power plan switch re-apply turbo off: {t}");
        }
        // 频率上限写在「当时的活动计划」里，切计划后必须重新下发到新计划
        if (_state.FreqLimitEnabled && _state.CpuFreqLimit > 0)
        {
            bool f = WinPower.SetMaxFreqMhz(_state.CpuFreqLimit);
            Log.Info($"home power plan switch re-apply freq limit {_state.CpuFreqLimit}MHz: {f}");
        }
        // 温度墙走固件通道，与电源计划无关；仅在启用时补写一次，避免无谓固件写
        if (_state.TempWallEnabled && _state.CpuAmdTccTarget is int tcc)
        {
            try { _mifs.SetCpuPower(4, (byte)Math.Clamp(tcc, 40, 110)); } catch { }
        }
        Log.Info($"home power plan high={high}: {(ok ? "OK" : "FAILED")} (当前 {WinPower.ActivePlan()})");
        _state.Save();
    }

    /// <summary>开机恢复主页控制项状态（Program.StartAsync 调用）。</summary>
    public void RestoreHomeSettings()
    {
        try
        {
            if (_state.TurboOff)
            {
                bool ok = WinPower.SetTurbo(true, 0);
                Log.Info($"restore turbo off: {ok} (当前 boost mode {WinPower.GetBoostMode()})");
            }
        }
        catch (Exception ex) { Log.Error($"restore turbo failed: {ex.Message}"); }

        // 频率上限：服务重启（开机）后补写一次，避免电源计划被别处切换后失效
        try
        {
            if (_state.FreqLimitEnabled && _state.CpuFreqLimit > 0)
            {
                bool ok = WinPower.SetMaxFreqMhz(_state.CpuFreqLimit);
                Log.Info($"restore freq limit {_state.CpuFreqLimit}MHz: {ok} (生效值 {WinPower.GetMaxFreqMhz()}MHz)");
            }
        }
        catch (Exception ex) { Log.Error($"restore freq limit failed: {ex.Message}"); }

        // 温度墙：固件 TCC 目标，服务重启后同样补写
        try
        {
            if (_state.TempWallEnabled && _state.CpuAmdTccTarget is int tcc)
            {
                _mifs.SetCpuPower(4, (byte)Math.Clamp(tcc, 40, 110));
                Log.Info($"restore temp wall {tcc}C");
            }
        }
        catch (Exception ex) { Log.Error($"restore temp wall failed: {ex.Message}"); }

        // 自定义风扇转速：EC 占空比闭环，服务重启（开机）后恢复控制环
        try
        {
            if (_state.FanMaxRpmEnabled && _state.FanMaxRpm > 0)
            {
                _fan.EnsureLoop();
                Log.Info($"restore fan fixed {_state.FanMaxRpm}RPM (EC closed-loop)");
            }
        }
        catch (Exception ex) { Log.Error($"restore fan fixed failed: {ex.Message}"); }

        try
        {
            if (_nvml?.Available == true && _state.GpuLockOn && _state.GpuLockMhz > 0)
            {
                bool ok = _nvml.SetLockedClocks((uint)_state.GpuLockMhz);
                Log.Info($"restore GPU lock {_state.GpuLockMhz}MHz: {ok}");
            }
        }
        catch (Exception ex) { Log.Error($"restore gpu lock failed: {ex.Message}"); }
    }
}
