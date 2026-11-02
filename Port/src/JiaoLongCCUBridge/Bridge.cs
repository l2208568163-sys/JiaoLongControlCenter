using System.Diagnostics;
using System.Linq;
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
        // 桥接自己注入 broker 的状态主题会被当成"客户端发布"回灌进来。
        // 直接丢弃：既不该处理，也不该刷日志（否则每次状态广播都多两条噪音）。
        if (topic is "Setting/Status" or "Settings/DeviceSwitchItemStatus" or "Setting/Error"
                  or "Fan/Status" or "Keyboard/Status" or "Display/Status")
            return;

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
                    if (root.TryGetProperty("light", out var lvl) && byte.TryParse(
                            lvl.ValueKind == JsonValueKind.Number ? lvl.GetInt32().ToString() : lvl.GetString(), out byte bright))
                    {
                        _state.KeyboardBrightness = Math.Clamp(bright, (byte)0, (byte)3);
                        _state.KeyboardOn = _state.KeyboardBrightness > 0;
                        ApplyKeyboardState(); // 亮度 0 = 关灯；调亮度时保持当前效果继续跑
                    }
                    break;

                case "SetEffectALL":
                {
                    string? effect = root.TryGetProperty("effect", out var e) ? e.GetString() : null;
                    string? mode = root.TryGetProperty("mode", out var m) ? m.GetString() : null;
                    string? direction = root.TryGetProperty("direction", out var d) ? d.GetString() : null;
                    string? speedStr = root.TryGetProperty("speed", out var spd)
                        ? (spd.ValueKind == JsonValueKind.Number ? spd.GetInt32().ToString() : spd.GetString())
                        : null;
                    byte? light = null;
                    if (root.TryGetProperty("light", out var l2))
                    {
                        var raw = l2.ValueKind == JsonValueKind.Number ? l2.GetInt32().ToString() : l2.GetString();
                        if (byte.TryParse(raw, out byte lb)) light = Math.Clamp(lb, (byte)0, (byte)3);
                    }
                    if (light.HasValue) _state.KeyboardBrightness = light.Value;

                    // 调色板：ColorBuffer 可能含多个颜色（Mix 用），全部解析
                    (byte r, byte g, byte b)[]? palette = null;
                    if (root.TryGetProperty("color", out var colorObj) &&
                        colorObj.TryGetProperty("ColorBuffer", out var buf) && buf.ValueKind == JsonValueKind.Array)
                    {
                        var list = new List<(byte, byte, byte)>();
                        foreach (var item in buf.EnumerateArray())
                        {
                            if (item.TryGetProperty("R", out var rEl) && item.TryGetProperty("G", out var gEl) && item.TryGetProperty("B", out var bEl))
                                list.Add(((byte)Clamp255(rEl), (byte)Clamp255(gEl), (byte)Clamp255(bEl)));
                        }
                        if (list.Count > 0) palette = list.ToArray();
                    }

                    ApplyKeyboardEffect(effect, mode, speedStr, palette is { Length: > 0 } ? palette[0] : null, direction, palette);
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

    private void ApplyKeyboardEffect(string? effect, string? mode, string? speed, (byte r, byte g, byte b)? color,
                                     string? direction = null, (byte r, byte g, byte b)[]? palette = null)
    {
        _state.KeyboardOn = true;
        string eff = string.IsNullOrWhiteSpace(effect) ? "Single" : effect!;
        if (color.HasValue)
        {
            _state.KeyboardColorHex = $"{color.Value.r:X2}{color.Value.g:X2}{color.Value.b:X2}";
        }
        int speedLevel = int.TryParse(speed, out int sp) ? Math.Clamp(sp, 0, 4) : _state.KeyboardSpeed;
        _state.KeyboardEffect = eff;
        _state.KeyboardSpeed = speedLevel;
        _state.KeyboardDirection = direction ?? "None";

        var prim = HexToRgb(_state.KeyboardColorHex);
        var pal = (palette is { Length: > 0 }) ? palette : new[] { prim };
        if (pal[0] != prim)
        {
            // 调色板首位即当前主色，保持两者一致
            prim = pal[0];
            _state.KeyboardColorHex = $"{prim.r:X2}{prim.g:X2}{prim.b:X2}";
        }
        _state.KeyboardPalette = string.Join(",", pal.Select(c => $"{c.r:X2}{c.g:X2}{c.b:X2}"));

        // 亮度先落地（0 会被当成关灯，这里至少给 1）
        SafeMifs(() => _mifs.SetKeyboardBrightness(Math.Max((byte)1, _state.KeyboardBrightness)));

        var p = new KeyboardEffects.Params(eff, prim, pal, speedLevel, _state.KeyboardBrightness, _state.KeyboardDirection);
        Log.Info($"keyboard effect={eff} speed={speedLevel} light={_state.KeyboardBrightness} dir={_state.KeyboardDirection} pal={_state.KeyboardPalette}");
        _kbdFx.Apply(p);
    }

    private void ApplyKeyboardState()
    {
        SafeMifs(() =>
        {
            if (!_state.KeyboardOn)
            {
                _kbdFx.StopAll();
                // 固件忽略 fn16=0；亮度 0 才是有效关灯
                _mifs.SetKeyboardBrightness(0);
                return;
            }
            _mifs.SetKeyboardBrightness(Math.Max((byte)1, _state.KeyboardBrightness));
            _kbdFx.InvalidateMode();
            var pal = (_state.KeyboardPalette ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(HexToRgb)
                .ToArray();
            if (pal.Length == 0) pal = new[] { HexToRgb(_state.KeyboardColorHex) };
            _kbdFx.Apply(new KeyboardEffects.Params(
                string.IsNullOrWhiteSpace(_state.KeyboardEffect) ? "Single" : _state.KeyboardEffect,
                HexToRgb(_state.KeyboardColorHex), pal,
                _state.KeyboardSpeed, _state.KeyboardBrightness, _state.KeyboardDirection));
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

            // 颜色/亮度回读只用于"关灯时不覆盖用户设置"的同步：
            // 动态效果运行中颜色每帧在变，回读会污染调色板/主色，故仅在静态时同步颜色；
            // 亮度为 0（关灯态）时不覆盖用户设定值。
            if (_kbdFx.IsStatic)
            {
                var (r, g, b) = _mifs.GetKeyboardColor();
                _state.KeyboardColorHex = $"{r:X2}{g:X2}{b:X2}";
            }
            byte fwBright = _mifs.GetKeyboardBrightness();
            if (_state.KeyboardOn && fwBright > 0) _state.KeyboardBrightness = fwBright;
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
        if (string.IsNullOrWhiteSpace(action))
        {
            var bad = ActionResult.Fail("BAD_REQUEST", "命令缺少 Action 字段");
            await PublishSettingErrorAsync("(missing)", bad);
            await SendSettingStatusAsync();
            return;
        }

        bool refreshGpuStatus = action is "GETSTATUS" or "GPU_HZSETTING";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ActionResult result;
        try
        {
            // 放到线程池执行：设备禁用走 cfgMgr32，遇到驱动 veto 会同步阻塞 5 秒，
            // 直接在 MQTT 回调线程里跑会把后续消息（遥测/风扇）一起堵住。
            result = await Task.Run(() => ExecuteSettingAction(action, root));
        }
        catch (Exception ex)
        {
            result = ActionResult.Fail("EXCEPTION", ex.Message);
            Log.Error($"SET {action} 异常: {ex}");
        }

        // 只有真正成功才落盘：失败时保持原状态，随后的 SendSettingStatusAsync 会把
        // 真实状态回发给 UI，开关自动弹回——这就是"操作没生效"的可视反馈。
        if (result.Ok) _state.Save();
        else await PublishSettingErrorAsync(action, result);

        Log.Info($"SET {action} -> {result} ({sw.ElapsedMilliseconds}ms)");

        await SendSettingStatusAsync();
        if (refreshGpuStatus) await PublishGpuDeviceStatusAsync();
    }

    /// <summary>
    /// 错误主题：官方 UI 不订阅（忽略即无害），供日志与第三方工具定位失败原因。
    /// </summary>
    private Task PublishSettingErrorAsync(string action, ActionResult r) =>
        PublishAsync("Setting/Error", new Dictionary<string, object?>
        {
            ["Action"] = action,
            ["Code"] = r.Code,
            ["Message"] = r.Message,
            ["Timestamp"] = DateTime.Now.ToString("O"),
        });

    /// <summary>同步执行单个设置动作。失败返回原因，调用方据此决定是否持久化。</summary>
    private ActionResult ExecuteSettingAction(string action, JsonElement root)
    {
        switch (action)
        {
            case "GETSTATUS":
                return ActionResult.Success("状态查询");

            // ===== 设备类开关（SetupAPI 真实启用/禁用）=====
            case "WIFI_OFF":   return SetDeviceEnabled("wifi", false, "Wi-Fi");
            case "WIFI_ON":    return SetDeviceEnabled("wifi", true,  "Wi-Fi");
            case "BT_OFF":     return SetDeviceEnabled("bt",   false, "蓝牙");
            case "BT_ON":      return SetDeviceEnabled("bt",   true,  "蓝牙");
            case "WEBCAM_OFF": return SetDeviceEnabled("cam",  false, "摄像头");
            case "WEBCAM_ON":  return SetDeviceEnabled("cam",  true,  "摄像头");

            case "SCANFORHARDWARECHANGE":
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pnputil", "/scan-devices")
                    { CreateNoWindow = true, UseShellExecute = false });
                    return ActionResult.Success("已触发硬件重新扫描");
                }
                catch (Exception ex)
                {
                    return ActionResult.Fail("SCAN_FAILED", "硬件扫描失败：" + ex.Message);
                }

            // ===== Win 键（用户会话低级键盘钩子，见 WinkeyHook.cs）=====
            case "WINKEY_LOCK":   return ApplyWinkeyLock(true);
            case "WINKEY_UNLOCK": return ApplyWinkeyLock(false);

            // ===== OSD（写官方约定的 OSDHidden）=====
            // 语义注意：OSD_HIDDEN_ON = 隐藏 OSD = 注册表写 1
            case "OSD_HIDDEN_ON":  return ApplyOsdHidden(true);
            case "OSD_HIDDEN_OFF": return ApplyOsdHidden(false);

            // ===== 触摸板（固件 fn12 + Windows 精密触摸板状态双写）=====
            case "TOUCHPAD_ON":
            case "TOUCHPAD_TOGGLE_ON":  return ApplyTouchpadEnabled(true);
            case "TOUCHPAD_OFF":
            case "TOUCHPAD_TOGGLE_OFF": return ApplyTouchpadEnabled(false);
            case "TOUCHPAD_LED_ON":
            case "TOUCHPAD_LED_OFF":
                return ActionResult.Success("指示灯由固件跟随状态自动变化，无需单独处理");

            // ===== Fn 键（MIFS fn11，写入后回读校验）=====
            case "FNKEY_LOCK":   return ApplyFnLock(true);
            case "FNKEY_UNLOCK": return ApplyFnLock(false);

            // ===== 能力位不满足的项：明确拒绝，避免"点了没反应" =====
            case "NUMPAD_LOCK":
            case "NUMPAD_UNLOCK":
                if (!SystemCaps.NumPadSupported)
                    return ActionResult.Fail("NOT_SUPPORTED", "本机型不支持数字键盘开关（NumPadSupport=0）");
                return ActionResult.Fail("NOT_IMPLEMENTED", "固件未提供数字键盘控制通道，未做状态伪装");

            case "Uni_ON":
            case "Uni_OFF":
            case "Omni_ON":
            case "Omni_OFF":
                if (!SystemCaps.MicNoiseReductionSupported)
                    return ActionResult.Fail("NOT_SUPPORTED", "本机型不支持麦克风降噪（MicNoiseReductionSupport=0）");
                return ActionResult.Fail("NOT_IMPLEMENTED", "麦克风降噪依赖 Realtek 音频组件，桥接未实现");

            case "LOCALDIMMING_ON":
            case "LOCALDIMMING_OFF":
                if (!SystemCaps.LocalDimmingSupported)
                    return ActionResult.Fail("NOT_SUPPORTED", "面板不支持局部调光（LocalDimmingSupport=0）");
                return ActionResult.Fail("NOT_IMPLEMENTED", "局部调光未实现");

            // ===== 高性能电源模式（计划 + overlay 一起切，失败不记账）=====
            case "HIGHPERFORMANCEPOWERMODE_ON":
            {
                if (!WinPower.SetActivePlan(WinPower.PlanHigh))
                    return ActionResult.Fail("POWER_PLAN_FAILED", "切换到高性能电源计划失败");
                // 只切计划不切 overlay 时，Win11 设置页的「电源模式」仍显示「平衡」，两处状态打架
                bool overlay = WinPower.SetOverlayBestPerformance();
                _state.HighPerformancePowerMode = true;
                return ActionResult.Success(overlay
                    ? "已切换到高性能电源计划 + 最佳性能模式"
                    : "已切换到高性能电源计划（本系统不支持电源模式覆盖层）");
            }
            case "HIGHPERFORMANCEPOWERMODE_OFF":
            {
                if (!WinPower.SetActivePlan(WinPower.PlanBalanced))
                    return ActionResult.Fail("POWER_PLAN_FAILED", "切换到平衡电源计划失败");
                WinPower.SetOverlayBalanced();
                _state.HighPerformancePowerMode = false;
                return ActionResult.Success("已切换到平衡电源计划 + 平衡模式");
            }

            // ===== 电池页其余开关（固件/BIOS 级，本机无接口，仅状态记忆）=====
            case "USB_CHARGER_ON":        _state.UsbChargerState = "ON";    return ActionResult.Success("仅状态记忆（固件无 USB 充电控制接口）");
            case "USB_CHARGER_OFF":       _state.UsbChargerState = "OFF";   return ActionResult.Success("仅状态记忆（固件无 USB 充电控制接口）");
            case "ACRECOVERY_TOGGLE_ON":  _state.AcRecoveryState = "ON";    return ActionResult.Success("仅状态记忆（BIOS 级功能）");
            case "ACRECOVERY_TOGGLE_OFF": _state.AcRecoveryState = "OFF";   return ActionResult.Success("仅状态记忆（BIOS 级功能）");
            case "BATTERYLOGO_TOGGLE_ON": _state.BatteryLogoState = "ON";   return ActionResult.Success("仅状态记忆（BIOS 级功能）");
            case "BATTERYLOGO_TOGGLE_OFF":_state.BatteryLogoState = "OFF";  return ActionResult.Success("仅状态记忆（BIOS 级功能）");

            case "DEEPSLEEP_ON":
            {
                int seconds = _state.DeepSleepSeconds;
                if (root.TryGetProperty("Secs", out var secs) && secs.ValueKind == JsonValueKind.Number)
                {
                    seconds = secs.GetInt32();
                    if (seconds < 0 || seconds > 86400)
                        return ActionResult.Fail("BAD_PARAM", "深度睡眠时长需在 0–86400 秒之间");
                }
                if (seconds <= 0)
                {
                    // UI 的 DeepSleepSwitch setter 在服务端回发状态变化时会回写一次，
                    // 此时下拉可能还没同步出有效值（Secs=0）。不要失败（失败回环会让开关反复弹），
                    // 依次回退到上次保存值 / 首档 900 秒。
                    seconds = _state.DeepSleepSeconds > 0 ? _state.DeepSleepSeconds : 900;
                }

                // 本机无 S3，官方 BIOS 级「深度睡眠」不可复刻；用「空闲 N 秒后休眠」等价替代。
                // 首次开启时记录用户原值，关闭时原样恢复（本机出厂为 0 = 从不休眠）。
                if (_state.DeepSleepPrevHibernateIdle is null)
                {
                    int prev = WinPower.GetHibernateIdleSeconds();
                    _state.DeepSleepPrevHibernateIdle = prev >= 0 ? prev : 0;
                }
                if (!WinPower.SetHibernateIdleSeconds(seconds))
                    return ActionResult.Fail("POWER_CFG_FAILED", "写入休眠空闲超时失败（电源计划不可写）");

                _state.DeepSleepSeconds = seconds;
                _state.DeepSleepState = "ON";
                return ActionResult.Success($"空闲 {seconds} 秒后进入休眠（本机无 S3，以此替代 BIOS 深度睡眠）");
            }
            case "DEEPSLEEP_OFF":
            {
                int restore = _state.DeepSleepPrevHibernateIdle ?? 0;
                if (!WinPower.SetHibernateIdleSeconds(restore))
                    return ActionResult.Fail("POWER_CFG_FAILED", "恢复休眠空闲超时失败");
                _state.DeepSleepState = "OFF";
                return ActionResult.Success(restore == 0 ? "已关闭（恢复为从不休眠）" : $"已恢复为 {restore} 秒后休眠");
            }

            // ===== 独显直连（fn9，重启生效）=====
            case "DGPU_DIRECT_CONNECT_TOGGLE_ON":
            {
                if (!TryMifs(() => _mifs.SetGpuMode(1)))
                    return ActionResult.Fail("MIFS_FAILED", "写入显卡模式失败（MIFS 不可用）");
                _state.DgpuDirectOn = true;
                return ActionResult.Success("已切换到独显直连（重启生效）");
            }
            case "DGPU_DIRECT_CONNECT_TOGGLE_OFF":
            {
                if (!TryMifs(() => _mifs.SetGpuMode(0)))
                    return ActionResult.Fail("MIFS_FAILED", "写入显卡模式失败（MIFS 不可用）");
                _state.DgpuDirectOn = false;
                return ActionResult.Success("已切换到混合模式（重启生效）");
            }
            case "DGPU_DIRECT_CONNECT_RESTART":
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown",
                        "/r /t 5 /c \"机械革命控制中心：显卡模式已切换，即将重启\"") { CreateNoWindow = true, UseShellExecute = false });
                    return ActionResult.Success("5 秒后重启");
                }
                catch (Exception ex)
                {
                    return ActionResult.Fail("RESTART_FAILED", ex.Message);
                }

            // ===== 刷新率（经用户会话助手执行）=====
            case "GPU_HZSETTING":
            {
                string hzText = root.TryGetProperty("Hz", out var hzEl) ? hzEl.ToString() : "";
                if (!int.TryParse(hzText, out int hz) || hz <= 0)
                    return ActionResult.Fail("BAD_PARAM", $"Hz 参数无效：{hzText}");

                var rr = RunDisplayHelper(hz);
                if (rr == null)
                    return ActionResult.Fail("DISPLAY_HELPER_FAILED", "显示助手执行失败（用户会话不可用或缺少 Python）");

                return rr.Value.applied == hz
                    ? ActionResult.Success($"已切换到 {hz}Hz")
                    : ActionResult.Fail("HZ_NOT_APPLIED", $"目标 {hz}Hz 未生效（当前 {rr.Value.cur}Hz）");
            }
            case "GPU_DC_HZ": // 自动刷新率：仅记录，不回发状态（避免与 UI 乒乓）
                Log.Info($"GPU_DC_HZ Enable={(root.TryGetProperty("Enable", out var en) && en.ValueKind == JsonValueKind.True)}");
                return ActionResult.Success("仅记录");

            // ===== 校色（sRGB ICC 关联）=====
            case "COLOR_CALIBRATION_ON":
            case "COLOR_CALIBRATION_ON_DEFAULT":
            case "COLOR_CALIBRATION_ON_SRGB":
            case "COLOR_CALIBRATION_ON_P3":
            case "COLOR_CALIBRATION_ON_ADOBERGB":
            {
                if (!DisplayController.EnableSrgb())
                    return ActionResult.Fail("ICC_FAILED", "关联 sRGB ICC 配置文件失败");
                _state.ColorCalibrationOn = true;
                RegistrySetup.WriteDword(@"SOFTWARE\OEM\GamingCenter2\MySetting\DisplayFeatures", "ColorCalibration", 2);
                return ActionResult.Success("已启用校色");
            }
            case "COLOR_CALIBRATION_OFF":
            {
                if (!DisplayController.DisableSrgb())
                    return ActionResult.Fail("ICC_FAILED", "取消 sRGB ICC 关联失败");
                _state.ColorCalibrationOn = false;
                RegistrySetup.WriteDword(@"SOFTWARE\OEM\GamingCenter2\MySetting\DisplayFeatures", "ColorCalibration", 0);
                return ActionResult.Success("已关闭校色");
            }

            // ===== 键盘/光条定时关灯 =====
            case "KEYBOARD_LIGHTBAR_TIMER_ON":
            {
                if (!root.TryGetProperty("Mins", out var mins) || mins.ValueKind != JsonValueKind.Number)
                    return ActionResult.Fail("BAD_PARAM", "Mins 参数无效");
                double m = mins.GetDouble();
                if (m <= 0 || m > 1440)
                    return ActionResult.Fail("BAD_PARAM", "定时时长需在 1–1440 分钟之间");
                _state.KeyboardTimerDeadline = DateTime.UtcNow.AddMinutes(m).ToString("O");
                return ActionResult.Success($"{m} 分钟后关灯");
            }
            case "KEYBOARD_LIGHTBAR_TIMER_OFF":
                _state.KeyboardTimerDeadline = null;
                return ActionResult.Success("已取消定时关灯");

            default:
                return ActionResult.Fail("UNKNOWN_ACTION", "未实现的设置动作");
        }
    }

    // -------------------- 设置动作的具体落地 --------------------

    // 防回声：官方 UI 的开关绑定在收到 Setting/Status 后会反向触发一次命令
    // （例如收到 OSD=OFF 就把开关 Unchecked，进而发 OSD_HIDDEN_ON），
    // 服务端再回发状态又触发 UI……实测会形成 ~60ms 的乒乓循环。
    // 这里记录"服务端最后一次真实设置的结果"，短窗口内的反向指令直接判为回声。
    private readonly Dictionary<string, (string state, long tick)> _lastApplied = new();
    private readonly object _debounceGate = new();
    private const int EchoWindowMs = 1000;

    private bool IsEcho(string key, string targetState)
    {
        lock (_debounceGate)
        {
            if (!_lastApplied.TryGetValue(key, out var last)) return false;
            return last.state != targetState && Environment.TickCount64 - last.tick < EchoWindowMs;
        }
    }

    private void MarkApplied(string key, string state)
    {
        lock (_debounceGate) _lastApplied[key] = (state, Environment.TickCount64);
    }

    /// <summary>设备类开关：找不到设备、权限不足、驱动否决都会带回明确原因。</summary>
    private ActionResult SetDeviceEnabled(string kind, bool enable, string label)
    {
        var t = DeviceToggles.SetEnabledEx(kind, enable);
        if (t.Ok)
            return ActionResult.Success($"{label}{(enable ? "已启用" : "已停用")}（{t.Changed}/{t.Total} 台）");

        // cfgMgr32 错误码翻译成人能看懂的原因（官方 Disable-PnpDevice 在此场景同样失败）
        string reason = t.ErrorCode switch
        {
            23 => "设备被驱动或占用方否决（CR_REMOVE_VETOED），请关闭正在使用该设备的程序后重试",
            24 => "设备资源冲突，无法移除（CR_CANT_SHARE_IRQ）",
            31 => "设备当前不可禁用（CR_NOT_DISABLEABLE）",
            _ => string.IsNullOrEmpty(t.Detail) ? "未知原因" : t.Detail,
        };
        return ActionResult.Fail("DEVICE_FAILED",
            $"{label}{(enable ? "启用" : "停用")}失败：{reason}" + (t.ErrorCode != 0 ? $"（cr={t.ErrorCode}）" : ""));
    }

    private ActionResult ApplyWinkeyLock(bool locked)
    {
        string target = locked ? "LOCK" : "UNLOCK";
        if (IsEcho("WinKey", target)) return ActionResult.Success("状态已一致（回声忽略）");

        if (!WinkeyController.Apply(locked))
            return ActionResult.Fail("HOOK_FAILED", "键盘钩子未就绪（用户会话不可用或被安全软件拦截）");
        _state.WinKeyState = target;
        MarkApplied("WinKey", target);
        return ActionResult.Success(locked ? "Win 键已锁定" : "Win 键已解锁");
    }

    private ActionResult ApplyOsdHidden(bool hidden)
    {
        string target = hidden ? "HIDDEN" : "VISIBLE";
        if (IsEcho("OSD", target)) return ActionResult.Success("状态已一致（回声忽略）");

        RegistrySetup.WriteDword(SettingPaths.MySettingKey, SettingPaths.OsdHiddenValue, hidden ? 1 : 0);
        int? readback = RegistrySetup.ReadInt(SettingPaths.MySettingKey, SettingPaths.OsdHiddenValue);
        if (readback != (hidden ? 1 : 0))
            return ActionResult.Fail("REG_WRITE_FAILED", "OSD 开关写入注册表后回读不一致");

        _state.OsdState = hidden ? "OFF" : "ON";
        MarkApplied("OSD", target);
        return ActionResult.Success(hidden ? "OSD 已关闭" : "OSD 已开启");
    }

    /// <summary>
    /// 触摸板：固件 fn12 与 Windows 精密触摸板状态同时设置。
    /// 只写一处会出现"控制中心说关了、系统设置里还开着"的割裂。
    /// </summary>
    private ActionResult ApplyTouchpadEnabled(bool enable)
    {
        string target = enable ? "ON" : "OFF";
        if (IsEcho("Touchpad", target)) return ActionResult.Success("状态已一致（回声忽略）");

        bool mifsOk = false;
        string mifsErr = "";
        try
        {
            _mifs.SetTouchpadLocked(!enable);
            mifsOk = _mifs.GetTouchpadLocked() == !enable;
            if (!mifsOk) mifsErr = "回读不一致";
        }
        catch (Exception ex)
        {
            mifsErr = ex.Message;
            Log.Error("touchpad MIFS: " + ex.Message);
        }

        bool ptpOk = UserRegistry.WriteDword(SettingPaths.PrecisionTouchpadKey,
            SettingPaths.PrecisionTouchpadEnabled, enable ? 1 : 0, out string ptpDetail);
        if (!ptpOk) Log.Error("touchpad PTP write: " + ptpDetail);

        if (!mifsOk && !ptpOk)
            return ActionResult.Fail("TOUCHPAD_FAILED", $"固件与系统触摸板状态均写入失败（{mifsErr} / {ptpDetail}）");

        MarkApplied("Touchpad", target);
        return ActionResult.Success($"{(enable ? "启用" : "停用")}触摸板（固件={mifsOk}，系统={ptpOk}）");
    }

    private ActionResult ApplyFnLock(bool locked)
    {
        string target = locked ? "LOCK" : "UNLOCK";
        if (IsEcho("FnKey", target)) return ActionResult.Success("状态已一致（回声忽略）");

        try
        {
            _mifs.SetFnLock(locked);
            if (_mifs.GetFnLock() != locked)
                return ActionResult.Fail("READBACK_MISMATCH", "Fn 锁写入后回读不一致");
            MarkApplied("FnKey", target);
            return ActionResult.Success(locked ? "Fn 键已锁定" : "Fn 键已解锁");
        }
        catch (Exception ex)
        {
            return ActionResult.Fail("MIFS_FAILED", "Fn 锁写入失败：" + ex.Message);
        }
    }

    private bool TryMifs(Action a)
    {
        try { a(); return true; }
        catch (Exception ex) { Log.Error("MIFS: " + ex.Message); return false; }
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
        // 深度睡眠已落到 HIBERNATEIDLE：回发系统真实值，避免 state.json 与系统脱节
        int hibernateIdle = WinPower.GetHibernateIdleSeconds();

        var setting = new Dictionary<string, object?>
        {
            ["FnKey"] = fnLock ? "LOCK" : "UNLOCK",
            ["WinKey"] = WinkeyController.IsLocked() ? "LOCK" : "UNLOCK",
            ["TouchpadToggle"] = tpLock ? "OFF" : "ON",
            ["PowerMode"] = _state.OperatingMode.ToString(),
            ["OSD"] = ReadOsdState(),
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
            ["DeepSleepSwitch"] = hibernateIdle > 0 ? "ON" : "OFF",
            ["DeepSleepTime"] = (hibernateIdle > 0 ? hibernateIdle : _state.DeepSleepSeconds).ToString(),
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

    /// <summary>OSD 开关以注册表为准（官方 OSD 进程读的就是这个键），读不到才回退内存状态。</summary>
    private string ReadOsdState()
    {
        try
        {
            int? v = RegistrySetup.ReadInt(SettingPaths.MySettingKey, SettingPaths.OsdHiddenValue);
            if (v.HasValue) return v.Value == 1 ? "OFF" : "ON";
        }
        catch { }
        return _state.OsdState;
    }

    private double? RemainingKeyboardTimerMinutes()    {
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
                // MIFS fn20/21 回环诊断：固件原生「自定义风扇转速」通道（开关+百分比）
                try
                {
                    var mode = _mifs.GetPerformanceMode();
                    _mifs.SetFanSpeedSwitch(true);
                    _mifs.SetFanSpeed(50);
                    var sw = _mifs.GetFanSpeedSwitch();
                    var val = _mifs.GetFanSpeedValue();
                    Log.Info($"MIFS fan diag: perfMode={mode} set(1,50) -> readback switch={sw} value={val}");
                }
                catch (Exception ex) { Log.Info("MIFS fan diag failed: " + ex.Message); }
                // 变体 B 若把占空比写进去了，风扇应有反应：读回并发布 FanInfo
                await PublishTelemetryAsync();
                break;
            }

            case "FAN_RESET":
            {
                // 风扇控制状态修复：Fan_Clear 清手动锁存 + 关固件自定义转速开关，
                // 交还固件自动控制。用于异常软件把风扇锁死后的恢复。
                int before = 0;
                try { var r = _mifs.GetFanSpeeds(); before = Math.Max(r.Item1, r.Item2); } catch { }
                _fan.StopLoop();
                if (_ec.Available) _ec.ReleaseFanManual();
                try { _mifs.SetFanSpeedSwitch(false); } catch { }
                _state.FanBoost = false;
                _state.Save();
                await Task.Delay(4000);
                int after = 0;
                try { var r2 = _mifs.GetFanSpeeds(); after = Math.Max(r2.Item1, r2.Item2); } catch { }
                Log.Info($"fan reset: rpm {before} -> {after} (ec manual cleared, fn20 off)");
                await SendFanStatusAsync();
                break;
            }

            case "FAN_BOOST_ON":
            {
                _state.FanBoost = true;
                _fan.StopLoop();
                // 实测有效序列（2026-10-18 21:13 验证）：EC 手动占空比 100 → 风扇
                // 保持厂商上限 ~5800RPM（与温度无关）。MIFS fn20/21 仅作回执兼容。
                if (_ec.Available)
                {
                    _ec.SetBoostManual();
                    Log.Info("fan boost ON via EC manual duty 100/100");
                }
                else
                {
                    try
                    {
                        _mifs.SetFanSpeedSwitch(true);
                        _mifs.SetFanSpeed(100);
                        Log.Info("fan boost ON via MIFS fn20/21 = 100%");
                    }
                    catch { }
                }
                _state.Save();
                await SendFanStatusAsync();
                break;
            }
            case "FAN_BOOST_OFF":
            {
                _state.FanBoost = false;
                if (_state.CustomLoopActive || _fan.FixedRpmActive) _fan.EnsureLoop(); // 恢复曲线/固定转速
                else
                {
                    if (_ec.Available) _ec.ReleaseFanManual(); // Fan_Clear → 自动
                    try { _mifs.SetFanSpeedSwitch(false); } catch { }
                }
                _state.Save();
                await SendFanStatusAsync();
                break;
            }

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
        if (_ec.FanEcSafe) _ec.ReleaseFanManual(); // 退出 EC 手动锁速，交还固件（仅 ITE 映射机型）
        try
        {
            _mifs.SetFanSpeedSwitch(false); // 同步清除自定义转速上限
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
        // 主路径：电池类驱动 IOCTL（对齐官方 BatteryModel/Win32.cs）。
        // 本机 root\wmi 下 BatteryStaticData 缺失、Win32_Battery.FullChargeCapacity 为空，
        // 只有 IOCTL 通道能拿到真实速率，从而算出剩余续航与充满剩余时间。
        var bat = BatteryIo.TryRead();

        int fallbackPercent = WinPower.BatteryPercent();
        int percent = bat.Present ? bat.PercentOr(fallbackPercent) : fallbackPercent;
        bool onAc = bat.Present ? bat.PowerOnline : WinPower.IsOnAc();
        long remain = bat.Present ? bat.SecondsRemaining() : WinPower.BatteryLifeSeconds();
        long full = bat.Present ? bat.SecondsToFull() : -1;

        // 异常码：0=正常 1=系统无电池 2=电池老化（健康度<80%）3=充电异常（接着电却在放电）
        int abnormal = 0;
        if (!bat.Present) abnormal = 1;
        else if (bat.HealthPercent is > 0 and < 80) abnormal = 2;
        else if (bat.Discharging && onAc) abnormal = 3;

        // 注意：UI 用 `batteryinfo["BatteryTime"] == "-1"` 判空，且随后 Convert.ToInt32 取值，
        // 因此这两个字段必须发「秒数字符串」而不是数字，否则 -1 会被格式化成 "0"。
        var payload = new Dictionary<string, object?>
        {
            ["HealthProtectionStatus"] = _state.BatteryHealthMode,
            ["BatteryPercent"] = percent >= 0 ? percent : 100,
            ["BatteryTime"] = remain.ToString(),
            ["BatteryFullTime"] = full.ToString(),
            // "1" = 接通电源（充电中/已充满），"0" = 使用电池
            ["BatteryPowerStatus"] = onAc ? "1" : "0",
            ["TypeCAdaptorPrioritySupport"] = false,
            ["TypeCAdaptorPrioritySwitch"] = "0",
            ["BatteryAbnormal"] = abnormal.ToString(),

            // —— 以下字段当前 UI 未绑定，供 HomeMod / 日志 / 后续页面使用 ——
            ["BatteryDesignedCapacity"] = bat.Present ? bat.DesignedCapacity : 0,
            ["BatteryFullChargedCapacity"] = bat.Present ? bat.FullChargedCapacity : 0,
            ["BatteryHealthPercent"] = bat.Present ? bat.HealthPercent : -1,
            ["BatteryCycleCount"] = bat.Present ? bat.CycleCount : 0,
            ["BatteryRate"] = bat.Present ? bat.Rate : 0,
            ["BatteryVoltage"] = bat.Present ? (int)bat.Voltage : 0,
            ["BatterySource"] = bat.Present ? "ioctl" : "kernel32",
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
