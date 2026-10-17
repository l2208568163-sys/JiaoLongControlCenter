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

            // 风扇卡：自定义转速上限（本机固件 fn20/21 与 EC 直写实测均无效，报告 §14.6，
            // 写入为状态记忆 + acknowledge；>5800 由 UI 提示超转风险）
            ["FanMaxRpm"] = _state.FanMaxRpm,
            ["FanMaxSwitch"] = _state.FanMaxSwitch,
            ["TempWall"] = ReadCpuPowerLimit(4, _state.CpuAmdTccTarget, 95),
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
                _state.CpuUndervoltSteps = Math.Clamp(steps, 0, 50);
                if (_ec.Available)
                {
                    bool ok = _ec.SetCpuCoAll(_state.CpuUndervoltSteps);
                    Log.Info($"home undervolt steps={_state.CpuUndervoltSteps}: {(ok ? "OK" : "FAILED")}");
                }
                _state.Save();
                break;
            }
            case "SPL":
            {
                int w = GetInt(root, "Watts");
                _state.CpuAmdSPL = w;
                try { _mifs.SetCpuPower(2, (byte)Math.Clamp(w, 0, 200)); } catch { }
                Log.Info($"home SPL = {w}W");
                _state.Save();
                break;
            }
            case "SPPT":
            {
                int w = GetInt(root, "Watts");
                _state.CpuAmdSPPT = w;
                try { _mifs.SetCpuPower(3, (byte)Math.Clamp(w, 0, 200)); } catch { }
                Log.Info($"home SPPT = {w}W");
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
                if (_state.GpuOverClockingSwitch) ApplyGpuOverclock();
                _state.Save();
                break;
            case "GPU_MEM_OFFSET":
                _state.GpuMemOffset = GetInt(root, "Mhz");
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
                if (_state.GpuLockOn)
                {
                    bool ok = _nvml?.SetLockedClocks((uint)_state.GpuLockMhz) ?? false;
                    Log.Info($"home GPU lock mhz={_state.GpuLockMhz}: {(ok ? "OK" : "FAILED")}");
                }
                _state.Save();
                break;
            }

            // ---------- 风扇卡 ----------
            case "MAX_FAN":
            {
                if (root.TryGetProperty("Switch", out var swEl))
                {
                    bool on = GetBool(root, "Switch", false);
                    _state.FanMaxSwitch = on;
                    Log.Info($"home MAX_FAN switch={on}");
                }
                if (root.TryGetProperty("Value", out _))
                {
                    int rpm = Math.Clamp(GetInt(root, "Value"), 0, 7000);
                    _state.FanMaxRpm = rpm;
                    // 写入通道：固件 fn20/21 与 EC 直写实测均无效（报告 §14.6），此处置为
                    // 状态记忆 + 确认回执；>5800 由 UI 提示超转风险。
                    Log.Info($"home MAX_FAN value={rpm}{(rpm > 5800 ? " (超转风险区 >5800)" : "")}");
                }
                _state.Save();
                break;
            }
            case "TEMP_WALL":
            {
                int t = Math.Clamp(GetInt(root, "Value"), 40, 110);
                _state.CpuAmdTccTarget = t;
                try { _mifs.SetCpuPower(4, (byte)t); } catch { }
                Log.Info($"home TEMP_WALL = {t}C");
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
