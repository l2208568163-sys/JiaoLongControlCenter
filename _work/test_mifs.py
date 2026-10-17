import sys, clr
clr.AddReference('System.Management')
from System.Management import ManagementObject, ManagementBaseObject
from System import Array, Byte

def mi_call(indata):
    mo = ManagementObject("root\\WMI", "MICommonInterface.InstanceName='ACPI\\PNP0C14\\MIFS_0'", None)
    mp = mo.GetMethodParameters("MiInterface")
    arr = Array.CreateInstance(Byte, 32)
    for i, v in enumerate(indata):
        arr[i] = v
    mp["InData"] = arr
    out = mo.InvokeMethod("MiInterface", mp, None)
    od = out["OutData"]
    return [od[i] for i in range(32)]

def get(fn):
    d = [0]*32
    d[1] = 1   # 1 = Get (WMIMethodType.Get) -- verify below
    d[3] = fn
    return mi_call(d)

# print raw hex for a few GETs; try type byte 1 and 0 to see which works
for fn, name in [(22, "CPUThermometer"), (13, "CPUGPUFanSpeed"), (21, "MaxFanSpeed"), (20, "MaxFanSpeedSwitch")]:
    for t in (0, 1, 2):
        r = get.__wrapped__ if False else None
        d = [0]*32
        d[1] = t
        d[3] = fn
        try:
            res = mi_call(d)
            print(f"fn={fn}({name}) type={t} -> {['%02X' % b for b in res[:10]]}")
        except Exception as e:
            print(f"fn={fn} type={t} ERROR: {e}")
        break
