# 显示助手：在用户会话中执行刷新率查询/切换（服务 Session 0 无法调用 GDI 显示 API）
param([int]$hz = 0)
$ErrorActionPreference = "Continue"

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class DisplayHelper {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool EnumDisplaySettingsExW(string dev, int mode, ref DEVMODE dm, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int ChangeDisplaySettingsExW(string dev, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr lParam);
    public static DEVMODE NewMode() {
        var dm = new DEVMODE();
        dm.dmDeviceName = new string('\0', 32);
        dm.dmFormName = new string('\0', 32);
        dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
        return dm;
    }
}
'@

function Get-PhysicalDev {
    # 枚举适配器，跳过虚拟显卡
    for ($i = 0; $i -lt 32; $i++) {
        $dd = New-Object DisplayHelper+DEVMODE
        # 用 EnumDisplayDevices 找 attached 的物理适配器
        $ddi = New-Object System.Text.StringBuilder
        $query = @'
using System;
using System.Runtime.InteropServices;
public class DispDev {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct DISPLAY_DEVICE {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceKey;
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool EnumDisplayDevicesW(string dev, uint num, ref DISPLAY_DEVICE dd, uint flags);
}
'@        if (-not ('DispDev' -as [type])) { Add-Type -TypeDefinition $query }
        $d = New-Object DispDev+DISPLAY_DEVICE
        $d.cb = [System.Runtime.InteropServices.Marshal]::SizeOf([type][DispDev+DISPLAY_DEVICE])
        if (-not [DispDev]::EnumDisplayDevicesW($null, $i, [ref]$d, 0)) { break }
        if (($d.StateFlags -band 1) -eq 0) { continue }           # 未接桌面
        if (($d.StateFlags -band 8) -ne 0) { continue }           # 镜像驱动
        if ($d.DeviceString -match 'Virtual') { continue }        # 虚拟显卡
        return $d.DeviceName
    }
    return $null
}

$dev = Get-PhysicalDev
$cur = [DisplayHelper]::NewMode()
$curHz = 0; $w = 0; $h = 0
if ([DisplayHelper]::EnumDisplaySettingsExW($dev, -1, [ref]$cur, 0)) {
    $curHz = [int]$cur.dmDisplayFrequency; $w = [int]$cur.dmPelsWidth; $h = [int]$cur.dmPelsHeight
}
$list = New-Object System.Collections.ArrayList
if ($w -gt 0) {
    for ($i = 0; ; $i++) {
        $dm = [DisplayHelper]::NewMode()
        if (-not [DisplayHelper]::EnumDisplaySettingsExW($dev, $i, [ref]$dm, 0)) { break }
        if ($dm.dmPelsWidth -eq $w -and $dm.dmPelsHeight -eq $h -and $dm.dmBitsPerPel -eq $cur.dmBitsPerPel -and $dm.dmDisplayFrequency -ge 50) {
            [void]$list.Add([int]$dm.dmDisplayFrequency)
        }
    }
}
$list = $list | Sort-Object -Descending -Unique

$applied = $null
if ($hz -gt 0 -and $hz -ne $curHz -and $w -gt 0) {
    $dm = [DisplayHelper]::NewMode()
    if ([DisplayHelper]::EnumDisplaySettingsExW($dev, -1, [ref]$dm, 0)) {
        $dm.dmFields = $dm.dmFields -bor 0x400000
        $dm.dmDisplayFrequency = [uint32]$hz
        $rc = [DisplayHelper]::ChangeDisplaySettingsExW($dev, [ref]$dm, [IntPtr]::Zero, 0, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 600
        $dm2 = [DisplayHelper]::NewMode()
        if ([DisplayHelper]::EnumDisplaySettingsExW($dev, -1, [ref]$dm2, 0)) { $applied = [int]$dm2.dmDisplayFrequency }
    }
}

@{ currentHz = $curHz; width = $w; height = $h; list = @($list); applied = $applied; device = $dev } | ConvertTo-Json -Compress
