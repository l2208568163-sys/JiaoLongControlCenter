using Microsoft.Win32;

namespace JiaoLongCCUBridge;

/// <summary>
/// 写 HKLM\SOFTWARE\OEM\GamingCenter2 —— UI 读取能力开关（ItemSupport）与品牌配置。
/// 键语义来自 CCU.WinUI 反编译代码（SplashWindow/NavigationPageViewModel/AppConfig 等）。
/// </summary>
public static class RegistrySetup
{
    public const string RootKey = @"SOFTWARE\OEM\GamingCenter2";
    public const string ItemSupportKey = @"SOFTWARE\OEM\GamingCenter2\ItemSupport";

    public static void WriteDword(string subkey, string name, int value) =>
        Registry.LocalMachine.CreateSubKey(subkey, true).SetValue(name, value, RegistryValueKind.DWord);

    public static void WriteString(string subkey, string name, string value) =>
        Registry.LocalMachine.CreateSubKey(subkey, true).SetValue(name, value, RegistryValueKind.String);

    public static object? Read(string subkey, string name)
    {
        using var k = Registry.LocalMachine.OpenSubKey(subkey);
        return k?.GetValue(name);
    }

    public static void SetupAll()
    {
        // 品牌与应用配置（等价官方安装器写入的注册表 + setup.ini 语义）
        WriteDword(RootKey, "AppType", 1);
        WriteString(RootKey, "AppIcon", "gamingcenter_STD.ico");
        WriteString(RootKey, "APPTarget", "Customize");
        WriteString(RootKey, "Language", "zh-cn");
        WriteDword(RootKey, "CustomizeTarget", ProtocolHandlers.CustomizeTarget);
        WriteDword(RootKey, "BridgeType", 0);
        WriteString(RootKey, "LanguageList", "en-us,zh-cn");
        WriteString(RootKey, "AutoDetectSystemLang", "DetectOn");
        WriteString(RootKey, "AppVersion", "5.56.60.34");
        WriteString(RootKey, "AppSubVersion", "5.56.60.34_JiaoLongPort");
        WriteString(RootKey, "AppName", "机械革命控制中心");
        WriteDword(RootKey, "ColorCalibration", 0);
        WriteDword(RootKey, "OsdOnly", 0);
        WriteDword(RootKey, "MonitorSupport", 0);
        WriteDword(RootKey, "DebugMode", 0);

        // 能力开关：v2 打开 风扇/性能模式/OC/键盘灯效/独显直连/刷新率/校色
        WriteDword(ItemSupportKey, "ServiceLoadingReady", 1);   // UI 加载闸门
        WriteString(ItemSupportKey, "BIOS_PROJECT_ID", ProtocolHandlers.ProjectId);
        WriteDword(ItemSupportKey, "IsNvGpu", 1);
        WriteDword(ItemSupportKey, "IsNewUMASupport", 0);
        WriteDword(ItemSupportKey, "DGpuDirectConnectionSupport", 1); // 独显直连（fn9）
        WriteDword(ItemSupportKey, "ColorCalibrationSupport", 1);     // 校色（sRGB ICC 开关）
        WriteDword(ItemSupportKey, "LocalDimmingSupport", 0);
        WriteDword(ItemSupportKey, "LCDOverdriveSupport", 0);
        WriteDword(ItemSupportKey, "DispalyRefresh", 2);              // 源码拼写如此，>=2 显示刷新率页
        WriteDword(ItemSupportKey, "PanelType", 0);
        WriteDword(ItemSupportKey, "KeyboardType", 1);                // >0 显示灯效页
        WriteDword(ItemSupportKey, "LightbarSupport", 0);
        WriteDword(ItemSupportKey, "RGBLightbarSupport", 0);
        WriteDword(ItemSupportKey, "NumPadSupport", 0);
        WriteDword(ItemSupportKey, "APVersionCheck", 0);
        WriteDword(ItemSupportKey, "MicNoiseReductionSupport", 0);
        WriteDword(ItemSupportKey, "OTASupport", 0);
        WriteDword(ItemSupportKey, "RamFan1p5Support", 1);      // Fan/Status 走 1p5 协议
        WriteDword(ItemSupportKey, "IsAMDPlatform", 1);         // AMD 平台：自定义设置页的
        // AMD 滑块初始化块（SPL/SPPT/FPPT/温度墙）以该键为前提，缺失则整个跳过、滑块全 0
        WriteDword(ItemSupportKey, "FanSettingsSupport", 1);    // 风扇设置页
        WriteDword(ItemSupportKey, "TurboModeSupport", 1);
        WriteDword(ItemSupportKey, "OcSettingsSupport", 1);     // SPL/SPPT/温度墙/GPU超频已实现
        WriteDword(ItemSupportKey, "CPUPerformanceAndOverClockMenuSupport", 1);
        WriteDword(ItemSupportKey, "IsTurboOCAdjustSupport", 0);
        WriteDword(ItemSupportKey, "IsTurboSubModeSupport", 0);
        WriteDword(ItemSupportKey, "IsTurboOCDefualtON", 0);
        WriteDword(ItemSupportKey, "IsSmartBalanceSupport", 0);
        WriteDword(ItemSupportKey, "IsProjectIdCommercial", 0);
        WriteDword(ItemSupportKey, "SilentPerformanceModeSwitch", 0);
        WriteDword(ItemSupportKey, "HighPerformancePowerModeSupport", 0);
        WriteDword(ItemSupportKey, "BatteryLogoSupport", 0);
        WriteDword(ItemSupportKey, "AcRecoverySwitchSupport", 0);
        WriteDword(ItemSupportKey, "iGPUModeOnlySupport", 0);
        WriteDword(ItemSupportKey, "IDR2Check", 0);
        WriteDword(ItemSupportKey, "LiquidCoolingSupport", 0);
        WriteDword(ItemSupportKey, "LiquidCoolingAutoModeSupport", 0);

        Log.Info("registry GamingCenter2 configured");
    }

    public static void CleanupAll()
    {
        try { Registry.LocalMachine.DeleteSubKeyTree(RootKey, false); } catch { }
    }
}
