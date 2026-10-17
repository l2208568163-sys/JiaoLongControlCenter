using System.Runtime.InteropServices;

namespace JiaoLongCCUBridge;

/// <summary>NVML 轻封装：读取 NVIDIA GPU 温度/占用/频率/显存。nvml.dll 随显卡驱动装在 System32。</summary>
public sealed class Nvml : IDisposable
{
    private const int NvmlTemperatureGpu = 0; // GPU 核心温度
    private const int ClockGraphics = 0;
    private const int ClockMem = 2;

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlInit_v2();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlShutdown();

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, ref NvmlUtilization utilization);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetMemoryInfo_v2(IntPtr device, ref NvmlMemory memory);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, ref NvmlMemory memory);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint clock);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetMaxClockInfo(IntPtr device, int clockType, out uint clock);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceSetGpuLockedClocks(IntPtr device, uint minGpuClockMHz, uint maxGpuClockMHz);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceResetGpuLockedClocks(IntPtr device);

    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetSupportedGraphicsClocks(IntPtr device, uint memoryClockMHz, ref uint count, uint[]? clocksMHz);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization { public uint Gpu, Memory; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory { public ulong Total, Free, Used; }

    private IntPtr _device;
    public bool Available { get; private set; }
    public string GpuName { get; private set; } = "";

    public static Nvml? TryStart()
    {
        try
        {
            if (nvmlInit_v2() != 0) return null;
            var nv = new Nvml();
            if (nvmlDeviceGetHandleByIndex_v2(0, out nv._device) != 0) { nvmlShutdown(); return null; }
            var nameBuffer = new byte[96];
            if (nvmlDeviceGetName(nv._device, nameBuffer, 96) == 0)
            {
                int len = Array.IndexOf(nameBuffer, (byte)0);
                if (len < 0) len = nameBuffer.Length;
                nv.GpuName = System.Text.Encoding.ASCII.GetString(nameBuffer, 0, len);
            }
            nv.Available = true;
            return nv;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public (uint tempC, uint usagePct, uint memTotalMb, uint coreMhz, uint memMhz) Sample()
    {
        uint temp = 0, usage = 0, memTotal = 0, core = 0, mem = 0;
        try
        {
            nvmlDeviceGetTemperature(_device, NvmlTemperatureGpu, out temp);
            var util = new NvmlUtilization();
            if (nvmlDeviceGetUtilizationRates(_device, ref util) == 0) usage = util.Gpu;
            var memInfo = new NvmlMemory();
            if (nvmlDeviceGetMemoryInfo_v2(_device, ref memInfo) != 0)
            {
                memInfo = new NvmlMemory();
                nvmlDeviceGetMemoryInfo(_device, ref memInfo);
            }
            if (memInfo.Total > 0) memTotal = (uint)(memInfo.Total / (1024 * 1024));
            nvmlDeviceGetClockInfo(_device, ClockGraphics, out core);
            nvmlDeviceGetClockInfo(_device, ClockMem, out mem);
        }
        catch
        {
            // 采样失败保持零值，UI 会显示 "-"
        }
        return (temp, usage, memTotal, core, mem);
    }

    public void Dispose()
    {
        try { nvmlShutdown(); } catch { }
    }

    // ---------- GPU 锁频（主页显卡卡「锁定频率」后端） ----------

    /// <summary>当前显存频率下的受支持核心频率列表（MHz，升序去重；失败返回空）。</summary>
    public List<uint> SupportedGraphicsClocks()
    {
        var result = new List<uint>();
        try
        {
            nvmlDeviceGetClockInfo(_device, ClockMem, out uint memClock);
            uint count = 0;
            // 第一遍：count=0 拿数量（返回 INSUFFICIENT_SIZE 时 count 被填）
            nvmlDeviceGetSupportedGraphicsClocks(_device, memClock, ref count, null);
            if (count == 0 || count > 512) return result;
            var buffer = new uint[count];
            if (nvmlDeviceGetSupportedGraphicsClocks(_device, memClock, ref count, buffer) == 0)
                result = buffer.Where(c => c > 0).Distinct().OrderBy(c => c).ToList();
        }
        catch { }
        return result;
    }

    /// <summary>最大核心频率（MHz，失败返回 0）。</summary>
    public uint MaxGraphicsClock()
    {
        try
        {
            return nvmlDeviceGetMaxClockInfo(_device, ClockGraphics, out uint c) == 0 ? c : 0;
        }
        catch { return 0; }
    }

    /// <summary>锁频到固定频率（min=max）。服务以 SYSTEM 运行，通常有权限；部分笔记本 GPU 可能不支持。</summary>
    public bool SetLockedClocks(uint mhz)
    {
        try
        {
            int rc = nvmlDeviceSetGpuLockedClocks(_device, mhz, mhz);
            if (rc != 0) Log.Error($"nvmlDeviceSetGpuLockedClocks({mhz}) rc={rc}");
            return rc == 0;
        }
        catch (Exception ex)
        {
            Log.Error($"SetLockedClocks: {ex.Message}");
            return false;
        }
    }

    /// <summary>解除锁频，恢复动态调频。</summary>
    public bool ResetLockedClocks()
    {
        try
        {
            int rc = nvmlDeviceResetGpuLockedClocks(_device);
            return rc == 0;
        }
        catch { return false; }
    }
}
