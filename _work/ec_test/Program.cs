using System.Threading;
using System;
using System.Runtime.InteropServices;

class EcTest
{
    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern nint LoadLibrary(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern nint GetProcAddress(nint module, string name);

    delegate int InitializeOlsDelegate();
    delegate byte ReadIoPortByteDelegate(ushort port);
    delegate void WriteIoPortByteDelegate(ushort port, byte value);

    static ReadIoPortByteDelegate ReadPort;
    static WriteIoPortByteDelegate WritePort;

    const ushort IDX = 0x4E, DAT = 0x4F, SC = 0x66;

    static byte ReadEcEx66(ushort addr)
    {
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2E); WritePort(0x62, 0x11);
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2F); WritePort(0x62, (byte)(addr >> 8));
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2E); WritePort(0x62, 0x10);
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2F); WritePort(0x62, (byte)(addr & 0xFF));
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2E); WritePort(0x62, 0x12);
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2F);
        return ReadPort(0x62);
    }

    static void WriteEcEx66(ushort addr, byte data)
    {
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2E); WritePort(0x62, 0x11);
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2F); WritePort(0x62, (byte)(addr >> 8));
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2E); WritePort(0x62, 0x10);
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2F); WritePort(0x62, (byte)(addr & 0xFF));
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2E); WritePort(0x62, 0x12);
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(SC, 0x2F); WritePort(0x62, data);
    }

    static void Main()
    {
        var m = LoadLibrary(@"C:\Program Files\JiaoLongCCUBridge\WinRing0x64.dll");
        if (m == nint.Zero) { Console.WriteLine("load failed"); return; }
        var init = (InitializeOlsDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m, "InitializeOls"), typeof(InitializeOlsDelegate));
        ReadPort = (ReadIoPortByteDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m, "ReadIoPortByte"), typeof(ReadIoPortByteDelegate));
        WritePort = (WriteIoPortByteDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m, "WriteIoPortByte"), typeof(WriteIoPortByteDelegate));
        Console.WriteLine("InitializeOls: " + init());

        Console.WriteLine("0x66 status: 0x" + ReadPort(SC).ToString("X2"));
        Console.WriteLine("0x4E reads back: 0x" + ReadPort(IDX).ToString("X2"));

        // 老程序 ECStatus(EC_IBF)：等 0x66 bit1 清零
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }

        // 老程序 Write_EC_Init: Read_EC_EX(0x2000)
        Console.WriteLine("chip id @0x2000: 0x" + ReadEcEx(0x2000).ToString("X2"));
        Console.WriteLine("chip id @0x2001: 0x" + ReadEcEx(0x2001).ToString("X2"));

        // 老程序 Write_EC_Init: 0x1060 |= 0x80
        byte ver = ReadEcEx(0x1060);
        Console.WriteLine("ver @0x1060: 0x" + ver.ToString("X2"));
        WriteEcEx(0x1060, (byte)(ver | 0x80));
        Console.WriteLine("ver @0x1060 after |=0x80: 0x" + ReadEcEx(0x1060).ToString("X2"));

        // ===== 变体 B：0x66/0x62 接口（报告 §2 记载的老路径）=====
        Console.WriteLine("== variant 0x66/0x62 ==");
        Console.WriteLine("chip id @0x2000: 0x" + ReadEcEx66(0x2000).ToString("X2"));
        byte v66 = ReadEcEx66(0x1060);
        Console.WriteLine("ver @0x1060: 0x" + v66.ToString("X2"));
        WriteEcEx66(0x1060, (byte)(v66 | 0x80));
        Console.WriteLine("ver after |=0x80: 0x" + ReadEcEx66(0x1060).ToString("X2"));
        WriteEcEx66(0xC83C, 100);
        WriteEcEx66(0xC83D, 100);
        byte b66 = ReadEcEx66(0xB20);
        WriteEcEx66(0xB20, (byte)(b66 | 2));
        Console.WriteLine("duty write 100: @0xC83C read 0x" + ReadEcEx66(0xC83C).ToString("X2") + " @0xC83D read 0x" + ReadEcEx66(0xC83D).ToString("X2") + " manual: 0x" + ReadEcEx66(0xB20).ToString("X2"));
        Thread.Sleep(2000);
        Console.WriteLine("2s later: @0xC83C read 0x" + ReadEcEx66(0xC83C).ToString("X2") + " @0xC83D read 0x" + ReadEcEx66(0xC83D).ToString("X2"));

        // 风扇占空比写读（0xC83C = 100）
        WriteEcEx(0xC83C, 100);
        WriteEcEx(0xC83D, 100);
        byte b = ReadEcEx(0xB20);
        WriteEcEx(0xB20, (byte)(b | 2));
        Console.WriteLine("after write duty 100: @0xC83C read 0x" + ReadEcEx(0xC83C).ToString("X2") + " @0xC83D read 0x" + ReadEcEx(0xC83D).ToString("X2") + " manual @0xB20: 0x" + ReadEcEx(0xB20).ToString("X2"));
        Thread.Sleep(2000);
        Console.WriteLine("2s later: @0xC83C read 0x" + ReadEcEx(0xC83C).ToString("X2") + " @0xC83D read 0x" + ReadEcEx(0xC83D).ToString("X2"));
    }

    static byte ReadEcEx(ushort addr)
    {
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(IDX, 0x2E); WritePort(DAT, 0x11);
        WritePort(IDX, 0x2F); WritePort(DAT, (byte)(addr >> 8));
        WritePort(IDX, 0x2E); WritePort(DAT, 0x10);
        WritePort(IDX, 0x2F); WritePort(DAT, (byte)(addr & 0xFF));
        WritePort(IDX, 0x2E); WritePort(DAT, 0x12);
        WritePort(IDX, 0x2F);
        return ReadPort(DAT);
    }

    static void WriteEcEx(ushort addr, byte data)
    {
        for (int i = 0; i < 100 && (ReadPort(SC) & 2) != 0; i++) { }
        WritePort(IDX, 0x2E); WritePort(DAT, 0x11);
        WritePort(IDX, 0x2F); WritePort(DAT, (byte)(addr >> 8));
        WritePort(IDX, 0x2E); WritePort(DAT, 0x10);
        WritePort(IDX, 0x2F); WritePort(DAT, (byte)(addr & 0xFF));
        WritePort(IDX, 0x2E); WritePort(DAT, 0x12);
        WritePort(IDX, 0x2F); WritePort(DAT, data);
    }
}
