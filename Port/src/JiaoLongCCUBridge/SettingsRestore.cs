namespace JiaoLongCCUBridge;

/// <summary>
/// 系统选项的开机恢复。与 RestoreHomeSettings（主页控制项）并列，
/// 由 Program.BridgeWorker.StartAsync 在 MQTT 起来之前调用一次。
///
/// 设计原则：
/// - 只恢复"用户显式设置过、且脱离进程就失效"的项（Win 键钩子、OSD 注册表值）；
/// - 触摸板/设备开关不主动改：它们的状态由系统自身持有，开机即恢复，强行改写会造成"开机莫名关网卡"。
/// </summary>
public sealed partial class ProtocolHandlers
{
    public void RestoreSystemSettings()
    {
        try
        {
            bool winkeyLocked = string.Equals(_state.WinKeyState, "LOCK", StringComparison.OrdinalIgnoreCase);
            WinkeyController.Restore(winkeyLocked); // 同时预热钩子进程，切锁才即时生效

            // OSD：以内存状态回写官方注册表键，保证任何 OSD 消费方读到一致的值
            int osdHidden = string.Equals(_state.OsdState, "OFF", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            RegistrySetup.WriteDword(SettingPaths.MySettingKey, SettingPaths.OsdHiddenValue, osdHidden);

            Log.Info($"restore system settings: winkey={_state.WinKeyState} osd={_state.OsdState} numpad={_state.NumPadState} " +
                     $"(caps: NumPad={SystemCaps.NumPadSupported} MicNR={SystemCaps.MicNoiseReductionSupported} " +
                     $"LocalDimming={SystemCaps.LocalDimmingSupported} TpToggle={SystemCaps.TouchpadToggleSupported} elevated={SystemCaps.IsElevated})");
        }
        catch (Exception ex)
        {
            Log.Error("restore system settings failed: " + ex.Message);
        }
    }
}
