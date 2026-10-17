using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JiaoLongCCUBridge;

/// <summary>
/// 蛟龙16 Pro 2023 (MRID6) 移植桥接服务：
/// 内嵌 MQTT broker(13688) ←→ 官方 CCU.WinUI 界面；
/// 硬件层走固件 MIFS WMI 接口（无内核驱动）。
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Contains("--console"))
        {
            await RunCoreAsync(CancellationToken.None);
            return;
        }

        if (args.Contains("--ecscan"))
        {
            // 在 ITE EC RAM 中扫描指定字节序列（默认粉色 FF 00 72），定位四区 RGB 控制寄存器
            byte p0 = byte.Parse(args[1], System.Globalization.NumberStyles.HexNumber);
            byte p1 = byte.Parse(args[2], System.Globalization.NumberStyles.HexNumber);
            byte p2 = byte.Parse(args[3], System.Globalization.NumberStyles.HexNumber);
            var ec = new EcAccess();
            if (!ec.Init()) { Console.WriteLine("WinRing0 init failed"); return; }
            Console.WriteLine($"scanning EC RAM for pattern {p0:X2} {p1:X2} {p2:X2} ...");
            int found = 0;
            for (int page = 0; page < 16 && found < 40; page++)
            {
                ushort baseAddr = (ushort)(page * 0x1000);
                var block = new byte[0x1000];
                bool ok = true;
                for (int i = 0; i < 0x1000; i++)
                {
                    try { block[i] = ec.ReadEc((ushort)(baseAddr + i)); }
                    catch { ok = false; break; }
                }
                if (!ok) continue;
                for (int i = 0; i <= 0x1000 - 3; i++)
                {
                    if (block[i] == p0 && block[i + 1] == p1 && block[i + 2] == p2)
                    {
                        Console.WriteLine($"  FOUND at 0x{baseAddr + i:X4}: {block[i]:X2} {block[i+1]:X2} {block[i+2]:X2} | next bytes: {block[i+3]:X2} {block[i+4]:X2} {block[i+5]:X2}");
                        found++;
                    }
                }
                Console.WriteLine($"  page 0x{baseAddr:X4} scanned");
            }
            Console.WriteLine($"total hits: {found}");
            return;
        }

        if (args.Contains("--devtest"))
        {
            Console.WriteLine("--- 全部设备前 8 台 ---");
            foreach (var d in DeviceToggles.DebugList(400))
                Console.WriteLine($"   {d.Class} | {d.Name}");
            foreach (var kind in new[] { "wifi", "bt", "cam" })
            {
                var devs = DeviceToggles.Enumerate(kind);
                Console.WriteLine($"[{kind}] {devs.Count} 台:");
                foreach (var d in devs) Console.WriteLine($"   {d.Name} (class={d.Class}, disabled={d.Disabled})");
                Console.WriteLine($"   enabled={DeviceToggles.QueryEnabled(kind)}");
            }
            Console.WriteLine("禁用摄像头 2 秒再恢复...");
            Console.WriteLine($"SetEnabled(cam,false) = {DeviceToggles.SetEnabled("cam", false)}");
            Thread.Sleep(2000);
            Console.WriteLine($"disabled? {DeviceToggles.QueryEnabled("cam") == false}");
            DeviceToggles.SetEnabled("cam", true);
            Thread.Sleep(1000);
            Console.WriteLine($"enabled again? {DeviceToggles.QueryEnabled("cam")}");
            return;
        }

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options => options.ServiceName = "JiaoLongCCUBridge");
        builder.Services.AddHostedService<BridgeWorker>();
        var host = builder.Build();
        await host.RunAsync();
    }

    private static async Task RunCoreAsync(CancellationToken token)
    {
        Console.WriteLine("JiaoLongCCUBridge 控制台模式（Ctrl+C 退出）");
        var worker = new BridgeWorker();
        await worker.StartAsync(token);
        try
        {
            await Task.Delay(Timeout.Infinite, token);
        }
        catch (OperationCanceledException) { }
        await worker.StopAsync(CancellationToken.None);
    }
}

