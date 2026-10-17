using dnlib.DotNet;
using dnlib.DotNet.Emit;

// 快速体检：确认两项补丁同时存在于 CCUWinUI.dll
public static class Check
{
    public static void Main(string[] args)
    {
        using var module = ModuleDefMD.Load(args[0]);

        var setter = module.GetTypes().SelectMany(t => t.Methods)
            .First(m => m.Name == "set_IsAppStoreVisible");
        Console.WriteLine($"appstore patch: instructions={setter.Body.Instructions.Count}, first={setter.Body.Instructions[0].OpCode}");

        foreach (var typeName in new[] { "WinUIGallery.Pages.HomePage", "CCUWinUI.Pages.HomePageForUMA" })
        {
            var t = module.Find(typeName, false);
            var ctor = t.Methods.First(m => m.IsConstructor && m.Parameters.Count == 1);
            var calls = ctor.Body.Instructions.Count(i =>
                i.OpCode == OpCodes.Call && i.Operand is IMethod im && im.Name == "Init");
            Console.WriteLine($"logomod patch {typeName}: Init calls = {calls}");
        }

        var asmRefs = module.GetAssemblyRefs().Select(a => a.Name).ToList();
        Console.WriteLine($"JiaoLongLogoMod ref present: {asmRefs.Contains("JiaoLongLogoMod")}");
    }
}
