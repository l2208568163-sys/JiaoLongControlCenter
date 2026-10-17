# 蛟龙电竞控制台 · JiaoLongControlCenter

机械革命 **蛟龙 16 Pro 2023（MRID6 / R7 7745HX + RTX 4060）** 的散热、功耗、灯光控制改造工程。

本仓库是**改造工程源码与脚本**，不含厂商原始安装包、成品二进制与反编译产物（体积与版权原因，见下文「如何重建」）。

---

## 这个项目在解决什么

官方「机械革命控制中心 5.56（GX 包）」装不进 MRID6 —— 机型不匹配被拒。而原厂预装的旧版控制中心功能残缺、且与风扇策略互相打架。于是做了三层改造：

| 层 | 内容 | 目录 |
|---|---|---|
| **① 桥接服务** | 独立常驻进程，直接吃 EC / WMI / NVML / WinRing0，对外暴露 MQTT 接口。所有控制能力的唯一权威源 | `Port/src/JiaoLongCCUBridge` |
| **② 官方 UI 移植** | 把 5.56 的 WinUI 界面重打包为可安装 MSIX，并把内部 MQTT 客户端从「直连自家服务」改成「连我们的桥接」 | `_work/tools/rebuild_ui.py`、`_work/logomod` |
| **③ 第三方应用客户端化** | 旧版「蛟龙游戏控制中心」（WPF/net9）反编译后改造，控制通道同样桥接到 ①，保留其 RGB / NvTuning 直连能力 | `_work/decomp_gcc` |

设计目标：**两个界面客户端（官方移植 UI / 旧版 WPF 应用）都只是 MQTT 客户端**，控制权集中在桥接服务，从此不再互抢风扇与性能模式。

---

## 目录结构

```
Port/
├── src/JiaoLongCCUBridge/     桥接服务（C# / .NET 9，自包含发布）
│   ├── Bridge.cs              MQTT 服务端与 topic 路由（核心，~57KB）
│   ├── EcAccess.cs            EC 寄存器读写（WinRing0）
│   ├── Mifs.cs                MIFS 通道：PL1/PL2（SPL/SPPT）
│   ├── FanController.cs       风扇曲线与转速
│   ├── Nvml.cs                GPU 锁频（SetGpuLockedClocks）
│   ├── KaronOc.cs             GPU 超频
│   ├── DisplayController.cs   刷新率 / 显示切换
│   ├── DeviceToggles.cs       WiFi / 蓝牙 / 摄像头 真实开关
│   ├── KeyboardEffects.cs     键盘灯效引擎（软件实现）
│   ├── HomeControls.cs        主页卡片控制项（性能模式 / 睿频 / 强冷 / 温度墙）
│   ├── StateStore.cs          状态持久化
│   └── MqttHost.cs            内嵌 broker（127.0.0.1:13688）
├── src/TestClient/            命令行测试客户端（模拟 UI 发包 + 独立观测）
└── 移植技术报告.md             ★ 完整技术复盘（协议、根因、实测矩阵，2.4 万字）

_work/
├── tools/                     重打包与装机管线
│   ├── rebuild_ui.py          解包官方 MSIX → 打四个补丁 → 改 deps.json → makeappx → signtool
│   ├── verify_msix.py         回解包逐字节哈希比对
│   └── homemod_install.ps1    一键装机（杀 UI → 装包 → 起 UI → 验证）
├── logomod/                   UI 侧改动
│   ├── HomeMod.cs             主页卡片控制 UI 注入（~45KB）
│   ├── LogoMod.cs             开机动画 / Logo 替换
│   └── JiaoLongLogoMod.csproj
├── decomp_gcc/                第三方 WPF 应用改造（反编译 + 桥接客户端化）
│   └── gamingcontrolcenter/JiaoLongBridge.cs   桥接客户端核心
├── ec_test/                   EC 寄存器探测工具
├── patcher/                   DLL 补丁工具
└── *.ps1 / *.py               逐项实测脚本（EC、显示、电池、点击注入等）
```

---

## 硬件实测结论（MRID6 / R7 7745HX）

这份结论是逐项探测出来的，别照抄到别的机型：

| 能力 | 通道 | 状态 |
|---|---|---|
| 睿频开关 | `powercfg` boost mode | ✅ |
| CPU PL1 / PL2 | MIFS SPL / SPPT | ✅（固件不回读，按默认 85/120W） |
| CPU 降压 | WinRing0 → SMU | ✅ |
| GPU 超频 | KaronOC | ✅ |
| GPU 锁频 | NVML `SetGpuLockedClocks`（210–3105MHz，145 档） | ✅ |
| 风扇曲线 / 强冷 / 温度墙 | 固件 fn20/21/23 | ❌ 写入被忽略，接口无效 |
| 键盘灯效 | EC + 软件灯效引擎 | ✅（原厂 HID 无效，已改为软件实现） |

---

## 几个踩过的坑（值得单独看）

1. **WinUI 宿主 API 被裁剪**：官方 UI 的投影程序集删掉了大量 WinUI 类型。代码**编译通过 ≠ 运行通过**，会在 JIT 时抛 `MethodNotFoundException`。已实测出黑白名单，写在 `移植技术报告.md` §17.2 / §17.3。
2. **两条铁律**：① 引用危险 API 的代码必须放独立小方法并用 try/catch 包住调用点（JIT 失败发生在调用边界，不拖垮宿主方法）；② 富控件与降级路径必须分属不同方法。
3. **`JsonElement.TryGetProperty` / `Enumerable.Min/Max` 都在裁剪黑名单里** —— 常用 BCL API 也可能没被投影，写代码前先反编译 grep 官方代码是否引用过。
4. **MQTT 库选型**：只能用 `M2Mqtt`，`MQTTnet` 依赖 `ExceptionDispatchInfo`，被投影裁剪，必挂。

---

## 如何重建（不含厂商二进制的部分）

本仓库刻意**不入库**以下内容，需要自备：

- 官方「机械革命控制中心 5.56 GX 安装包」→ 解包得到 `CCU.WinUI.msix` 与 `AiStoneService` 等
- 旧版「蛟龙游戏控制中心 7.3」安装目录（`JiaoLong7.3/`）
- 反编译产物（`_work/decomp_ui`、`decomp_svc`、`decomp_bridge` 等）：
  ```bash
  ilspycmd -p -o _work/decomp_ui  <解包目录>/CCUWinUI.dll
  ilspycmd -p -o _work/decomp_svc <解包目录>/GCUService.exe
  ```

重建桥接服务：

```bash
dotnet publish Port/src/JiaoLongCCUBridge -c Release -r win-x64 --self-contained true
```

重打包官方 UI：

```bash
python _work/tools/rebuild_ui.py     # 需要 makeappx / signtool（Windows SDK）
python _work/tools/verify_msix.py    # 校验
```

---

## 免责声明

本项目为**个人设备上的私有改造**，与机械革命 / 同方（UNIWILL）无任何关联。
仓库中的反编译代码仅用于互操作研究与自有设备修复，版权归原厂所有。
涉及 EC 写入、SMU 降压、GPU 超频、驱动加载（WinRing0）等**高风险操作**，盲目套用到其他机型可能损坏硬件。自担风险。

原厂二进制、安装包、成品 exe/dll/msix **均不在本仓库内**。
