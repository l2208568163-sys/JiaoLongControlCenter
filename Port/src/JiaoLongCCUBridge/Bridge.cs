using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace JiaoLongCCUBridge;

/// <summary>简单文件日志。</summary>
public static class Log
{
    private static readonly object Lock = new();
    private static string? _dir;

    public static string Dir => _dir ??= Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "JiaoLongCCUBridge");

    public static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Dir);
                var path = Path.Combine(Dir, $"bridge_{DateTime.Now:yyyyMMdd}.log");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}| {level} | {message}{Environment.NewLine}";
                File.AppendAllText(path, line);
                if (new FileInfo(path).Length > 8 * 1024 * 1024) // 简单限容：超 8MB 截断
                {
                    var text = File.ReadAllText(path);
                    File.WriteAllText(path, text[^(4 * 1024 * 1024)..]);
                }
            }
        }
        catch { }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);
}

/// <summary>MQTT 发布助手。</summary>
public interface IPublisher
{
    Task PublishAsync(string topic, object payload, bool retain = false);
}

/// <summary>协议核心：解析 UI 发来的命令 Topic 并回复。</summary>
public sealed partial class ProtocolHandlers : IPublisher
{
    public const string BrokerHost = "127.0.0.1";
    public const int BrokerPort = 13688;

    public const int CustomizeTarget = 56;             // Mechrevo_GX
    public const string ProjectId = "ID6";             // 蛟龙16 Pro 2023 (MRID6)

    private readonly Mifs _mifs;
    private readonly StateStore _state;
    private readonly Nvml? _nvml;
    private readonly FanController _fan;
    private readonly EcAccess _ec;
    private readonly KeyboardEffects _kbdFx;
    private readonly Func<bool> _telemetryGate;
    private readonly Action<bool> _telemetrySwitch;

    // —— 本机静态信息（构造时采集）——
    private readonly string _cpuName = "";
    private readonly int _baseClockMhz;
    private readonly string _gpuCardName = "";
    private readonly string _productInfo;
    private readonly string _memoryInfo;
    private double _maxSeenCpuMhz;

    private static readonly JsonSerializerOptions JsonOpts = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public ProtocolHandlers(Mifs mifs, StateStore state, Nvml? nvml, FanController fan, EcAccess ec, KeyboardEffects kbdFx,
        Func<bool> telemetryGate, Action<bool> telemetrySwitch)
    {
        _mifs = mifs;
        _state = state;
        _nvml = nvml;
        _fan = fan;
        _ec = ec;
        _kbdFx = kbdFx;
        _telemetryGate = telemetryGate;
        _telemetrySwitch = telemetrySwitch;
        (_cpuName, _baseClockMhz) = ReadCpu();
        _gpuCardName = _nvml?.GpuName ?? ReadGpuName();
        _productInfo = ReadProductInfo();
        _memoryInfo = ReadMemory();
    }

    // ============================ 发布 ============================

    public async Task PublishAsync(string topic, object payload, bool retain = false)
    {
        var json = JsonSerializer.Serialize(payload);
        PublishJson(topic, json, retain);
        await Task.CompletedTask;
    }

    public event Func<string, string, bool, Task>? PublishRequested;

    public void PublishJson(string topic, string json, bool retain = false)
    {
        PublishRequested?.Invoke(topic, json, retain);
        Log.Info($"PUB {topic} {json}");
    }

    // ============================ 分发 ============================

