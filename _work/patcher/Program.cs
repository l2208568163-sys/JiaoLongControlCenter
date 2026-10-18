using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

// 统一补丁工具（幂等）:
//   args: <CCUWinUI.dll> <JiaoLongLogoMod.dll | none>
//   1) set_IsAppStoreVisible 方法体置空 -> 侧栏不显示"应用商店"
//   2) helper != none 时，HomePage/HomePageForUMA 构造函数注入 LogoMod.Init(this)
var dllPath = args[0];
var helperPath = args.Length > 1 ? args[1] : "none";

// 诊断模式：--dump <dll> <全类型名>  列出该类型的所有构造函数
if (dllPath == "--dump")
{
    using var dm = ModuleDefMD.Load(args[1]);
    var dt = dm.Find(args[2], false);
    if (dt is null) { Console.WriteLine("type not found: " + args[2]); return; }
    Console.WriteLine($"type {dt.FullName} ctors={dt.Methods.Count(m => m.IsConstructor)}");
    foreach (var m in dt.Methods.Where(m => m.IsConstructor))
        Console.WriteLine($"  {m.Name} params={m.Parameters.Count} isStatic={m.IsStatic} sig={m.MethodSig} body={(m.Body is null ? "null" : m.Body.Instructions.Count.ToString())}");
    return;
}

using var module = ModuleDefMD.Load(dllPath);

// ---- 补丁 1: 应用商店 ----
var setter = module.GetTypes().SelectMany(t => t.Methods)
    .FirstOrDefault(m => m.Name == "set_IsAppStoreVisible")
    ?? throw new Exception("set_IsAppStoreVisible not found");
if (setter.Body.Instructions.Count != 1)
{
    int callers = 0;
    foreach (var t in module.GetTypes())
        foreach (var m in t.Methods)
        {
            if (m.Body is null) continue;
            foreach (var ins in m.Body.Instructions)
                if (ins.Operand is IMethod im && im.Name == "set_IsAppStoreVisible")
                    callers++;
        }
    if (callers != 1) throw new Exception($"unexpected set_IsAppStoreVisible caller count: {callers}");
    setter.Body = new CilBody { Instructions = { Instruction.Create(OpCodes.Ret) } };
    setter.Body.MaxStack = 8;
    Console.WriteLine("patch1: set_IsAppStoreVisible -> ret");
}
else Console.WriteLine("patch1: already applied");

// ---- 补丁 4: 主页"自定义模式"单选永久隐藏 ----
// HomePage.GetCustomModeVisibility(bool isNvGpu) 原本按 customizeTarget(39/43)
// 或 isNvGpu 返回显隐。直接改为恒返回 Collapsed。
// 不走注册表 CustomizeTarget=39/43：那会把品牌从 MR(56) 切成 STD
// （SettingViewModel.IsMREnable = customizeTarget == 56）并影响灯效/Profile 分支。
// 注意：Visibility 枚举 Visible=0 / Collapsed=1，故 ldc.i4.1。
{
    var homePage = module.Find("WinUIGallery.Pages.HomePage", false)
        ?? throw new Exception("type not found: WinUIGallery.Pages.HomePage");
    var gcm = homePage.Methods.FirstOrDefault(m => m.Name == "GetCustomModeVisibility")
        ?? throw new Exception("GetCustomModeVisibility not found");
    if (gcm.Body.Instructions.Count == 2
        && gcm.Body.Instructions[0].OpCode == OpCodes.Ldc_I4_1
        && gcm.Body.Instructions[1].OpCode == OpCodes.Ret)
    {
        Console.WriteLine("patch4: already applied");
    }
    else
    {
        gcm.Body = new CilBody
        {
            Instructions =
            {
                Instruction.Create(OpCodes.Ldc_I4_1),
                Instruction.Create(OpCodes.Ret)
            },
            MaxStack = 8
        };
        Console.WriteLine("patch4: GetCustomModeVisibility -> Collapsed");
    }
}

