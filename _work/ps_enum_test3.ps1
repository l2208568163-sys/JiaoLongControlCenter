Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class DispDevU {
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
'@
for ($i = 0; $i -lt 16; $i++) {
    $d = New-Object DispDevU+DISPLAY_DEVICE
    $d.cb = [System.Runtime.InteropServices.Marshal]::SizeOf([type][DispDevU+DISPLAY_DEVICE])
    if (-not [DispDevU]::EnumDisplayDevicesW($null, $i, [ref]$d, 0)) { Write-Output "stop at $i"; break }
    Write-Output ("[{0}] '{1}' name={2} flags={3}" -f $i, $d.DeviceString, $d.DeviceName, $d.StateFlags)
}
