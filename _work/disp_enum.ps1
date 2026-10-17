Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class DispEnum {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct DISPLAY_DEVICE {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceKey;
    }
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
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool EnumDisplayDevicesW(string dev, uint num, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool EnumDisplaySettingsExW(string dev, int mode, ref DEVMODE dm, uint flags);
}
'@
for ($i = 0; $i -lt 12; $i++) {
    $dd = New-Object DispEnum+DISPLAY_DEVICE
    $dd.cb = [System.Runtime.InteropServices.Marshal]::SizeOf([type][DispEnum+DISPLAY_DEVICE])
    if (-not [DispEnum]::EnumDisplayDevicesW($null, $i, [ref]$dd, 0)) { break }
    $flags = $dd.StateFlags
    $cur = "?"
    if ($flags -band 1) {
        $dm = New-Object DispEnum+DEVMODE
        $dm.dmSize = [System.Runtime.InteropServices.Marshal]::SizeOf([type][DispEnum+DEVMODE])
        if ([DispEnum]::EnumDisplaySettingsExW($dd.DeviceName, -1, [ref]$dm, 0)) { $cur = "$($dm.dmPelsWidth)x$($dm.dmPelsHeight)@$($dm.dmDisplayFrequency)" }
    }
    Write-Host ("[{0}] {1} | flags={2} | cur={3}" -f $i, $dd.DeviceString, $flags, $cur)
    # monitor level
    $mon = New-Object DispEnum+DISPLAY_DEVICE
    $mon.cb = [System.Runtime.InteropServices.Marshal]::SizeOf([type][DispEnum+DISPLAY_DEVICE])
    if ([DispEnum]::EnumDisplayDevicesW($dd.DeviceName, 0, [ref]$mon, 0)) {
        Write-Host ("      monitor: " + $mon.DeviceString + " flags=" + $mon.StateFlags)
    }
}
