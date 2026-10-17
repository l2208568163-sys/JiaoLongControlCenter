using System.Collections.Generic;
using Universal_x86_Tuning_Utility.Scripts;

namespace RyzenSmu;

internal class Addresses
{
	public static void setAddresses()
	{
		Smu.SMU_PCI_ADDR = 0u;
		Smu.SMU_OFFSET_ADDR = 184u;
		Smu.SMU_OFFSET_DATA = 188u;
		if (Family.FAM == Family.RyzenFamily.SummitRidge || Family.FAM == Family.RyzenFamily.PinnacleRidge)
		{
			Socket_AM4_V1();
		}
		if (Family.FAM == Family.RyzenFamily.RavenRidge || Family.FAM == Family.RyzenFamily.Picasso || Family.FAM == Family.RyzenFamily.Dali || Family.FAM == Family.RyzenFamily.Pollock || Family.FAM == Family.RyzenFamily.FireFlight)
		{
			Socket_FT5_FP5_AM4();
		}
		if (Family.FAM == Family.RyzenFamily.Matisse || Family.FAM == Family.RyzenFamily.Vermeer)
		{
			Socket_AM4_V2();
		}
		if (Family.FAM == Family.RyzenFamily.Renoir || Family.FAM == Family.RyzenFamily.Lucienne || Family.FAM == Family.RyzenFamily.Cezanne_Barcelo)
		{
			Socket_FP6_AM4();
		}
		if (Family.FAM == Family.RyzenFamily.VanGogh)
		{
			Socket_FF3();
		}
		if (Family.FAM == Family.RyzenFamily.Mendocino || Family.FAM == Family.RyzenFamily.Rembrandt || Family.FAM == Family.RyzenFamily.PhoenixPoint || Family.FAM == Family.RyzenFamily.PhoenixPoint2 || Family.FAM == Family.RyzenFamily.HawkPoint || Family.FAM == Family.RyzenFamily.StrixPoint || Family.FAM == Family.RyzenFamily.KrackanPoint || Family.FAM == Family.RyzenFamily.StrixHalo)
		{
			Socket_FT6_FP7_FP8();
		}
		if (Family.FAM == Family.RyzenFamily.Raphael || Family.FAM == Family.RyzenFamily.DragonRange || Family.FAM == Family.RyzenFamily.GraniteRidge || Family.FAM == Family.RyzenFamily.FireRange)
		{
			Socket_AM5_V1();
		}
		SMUCommands.RyzenAccess.Initialize();
	}

	private static void Socket_FT5_FP5_AM4()
	{
		Smu.MP1_ADDR_MSG = 61932840u;
		Smu.MP1_ADDR_RSP = 61932900u;
		Smu.MP1_ADDR_ARG = 61933976u;
		Smu.PSMU_ADDR_MSG = 61934112u;
		Smu.PSMU_ADDR_RSP = 61934208u;
		Smu.PSMU_ADDR_ARG = 61934216u;
		SMUCommands.commands = new List<(string, bool, uint)>
		{
			("stapm-limit", true, 26u),
			("stapm-time", true, 30u),
			("fast-limit", true, 27u),
			("slow-limit", true, 28u),
			("slow-time", true, 29u),
			("tctl-temp", true, 31u),
			("cHTC-temp", false, 86u),
			("vrm-current", true, 32u),
			("vrmmax-current", true, 34u),
			("vrmsoc-current", true, 33u),
			("vrmsocmax-current", true, 35u),
			("prochot-deassertion-ramp", true, 37u),
			("pbo-scalar", false, 104u),
			("power-saving", true, 25u),
			("max-performance", true, 24u),
			("oc-clk", false, 125u),
			("per-core-oc-clk", false, 126u),
			("oc-volt", false, 127u),
			("enable-oc", false, 105u),
			("disable-oc", false, 106u),
			("max-cpuclk", true, 68u),
			("min-cpuclk", true, 69u),
			("max-gfxclk", true, 70u),
			("min-gfxclk", true, 71u),
			("max-socclk-frequency", true, 72u),
			("min-socclk-frequency", true, 73u),
			("max-fclk-frequency", true, 74u),
			("min-fclk-frequency", true, 75u),
			("max-vcn", true, 76u),
			("min-vcn", true, 77u),
			("max-lclk", true, 78u),
			("min-lclk", true, 79u)
		};
	}

