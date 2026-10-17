import ctypes, json, sys
from ctypes import wintypes

user32 = ctypes.windll.user32

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
DM_DISPLAYFREQUENCY = 0x400000
DISP_CHANGE_SUCCESSFUL = 0

def new_mode():
    dm = DEVMODE()
    dm.dmSize = ctypes.sizeof(DEVMODE)
    return dm

def find_physical():
    """扫描 DISPLAY1-9 的模式数据库，返回模式最丰富的设备（物理屏胜出虚拟屏）。"""
    best, best_modes = None, 0
    for n in range(1, 10):
        dev = f"\\\\.\\DISPLAY{n}"
        cur = new_mode()
        if not user32.EnumDisplaySettingsExW(dev, ENUM_CURRENT_SETTINGS, ctypes.byref(cur), 0):
            continue
        freqs = set()
        j = 0
        while True:
            m = new_mode()
            if not user32.EnumDisplaySettingsExW(dev, j, ctypes.byref(m), 0):
                break
            if m.dmPelsWidth == cur.dmPelsWidth and m.dmPelsHeight == cur.dmPelsHeight and m.dmBitsPerPel == cur.dmBitsPerPel:
                freqs.add(m.dmDisplayFrequency)
            j += 1
        if len(freqs) > best_modes:
            best, best_modes = dev, len(freqs)
    return best

dev = find_physical() or "\\\\.\\DISPLAY1"
cur = new_mode()
curHz, w, h = 0, 0, 0
if user32.EnumDisplaySettingsExW(dev, ENUM_CURRENT_SETTINGS, ctypes.byref(cur), 0):
    curHz, w, h = cur.dmDisplayFrequency, cur.dmPelsWidth, cur.dmPelsHeight

freqs = []
if w:
    j = 0
    while True:
        m = new_mode()
        if not user32.EnumDisplaySettingsExW(dev, j, ctypes.byref(m), 0):
            break
        if m.dmPelsWidth == w and m.dmPelsHeight == h and m.dmBitsPerPel == cur.dmBitsPerPel and m.dmDisplayFrequency >= 50:
            freqs.append(m.dmDisplayFrequency)
        j += 1
freqs = sorted(set(freqs), reverse=True)

applied = None
hz = int(sys.argv[1]) if len(sys.argv) > 1 else 0
if hz and w and hz != curHz:
    dm = new_mode()
    if user32.EnumDisplaySettingsExW(dev, ENUM_CURRENT_SETTINGS, ctypes.byref(dm), 0):
        dm.dmFields |= DM_DISPLAYFREQUENCY
        dm.dmDisplayFrequency = hz
        rc = user32.ChangeDisplaySettingsExW(dev, ctypes.byref(dm), None, 0, None)
        if rc == DISP_CHANGE_SUCCESSFUL:
            cur2 = new_mode()
            if user32.EnumDisplaySettingsExW(dev, ENUM_CURRENT_SETTINGS, ctypes.byref(cur2), 0):
                applied = cur2.dmDisplayFrequency

print(json.dumps({"device": dev, "currentHz": curHz, "list": freqs, "applied": applied}))
