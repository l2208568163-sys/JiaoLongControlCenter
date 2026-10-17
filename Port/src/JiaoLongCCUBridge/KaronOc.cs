using System.Runtime.InteropServices;

namespace JiaoLongCCUBridge;

/// <summary>
/// KaronOC（来自第三方蛟龙控制中心，本机已验证）：读写 NVIDIA P0 状态的核心/显存频率偏移（MHz）。
/// </summary>
public static class KaronOc
{
    private const int CoreOffsetMax = 250;
    private const int CoreOffsetMin = 0;
    private const int MemOffsetMax = 1000;
    private const int MemOffsetMin = 0;

    [DllImport("KaronOC.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ChangePstatesLevel0Settings(int grClockOffset, int memClockOffset);

    [DllImport("KaronOC.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetPstatesLevel0Settings(ref int grClockOffset, ref int memClockOffset);

    public static bool Available { get; private set; }

    public static bool Init()
    {
        try
        {
            int g = 0, m = 0;
            var rc = GetPstatesLevel0Settings(ref g, ref m);
            Available = rc == 0;
            Log.Info($"KaronOC available: {Available} (当前偏移 core={g} mem={m})");
            return Available;
        }
        catch (DllNotFoundException ex)
        {
            Log.Error("KaronOC.dll 缺失: " + ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("KaronOC init: " + ex.Message);
            return false;
        }
    }

    public static (int core, int mem) Get()
    {
        int g = 0, m = 0;
        try
        {
            if (Available && GetPstatesLevel0Settings(ref g, ref m) != 0) return (0, 0);
        }
        catch { }
        return (g, m);
    }

    public static bool Set(int coreOffset, int memOffset)
    {
        if (!Available) return false;
        coreOffset = Math.Clamp(coreOffset, CoreOffsetMin, CoreOffsetMax);
        memOffset = Math.Clamp(memOffset, MemOffsetMin, MemOffsetMax);
        for (int i = 0; i < 3; i++)
        {
            if (ChangePstatesLevel0Settings(coreOffset, memOffset) == 0) return true;
        }
        return false;
    }
}
