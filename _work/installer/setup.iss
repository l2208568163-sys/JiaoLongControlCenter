; ============================================================



;  蛟龙电竞控制台（移植版）— 一键安装包主脚本



;  编译：ISCC.exe setup.iss



;  产物：<仓库根>\Port\dist\JiaoLongCCU-Setup-1.1.0.exe



;  说明：所有路径均相对本脚本目录，克隆到任意位置都能直接编译。



;        中文界面需要 Languages\ChineseSimplified.isl（已随仓库附带）。



; ============================================================



#define MyAppName "蛟龙电竞控制台"



#define MyAppVersion "1.1.0"



#define Payload "..\..\Port\dist\JiaoLongCCU-Port-v1.0\payload"



#define SrcDir "."







[Setup]



AppId={{05FDD432-A229-480D-90AD-4B99F7DBC814}



AppName={#MyAppName}



AppVersion={#MyAppVersion}



AppVerName={#MyAppName} {#MyAppVersion}（移植版）



AppPublisher=JiaoLongControlCenter Project



AppPublisherURL=https://github.com/l2208568163-sys/JiaoLongControlCenter



DefaultDirName={autopf}\JiaoLongCCU



DefaultGroupName={#MyAppName}



PrivilegesRequired=admin



DisableProgramGroupPage=no



OutputDir=..\..\Port\dist



OutputBaseFilename=JiaoLongCCU-Setup-{#MyAppVersion}



SetupIconFile={#Payload}\gamingcenter_STD.ico



Compression=lzma2/max



SolidCompression=yes



WizardStyle=modern



MinVersion=10.0.22621



ArchitecturesAllowed=x64



LicenseFile={#SrcDir}\license.txt



UninstallDisplayName={#MyAppName}（移植版）



UninstallDisplayIcon={app}\ui\gamingcenter_STD.ico



CloseApplications=no







[Languages]



#if FileExists("Languages\ChineseSimplified.isl")



Name: "chs"; MessagesFile: "Languages\ChineseSimplified.isl"



#else



Name: "english"; MessagesFile: "compiler:Default.isl"



#endif







[Messages]



WelcomeLabel2=本向导将把「[name/ver]」安装到您的电脑。%n%n安装内容：桥接服务 JiaoLongCCUBridge（风扇/功耗/灯效后端） + 机械革命控制中心 5.56 官方 UI 移植版。%n%n%n安装前请先关闭其他风扇/性能控制软件（原厂控制中心、第三方「蛟龙游戏控制中心」等）——两个程序同时控制风扇会互相冲突。



SelectComponentsLabel2=选择要安装的组件。桥接服务为必需组件；控制中心 UI 为官方 5.56 移植版（推荐安装）。







[Types]



Name: "full"; Description: "完整安装（推荐）"



Name: "compact"; Description: "仅桥接服务（自备 UI）"



Name: "custom"; Description: "自定义"; Flags: iscustom







[Components]



Name: "bridge"; Description: "桥接服务 JiaoLongCCUBridge（必需：风扇/功耗墙/降压/灯效后端）"; Types: full compact custom; Flags: fixed



Name: "ui"; Description: "机械革命控制中心 UI（官方 5.56 移植版，MSIX 应用）"; Types: full custom







[Files]



; ---- 桥接服务（{app}\bridge） ----



Source: "{#Payload}\JiaoLongCCUBridge.exe"; DestDir: "{app}\bridge"; Components: bridge; Flags: ignoreversion



Source: "{#Payload}\KaronOC.dll"; DestDir: "{app}\bridge"; Components: bridge; Flags: ignoreversion



Source: "{#Payload}\WinRing0x64.dll"; DestDir: "{app}\bridge"; Components: bridge; Flags: ignoreversion



Source: "{#Payload}\WinRing0x64.sys"; DestDir: "{app}\bridge"; Components: bridge; Flags: ignoreversion



Source: "{#Payload}\display_helper.py"; DestDir: "{app}\bridge"; Components: bridge; Flags: ignoreversion



; ---- UI 负载（{app}\ui） ----



Source: "{#Payload}\CCU.WinUI.msix"; DestDir: "{app}\ui"; Components: ui; Flags: ignoreversion



Source: "{#Payload}\Microsoft.WindowsAppRuntime.1.7.msix"; DestDir: "{app}\ui"; Components: ui; Flags: ignoreversion



Source: "{#Payload}\JiaoLongCCU-Port.cer"; DestDir: "{app}\ui"; Components: ui; Flags: ignoreversion



Source: "{#Payload}\gamingcenter_STD.ico"; DestDir: "{app}\ui"; Components: ui; Flags: ignoreversion



; ---- 辅助脚本与文档（{app}） ----



Source: "{#SrcDir}\install_service.ps1"; DestDir: "{app}"; Flags: ignoreversion



Source: "{#SrcDir}\install_ui.ps1"; DestDir: "{app}"; Flags: ignoreversion



Source: "{#SrcDir}\uninstall_bridge.ps1"; DestDir: "{app}"; Flags: ignoreversion



Source: "{#SrcDir}\uninstall_ui.ps1"; DestDir: "{app}"; Flags: ignoreversion



Source: "{#SrcDir}\使用说明.md"; DestDir: "{app}"; Flags: ignoreversion







[Registry]



; GamingCenter2 能力开关（UI 启动时读取；卸载时整键删除）



Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueType: dword; ValueName: "AppType"; ValueData: "1"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueType: string; ValueName: "APPTarget"; ValueData: "Customize"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueType: string; ValueName: "Language"; ValueData: "zh-cn"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueType: dword; ValueName: "CustomizeTarget"; ValueData: "56"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueType: string; ValueName: "AppName"; ValueData: "机械革命控制中心"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2"; ValueType: string; ValueName: "LanguageList"; ValueData: "en-us,zh-cn"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: dword; ValueName: "ServiceLoadingReady"; ValueData: "1"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: string; ValueName: "BIOS_PROJECT_ID"; ValueData: "ID6"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: dword; ValueName: "IsNvGpu"; ValueData: "1"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: dword; ValueName: "RamFan1p5Support"; ValueData: "1"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: dword; ValueName: "FanSettingsSupport"; ValueData: "1"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: dword; ValueName: "TurboModeSupport"; ValueData: "1"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: dword; ValueName: "OcSettingsSupport"; ValueData: "1"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: dword; ValueName: "CPUPerformanceAndOverClockMenuSupport"; ValueData: "1"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: dword; ValueName: "KeyboardType"; ValueData: "1"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: dword; ValueName: "DGpuDirectConnectionSupport"; ValueData: "1"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: dword; ValueName: "DispalyRefresh"; ValueData: "2"

Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; ValueType: dword; ValueName: "ColorCalibrationSupport"; ValueData: "1"

; 卸载时删除整键



Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2"; Flags: uninsdeletekey dontcreatekey



Root: HKA64; Subkey: "SOFTWARE\OEM\GamingCenter2\ItemSupport"; Flags: uninsdeletekey dontcreatekey







[Icons]



Name: "{group}\使用说明"; Filename: "{app}\使用说明.md"; Components: bridge



Name: "{group}\卸载蛟龙电竞控制台"; Filename: "{uninstallexe}"







[Run]



Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \



  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install_service.ps1"" -Root ""{app}"""; \



  Components: bridge; Flags: runhidden; StatusMsg: "正在注册桥接服务…"



Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \



  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install_ui.ps1"" -Root ""{app}"""; \



  Components: ui; Flags: runhidden; StatusMsg: "正在安装控制中心 UI（MSIX）…"



Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \



  Parameters: "-NoProfile -WindowStyle Hidden -Command ""explorer.exe ('shell:AppsFolder\' + (Get-AppxPackage CCU.WinUI).PackageFamilyName + '!App')"""; \



  Description: "立即启动 机械革命控制中心"; Components: ui; Flags: postinstall nowait skipifsilent runhidden







[UninstallRun]



Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \



  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall_ui.ps1"""; Flags: runhidden



Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \



  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall_bridge.ps1"""; Flags: runhidden







[Code]



function PrepareToInstall(var NeedsRestart: Boolean): String;



var



  RC: Integer;



begin



  Result := '';



  WizardForm.StatusLabel.Caption := '正在停止现有的桥接服务与控制中心进程…';



  Exec(ExpandConstant('{sys}\net.exe'), 'stop JiaoLongCCUBridge', '', SW_HIDE, ewWaitUntilTerminated, RC);



  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),



       '-NoProfile -Command ""Get-Process JiaoLongCCUBridge,CCUWinUI -ErrorAction SilentlyContinue | Stop-Process -Force""',



       '', SW_HIDE, ewWaitUntilTerminated, RC);



  Sleep(2000);



end;



