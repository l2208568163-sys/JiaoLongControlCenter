using System;
using System.Threading;
using OpenLibSys_Mem;

namespace Universal_x86_Tuning_Utility.Scripts.AMD_Backend;

public class Mem_Timings
{
	public bool BGS { get; set; }

	public bool BGSA { get; set; }

	public bool Preamble2T { get; set; }

	public bool GDM { get; set; }

	public bool Cmd2T { get; set; }

	public uint MEMCLK { get; set; }

	public uint tRCDWR { get; set; }

	public uint tRCDRD { get; set; }

	public uint tRAS { get; set; }

	public uint tCL { get; set; }

	public uint tRPPB { get; set; }

	public uint tRP { get; set; }

	public uint tRCPB { get; set; }

	public uint tRC { get; set; }

	public uint tRTP { get; set; }

	public uint tRRDDLR { get; set; }

	public uint tRRDL { get; set; }

	public uint tRRDS { get; set; }

	public uint tFAWDLR { get; set; }

	public uint tFAWSLR { get; set; }

	public uint tFAW { get; set; }

	public uint tWTRL { get; set; }

	public uint tWTRS { get; set; }

	public uint tCWL { get; set; }

	public uint tWR { get; set; }

	public uint tRCPage { get; set; }

	public uint tRDRDBAN { get; set; }

	public uint tRDRDSCL { get; set; }

	public uint tRDRDSCDLR { get; set; }

	public uint tRDRDSC { get; set; }

	public uint tRDRDSD { get; set; }

	public uint tRDRDDD { get; set; }

	public uint tWRWRBAN { get; set; }

	public uint tWRWRSCL { get; set; }

	public uint tWRWRSCDLR { get; set; }

	public uint tWRWRSC { get; set; }

	public uint tWRWRSD { get; set; }

	public uint tWRWRDD { get; set; }

	public uint tWRRDSCDLR { get; set; }

	public uint tRDWR { get; set; }

	public uint tWRRD { get; set; }

	public uint tREF { get; set; }

	public uint tREFCT { get; set; }

	public uint tMODPDA { get; set; }

	public uint tMRDPDA { get; set; }

	public uint tMOD { get; set; }

	public uint tMRD { get; set; }

	public uint tSTAG { get; set; }

	public uint tCKE { get; set; }

	public uint tPHYWRD { get; set; }

	public uint tPHYRDLAT { get; set; }

	public uint tPHYWRLAT { get; set; }

	public uint tRDDATA { get; set; }

	public uint tRFC4 { get; set; }

	public uint tRFC4CT { get; set; }

	public uint tRFC2 { get; set; }

	public uint tRFC2CT { get; set; }

	public uint tRFC { get; set; }

	public uint tRFCCT { get; set; }

	public uint tSTAG4LR { get; set; }

	public uint tSTAG2LR { get; set; }

	public uint tSTAGLR { get; set; }

	public uint tWRMPR { get; set; }

	public string ProcODT { get; set; }

	public string RttNom { get; set; }

	public string RttWr { get; set; }

	public string RttPark { get; set; }

	public string AddrCmdSetup { get; set; }

	public string CsOdtSetup { get; set; }

	public string CkeSetup { get; set; }

	public string ClkDrvStrength { get; set; }

	public string AddrCmdDrvStrength { get; set; }

	public string CsOdtDrvStrength { get; set; }

	public string CkeDrvStrength { get; set; }

