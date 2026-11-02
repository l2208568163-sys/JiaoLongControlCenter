# 后端源码说明 · BACKEND

> **本文档用途**：说明 `JiaoLongCCUBridge` 后端的模块划分、每个源文件的职责、MQTT 协议约定，
> 以及如何从源码完整构建并运行。**这是纯工程文档，不包含任何对话记录、聊天日志或过程导出内容。**
>
> 其他文档分工：
> - 根目录 [`README.md`](../README.md) —— 项目总览、安装使用
> - [`Port/移植技术报告.md`](../Port/移植技术报告.md) —— 完整技术复盘（协议还原过程、逐项根因分析、实测矩阵）
> - 本文档 —— 后端源码**结构与构建**层面的索引

---

## 1. 模块总览

后端由 4 个可独立构建的工程组成，全部不含 UI 代码：

| 工程 | 目标框架 | 输出 | 作用 |
|---|---|---|---|
| `Port/src/JiaoLongCCUBridge` | net10.0-windows | Windows 服务（自包含单文件） | **核心后端**：硬件控制 + MQTT 服务端 |
| `Port/src/TestClient` | net10.0 | 控制台程序 | 协议测试客户端，模拟 UI 发包并独立观测结果 |
| `_work/ec_test` | net10.0 | 控制台程序 | EC 寄存器探测工具（读/写/扫描） |
| `_work/patcher` | net10.0 | 控制台程序 | 基于 dnlib 的 DLL 补丁工具（UI 重打包用） |

生成 MSIX 与装机脚本属于构建管线，不是后端运行时：

| 位置 | 作用 |
|---|---|
| `_work/tools/rebuild_ui.py` | 解包官方包 → 打补丁 → 改 deps.json → makeappx → signtool |
| `_work/tools/verify_msix.py` | 回解包逐字节哈希比对 |
| `_work/tools/homemod_install.ps1` | 一键装机（杀 UI → 装包 → 起 UI → 验证） |

---

## 2. `JiaoLongCCUBridge` 源文件职责

共 20 个 .cs / 6035 行。

| 文件 | 行数 | 职责 |
|---|---:|---|
| `Bridge.cs` | 1729 | **核心**。MQTT 服务端与全部 topic 路由、请求分发、状态广播、系统选项动作分发。所有硬件能力在此汇总成协议 |
| `HomeControls.cs` | 392 | 主页卡片控制项的后端：性能模式、睿频开关、强冷、温度墙、风扇自动策略、频率限制 |
| `EcAccess.cs` | 377 | EC（嵌入式控制器）寄存器读写，经 WinRing0 走 I/O 端口 `0x60/0x64`，含互斥与超时保护 |
| `BatteryIo.cs` | 347 | 电池类驱动直读：`SetupDiGetClassDevs(GUID_DEVCLASS_BATTERY)` → `CreateFile` → `IOCTL_BATTERY_QUERY_TAG/_INFORMATION/_STATUS`，拿到 WMI 拿不到的设计容量 / 循环次数 / 带符号速率 |
| `WinkeyHook.cs` | 327 | Win 键锁定：`WH_KEYBOARD_LL` 低级键盘钩子（注册表 `NoWinKeys` 挡不住单独按 Win）。以普通用户身份跑在用户会话，经 Public 交换目录与桥接通信 |
| `Program.cs` | 328 | 入口。命令行开关（`--console` / `--ecscan` / `--devtest` / `--battest` / `--powertest` / `--send` / `--winkey-hook`）、Windows 服务宿主、DI 装配 |
| `WinPower.cs` | 318 | 电源相关：睿频（`powercfg` boost mode）、电源方案与 overlay（`SetActiveOverlayScheme`）、深度睡眠映射 `HIBERNATEIDLE`、电池状态 |
| `UserSessionRunner.cs` | 301 | 在**用户会话**里执行需要交互的操作（服务在 Session 0，无法直接弹窗 / 操作显示器）。`CreateProcessAsUserW` 的 `lpDesktop` 必须留空 |
| `KeyboardEffects.cs` | 296 | 软件灯效引擎：呼吸 / 波浪 / 彩虹 / 混色 / 闪烁 / 单色，参数对象原地更新，25–40 fps 帧动画下发 |
| `DeviceToggles.cs` | 279 | WiFi / 蓝牙 / 摄像头真实开关（`CM_Disable_DevNode` 禁用 PnP 设备 + 状态回读，失败带 cfgMgr32 错误码） |
| `DisplayController.cs` | 215 | 刷新率枚举与切换、显示模式切换、ICC 校色关联 |
| `StateStore.cs` | 204 | 状态持久化（`state.json`，原子写 + 串行化，损坏时备份为 `.corrupt`），服务重启后恢复用户设置 |
| `Nvml.cs` | 175 | NVML P/Invoke：GPU 温度/占用/频率/显存、锁频 `SetGpuLockedClocks` |
| `Mifs.cs` | 169 | 固件 MIFS WMI 通道（`root\WMI\MICommonInterface`）：SPL/SPPT、模式、风扇锁速、Fn 键、触摸板、独显直连、键盘灯 fn16/17/18 |
| `SettingsGuard.cs` | 157 | 机型能力位（`ItemSupport`）与 `ActionResult` 封装：服务端据此决定「接受不接受」，与 UI 的「显示与否」保持一致 |
| `RegistrySetup.cs` | 107 | 写入 `HKLM\SOFTWARE\OEM\GamingCenter2` 能力开关与品牌配置（官方 UI 读取这些键决定显示哪些页面） |
| `MqttHost.cs` | 77 | 内嵌 MQTT broker（`127.0.0.1:13688`），供 UI 与第三方客户端连接 |
| `KaronOc.cs` | 67 | KaronOC 内核驱动接口：NVIDIA P0 状态核心/显存偏移 |
| `SettingsRestore.cs` | 33 | 系统选项开机恢复（Win 键钩子、OSD 注册表值），与主页控制项恢复并列，在 MQTT 起来之前调用 |
| `JiaoLongCCUBridge.csproj` | 44 | 工程文件。原生依赖（KaronOC / WinRing0）按「仓库内 payload → 本地 JiaoLong7.3」顺序查找 |

