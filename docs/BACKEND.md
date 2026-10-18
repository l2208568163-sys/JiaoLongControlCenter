# 后端源码说明 · BACKEND

> **本文档用途**：说明 `JiaoLongCCUBridge` 后端的模块划分、每个源文件的职责、MQTT 协议约定，
> 以及如何从源码完整构建并运行。**这是纯工程文档，不包含任何对话记录、聊天日志或过程导出内容。**
>
> 其他文档分工：
> - 根目录 [`README.md`](../../README.md) —— 项目总览、安装使用
> - [`Port/移植技术报告.md`](../移植技术报告.md) —— 完整技术复盘（协议还原过程、逐项根因分析、实测矩阵）
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

共 17 个文件 / 4050 行。

| 文件 | 行数 | 职责 |
|---|---:|---|
| `Bridge.cs` | 1377 | **核心**。MQTT 服务端与全部 topic 路由、请求分发、状态广播。所有硬件能力在此汇总成协议 |
| `EcAccess.cs` | 335 | EC（嵌入式控制器）寄存器读写，经 WinRing0 走 I/O 端口 `0x60/0x64`，含互斥与超时保护 |
| `HomeControls.cs` | 320 | 主页卡片控制项的后端：性能模式、睿频开关、强冷、温度墙、风扇自动策略 |
| `DeviceToggles.cs` | 218 | WiFi / 蓝牙 / 摄像头真实开关（设备禁用 + 状态回读） |
| `DisplayController.cs` | 215 | 刷新率枚举与切换、显示模式切换、ICC 校色关联 |
| `UserSessionRunner.cs` | 201 | 在**用户会话**里执行需要交互的操作（服务在 Session 0，无法直接弹窗/操作显示器） |
| `Program.cs` | 200 | 入口。命令行开关（`--console` / `--ecscan` / `--devtest`）、Windows 服务宿主、DI 装配 |
| `Nvml.cs` | 175 | NVML P/Invoke：GPU 温度/占用/频率/显存、锁频 `SetGpuLockedClocks` |
| `Mifs.cs` | 169 | 固件 MIFS WMI 通道（`root\WMI\MICommonInterface`）：SPL/SPPT、模式、风扇锁速、Fn 键、触摸板、独显直连 |
| `WinPower.cs` | 168 | 电源相关：睿频（`powercfg` boost mode）、电源方案、电池状态 |
| `StateStore.cs` | 161 | 状态持久化（JSON），服务重启后恢复用户设置 |
| `KeyboardEffects.cs` | 129 | 软件灯效引擎（固件只支持有限灯效，复杂效果在软件侧渲染后下发） |
| `FanController.cs` | 103 | 风扇曲线计算与占空比下发，曲线环 / 固件锁速两种模式切换与交还 |
| `RegistrySetup.cs` | 91 | 写入 `HKLM\SOFTWARE\OEM\GamingCenter2` 能力开关与品牌配置（官方 UI 读取这些键决定显示哪些页面） |
| `MqttHost.cs` | 77 | 内嵌 MQTT broker（`127.0.0.1:13688`），供 UI 与第三方客户端连接 |
| `KaronOc.cs` | 67 | KaronOC 内核驱动接口：NVIDIA P0 状态核心/显存偏移 |
| `JiaoLongCCUBridge.csproj` | 44 | 工程文件。原生依赖（KaronOC / WinRing0）按「仓库内 payload → 本地 JiaoLong7.3」顺序查找 |

### 硬件通道选型原则

| 通道 | 能力 | 安全性 |
|---|---|---|
| **MIFS WMI**（首选） | 性能模式、风扇锁速、温度、键盘灯、Fn 锁、触摸板、独显直连 | 固件级，最安全 |
| **WinRing0** | EC 独立风扇曲线直写、SMU 全核降压 | 需要内核驱动，仅这两项使用 |
| **KaronOC** | GPU 超频（P0 偏移） | 需要内核驱动 |
| **NVML** | GPU 监控、锁频 | 官方 SDK，只读为主 |
| **Windows API** | 刷新率、ICC 校色、设备开关、电源方案 | 系统级，无风险 |

> 服务停止或切换模式时会**自动交还固件风扇控制**，不会把风扇留在手动状态。

---

## 3. MQTT 协议约定

- 地址：`127.0.0.1:13688`
- 客户端凭据：`UWPClient_User_5` / `UWPClient_Pwd888881772688_5`（沿用官方 UI 内置值，仅本机回环）
- Topic 命名、请求/响应结构、字段含义见 [`Port/移植技术报告.md`](../移植技术报告.md) §3「官方 UI 协议」与 §16「主页控制项协议契约」。

统一约定：

| 方向 | Topic 形态 | 说明 |
|---|---|---|
| UI → 服务 | `.../cmd/...` | 请求，带 `id` 用于配对 |
| 服务 → UI | `.../ack/...` | 单次请求应答 |
| 服务 → 全部 | `.../status/...` | 周期遥测广播（CPU/GPU/风扇/模式） |

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

日志：`C:\ProgramData\JiaoLongCCUBridge\bridge_YYYYMMDD.log`

### 协议自测

```bash
dotnet run --project Port/src/TestClient -- hometest
```

`TestClient` 会模拟 UI 发送 MQTT 请求，并在桥接日志里核对实际硬件写入结果。

---

## 5. 不随仓库分发的部分

以下内容因体积或版权原因未入库，需要自备：

| 缺失内容 | 获取方式 |
|---|---|
| 官方安装包 `ControlCenter_5.56.60.34_Mechrevo_GX.exe` | 厂商官网 |
| 官方二进制反编译产物 `_work/decomp_ui`、`decomp_svc`、`decomp_bridge`、`decomp_old` | `ilspycmd -p -o <输出目录> <目标 dll/exe>` |
| 旧版「蛟龙游戏控制中心」安装目录 `JiaoLong7.3/` | 原厂预装 |

> 注：`Port/dist/JiaoLongCCU-Port-v1.0/` 里的 `KaronOC.dll`、`WinRing0x64.dll/.sys` 已入库，
> 因此仅构建后端**不需要**原厂目录。
