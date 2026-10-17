import ctypes
from ctypes import wintypes

user32 = ctypes.windll.user32

class DISPLAY_DEVICE(ctypes.Structure):
    _fields_ = [("cb", wintypes.DWORD),
                ("DeviceName", ctypes.c_wchar * 32),
                ("DeviceString", ctypes.c_wchar * 128),
                ("StateFlags", wintypes.DWORD),
                ("DeviceID", ctypes.c_wchar * 128),
                ("DeviceKey", ctypes.c_wchar * 128)]

class DEVMODE(ctypes.Structure):
    _fields_ = [("dmDeviceName", ctypes.c_wchar * 32),
                ("dmSpecVersion", wintypes.WORD), ("dmDriverVersion", wintypes.WORD),
                ("dmSize", wintypes.WORD), ("dmDriverExtra", wintypes.WORD),
                ("dmFields", wintypes.DWORD),
                ("dmPositionX", ctypes.c_long), ("dmPositionY", ctypes.c_long),
                ("dmDisplayOrientation", wintypes.DWORD), ("dmDisplayFixedOutput", wintypes.DWORD),
                ("dmColor", ctypes.c_short), ("dmDuplex", ctypes.c_short),
                ("dmYResolution", ctypes.c_short), ("dmTTOption", ctypes.c_short),
                ("dmCollate", ctypes.c_short),
                ("dmFormName", ctypes.c_wchar * 32),
                ("dmLogPixels", wintypes.WORD),
                ("dmBitsPerPel", wintypes.DWORD), ("dmPelsWidth", wintypes.DWORD),
                ("dmPelsHeight", wintypes.DWORD), ("dmDisplayFlags", wintypes.DWORD),
                ("dmDisplayFrequency", wintypes.DWORD),
                ("dmICMMethod", wintypes.DWORD), ("dmICMIntent", wintypes.DWORD),
                ("dmMediaType", wintypes.DWORD), ("dmDitherType", wintypes.DWORD),
                ("dmReserved1", wintypes.DWORD), ("dmReserved2", wintypes.DWORD),
                ("dmPanningWidth", wintypes.DWORD), ("dmPanningHeight", wintypes.DWORD)]

ENUM_CURRENT_SETTINGS = -1

i = 0
while True:
    dd = DISPLAY_DEVICE(); dd.cb = ctypes.sizeof(DISPLAY_DEVICE)
    if not user32.EnumDisplayDevicesW(None, i, ctypes.byref(dd), 0):
        break
    attached = dd.StateFlags & 1
    cur = ""
    if attached:
        dm = DEVMODE(); dm.dmSize = ctypes.sizeof(DEVMODE)
        if user32.EnumDisplaySettingsExW(dd.DeviceName, ENUM_CURRENT_SETTINGS, ctypes.byref(dm), 0):
            cur = f"{dm.dmPelsWidth}x{dm.dmPelsHeight}@{dm.dmDisplayFrequency}"
        # count modes at this res
        freqs = []
        j = 0
        while True:
            dm2 = DEVMODE(); dm2.dmSize = ctypes.sizeof(DEVMODE)
            if not user32.EnumDisplaySettingsExW(dd.DeviceName, j, ctypes.byref(dm2), 0):
                break
            if dm2.dmPelsWidth == dm.dmPelsWidth and dm2.dmPelsHeight == dm.dmPelsHeight and dm2.dmBitsPerPel == dm.dmBitsPerPel:
                freqs.append(dm2.dmDisplayFrequency)
            j += 1
        cur += f"  modes_at_res={sorted(set(freqs))}"
    print(f"[{i}] '{dd.DeviceString}' name={dd.DeviceName} attached={bool(attached)} mirror={bool(dd.StateFlags & 8)} primary={bool(dd.StateFlags & 4)} {cur}")
    mon = DISPLAY_DEVICE(); mon.cb = ctypes.sizeof(DISPLAY_DEVICE)
    if user32.EnumDisplayDevicesW(dd.DeviceName, 0, ctypes.byref(mon), 0):
        print(f"     monitor: '{mon.DeviceString}'")
    i += 1
