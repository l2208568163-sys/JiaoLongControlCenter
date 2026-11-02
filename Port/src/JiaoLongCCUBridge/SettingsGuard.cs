using Microsoft.Win32;
using System.Security.Principal;

namespace JiaoLongCCUBridge;

/// <summary>单个设置动作的执行结果。Code 供日志/错误主题使用，Message 面向人。</summary>
public readonly record struct ActionResult(bool Ok, string Code, string Message)
{
    public static ActionResult Success(string message = "OK") => new(true, "OK", message);
    public static ActionResult Fail(string code, string message) => new(false, code, message);

    public override string ToString() => Ok ? $"OK({Message})" : $"{Code}({Message})";
}

/// <summary>
/// 机型能力位（HKLM\SOFTWARE\OEM\GamingCenter2\ItemSupport）。
/// 官方 UI 用它决定"显示不显示"，服务端用它决定"接受不接受"——
/// 两者必须一致，否则会出现 UI 不显示但指令仍被执行的情况。
/// </summary>
public static class SystemCaps
{
    private const string Key = @"SOFTWARE\OEM\GamingCenter2\ItemSupport";
    private static readonly object Gate = new();
    private static Dictionary<string, object?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _stamp = DateTime.MinValue;

    private static void Refresh(bool force)
    {
        lock (Gate)
        {
            if (!force && (DateTime.UtcNow - _stamp).TotalSeconds < 10) return;
            var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(Key);
                if (k != null)
                    foreach (var name in k.GetValueNames())
                        map[name] = k.GetValue(name);
            }
            catch (Exception ex)
            {
                Log.Error("SystemCaps read failed: " + ex.Message);
            }
            _cache = map;
            _stamp = DateTime.UtcNow;
        }
    }

    public static int Int(string name, int fallback = 0)
    {
        Refresh(false);
        lock (Gate)
            return _cache.TryGetValue(name, out var v) && v != null && int.TryParse(v.ToString(), out int i) ? i : fallback;
    }

    public static bool Flag(string name) => Int(name) != 0;

    public static string Str(string name, string fallback = "")
    {
        Refresh(false);
        lock (Gate)
            return _cache.TryGetValue(name, out var v) ? v?.ToString() ?? fallback : fallback;
    }

    public static string BiosProjectId => Str("BIOS_PROJECT_ID");
    public static bool NumPadSupported => Flag("NumPadSupport");
    public static bool MicNoiseReductionSupported => Flag("MicNoiseReductionSupport");
    public static bool LocalDimmingSupported => Flag("LocalDimmingSupport");

    /// <summary>与 UI 的 RefreshItemSupport 保持同一份白名单：命中则「触摸板锁定」项不可用。</summary>
    private static readonly HashSet<string> TouchpadToggleUnsupportedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "IDA", "IDB", "IDU", "IDK", "IDW", "IDY", "ID5", "ID55", "ID6", "ID65",
        "ID1New", "ID9", "IDQ", "IDX", "IDE", "IDN", "IDS", "IDC", "ID2", "IDJ",
        "IDP", "IDH", "IDR2", "ID7", "IDD", "IDO", "IDI", "IDG", "IDL", "IDV",
    };

    public static bool TouchpadToggleSupported
    {
        get
        {
            string id = BiosProjectId;
            if (id.Length == 5) return false;          // 与 UI 同规则：5 位代号一律不支持
            if (TouchpadToggleUnsupportedIds.Contains(id)) return false;
            return true;
        }
    }

    /// <summary>服务是否具备修改设备/注册表所需的权限（SYSTEM 或管理员）。</summary>
    public static bool IsElevated { get; } = DetectElevated();

    private static bool DetectElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 以**当前登录用户**身份读写 HKCU。
/// 服务跑在 SYSTEM 下，直接写 HKCU 会落到 SYSTEM 的配置单元，用户侧完全看不到；
/// 所以统一借用户令牌执行 reg.exe。
/// </summary>
public static class UserRegistry
{
    private static string RegExe => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "reg.exe");

    /// <summary>写 HKCU DWORD，返回是否成功。</summary>
    public static bool WriteDword(string subKey, string name, int value, out string detail)
    {
        detail = "";
        string args = $"add \"HKCU\\{subKey}\" /v \"{name}\" /t REG_DWORD /d {value} /f";
        if (!UserSessionRunner.RunInUserSession(RegExe, args, out string output, 8000))
        {
            detail = "用户会话不可用";
            return false;
        }
        detail = output.Trim();
        return true;
    }

    /// <summary>读 HKCU DWORD；读不到返回 null。</summary>
    public static int? ReadDword(string subKey, string name, int timeoutMs = 8000)
    {
        string args = $"query \"HKCU\\{subKey}\" /v \"{name}\"";
        if (!UserSessionRunner.RunInUserSession(RegExe, args, out string output, timeoutMs)) return null;
        // 输出形如：    Name    REG_DWORD    0x1
        int idx = output.IndexOf("REG_DWORD", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        string tail = output[(idx + "REG_DWORD".Length)..].Trim();
        string token = tail.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(token[2..], System.Globalization.NumberStyles.HexNumber, null, out int hex))
            return hex;
        return int.TryParse(token, out int dec) ? dec : null;
    }
}

/// <summary>系统选项注册表位置常量（官方 GCUService 同名键）。</summary>
public static class SettingPaths
{
    /// <summary>OSD 显示开关：官方 OSDTpDetect.exe 读此值决定是否弹 OSD。</summary>
    public const string MySettingKey = @"SOFTWARE\OEM\GamingCenter2\MySetting";
    public const string OsdHiddenValue = "OSDHidden";

    /// <summary>Windows 精密触摸板状态（与系统设置页共用）。</summary>
    public const string PrecisionTouchpadKey = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status";
    public const string PrecisionTouchpadEnabled = "Enabled";
}