	public static Mem_Timings RetrieveTimings()
	{
		Mem_Timings mem_Timings = new Mem_Timings();
		int num = (false ? 60 : 10);
		Ols ols = new Ols();
		if (ols.Status != Ols.OlsStatus.NO_ERROR || ols.DllStatus != Ols.OlsDllStatus.OLS_DLL_NO_ERROR)
		{
			throw new ApplicationException("Ols initialization error.");
		}
		uint eax = 0u;
		uint ebx = 0u;
		uint ecx = 0u;
		uint edx = 0u;
		ols.CpuidPx(2147483649u, ref eax, ref ebx, ref ecx, ref edx, 1u);
		uint value = ols.ReadPciConfigDword(0u, 184);
		Thread.Sleep(num);
		uint num2 = ((ReadDword(328192u, ols, num) == 768) ? 1048576u : 0u);
		uint num3 = ReadDword(327768 + num2, ols, num);
		uint num4 = ReadDword(327888 + num2, ols, num);
		uint num5 = ReadDword(328192 + num2, ols, num);
		uint num6 = ReadDword(328196 + num2, ols, num);
		uint num7 = ReadDword(328200 + num2, ols, num);
		uint num8 = ReadDword(328204 + num2, ols, num);
		uint num9 = ReadDword(328208 + num2, ols, num);
		uint num10 = ReadDword(328212 + num2, ols, num);
		uint num11 = ReadDword(328216 + num2, ols, num);
		uint num12 = ReadDword(328220 + num2, ols, num);
		uint num13 = ReadDword(328224 + num2, ols, num);
		uint num14 = ReadDword(328228 + num2, ols, num);
		uint num15 = ReadDword(328232 + num2, ols, num);
		uint num16 = ReadDword(328240 + num2, ols, num);
		uint num17 = ReadDword(328244 + num2, ols, num);
		uint num18 = ReadDword(328272 + num2, ols, num);
		uint num19 = ReadDword(328276 + num2, ols, num);
		uint num20 = ReadDword(328280 + num2, ols, num);
		uint num21 = ReadDword(328288 + num2, ols, num);
		uint num22 = ReadDword(328292 + num2, ols, num);
		uint num23 = ReadDword(328304 + num2, ols, num);
		uint num24 = ReadDword(328308 + num2, ols, num);
		uint num25 = ReadDword(328332 + num2, ols, num);
		uint num26;
		uint num27;
		if (num21 == num22)
		{
			num26 = num21;
			num27 = num23;
		}
		else if (num21 == 554041656)
		{
			num26 = num22;
			num27 = num24;
		}
		else
		{
			num26 = num21;
			num27 = num23;
		}
		mem_Timings.BGS = num3 != 2271560481u;
		mem_Timings.BGSA = num4 == 286328817;
		mem_Timings.Preamble2T = (num5 & 0x1000) >> 12 != 0;
		mem_Timings.GDM = (num5 & 0x800) >> 11 != 0;
		mem_Timings.Cmd2T = (num5 & 0x400) >> 10 != 0;
		mem_Timings.MEMCLK = num5 & 0x7F;
		float num28 = (float)mem_Timings.MEMCLK / 3f * 100f;
		mem_Timings.MEMCLK = (uint)((float)mem_Timings.MEMCLK / 3f * 200f);
		mem_Timings.tRCDWR = (num6 & 0x3F000000) >> 24;
		mem_Timings.tRCDRD = (num6 & 0x3F0000) >> 16;
		mem_Timings.tRAS = (num6 & 0x7F00) >> 8;
		mem_Timings.tCL = num6 & 0x3F;
		mem_Timings.tRPPB = (num7 & 0x3F000000) >> 24;
		mem_Timings.tRP = (num7 & 0x3F0000) >> 16;
		mem_Timings.tRCPB = (num7 & 0xFF00) >> 8;
		mem_Timings.tRC = num7 & 0xFF;
		mem_Timings.tRTP = (num8 & 0x1F000000) >> 24;
		mem_Timings.tRRDDLR = (num8 & 0x1F0000) >> 16;
		mem_Timings.tRRDL = (num8 & 0x1F00) >> 8;
		mem_Timings.tRRDS = num8 & 0x1F;
		mem_Timings.tFAWDLR = (num9 & 0x7E000000) >> 25;
		mem_Timings.tFAWSLR = (num9 & 0xFC0000) >> 18;
		mem_Timings.tFAW = num9 & 0x7F;
		mem_Timings.tWTRL = (num10 & 0x7F0000) >> 16;
		mem_Timings.tWTRS = (num10 & 0x1F00) >> 8;
		mem_Timings.tCWL = num10 & 0x3F;
		mem_Timings.tWR = num11 & 0x7F;
		mem_Timings.tRCPage = (num12 & 0xFFF00000u) >> 20;
		mem_Timings.tRDRDBAN = (num13 & 0xC0000000u) >> 30;
		mem_Timings.tRDRDSCL = (num13 & 0x3F000000) >> 24;
		mem_Timings.tRDRDSCDLR = (num13 & 0xF00000) >> 20;
		mem_Timings.tRDRDSC = (num13 & 0xF0000) >> 16;
		mem_Timings.tRDRDSD = (num13 & 0xF00) >> 8;
		mem_Timings.tRDRDDD = num13 & 0xF;
		mem_Timings.tWRWRBAN = (num14 & 0xC0000000u) >> 30;
		mem_Timings.tWRWRSCL = (num14 & 0x3F000000) >> 24;
		mem_Timings.tWRWRSCDLR = (num14 & 0xF00000) >> 20;
		mem_Timings.tWRWRSC = (num14 & 0xF0000) >> 16;
		mem_Timings.tWRWRSD = (num14 & 0xF00) >> 8;
		mem_Timings.tWRWRDD = num14 & 0xF;
		mem_Timings.tWRRDSCDLR = (num15 & 0x1F0000) >> 16;
		mem_Timings.tRDWR = (num15 & 0x1F00) >> 8;
		mem_Timings.tWRRD = num15 & 0xF;
		mem_Timings.tREF = num16 & 0xFFFF;
		mem_Timings.tREFCT = (uint)(1000f / num28 * (float)mem_Timings.tREF);
		mem_Timings.tMODPDA = (num17 & 0x3F000000) >> 24;
		mem_Timings.tMRDPDA = (num17 & 0x3F0000) >> 16;
		mem_Timings.tMOD = (num17 & 0x3F00) >> 8;
		mem_Timings.tMRD = num17 & 0x3F;
		mem_Timings.tSTAG = (num18 & 0xFF0000) >> 16;
		mem_Timings.tCKE = (num19 & 0x1F000000) >> 24;
		mem_Timings.tPHYWRD = (num20 & 0x7000000) >> 24;
		mem_Timings.tPHYRDLAT = (num20 & 0x3F0000) >> 16;
		mem_Timings.tPHYWRLAT = (num20 & 0x1F00) >> 8;
		mem_Timings.tRDDATA = num20 & 0x7F;
		mem_Timings.tRFC4 = (num26 & 0xFFC00000u) >> 22;
		mem_Timings.tRFC4CT = (uint)(1000f / num28 * (float)mem_Timings.tRFC4);
		mem_Timings.tRFC2 = (num26 & 0x3FF800) >> 11;
		mem_Timings.tRFC2CT = (uint)(1000f / num28 * (float)mem_Timings.tRFC2);
		mem_Timings.tRFC = num26 & 0x7FF;
		mem_Timings.tRFCCT = (uint)(1000f / num28 * (float)mem_Timings.tRFC);
		mem_Timings.tSTAG4LR = (num27 & 0x1FF00000) >> 20;
		mem_Timings.tSTAG2LR = (num27 & 0x7FC00) >> 10;
		mem_Timings.tSTAGLR = num27 & 0x1FF;
		mem_Timings.tWRMPR = (num25 & 0x3F000000) >> 24;
		uint eax2 = 0u;
		uint ebx2 = 0u;
		uint ecx2 = 0u;
		uint edx2 = 0u;
		ols.CpuidPx(2147483649u, ref eax2, ref ebx2, ref ecx2, ref edx2, 1u);
		eax2 &= 0xFFFF00;
		ebx2 = (ebx2 & 0xF0000000u) >> 28;
		if (ebx2 != 7)
		{
			_ = 2;
		}
		ols.WritePciConfigDword(0u, 184, 61932840u);
		ols.WritePciConfigDword(0u, 188, 2u);
		ols.WritePciConfigDword(0u, 184, value);
		Thread.Sleep(num);
		ols.Dispose();
		return mem_Timings;
	}

	private static uint ReadDword(uint value, Ols ols, int delay)
	{
		ols.WritePciConfigDword(0u, 184, value);
		Thread.Sleep(delay);
		return ols.ReadPciConfigDword(0u, 188);
	}
}