### 硬件通道选型原则

| 通道 | 能力 | 安全性 |
|---|---|---|
| **MIFS WMI**（首选） | 性能模式、风扇锁速、温度、键盘灯、Fn 锁、触摸板、独显直连 | 固件级，最安全 |
| **WinRing0** | EC 独立风扇曲线直写、SMU 全核降压 | 需要内核驱动，仅这两项使用 |
| **KaronOC** | GPU 超频（P0 偏移） | 需要内核驱动 |
| **NVML** | GPU 监控、锁频 | 官方 SDK，只读为主 |
| **电池类驱动 IOCTL** | 设计容量 / 循环次数 / 满充容量 / 带符号速率 | 官方电池类驱动 |
| **Windows API** | 刷新率、ICC 校色、设备开关、电源计划、休眠超时、Win 键钩子 | 系统级，无风险 |

> 服务停止或切换模式时会**自动交还固件风扇控制**，不会把风扇留在手动状态。

---

## 3. MQTT 协议约定

- 地址：`127.0.0.1:13688`
- 客户端凭据：`UWPClient_User_5` / `UWPClient_Pwd888881772688_5`（沿用官方 UI 内置值，仅本机回环）
- Topic 命名、请求/响应结构、字段含义见 [`Port/移植技术报告.md`](../Port/移植技术报告.md) §3「官方 UI 协议」与 §16「主页控制项协议契约」。

主要 topic（以 `Bridge.HandleMessage` 的实际分支为准）：

| 方向 | Topic | 说明 |
|---|---|---|
| UI → 服务 | `Setting/Control` | 系统选项 / 电源 / 显示 / 键盘等设置动作，payload 形如 `{"Action":"DEEPSLEEP_ON","Secs":900}` |
| UI → 服务 | `Keyboard/Ctrl` | 键盘灯效控制（效果、速度、亮度、方向、调色板） |
| UI → 服务 | `BatteryProtection/Control` | 电池模式（`{"Action":"HEALTHYMODE"}` / `{"Report":"GET"}`） |
| UI → 服务 | `Home/*` | 主页卡片控制项（性能模式、睿频、强冷、温度墙） |
| 服务 → UI | `Setting/Status` | 设置项状态回发（带 1 秒防回声窗口，抑制 UI 状态回环） |
| 服务 → UI | `Keyboard/Status` | 键盘状态回发 |
| 服务 → UI | `System/BatteryProtection` | 电池模式回发（含 `HealthProtectionStatus`） |
| 服务 → UI | `JiaoLong/HomeStatus` | 主页状态回发 |