    public async void HandleMessage(string topic, byte[] payloadBytes)
    {
        string json = Encoding.UTF8.GetString(payloadBytes);
        Log.Info($"MSG {topic} {json}");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? action = root.TryGetProperty("Action", out var a) ? a.GetString() : null;

            switch (topic)
            {
                case "Customize/SupportControl":
                    await SendSupportInfoAsync();
                    break;

                case "Customize/Control":
                    await SendCustomizeInfoAsync();
                    break;

                case "System/Control":
                    if (action == "System_ON") _telemetrySwitch(true);
                    else if (action == "System_OFF") _telemetrySwitch(false);
                    break;

                case "Setting/Control":
                    await HandleSettingControlAsync(action, root);
                    break;

                case "Keyboard/Ctrl":
                    await HandleKeyboardControlAsync(action, root);
                    break;

                case "Fan/Control":
                    await HandleFanControlAsync(action, root);
                    break;

                case "BatteryProtection/Control":
                    await HandleBatteryControlAsync(action, root);
                    break;

                case "JiaoLong/HomeControl":
                    await HandleHomeControlAsync(action, root);
                    break;

                case "Languages/Control":
                    if (root.TryGetProperty("Lang", out var lang))
                    {
                        RegistrySetup.WriteString(@"SOFTWARE\OEM\GamingCenter2", "Language", lang.GetString() ?? "zh-cn");
                    }
                    break;

                default:
                    Log.Info($"IGNORED {topic}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"handle {topic} failed: {ex}");
        }
    }

    // ============================ Keyboard（RGB 键盘灯效，MIFS fn16/17/18） ============================

    /// <summary>
    /// 本机固件仅支持：0=关 1=内置流光(Rainbow) 2=固定颜色(Single)，亮度 0-3。
    /// UI 的其他效果做近似映射：Wave/雨滴等→固件流光；Manual→取第一色固定；Breathing→固定色。
    /// </summary>
    private async Task HandleKeyboardControlAsync(string? action, JsonElement root)
    {
        string function = action ?? (root.TryGetProperty("function", out var f) ? f.GetString() : null) ?? "";
        try
        {
            switch (function)
            {
                case "GETSTATUS":
                    break; // 末尾统一回发状态

                case "SetPower":
                    if (root.TryGetProperty("powerstatus", out var ps))
                    {
                        bool on = ps.ValueKind == JsonValueKind.Number ? ps.GetInt32() != 0 : ps.GetString() == "1";
                        _state.KeyboardOn = on;
                        _state.KeyboardMode = on ? (_state.KeyboardMode == 0 ? (byte)2 : _state.KeyboardMode) : (byte)0;
                        ApplyKeyboardState();
                    }
                    break;

                case "SetLightingLevel":
                    if (root.TryGetProperty("light", out var lvl) && byte.TryParse(lvl.GetString(), out byte bright))
                    {
                        _state.KeyboardBrightness = Math.Clamp(bright, (byte)0, (byte)3);
                        _state.KeyboardOn = _state.KeyboardBrightness > 0 || _state.KeyboardOn;
                        if (_state.KeyboardOn) SafeMifs(() => _mifs.SetKeyboardBrightness(_state.KeyboardBrightness));
                    }
                    break;

                case "SetEffectALL":
                {
                    string? effect = root.TryGetProperty("effect", out var e) ? e.GetString() : null;
                    string? mode = root.TryGetProperty("mode", out var m) ? m.GetString() : null;
                    byte? light = null;
                    if (root.TryGetProperty("light", out var l2) && byte.TryParse(l2.GetString(), out byte lb))
                        light = Math.Clamp(lb, (byte)0, (byte)3);
                    if (light.HasValue) _state.KeyboardBrightness = light.Value;

                    // 颜色：取 ColorBuffer[0]（SingleZone 协议实际生效的第一色）
                    (byte r, byte g, byte b)? color = null;
                    if (root.TryGetProperty("color", out var colorObj) &&
                        colorObj.TryGetProperty("ColorBuffer", out var buf) &&
                        buf.GetArrayLength() > 0 &&
                        buf[0].TryGetProperty("R", out var rEl) && buf[0].TryGetProperty("G", out var gEl) && buf[0].TryGetProperty("B", out var bEl))
                    {
                        color = ((byte)Clamp255(rEl), (byte)Clamp255(gEl), (byte)Clamp255(bEl));
                    }

                    string? speed = root.TryGetProperty("speed", out var spd) ? spd.GetString() : null;
                    ApplyKeyboardEffect(effect, mode, speed, color);
                    break;
                }

                default:
                    Log.Info($"keyboard function {function} acknowledged");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("keyboard control: " + ex.Message);
        }
        _state.Save();
        await SendKeyboardStatusAsync();
    }

    private static int Clamp255(JsonElement e)
    {
        int v = e.ValueKind == JsonValueKind.Number ? e.GetInt32() : (int.TryParse(e.GetString(), out var x) ? x : 0);
        return Math.Clamp(v, 0, 255);
    }

    private void ApplyKeyboardEffect(string? effect, string? mode, string? speed, (byte r, byte g, byte b)? color)
    {
        _state.KeyboardOn = true;
        if (color.HasValue)
        {
            _state.KeyboardColorHex = $"{color.Value.r:X2}{color.Value.g:X2}{color.Value.b:X2}";
        }
        string eff = (effect ?? "single").ToLowerInvariant();
        SafeMifs(() => _mifs.SetKeyboardBrightness(_state.KeyboardBrightness));

        switch (eff)
        {
            case "single":
            case "manual":
            case "usermode":
                // 固定颜色
                _kbdFx.StopEffects();
                _state.KeyboardMode = 2;
                SafeMifs(() =>
                {
                    _mifs.SetKeyboardMode(2);
                    Thread.Sleep(500); // 固件时序：切固定模式后延时写颜色
                    var c = HexToRgb(_state.KeyboardColorHex);
                    _mifs.SetKeyboardColor(c.r, c.g, c.b);
                });
                break;

            case "breathing":
                // 软件呼吸：亮度脉冲 + 静态色
                _kbdFx.StartBreathing(_state.KeyboardColorHex);
                _state.KeyboardMode = 2;
                break;

            default:
                // 流光类（Rainbow/Wave/Mix/Flash/Ripple/Raindrop/Marquee/Spark/Aurora/Gaming/Sine/...）
                // 固件"流光"模式在本机无可见效果 → 软件彩虹循环（旧程序同款）
                _kbdFx.StartRainbowFlow(speed ?? "2");
                _state.KeyboardMode = 2;
                break;
        }
        Log.Info($"keyboard effect={effect} mode={mode} -> fx={_kbdFx.CurrentEffect} color={_state.KeyboardColorHex}");
    }

    private void ApplyKeyboardState()
    {
        SafeMifs(() =>
        {
            if (!_state.KeyboardOn)
            {
                _kbdFx.StopEffects();
                _mifs.SetKeyboardMode(0);
                return;
            }
            _mifs.SetKeyboardBrightness(_state.KeyboardBrightness);
            if (_kbdFx.CurrentEffect == "rainbow" || _kbdFx.CurrentEffect == "breathing")
            {
                return; // 动态效果循环运行中，不覆盖
            }
            _mifs.SetKeyboardMode(_state.KeyboardMode);
            if (_state.KeyboardMode == 2)
            {
                Thread.Sleep(500);
                var c = HexToRgb(_state.KeyboardColorHex);
                _mifs.SetKeyboardColor(c.r, c.g, c.b);
            }
        });
    }

    private static (byte r, byte g, byte b) HexToRgb(string hex)
    {
        try { return (Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)); }
        catch { return (255, 255, 255); }
    }

    /// <summary>键盘定时关灯检查（由 Program 的 30 秒定时器调用）。</summary>
    public async Task CheckKeyboardTimerAsync()
    {
        var remain = RemainingKeyboardTimerMinutes();
        if (remain.HasValue && remain.Value <= 0 && _state.KeyboardOn)
        {
            Log.Info("键盘定时关灯触发");
            _state.KeyboardOn = false;
            _state.KeyboardTimerDeadline = null;
            ApplyKeyboardState();
            _state.Save();
            await SendKeyboardStatusAsync();
        }
    }

    private async Task SendKeyboardStatusAsync()
    {
        // 优先读固件真实状态
        try
        {
            byte fwMode = _mifs.GetKeyboardMode();
            if (fwMode == 0 && _state.KeyboardOn) _state.KeyboardOn = false;
            else if (fwMode != 0) { _state.KeyboardOn = true; _state.KeyboardMode = fwMode; }
            var (r, g, b) = _mifs.GetKeyboardColor();
            _state.KeyboardColorHex = $"{r:X2}{g:X2}{b:X2}";
            _state.KeyboardBrightness = _mifs.GetKeyboardBrightness();
        }
        catch { }

        var payload = new Dictionary<string, object?>
        {
            ["type"] = "FourZone",
            ["powerStatus"] = _state.KeyboardOn ? "On" : "Off",
            ["brightNess"] = _state.KeyboardBrightness.ToString(),
            ["currentPowerMode"] = "AC",
            // 注意：不发送 MonochromeIndex/ManualIndex*/DefaultData（曾触发 UI 解析闪退），
            // 视图侧的本地保存状态足以初始化页面。
        };
        await PublishAsync("Keyboard/Status", payload);
    }

    // ============================ Customize ============================

    private Task SendSupportInfoAsync()
    {
        var payload = new Dictionary<string, object>
        {
            ["ServiceReady"] = 1,
            ["CustomizeTarget"] = CustomizeTarget,
            ["ProjectID"] = ProjectId,
            ["BIOS_PROJECT_ID"] = ProjectId,
            ["IsAMDPlatform"] = 1,
            ["IsNvGpu"] = 1,
            ["LightbarSupport"] = 0,
            ["RGBLightbarSupport"] = 0,
            ["KeyboardType"] = 0,
            ["Language"] = "zh-cn",
            ["IsProjectIdCommercial"] = 0,
            ["IsProjectIdCommercialHAVE20DB"] = 0,
            ["AcRecoverySwitchSupport"] = 0,
        };
        return PublishAsync("Customize/SupportInfo", payload);
    }

    private Task SendCustomizeInfoAsync()
    {
        // ProjectID 必须是 UI 端 ProjectID 枚举的合法成员名（LightViewModel 会
        // NBProjectID = Support["ProjectID"] 强转，非法名（如本机代号 ID6）会抛
        // ArgumentException → 闪退）。语义上本机非 Uniwill 项目，发 None。
        var payload = new Dictionary<string, object?>
        {
            ["CustomizeTarget"] = CustomizeTarget,
            ["ProjectID"] = "None",
            ["KeyboardType"] = "1", // 非"2"(单色键盘)：由 Keyboard/Status 的 type=FourZone 决定页面形态
            ["LightbarType"] = "0",
        };
        return PublishAsync("Customize/Info", payload);
    }

    // ============================ Setting ============================

    private async Task HandleSettingControlAsync(string? action, JsonElement root)
    {
        bool refreshGpuStatus = false;
        switch (action)
        {
            case "GETSTATUS":
                refreshGpuStatus = true;
                break;

            // —— 设备开关（SetupAPI 真实启用/禁用，官方 CCU 同语义）——
            case "WIFI_OFF":
                DeviceToggles.SetEnabled("wifi", false);
                break;
            case "WIFI_ON":
                DeviceToggles.SetEnabled("wifi", true);
                break;
            case "BT_OFF":
                DeviceToggles.SetEnabled("bt", false);
                break;
            case "BT_ON":
                DeviceToggles.SetEnabled("bt", true);
                break;
            case "WEBCAM_OFF":
                DeviceToggles.SetEnabled("cam", false);
                break;
            case "WEBCAM_ON":
                DeviceToggles.SetEnabled("cam", true);
                break;
            case "SCANFORHARDWARECHANGE":
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pnputil", "/scan-devices") { CreateNoWindow = true, UseShellExecute = false });
                break;

            // —— Fn 锁 / 触摸板（MIFS fn11/fn12，真实生效）——
            case "FNKEY_LOCK":
                SafeMifs(() => _mifs.SetFnLock(true));
                break;
            case "FNKEY_UNLOCK":
                SafeMifs(() => _mifs.SetFnLock(false));
                break;
            case "TOUCHPAD_TOGGLE_ON":
            case "TOUCHPAD_ON":
                SafeMifs(() => _mifs.SetTouchpadLocked(false));
                break;
            case "TOUCHPAD_TOGGLE_OFF":
            case "TOUCHPAD_OFF":
                SafeMifs(() => _mifs.SetTouchpadLocked(true));
                break;

            // —— 状态记忆类（无固件后端，UI 开关状态可保持）——
            case "WINKEY_LOCK": _state.WinKeyState = "LOCK"; break;
            case "WINKEY_UNLOCK": _state.WinKeyState = "UNLOCK"; break;
            case "NUMPAD_LOCK": _state.NumPadState = "LOCK"; break;
            case "NUMPAD_UNLOCK": _state.NumPadState = "UNLOCK"; break;
            case "OSD_HIDDEN_ON": _state.OsdState = "OFF"; break;
            case "OSD_HIDDEN_OFF": _state.OsdState = "ON"; break;

            // —— 高性能电源模式（电池页「电源设置」，真实生效：切 Windows 电源计划，
            //     官方 GCUService 的 PowerPlanSetting.HighPerformanceActive 同语义）——
            case "HIGHPERFORMANCEPOWERMODE_ON":
            {
                _state.HighPerformancePowerMode = true;
                bool ok = WinPower.SetActivePlan(WinPower.PlanHigh);
                Log.Info($"高性能电源模式 ON -> 电源计划 高性能{(ok ? " 已激活" : " 激活失败")}");
                break;
            }
            case "HIGHPERFORMANCEPOWERMODE_OFF":
            {
                _state.HighPerformancePowerMode = false;
                bool ok = WinPower.SetActivePlan(WinPower.PlanBalanced);
                Log.Info($"高性能电源模式 OFF -> 电源计划 平衡{(ok ? " 已激活" : " 激活失败")}");
                break;
            }

            // —— 电池页其余开关（BIOS/固件级功能，本机无接口，仅状态记忆保持 UI 开关）——
            case "USB_CHARGER_ON": _state.UsbChargerState = "ON"; break;
            case "USB_CHARGER_OFF": _state.UsbChargerState = "OFF"; break;
            case "ACRECOVERY_TOGGLE_ON": _state.AcRecoveryState = "ON"; break;
            case "ACRECOVERY_TOGGLE_OFF": _state.AcRecoveryState = "OFF"; break;
            case "BATTERYLOGO_TOGGLE_ON": _state.BatteryLogoState = "ON"; break;
            case "BATTERYLOGO_TOGGLE_OFF": _state.BatteryLogoState = "OFF"; break;
            case "DEEPSLEEP_ON":
                _state.DeepSleepState = "ON";
                if (root.TryGetProperty("Secs", out var secs)) _state.DeepSleepSeconds = secs.GetInt32();
                Log.Info("DEEPSLEEP_ON 仅状态记忆：深度睡眠为 BIOS 级功能，本机无固件接口");
                break;
            case "DEEPSLEEP_OFF": _state.DeepSleepState = "OFF"; break;

            // —— 独显直连（fn9，重启生效；状态以固件 GET 为准，与旧程序一致）——
            case "DGPU_DIRECT_CONNECT_TOGGLE_ON":
                SafeMifs(() => _mifs.SetGpuMode(1));
                _state.DgpuDirectOn = true;
                break;
            case "DGPU_DIRECT_CONNECT_TOGGLE_OFF":
                SafeMifs(() => _mifs.SetGpuMode(0));
                _state.DgpuDirectOn = false;
                break;
            case "DGPU_DIRECT_CONNECT_RESTART":
                Log.Info("用户确认重启以应用显卡模式");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 5 /c \"机械革命控制中心：显卡模式已切换，即将重启\"") { CreateNoWindow = true, UseShellExecute = false });
                break;

            // —— 刷新率（经用户会话助手执行） ——
            case "GPU_HZSETTING":
                if (root.TryGetProperty("Hz", out var hzEl) && int.TryParse(hzEl.GetString(), out int hz))
                {
                    var r = RunDisplayHelper(hz);
                    Log.Info($"刷新率切换 {hz}Hz: {(r.HasValue ? $"applied={r.Value.applied}" : "失败")}");
                }
                refreshGpuStatus = true;
                break;
            case "GPU_DC_HZ": // 自动刷新率开关：仅记录，不回发状态（避免与 UI 形成乒乓循环）
                Log.Info($"GPU_DC_HZ Enable={root.TryGetProperty("Enable", out var en) && en.GetBoolean()}");
                break;

            // —— 校色（sRGB ICC 关联）——
            case "COLOR_CALIBRATION_ON":
            case "COLOR_CALIBRATION_ON_DEFAULT":
            case "COLOR_CALIBRATION_ON_SRGB":
            case "COLOR_CALIBRATION_ON_P3":
            case "COLOR_CALIBRATION_ON_ADOBERGB":
                _state.ColorCalibrationOn = DisplayController.EnableSrgb();
                RegistrySetup.WriteDword(@"SOFTWARE\OEM\GamingCenter2\MySetting\DisplayFeatures", "ColorCalibration", 2);
                break;
            case "COLOR_CALIBRATION_OFF":
                _state.ColorCalibrationOn = !DisplayController.DisableSrgb();
                RegistrySetup.WriteDword(@"SOFTWARE\OEM\GamingCenter2\MySetting\DisplayFeatures", "ColorCalibration", 0);
                break;

            // —— 键盘/光条定时关灯 ——
            case "KEYBOARD_LIGHTBAR_TIMER_ON":
                if (root.TryGetProperty("Mins", out var mins))
                {
                    _state.KeyboardTimerDeadline = DateTime.UtcNow.AddMinutes(mins.GetDouble()).ToString("O");
                    Log.Info($"键盘定时关灯: {mins.GetRawText()} 分钟后");
                }
                break;
            case "KEYBOARD_LIGHTBAR_TIMER_OFF":
                _state.KeyboardTimerDeadline = null;
                break;

            default:
                Log.Info($"setting action {action} acknowledged");
                break;
        }
        _state.Save();
        await SendSettingStatusAsync();
        if (refreshGpuStatus) await PublishGpuDeviceStatusAsync();
    }

    /// <summary>MIFS 调用容错包装。</summary>
    private void SafeMifs(Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Error("MIFS set: " + ex.Message); }
    }

    private static string HelperPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "JiaoLongCCUBridge", "display_helper.py");

    private static string? _pythonExe;

    /// <summary>定位用户会话可用的 python.exe（搜索常见安装位置）。</summary>
    private string? FindPython()
    {
        if (_pythonExe != null) return _pythonExe;
        var candidates = new List<string>();
        try
        {
            foreach (var users in new[] { "C:\\Users" })
            {
                if (!Directory.Exists(users)) continue;
                foreach (var user in Directory.GetDirectories(users))
                {
                    var p = Path.Combine(user, "AppData", "Local", "Programs", "Python");
                    if (!Directory.Exists(p)) continue;
                    foreach (var v in Directory.GetDirectories(p, "Python3*"))
                        candidates.Add(Path.Combine(v, "python.exe"));
                }
            }
            foreach (var p in Directory.GetDirectories("C:\\", "Python3*"))
                candidates.Add(Path.Combine(p, "python.exe"));
            foreach (var p in Directory.GetDirectories("C:\\Program Files", "Python3*"))
                candidates.Add(Path.Combine(p, "python.exe"));
        }
        catch { }
        // 取版本号最高的
        candidates.Sort((a, b) => string.Compare(b, a, StringComparison.OrdinalIgnoreCase));
        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                _pythonExe = c;
                Log.Info($"python resolved: {c}");
                return c;
            }
        }
        Log.Error("未找到 python.exe");
        return null;
    }

    /// <summary>在用户会话中运行显示助手（Python），返回 (currentHz, list, applied)。</summary>
    private (int cur, List<int> list, int? applied)? RunDisplayHelper(int hz = 0)
    {
        string? python = FindPython();
        if (python == null) return null;
        var args = hz > 0 ? $"\"{HelperPath}\" {hz}" : $"\"{HelperPath}\"";
        string output = "";
        var rc = UserSessionRunner.RunInUserSession(python, args, out output);
        if (rc == null)
        {
            Log.Error("display helper 未执行（用户会话不可用）");
            return null;
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(output.Trim());
            var root = doc.RootElement;
            int cur = root.TryGetProperty("currentHz", out var c) ? c.GetInt32() : 0;
            var list = new List<int>();
            if (root.TryGetProperty("list", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var it in arr.EnumerateArray()) list.Add(it.GetInt32());
            int? applied = root.TryGetProperty("applied", out var ap) && ap.ValueKind == JsonValueKind.Number ? ap.GetInt32() : null;
            return (cur, list, applied);
        }
        catch (Exception ex)
        {
            Log.Error($"display helper 解析失败: {ex.Message} raw={output[..Math.Min(output.Length, 200)]}");
            return null;
        }
    }

    /// <summary>GPUDevice/Status：刷新率页数据源（经用户会话助手查询）。</summary>
    private async Task PublishGpuDeviceStatusAsync()
    {
        int current;
        var list = new List<int>();
        var result = RunDisplayHelper();
        if (result.HasValue)
        {
            current = result.Value.cur;
            list = result.Value.list;
        }
        else
        {
            // 回退：Session 0 内 GDI 不可用时给安全默认
            current = DisplayController.CurrentHz();
            list = DisplayController.SupportedHz();
        }
        if (current > 0 && !list.Contains(current)) list.Add(current);
        list.Sort((a, b) => b.CompareTo(a));
        Log.Info($"refresh rates: current={current} list=[{string.Join(",", list)}]");
        var payload = new Dictionary<string, object?>
        {
            ["currentSaveingMode"] = "0", // 源码拼写如此
            ["currentHZ"] = current > 0 ? current.ToString() : "60",
            ["DisconnectMonitor"] = false,
            ["DC_Once"] = false,
            ["DC_HZ"] = false,
            ["MODE_HZ"] = false,
            ["AutoEnable"] = true,
            ["Auto"] = true, // 强制 UI 每次重建刷新率列表
            ["currentHZList"] = list,
        };
        await PublishAsync("GPUDevice/Status", payload);
    }

    private async Task SendSettingStatusAsync()
    {
        bool fnLock = false, tpLock = false;
        byte gpuMode = 0;
        try { fnLock = _mifs.GetFnLock(); } catch { }
        try { tpLock = _mifs.GetTouchpadLocked(); } catch { }
        try { gpuMode = _mifs.GetGpuMode(); } catch { }

        var setting = new Dictionary<string, object?>
        {
            ["FnKey"] = fnLock ? "LOCK" : "UNLOCK",
            ["WinKey"] = _state.WinKeyState,
            ["TouchpadToggle"] = tpLock ? "OFF" : "ON",
            ["PowerMode"] = _state.OperatingMode.ToString(),
            ["OSD"] = _state.OsdState,
            ["NumPad"] = _state.NumPadState,
            ["FnWith1HotkeySwitch_Status"] = "OFF",
            ["AcRecoverySwitch_Support"] = "0",
            ["DiscreteGpuDirectConnectionSwitch_Support"] = "1",
            ["DiscreteGpuDirectConnectionSwitch_Status"] = gpuMode == 1
                ? "DGPU_DIRECT_CONNECT_TOGGLE_ON"
                : "DGPU_DIRECT_CONNECT_TOGGLE_OFF",
            ["IGpuOnlyConnectionSwitch_Support"] = "0",
            ["CloseTimer"] = RemainingKeyboardTimerMinutes()?.ToString() ?? "0",
            ["ColorCalibrationResultCode"] = "0",
            ["ColorCalibrationSwitch"] = _state.ColorCalibrationOn ? "ON" : "OFF",
            // —— 电池页（UI 按 Contains("OFF") 判开关状态）——
            ["UsbCharger"] = _state.UsbChargerState,
            ["HighPerformancePowerModeSwitch"] = _state.HighPerformancePowerMode ? "ON" : "OFF",
            ["DeepSleepSwitch"] = _state.DeepSleepState,
            ["DeepSleepTime"] = _state.DeepSleepSeconds.ToString(),
            ["AcRecoverySwitch_Status"] = _state.AcRecoveryState,
            ["BatteryLogo_Status"] = _state.BatteryLogoState,
            ["LocalDimmingSwitch"] = "LOCALDIMMING_OFF",
            ["LCDOverdriveSwitch"] = "LCDOverdrive_OFF",
        };
        await PublishAsync("Setting/Status", setting);

        var switches = new Dictionary<string, object>
        {
            ["WIFIEnable"] = DeviceToggles.QueryEnabled("wifi"),
            ["BTEnable"] = DeviceToggles.QueryEnabled("bt"),
            ["TochpadEnable"] = !tpLock, // 源码拼写如此
            ["WebCamEnable"] = DeviceToggles.QueryEnabled("cam"),
        };
        await PublishAsync("Settings/DeviceSwitchItemStatus", switches);
    }

    private double? RemainingKeyboardTimerMinutes()
    {
        if (string.IsNullOrEmpty(_state.KeyboardTimerDeadline)) return null;
        if (!DateTime.TryParse(_state.KeyboardTimerDeadline, null, System.Globalization.DateTimeStyles.RoundtripKind, out var deadline))
            return null;
        var remain = deadline - DateTime.UtcNow;
        return remain.TotalMinutes > 0 ? Math.Ceiling(remain.TotalMinutes) : 0;
    }

    // ============================ Fan ============================

    private async Task HandleFanControlAsync(string? action, JsonElement root)
    {
        switch (action)
        {
            case "GETSTATUS":
                await SendFanStatusAsync();
                break;

            case "OPERATING_OFFICE_MODE":
                await SwitchModeAsync(0, mifsMode: 2);
                break;
            case "OPERATING_GAMING_MODE":
                await SwitchModeAsync(1, mifsMode: 0);
                break;
            case "OPERATING_TURBO_MODE":
                await SwitchModeAsync(2, mifsMode: 1);
                break;
            case "OPERATING_CUSTOM_MODE":
                await SwitchCustomModeAsync(root);
                break;

            case "EC_PROBE":
            {
                Log.Info("EC probe: " + _ec.Probe());
                // 变体 B 若把占空比写进去了，风扇应有反应：读回并发布 FanInfo
                await PublishTelemetryAsync();
                break;
            }

            case "FAN_BOOST_ON":
                _state.FanBoost = true;
                // 固件 fn20/fn21 锁速对风扇无实际作用，走 EC 直写（旧程序验证过的路径）
                if (_ec.Available)
                {
                    _fan.StopLoop();
                    _ec.SetFanDuty(100, 100);
                }
                else
                {
                    _mifs.SetFanSpeedSwitch(true);
                    _mifs.SetFanSpeed(100);
                }
                _state.Save();
                await SendFanStatusAsync();
                break;
            case "FAN_BOOST_OFF":
                _state.FanBoost = false;
                if (_state.CustomLoopActive) _fan.EnsureLoop(); // 自定义模式下恢复曲线
                else if (_ec.Available) _ec.ReleaseFanManual();
                else _mifs.SetFanSpeedSwitch(false);
                _state.Save();
                await SendFanStatusAsync();
                break;

            case "GET_FAN_SPEED_CURVE_SETTING":
                await SendFanTableAsync();
                break;

            case "SET_FAN_SPEED_CURVE_SETTING":
                await HandleSetCurveAsync(root);
                break;

            case "SET_FAN_CONTROL_RESPECTIVE":
                if (root.TryGetProperty("FanControlRespective", out var resp))
                {
                    _state.FanControlRespective = resp.GetBoolean();
                    _state.Save();
                }
                break;

            case "SET_OPERATING_MODE_DETAIL":
                await HandleModeDetailAsync(root);
                break;

            case "RESTORE_OPERATING_MODE_DETAIL":
                _state.CpuAmdSPL = _state.CpuAmdSPPT = _state.CpuAmdFPPT = _state.CpuAmdTccTarget = null;
                _state.GpuOverClockingSwitch = false;
                _state.GpuCoreOffset = 0;
                _state.GpuMemOffset = 0;
                _state.CpuUndervoltSteps = 0;
                ApplyGpuOverclock();
                if (_ec.Available) _ec.SetCpuCoAll(0);
                try { _mifs.CloseCustomCpuPower(); } catch { }
                _state.Save();
                await SendFanStatusAsync();
                break;

            case "RESTORE_FAN_SPEED_CURVE_SETTING":
                _state.CpuCurve = FanCurve.DefaultCpu();
                _state.GpuCurve = FanCurve.DefaultGpu();
                _state.Save();
                await SendFanTableAsync();
                break;

            case "SET_BALANCE_SMART":
                _state.SmartBalance = root.TryGetProperty("SmartBalanceSwitch", out var sb) ? sb.GetInt32() : 0;
                break;

            case "OPERATING_GAME_WHITE_LIST":
                _state.GameWhitelist = root.TryGetProperty("GameWhitelistSwitch", out var gw) ? gw.GetInt32() : 0;
                break;

            case "SET_CUSTOM_PROFILE_OSD_STRING":
                _state.CustomOsdName = root.TryGetProperty("ProfileName", out var pn) ? pn.GetString() : null;
                break;

            case "SET_CPU_CORE_OFFSET_EXTREME":
            case "SET_CPU_CORE_OFFSET_SILENT":
            default:
                Log.Info($"fan action {action} acknowledged (no-op)");
                break;
        }
    }

    /// <summary>切换性能模式。ccxMode: 0=办公 1=游戏 2=狂暴 3=自定义。</summary>
    private async Task SwitchModeAsync(int ccxMode, byte mifsMode)
    {
        _state.OperatingMode = ccxMode;
        _state.CustomLoopActive = false;
        _state.FanBoost = false; // 模式切换恢复固件自动风扇
        _fan.StopLoop();
        _ec.ReleaseFanManual(); // 退出 EC 手动锁速，交还固件
        try
        {
            _mifs.SetFanSpeedSwitch(false);
        }
        catch { }
        try
        {
            _mifs.SetPerformanceMode(mifsMode);
        }
        catch (Exception ex)
        {
            Log.Error($"SetPerformanceMode({mifsMode}) failed: {ex.Message}");
        }
        _state.Save();
        await SendFanStatusAsync();
        await SendSettingStatusAsync();
    }

    private async Task SwitchCustomModeAsync(JsonElement root)
    {
        int profile = 0;
        if (root.TryGetProperty("ProfileIndex", out var pi))
        {
            if (pi.ValueKind == JsonValueKind.Number) profile = pi.GetInt32();
            else int.TryParse(pi.GetString(), out profile);
        }
        _state.OperatingMode = 3;
        _state.CustomLoopActive = true;
        _state.FanBoost = false; // 与其他模式一致：进入自定义模式先解除风扇加速锁，否则曲线被 100% 锁速掩盖
        try
        {
            // 自定义模式下恢复用户保存的功耗墙（如有）
            if (_state.CpuAmdSPL is int spl) _mifs.SetCpuPower(2, (byte)Math.Clamp(spl, 0, 200));
            if (_state.CpuAmdSPPT is int sppt) _mifs.SetCpuPower(3, (byte)Math.Clamp(sppt, 0, 200));
            if (_state.CpuAmdTccTarget is int tcc) _mifs.SetCpuPower(4, (byte)Math.Clamp(tcc, 40, 110));
        }
        catch (Exception ex)
        {
            Log.Error($"apply custom power failed: {ex.Message}");
        }
        _mifs.SetFanSpeedSwitch(true); // 手动锁速交给曲线环
        _fan.EnsureLoop();
        _state.Save();
        await SendFanStatusAsync();
        await SendFanTableAsync();
    }

    /// <summary>激活自定义模式（自定义页改曲线/功耗墙时，官方服务语义即切入自定义）。</summary>
    private void ActivateCustomMode()
    {
        _state.OperatingMode = 3;
        _state.CustomLoopActive = true;
        _state.FanBoost = false; // 曲线与 100% 锁速互斥
        try { _mifs.SetFanSpeedSwitch(true); } catch { } // 手动锁速交给曲线环
        _fan.EnsureLoop();
    }

    private async Task HandleSetCurveAsync(JsonElement root)
    {
        string? type = root.TryGetProperty("Type", out var t) ? t.GetString() : "CPU";
        string? name = root.TryGetProperty("Name", out var n) ? n.GetString() : "Profile1";
        var duty = new List<int>();
        for (int i = 0; i < 16; i++)
        {
            if (root.TryGetProperty($"T{i}", out var tv) &&
                int.TryParse(tv.ValueKind == JsonValueKind.String ? tv.GetString() : tv.GetRawText(), out int d))
                duty.Add(d);
            else duty.Add(50);
        }
        var curve = new FanCurve { Name = name ?? "Profile1", Duty = duty, Activated = true, Level = 16 };
        if (type == "GPU") _state.GpuCurve = curve;
        else _state.CpuCurve = curve;
        _state.Save();
        // 用户在自定义设置页应用了风扇曲线 → 切入自定义模式（官方服务同语义）
        ActivateCustomMode();
        await SendFanStatusAsync();
        await SendFanTableAsync();
    }

    private async Task HandleModeDetailAsync(JsonElement root)
    {
        // —— GPU 超频（KaronOC P0 偏移）——
        if (root.TryGetProperty("OverClockingSwitch", out var ocSw))
        {
            _state.GpuOverClockingSwitch = ParseInt(ocSw) != 0;
            ApplyGpuOverclock();
        }
        if (root.TryGetProperty("GpuCoreClockOffsetOC", out var coreOc))
        {
            _state.GpuCoreOffset = ParseInt(coreOc);
            if (_state.GpuOverClockingSwitch) ApplyGpuOverclock();
        }
        if (root.TryGetProperty("GpuMemoryClockOffsetOC", out var memOc))
        {
            _state.GpuMemOffset = ParseInt(memOc);
            if (_state.GpuOverClockingSwitch) ApplyGpuOverclock();
        }

        // —— AMD 功耗墙：SPL/SPPT/FPPT/温度墙 —— MIFS fn23 直接写固件
        if (root.TryGetProperty("CpuAmdSPL", out var spl))
        {
            int v = ParseInt(spl); _state.CpuAmdSPL = v;
            try { _mifs.SetCpuPower(2, (byte)Math.Clamp(v, 0, 200)); } catch { }
        }
        if (root.TryGetProperty("CpuAmdSPPT", out var sppt))
        {
            int v = ParseInt(sppt); _state.CpuAmdSPPT = v;
            try { _mifs.SetCpuPower(3, (byte)Math.Clamp(v, 0, 200)); } catch { }
        }
        if (root.TryGetProperty("CpuAmdFPPT", out var fppt))
        {
            int v = ParseInt(fppt); _state.CpuAmdFPPT = v;
            try { _mifs.SetCpuPower(3, (byte)Math.Clamp(v, 0, 200)); } catch { } // 固件无独立 FPPT，写入 SPPT
        }
        if (root.TryGetProperty("CpuAmdTccTarget", out var tcc))
        {
            int v = ParseInt(tcc); _state.CpuAmdTccTarget = v;
            try { _mifs.SetCpuPower(4, (byte)Math.Clamp(v, 40, 110)); } catch { }
        }
        if (root.TryGetProperty("CpuTccOffsetSwitch", out var tcsSw))
        {
            _state.CpuTccOffsetSwitch = tcsSw.ToString();
        }
        // —— CPU 全核电压偏移（SMU set-coall，仅降压）——
        if (root.TryGetProperty("CpuAmdCoreVoltageOC", out var volt))
        {
            int steps = ParseInt(volt); // UI 语义 0-50，越小越降压
            _state.CpuUndervoltSteps = Math.Clamp(steps, 0, 50);
            if (_ec.Available)
            {
                bool ok = _ec.SetCpuCoAll(_state.CpuUndervoltSteps);
                Log.Info($"SMU set-coall steps={_state.CpuUndervoltSteps}: {(ok ? "OK" : "FAILED")}");
            }
        }
        // CpuAmdCoreFreqOC：SMU oc-clk 旧程序未使用，语义不确定 → 仅回显
        // 自定义设置页的功耗墙/超频调整 → 切入自定义模式（官方服务同语义；
        // SET_OPERATING_MODE_DETAIL 仅从自定义设置页发出，已核对反编译源码）
        ActivateCustomMode();
        _state.Save();
        await SendFanStatusAsync();
    }

    private void ApplyGpuOverclock()
    {
        if (!KaronOc.Available) return;
        int core = (_state.GpuOverClockingSwitch && _state.GpuCoreOffsetEnabled) ? _state.GpuCoreOffset : 0;
        int mem = (_state.GpuOverClockingSwitch && _state.GpuMemOffsetEnabled) ? _state.GpuMemOffset : 0;
        bool ok = KaronOc.Set(core, mem);
        Log.Info($"GPU OC: core={core}MHz mem={mem}MHz -> {(ok ? "OK" : "FAILED")}");
    }

    private static int ParseInt(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number ? e.GetInt32() : (int.TryParse(e.GetString(), out var v) ? v : 0);

    private async Task SendFanStatusAsync()
    {
        string operatingMode;
        try
        {
            byte mifsMode = _mifs.GetPerformanceMode();
            operatingMode = _state.CustomLoopActive
                ? "3"
                : mifsMode switch { 0 => "1", 1 => "2", 2 => "0", _ => _state.OperatingMode.ToString() };
        }
        catch
        {
            operatingMode = _state.OperatingMode.ToString();
        }

        var payload = new Dictionary<string, object?>
        {
            ["IsAC"] = SafeIsAc(),
            ["TurboGPUOCEnabled"] = false,
            ["OverClockingSwitch"] = _state.GpuOverClockingSwitch ? "1" : "0",
            ["OperatingMode"] = operatingMode,
            ["GamingProfileIndex"] = "0",
            ["OfficeProfileIndex"] = "0",
            ["TurboProfileIndex"] = "0",
            ["CustomProfileIndex"] = "0",
            ["FanBoostEnable"] = _state.FanBoost ? "1" : "0",
            ["FAN_FanSwitchSpeedEnabled"] = "0",
            ["FAN_FanSwitchSpeed"] = "60",
            ["FAN_SafetyProtectNotify"] = "0",
            ["FAN_SafetyProtect"] = "0",
            ["CPU_AmdSPL"] = (_state.CpuAmdSPL ?? 85).ToString(),
            ["CPU_AmdSPPT"] = (_state.CpuAmdSPPT ?? 120).ToString(),
            ["CPU_AmdFPPT"] = (_state.CpuAmdFPPT ?? 120).ToString(),
            ["Cpu_AmdTccTarget"] = (_state.CpuAmdTccTarget ?? 95).ToString(),
            ["CPU_AmdOverClockSupport"] = "1",
            // 自定义设置页初始化要读的其余字段（缺失时 Convert.ToInt32(null)=0，
            // 滑块全部归 0）。PL1/PL2/PL4 为 Intel 语义字段，AMD 平台仅防 0 崩。
            ["CPU_PL1Maximum"] = "120",
            ["CPU_PL1Minimum"] = "35",
            ["CPU_PL2Maximum"] = "140",
            ["CPU_PL2Minimum"] = "35",
            ["CPU_PL4Maximum"] = "160",
            ["CPU_PL4Minimum"] = "35",
            ["CPU_TccOffsetMaximum"] = "95",
            ["CPU_TccOffsetSwitch"] = _state.CpuTccOffsetSwitch,
            ["CPU_AmdCoreFreqOC"] = "0",
            ["CPU_AmdCoreVoltageOC"] = _state.CpuUndervoltSteps.ToString(),
            ["GPU_CoreClockOffset"] = KaronOc.Get().core.ToString(),
            ["GPU_CoreClockOffsetOC"] = _state.GpuCoreOffset.ToString(),
            ["GPU_CoreClockOffsetMaximum"] = "250",
            ["GPU_CoreClockOffsetMinimum"] = "0",
            ["GPU_MemoryClockOffset"] = KaronOc.Get().mem.ToString(),
            ["GPU_MemoryClockOffsetOC"] = _state.GpuMemOffset.ToString(),
            ["GPU_MemoryClockOffsetMaximum"] = "1000",
            ["GPU_MemoryClockOffsetMinimum"] = "0",
            ["GPU_ConfigurableTGPSwitch"] = "0",
            ["GPU_DynamicBoostSwitch"] = "0",
            ["TjMax"] = "95",
            // 官方协议语义（实测校准）：Fan/Status 的 PowerMode "1"=电源适配器，
            // "0"=使用电池——自定义设置页在 "0"（电池）时显示
            // “在电池模式下，自定义设置已被禁用”。注意与直觉相反：接适配器要发 "1"。
            ["PowerMode"] = SafeIsAc() ? "1" : "0",
            ["CPU_PerformanceAndOverClockMenuSwitch"] = "1",
            ["CPU_PL4_Double_Flag"] = "0",
            ["BSOD_DefaultNotify"] = "0",
            ["GameWhitelistSwitch"] = 0,
            ["SmartBalance"] = 0,
            ["Extreme"] = 0,
            ["Silent"] = 0,
            ["GPU_WhisperModeSupport"] = false,
        };
        await PublishAsync("Fan/Status", payload);
    }

    private bool SafeIsAc()
    {
        try { return _mifs.IsOnAc(); }
        catch { return WinPower.IsOnAc(); }
    }

    private async Task SendFanTableAsync()
    {
        var cpu = _state.CpuCurve;
        var gpu = _state.GpuCurve;
        var payload = new Dictionary<string, object?>
        {
            ["Activated"] = _state.OperatingMode == 3,
            ["Name"] = "Profile1",
            ["FanControlRespective"] = _state.FanControlRespective,
            ["CpuTemp_DefaultMaxLevel"] = Math.Max(cpu.Duty.Count, 2),
            ["GpuTemp_DefaultMaxLevel"] = Math.Max(gpu.Duty.Count, 2),
            ["CPU"] = cpu.Duty.Select((d, i) => new { ID = i, UpT = (byte)0, DownT = (byte)0, Duty = (byte)d }).ToList(),
            ["GPU"] = gpu.Duty.Select((d, i) => new { ID = i, UpT = (byte)0, DownT = (byte)0, Duty = (byte)d }).ToList(),
        };
        await PublishAsync("Fan/Table", payload);
    }

    // ============================ Battery ============================

    private async Task HandleBatteryControlAsync(string? action, JsonElement root)
    {
        if (root.TryGetProperty("Report", out var rep) && rep.GetString() == "GET")
        {
            await SendBatteryStatusAsync();
            return;
        }
        // 模式切换：本机固件未暴露充电阈值接口（fn0-7/24-40 全空），选择持久化并回显（状态记忆）
        _state.BatteryHealthMode = action switch
        {
            "PERFORMANCEDMODE" => 0,
            "BALANCEDMODE" => 1,
            "HEALTHYMODE" => 2,
            _ => _state.BatteryHealthMode,
        };
        _state.Save();
        Log.Info($"battery protection mode -> {_state.BatteryHealthMode} (状态记忆，本机固件无充电阈值接口)");
        await SendBatteryStatusAsync();
    }

    private async Task SendBatteryStatusAsync()
    {
        // 内核电源状态：本机 Win32_Battery 不上报容量（空值），GetSystemPowerStatus 可靠
        int percent = WinPower.BatteryPercent();
        bool onAc = WinPower.IsOnAc();
        long remain = WinPower.BatteryLifeSeconds();
        var payload = new Dictionary<string, object?>
        {
            ["HealthProtectionStatus"] = _state.BatteryHealthMode,
            ["BatteryPercent"] = percent >= 0 ? percent : 100,
            ["BatteryTime"] = remain,
            ["BatteryFullTime"] = -1,
            // "1" = 接通电源（充电中/已充满），"0" = 使用电池
            ["BatteryPowerStatus"] = onAc ? "1" : "0",
            ["TypeCAdaptorPrioritySupport"] = false,
            ["TypeCAdaptorPrioritySwitch"] = "0",
            ["BatteryAbnormal"] = "0",
        };
        await PublishAsync("System/BatteryProtection", payload);
    }

    // ============================ 遥测 ============================

    private PerformanceCounter? _cpuUsage;
    private PerformanceCounter? _cpuPerf;

    public async Task PublishTelemetryAsync()
    {
        try
        {
            // CPU 卡片
            string cpuTemp = "-", cpuUsage = "-", cpuFreq = "-", cpuMaxFreq = "-";
            try { cpuTemp = _mifs.GetCpuTemperature().ToString(); } catch { }
            try
            {
                _cpuUsage ??= new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                _cpuUsage.NextValue();
                await Task.Delay(120);
                cpuUsage = ((int)Math.Round(_cpuUsage.NextValue())).ToString();
            }
            catch { }
            try
            {
                _cpuPerf ??= new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                _cpuPerf.NextValue();
                double pct = _cpuPerf.NextValue();
                if (pct > 0 && _baseClockMhz > 0)
                {
                    double mhz = _baseClockMhz * pct / 100.0;
                    if (mhz > _maxSeenCpuMhz) _maxSeenCpuMhz = mhz;
                    cpuFreq = ((int)Math.Round(mhz)).ToString();
                    // UI 把该值既当"基准频率"原样显示，又 ×4 当作最大频率仪表盘，因此发送基准时钟
                    cpuMaxFreq = _baseClockMhz.ToString();
                }
            }
            catch { }
            await PublishAsync("System/CpuInfo", new Dictionary<string, object?>
            {
                ["CpuTemperature"] = cpuTemp,
                ["CpuUsage"] = cpuUsage,
                ["CpuMaxFrequency"] = cpuMaxFreq,
                ["CpuFrequency"] = cpuFreq,
            });

            // GPU 卡片
            string gpuMem = "-", gpuTemp = "-", gpuUsage = "-", gpuMemFreq = "-", gpuCoreFreq = "-";
            if (_nvml?.Available == true)
            {
                var (t, u, mem, core, memClock) = _nvml.Sample();
                if (mem > 0) gpuMem = mem.ToString();
                if (t > 0) gpuTemp = t.ToString();
                if (u > 0 || core > 0) gpuUsage = u.ToString();
                if (memClock > 0) gpuMemFreq = ((int)(memClock / 4)).ToString(); // UI ×4 还原
                if (core > 0) gpuCoreFreq = core.ToString();
            }
            await PublishAsync("System/GpuInfo", new Dictionary<string, object?>
            {
                ["GpuMem"] = gpuMem,
                ["GpuTemperature"] = gpuTemp,
                ["GpuUsage"] = gpuUsage,
                ["GpuMemFreq"] = gpuMemFreq,
                ["GpuCoreFreq"] = gpuCoreFreq,
            });

            // 风扇卡片
            string cpuRpm = "-", gpuRpm = "-", cpuDuty = "-", gpuDuty = "-";
            try
            {
                var (cr, gr) = _mifs.GetFanSpeeds();
                if (cr > 0) cpuRpm = cr.ToString();
                if (gr > 0) gpuRpm = gr.ToString();
            }
            catch { }
            if (_state.FanBoost)
            {
                cpuDuty = "100"; gpuDuty = "100";
            }
            else if (_state.CustomLoopActive && _fan.LastCpuDuty > 0)
            {
                cpuDuty = _fan.LastCpuDuty.ToString();
                gpuDuty = _fan.LastGpuDuty.ToString();
            }
            else if (_ec.Available)
            {
                try
                {
                    byte cpu = _ec.ReadEc(EcAccess.EcRegFanCpu);
                    byte gpu = _ec.ReadEc(EcAccess.EcRegFanGpu);
                    if (cpu > 0) cpuDuty = cpu.ToString();
                    if (gpu > 0) gpuDuty = gpu.ToString();
                }
                catch { }
            }
            await PublishAsync("System/FanInfo", new Dictionary<string, object?>
            {
                ["CpuFanDuty"] = cpuDuty,
                ["GpuFanDuty"] = gpuDuty,
                ["CpuFanRpm"] = cpuRpm,
                ["GpuFanRpm"] = gpuRpm,
            });

            await PublishAsync("System/FanErrorInfo", new Dictionary<string, object> { ["FanErrorStatus"] = "0" });

            // 硬件信息（静态）
            await PublishAsync("System/HardwareInfo", new Dictionary<string, object?>
            {
                ["SystemProductInfo"] = _productInfo,
                ["ProcessorInfo"] = $"{_cpuName},",
                ["GraphicInfo"] = $",{_gpuCardName},",
                ["MemoryUsableInfo"] = _memoryInfo,
            });

            // 磁盘温度（v1 仅型号）
            await PublishAsync("System/DiskInfo", ReadDiskInfo());

            // 电池
            await SendBatteryStatusAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"telemetry: {ex}");
        }
    }

    private object ReadDiskInfo()
    {
        string model1 = "null", temp1 = "0";
        try
        {
            using var s = new ManagementObjectSearcher("root\\CIMV2", "SELECT Model, Index FROM Win32_DiskDrive WHERE MediaType='Fixed hard disk media'");
            int i = 0;
            foreach (var d in s.Get())
            {
                if (i == 0)
                {
                    model1 = (d["Model"]?.ToString() ?? "null");
                    if (string.IsNullOrWhiteSpace(model1)) model1 = "null";
                }
                i++;
                if (i > 1) break;
            }
        }
        catch { }
        return new Dictionary<string, object?>
        {
            ["SSD_One_Model"] = model1,
            ["SSD_Two_Model"] = "null",
            ["HDDModel"] = "null",
            ["SSD_One_Temperature"] = temp1,
            ["SSD_Two_Temperature"] = "0",
            ["HDDTemperature"] = "0",
        };
    }

    // ============================ 静态信息采集 ============================

    private static (string name, int baseMhz) ReadCpu()
    {
        try
        {
            using var c = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, MaxClockSpeed FROM Win32_Processor").Get()
                .Cast<ManagementObject>().First();
            return (c["Name"]?.ToString() ?? "AMD CPU", (int)(uint)(c["MaxClockSpeed"] ?? 3600u));
        }
        catch
        {
            return ("AMD CPU", 3600);
        }
    }

    private static string ReadGpuName()
    {
        try
        {
            using var s = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name FROM Win32_VideoController");
            foreach (var v in s.Get())
            {
                var name = v["Name"]?.ToString() ?? "";
                if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                    return name;
            }
        }
        catch { }
        return "";
    }

    private static string ReadProductInfo()
    {
        try
        {
            using var c = new ManagementObjectSearcher("root\\CIMV2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem").Get()
                .Cast<ManagementObject>().First();
            var model = c["Model"]?.ToString() ?? "MECHREVO";
            var maker = c["Manufacturer"]?.ToString() ?? "MECHREVO";
            return $"蛟龙16 Pro,{model},{maker}";
        }
        catch
        {
            return "蛟龙16 Pro,MECHREVO";
        }
    }

    private static string ReadMemory()
    {
        try
        {
            ulong totalKb = 0;
            string type = "";
            uint maxSpeed = 0;
            using var s = new ManagementObjectSearcher("root\\CIMV2", "SELECT Capacity, SMBIOSMemoryType, Speed FROM Win32_PhysicalMemory");
            foreach (var m in s.Get())
            {
                totalKb += (ulong)(m["Capacity"] as ulong? ?? 0);
                ushort smType = m["SMBIOSMemoryType"] is ushort t ? t : (ushort)0;
                if (smType == 34) type = "DDR5";
                else if (smType == 26) type = "DDR4";
                if (m["Speed"] is uint sp && sp > maxSpeed) maxSpeed = sp;
            }
            if (string.IsNullOrEmpty(type)) type = maxSpeed >= 4800 ? "DDR5" : maxSpeed >= 1600 ? "DDR4" : "DDR";
            double gb = totalKb / 1024.0 / 1024.0 / 1024.0;
            return $"{(gb >= 1 ? $"{gb:0}GB" : $"{gb * 1024:0}MB")} ({type})";
        }
        catch
        {
            return "";
        }
    }
}
