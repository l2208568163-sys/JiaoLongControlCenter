Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Prot3 {
  [DllImport("ntdll.dll")]
  public static extern int NtQueryInformationProcess(IntPtr h, int cls, byte[] info, int len, out int ret);
  [DllImport("kernel32.dll", SetLastError=true)]
  public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
'@
foreach ($procId in @(2236,7272,9956,13308,35576,39168,42676)) {
  $h = [Prot3]::OpenProcess(0x1000, $false, $procId)
  if ($h -eq [IntPtr]::Zero) {
    Write-Host ("{0}: open denied err={1}" -f $procId, [Runtime.InteropServices.Marshal]::GetLastWin32Error())
    continue
  }
  $buf = New-Object byte[] 4
  $ret = 0
  $r = [Prot3]::NtQueryInformationProcess($h, 61, $buf, 4, [ref]$ret)
  $prot = $buf[0]
  $type = $prot -band 0x7
  $signer = ($prot -shr 3) -band 0x7
  Write-Host ("{0}: ntstatus={1} protection=0x{2:X2} type={3} signer={4}" -f $procId, $r, $prot, $type, $signer)
  [Prot3]::CloseHandle($h) | Out-Null
}