新增能力时：先在 `Bridge.cs` 注册 topic 处理，再在 `HomeControls.cs` 或对应能力类里实现后端，最后用 `TestClient` 跑一遍验证。

---

## 4. 构建与运行

### 环境

- .NET 10 SDK
- Windows 10 19041+ / Windows 11
- 管理员权限（加载 WinRing0 驱动、写 HKLM 注册表）

### 构建

```bash
# 调试构建（输出带依赖文件，便于看日志）
dotnet build Port/src/JiaoLongCCUBridge -c Debug

# 发布为自包含单文件
dotnet publish Port/src/JiaoLongCCUBridge -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true
```

原生依赖查找顺序写在 `.csproj` 里：优先用仓库自带的
`Port/dist/JiaoLongCCU-Port-v1.0/payload/`，找不到才回落到本机 `JiaoLong7.3/`。
两者都没有时会给出 Warning，程序仍能编译，但 GPU 超频与 EC 直写功能不可用。

### 运行

```bash
# 前台带控制台输出（调试用）
JiaoLongCCUBridge.exe --console

# 注册为 Windows 服务（管理员）
sc create JiaoLongCCUBridge binPath= "C:\Program Files\JiaoLongCCUBridge\JiaoLongCCUBridge.exe" start= auto
sc failure JiaoLongCCUBridge reset= 86400 actions= restart/5000/restart/5000/restart/10000
sc start JiaoLongCCUBridge
```

其他命令行开关：

| 开关 | 作用 |
|---|---|
| `--console` | 前台运行并输出日志到控制台 |
| `--ecscan` | 扫描 EC 寄存器区间 |
| `--devtest` | 设备控制自检 |
| `--battest` | 只读自检：电池信息（设计容量/循环/健康）、电源计划、休眠超时 |
| `--powertest on\|off` | 切高性能 / 平衡电源计划 + 对应 overlay（**会改系统**） |
| `--powertest deep <秒>` | 写 `HIBERNATEIDLE`（0 = 从不，**会改系统**） |
| `--send <topic> <json>` | 模拟 UI 下发，走完整业务路径 |
| `--winkey-hook` | 以用户身份启动 Win 键钩子进程（服务一般会自动拉起） |

日志：`C:\ProgramData\JiaoLongCCUBridge\bridge_YYYYMMDD.log`
状态：`C:\ProgramData\JiaoLongCCUBridge\state.json`
与用户会话进程交换目录：`C:\Users\Public\JiaoLongCCUBridge\`

### 协议自测

```bash
dotnet run --project Port/src/TestClient -- hometest       # 主页控制项
dotnet run --project Port/src/TestClient -- batterytest    # 电池页（前半）
dotnet run --project Port/src/TestClient -- batterytest2   # 电池页（后半：还原状态）
dotnet run --project Port/src/TestClient -- send <topic> <json>
```

`TestClient` 会模拟 UI 发送 MQTT 请求，并在桥接日志里核对实际硬件写入结果。

---

## 5. 不随仓库分发的部分

以下内容因体积或版权原因未入库，需要自备：

| 缺失内容 | 获取方式 |
|---|---|
| 官方安装包 `ControlCenter_5.56.60.34_Mechrevo_GX.exe` | 厂商官网 |
| 官方二进制反编译产物 `_work/decomp_ui`、`decomp_svc`、`decomp_svc_full`、`decomp_osdtp`、`decomp_bridge`、`decomp_old` | `ilspycmd -p -o <输出目录> <目标 dll/exe>` |
| 旧版「蛟龙游戏控制中心」安装目录 `JiaoLong7.3/` | 原厂预装 |

> 注：`Port/dist/JiaoLongCCU-Port-v1.0/` 里的 `KaronOC.dll`、`WinRing0x64.dll/.sys` 已入库，
> 因此仅构建后端**不需要**原厂目录。