public sealed class BridgeWorker : Microsoft.Extensions.Hosting.IHostedService
{
    private Mifs _mifs = new();
    private Nvml? _nvml;
    private StateStore _state = new();
    private ProtocolHandlers _handlers = null!;
    private MqttHost _mqtt = null!;
    private FanController _fan = null!;
    private EcAccess _ec = new();
    private KeyboardEffects _kbdFx = null!;
    private Timer? _telemetryTimer;
    private Timer? _keyboardTimerTimer;
    private volatile bool _telemetryOn;

    public async Task StartAsync(CancellationToken token)
    {
        Log.Info("=== JiaoLongCCUBridge starting ===");

        _mifs = new Mifs();
        bool mifsOk = _mifs.Init();
        Log.Info($"MIFS available: {mifsOk} {_mifs.LastError}");

        _nvml = Nvml.TryStart();
        Log.Info($"NVML available: {_nvml?.Available} {_nvml?.GpuName}");

        _ec = new EcAccess();
        _ec.Init(); // 失败则独立风扇/降压功能自动降级

        KaronOc.Init();

        _state = StateStore.Load();

        try { RegistrySetup.SetupAll(); }
        catch (Exception ex) { Log.Error($"registry setup failed (需要管理员/SYSTEM): {ex.Message}"); }

        // 恢复持久化的 GPU 超频 / CPU 降压
        if (KaronOc.Available && _state.GpuOverClockingSwitch)
        {
            bool ok = KaronOc.Set(_state.GpuCoreOffset, _state.GpuMemOffset);
            Log.Info($"restore GPU OC core={_state.GpuCoreOffset} mem={_state.GpuMemOffset}: {ok}");
        }
        if (_ec.Available && _state.CpuUndervoltSteps > 0)
        {
            bool ok = _ec.SetCpuCoAll(_state.CpuUndervoltSteps);
            Log.Info($"restore CPU undervolt {_state.CpuUndervoltSteps}: {ok}");
        }

        // 恢复电池页「高性能电源模式」（开启状态下每次开机重新激活高性能电源计划）
        try
        {
            if (_state.HighPerformancePowerMode)
            {
                bool ok = WinPower.SetActivePlan(WinPower.PlanHigh);
                Log.Info($"restore 高性能电源模式 -> 高性能电源计划: {ok} (当前计划 {WinPower.ActivePlan()})");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"restore power plan failed: {ex.Message}"); // 启动链路必须兜底，任何异常都不能中断服务启动
        }

        _fan = new FanController(_mifs, _ec, _state, GpuTempProvider);
        _kbdFx = new KeyboardEffects(_mifs);
        _handlers = new ProtocolHandlers(_mifs, _state, _nvml, _fan, _ec, _kbdFx,
            () => _telemetryOn, on => _telemetryOn = on);
        _handlers.RestoreHomeSettings(); // 睿频关闭态 / GPU 锁频 开机恢复
        _mqtt = new MqttHost(_handlers);
        await _mqtt.StartAsync();

        // 遥测推送：UI 发 System_ON 后开启；每 3 秒一轮
        _telemetryTimer = new Timer(async _ =>
        {
            if (!_telemetryOn) return;
            await _handlers.PublishTelemetryAsync();
        }, null, 3000, 3000);

        // 键盘定时关灯检查（每 30 秒）
        _keyboardTimerTimer = new Timer(async _ => await _handlers.CheckKeyboardTimerAsync(), null, 30000, 30000);

        _fan.StartIfActive();
        Log.Info("=== JiaoLongCCUBridge started ===");
    }

    private uint GpuTempProvider()
    {
        if (_nvml?.Available != true) return 0;
        var (temp, _, _, _, _) = _nvml.Sample();
        return temp;
    }

    public async Task StopAsync(CancellationToken token)
    {
        _telemetryTimer?.Dispose();
        _keyboardTimerTimer?.Dispose();
        _fan?.StopLoop();
        _kbdFx?.StopAll();
        _ec?.ReleaseFanManual(); // 交还固件风扇控制
        if (_mqtt != null) await _mqtt.StopAsync();
        _nvml?.Dispose();
        Log.Info("=== JiaoLongCCUBridge stopped ===");
    }
}