// ---- 补丁 2: Logo 三连击 ----
if (helperPath != "none")
{
    using var modModule = ModuleDefMD.Load(helperPath);
    var modAsm = modModule.Assembly.ToAssemblyRef();
    var objectType = modModule.Find("JiaoLongLogoMod.LogoMod", false)
        ?? throw new Exception("LogoMod type not found in helper");

    var typeRef = new TypeRefUser(module, objectType.Namespace, objectType.Name, modAsm);
    var memberRef = new MemberRefUser(module, "Init",
        new MethodSig(CallingConvention.Default, 0, module.CorLibTypes.Void,
            new[] { module.CorLibTypes.Object }),
        typeRef);

    foreach (var typeName in new[] { "WinUIGallery.Pages.HomePage", "CCUWinUI.Pages.HomePageForUMA" })
    {
        var type = module.Find(typeName, false)
            ?? throw new Exception($"type not found: {typeName}");
        var ctor = type.Methods.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 1)
            ?? throw new Exception($"ctor not found: {typeName}");
        var body = ctor.Body;
        if (body.Instructions.Any(i => i.Operand is IMethod im && im.Name == "Init"))
        {
            Console.WriteLine($"patch2: already injected: {typeName}");
            continue;
        }
        body.SimplifyBranches();
        var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
        foreach (var ret in rets)
        {
            var idx = body.Instructions.IndexOf(ret);
            body.Instructions.Insert(idx, Instruction.Create(OpCodes.Call, memberRef));
            body.Instructions.Insert(idx, Instruction.Create(OpCodes.Ldarg_0));
        }
        Console.WriteLine($"patch2: injected {rets.Count} call(s) into {typeName}::.ctor");
    }

    // ---- 补丁 5: 设置页二维码移除（SettingsMod.Init 注入 SettingsPage 构造函数） ----
    var settingsRef = new MemberRefUser(module, "Init",
        new MethodSig(CallingConvention.Default, 0, module.CorLibTypes.Void,
            new[] { module.CorLibTypes.Object }),
        new TypeRefUser(module, "JiaoLongLogoMod", "SettingsMod", modAsm));

    {
        var type = module.Find("WinUIGallery.Pages.SettingsPage", false)
            ?? throw new Exception("type not found: WinUIGallery.Pages.SettingsPage");
        // 注意：dnlib 对实例 .ctor 的 Parameters.Count 会把 this 算进去（实测
        // SettingsPage 无参构造 => Parameters.Count==1），这里直接按方法名匹配。
        var ctor = type.Methods.FirstOrDefault(m => m.IsConstructor && m.Name == ".ctor")
            ?? throw new Exception("SettingsPage ctor(.ctor()) not found");
        var body = ctor.Body;
        if (body.Instructions.Any(i => i.Operand is IMethod im
                                       && im.Name == "Init"
                                       && im.DeclaringType is not null
                                       && im.DeclaringType.Name == "SettingsMod"))
        {
            Console.WriteLine("patch5: already injected: SettingsPage");
        }
        else
        {
            body.SimplifyBranches();
            var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            foreach (var ret in rets)
            {
                var idx = body.Instructions.IndexOf(ret);
                body.Instructions.Insert(idx, Instruction.Create(OpCodes.Call, settingsRef));
                body.Instructions.Insert(idx, Instruction.Create(OpCodes.Ldarg_0));
            }
            Console.WriteLine($"patch5: injected {rets.Count} call(s) into SettingsPage::.ctor");
        }
    }

    // ---- 补丁 3: DecideRedirection 接管（8s 超时自愈，防僵尸单实例卡死） ----
    var decideType = new TypeRefUser(module, objectType.Namespace, objectType.Name, modAsm);
    var decideRef = new MemberRefUser(module, "DecideRedirectionHook",
        new MethodSig(CallingConvention.Default, 0, module.CorLibTypes.Boolean, Array.Empty<TypeSig>()),
        decideType);
    var program = module.Find("WinUIGallery.Program", false)
        ?? throw new Exception("WinUIGallery.Program not found");
    var decide = program.Methods.FirstOrDefault(m => m.Name == "DecideRedirection")
        ?? throw new Exception("DecideRedirection not found");
    if (decide.Body.Instructions.Count == 2
        && decide.Body.Instructions[0].OpCode == OpCodes.Call
        && decide.Body.Instructions[0].Operand is IMethod im0 && im0.Name == "DecideRedirectionHook")
    {
        Console.WriteLine("patch3: already applied");
    }
    else
    {
        decide.Body = new CilBody
        {
            Instructions =
            {
                Instruction.Create(OpCodes.Call, decideRef),
                Instruction.Create(OpCodes.Ret)
            },
            MaxStack = 8
        };
        Console.WriteLine("patch3: DecideRedirection -> LogoMod.DecideRedirectionHook");
    }
}

var outTmp = dllPath + ".new";
module.Write(outTmp, new ModuleWriterOptions(module) { Logger = DummyLogger.NoThrowInstance });
module.Dispose();
File.Move(outTmp, dllPath, true);
Console.WriteLine($"written: {dllPath}");