	private static void Socket_FP6_AM4()
	{
		Smu.MP1_ADDR_MSG = 61932840u;
		Smu.MP1_ADDR_RSP = 61932900u;
		Smu.MP1_ADDR_ARG = 61933976u;
		Smu.PSMU_ADDR_MSG = 61934112u;
		Smu.PSMU_ADDR_RSP = 61934208u;
		Smu.PSMU_ADDR_ARG = 61934216u;
		SMUCommands.commands = new List<(string, bool, uint)>
		{
			("stapm-limit", true, 20u),
			("ppt-limit", false, 51u),
			("stapm-time", true, 24u),
			("fast-limit", true, 21u),
			("slow-limit", true, 22u),
			("slow-time", true, 23u),
			("tctl-temp", true, 25u),
			("cHTC-temp", false, 55u),
			("apu-skin-temp", true, 56u),
			("vrm-current", true, 26u),
			("vrmmax-current", true, 28u),
			("vrmsoc-current", true, 27u),
			("vrmsocmax-current", true, 29u),
			("prochot-deassertion-ramp", true, 32u),
			("gfx-clk", false, 137u),
			("dgpu-skin-temp", true, 55u),
			("power-saving", true, 18u),
			("max-performance", true, 17u),
			("pbo-scalar", false, 63u),
			("oc-clk", false, 25u),
			("oc-clk", true, 49u),
			("per-core-oc-clk", false, 26u),
			("per-core-oc-clk", true, 50u),
			("oc-volt", false, 27u),
			("oc-volt", true, 51u),
			("set-coall", true, 85u),
			("set-coall", false, 177u),
			("set-coper", true, 84u),
			("set-cogfx", true, 100u),
			("set-cogfx", false, 87u),
			("enable-oc", false, 23u),
			("enable-oc", true, 47u),
			("disable-oc", false, 24u),
			("disable-oc", true, 48u)
		};
	}

	private static void Socket_FT6_FP7_FP8()
	{
		if (Family.FAM >= Family.RyzenFamily.StrixPoint)
		{
			Smu.MP1_ADDR_MSG = 61933864u;
			Smu.MP1_ADDR_RSP = 61933944u;
			Smu.MP1_ADDR_ARG = 61933976u;
			Smu.PSMU_ADDR_MSG = 61934112u;
			Smu.PSMU_ADDR_RSP = 61934208u;
			Smu.PSMU_ADDR_ARG = 61934216u;
		}
		else
		{
			Smu.MP1_ADDR_MSG = 61932840u;
			Smu.MP1_ADDR_RSP = 61932920u;
			Smu.MP1_ADDR_ARG = 61933976u;
			Smu.PSMU_ADDR_MSG = 61934112u;
			Smu.PSMU_ADDR_RSP = 61934208u;
			Smu.PSMU_ADDR_ARG = 61934216u;
		}
		SMUCommands.commands = new List<(string, bool, uint)>
		{
			("stapm-limit", true, 20u),
			("stapm-limit", false, 49u),
			("stapm-time", true, 24u),
			("fast-limit", true, 21u),
			("fast-limit", false, 50u),
			("slow-limit", true, 22u),
			("slow-limit", false, 51u),
			("slow-time", true, 23u),
			("tctl-temp", true, 25u),
			("cHTC-temp", false, 55u),
			("apu-skin-temp", true, 51u),
			("vrm-current", true, 26u),
			("vrmmax-current", true, 28u),
			("vrmsoc-current", true, 27u),
			("vrmsocmax-current", true, 29u),
			("prochot-deassertion-ramp", true, 31u),
			("gfx-clk", false, 137u),
			("dgpu-skin-temp", true, 50u),
			("power-saving", true, 18u),
			("max-performance", true, 17u),
			("pbo-scalar", false, 62u),
			("oc-clk", false, 25u),
			("per-core-oc-clk", false, 26u),
			("set-coall", true, 76u),
			("set-coall", false, 93u),
			("set-coper", true, 75u),
			("set-cogfx", false, 183u),
			("enable-oc", false, 23u),
			("disable-oc", false, 24u)
		};
	}

	private static void Socket_FF3()
	{
		Smu.MP1_ADDR_MSG = 61932840u;
		Smu.MP1_ADDR_RSP = 61932920u;
		Smu.MP1_ADDR_ARG = 61933976u;
		Smu.PSMU_ADDR_MSG = 61934112u;
		Smu.PSMU_ADDR_RSP = 61934208u;
		Smu.PSMU_ADDR_ARG = 61934216u;
		SMUCommands.commands = new List<(string, bool, uint)>
		{
			("stapm-limit", true, 20u),
			("stapm-time", true, 24u),
			("fast-limit", true, 21u),
			("slow-limit", true, 22u),
			("slow-time", true, 23u),
			("tctl-temp", true, 25u),
			("cHTC-temp", false, 55u),
			("apu-skin-temp", true, 51u),
			("vrm-current", true, 26u),
			("vrmmax-current", true, 28u),
			("vrmsoc-current", true, 27u),
			("vrmsocmax-current", true, 29u),
			("vrmgfx-current", true, 30u),
			("vrmgfxmax-current", true, 31u),
			("prochot-deassertion-ramp", true, 34u),
			("gfx-clk", false, 137u),
			("power-saving", true, 18u),
			("max-performance", true, 17u),
			("set-coall", true, 76u),
			("set-coall", false, 93u),
			("set-coper", true, 75u),
			("set-cogfx", false, 183u)
		};
	}

	private static void Socket_AM4_V1()
	{
		Smu.MP1_ADDR_MSG = 61932840u;
		Smu.MP1_ADDR_RSP = 61932900u;
		Smu.MP1_ADDR_ARG = 61932952u;
		Smu.PSMU_ADDR_MSG = 61932828u;
		Smu.PSMU_ADDR_RSP = 61932904u;
		Smu.PSMU_ADDR_ARG = 61932944u;
		SMUCommands.commands = new List<(string, bool, uint)>
		{
			("ppt-limit", false, 100u),
			("tdc-limit", false, 101u),
			("edc-limit", false, 102u),
			("tctl-temp", false, 104u),
			("pbo-scalar", false, 106u),
			("oc-clk", false, 108u),
			("per-core-oc-clk", false, 109u),
			("oc-volt", false, 110u),
			("enable-oc", true, 35u),
			("enable-oc", false, 107u),
			("disable-oc", true, 36u)
		};
	}

	private static void Socket_AM4_V2()
	{
		Smu.MP1_ADDR_MSG = 61932848u;
		Smu.MP1_ADDR_RSP = 61932924u;
		Smu.MP1_ADDR_ARG = 61934020u;
		Smu.PSMU_ADDR_MSG = 61932836u;
		Smu.PSMU_ADDR_RSP = 61932912u;
		Smu.PSMU_ADDR_ARG = 61934144u;
		SMUCommands.commands = new List<(string, bool, uint)>
		{
			("ppt-limit", true, 61u),
			("ppt-limit", false, 83u),
			("tdc-limit", true, 59u),
			("tdc-limit", false, 84u),
			("edc-limit", true, 60u),
			("edc-limit", false, 85u),
			("tctl-temp", true, 62u),
			("tctl-temp", false, 86u),
			("pbo-scalar", false, 88u),
			("oc-clk", true, 38u),
			("oc-clk", false, 92u),
			("per-core-oc-clk", true, 39u),
			("per-core-oc-clk", false, 93u),
			("oc-volt", true, 40u),
			("oc-volt", false, 97u),
			("set-coall", true, 54u),
			("set-coall", false, 11u),
			("set-coper", true, 53u),
			("enable-oc", true, 36u),
			("enable-oc", false, 90u),
			("disable-oc", true, 37u),
			("disable-oc", false, 91u)
		};
	}

	private static void Socket_AM5_V1()
	{
		Smu.MP1_ADDR_MSG = 61932848u;
		Smu.MP1_ADDR_RSP = 61932924u;
		Smu.MP1_ADDR_ARG = 61934020u;
		Smu.PSMU_ADDR_MSG = 61932836u;
		Smu.PSMU_ADDR_RSP = 61932912u;
		Smu.PSMU_ADDR_ARG = 61934144u;
		SMUCommands.commands = new List<(string, bool, uint)>
		{
			("stapm-limit", true, 79u),
			("stapm-time", true, 83u),
			("fast-limit", true, 62u),
			("slow-limit", true, 95u),
			("slow-time", true, 96u),
			("vrm-current", true, 60u),
			("vrm-current", false, 87u),
			("ppt-limit", true, 62u),
			("ppt-limit", false, 86u),
			("tdc-limit", true, 60u),
			("tdc-limit", false, 87u),
			("edc-limit", true, 61u),
			("edc-limit", false, 88u),
			("tctl-temp", true, 63u),
			("tctl-temp", false, 89u),
			("pbo-scalar", false, 91u),
			("oc-clk", false, 95u),
			("per-core-oc-clk", false, 96u),
			("oc-volt", false, 97u),
			("set-coall", false, 7u),
			("set-coper", false, 6u),
			("enable-oc", false, 93u),
			("disable-oc", false, 94u)
		};
	}
}
