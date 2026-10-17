Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# 找“选择 Logo 图片”对话框（顶层窗口）
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "选择 Logo 图片")
$dlg = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $dlg) { Write-Host "NO-DIALOG"; exit 1 }
Write-Host "dialog found"

# 文件名输入框：ComboBox 里的 Edit（Office/经典对话框都有）
$editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$edits = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)
$edit = $null
foreach ($e in $edits) { $edit = $e; break }   # 第一个 Edit = 文件名框
if (-not $edit) { Write-Host "NO-EDIT"; exit 2 }
$value = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$value.SetValue("F:\JiaoLongControlCenter\_work\screen.png")
Write-Host "path set"

Start-Sleep -Milliseconds 500

# 点“打开”(Open) 按钮
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "打开")
$btn = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
if (-not $btn) {
    $btnCond2 = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Open")
    $btn = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond2)
}
if (-not $btn) { Write-Host "NO-OPEN-BUTTON"; exit 3 }
$inv = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$inv.Invoke()
Write-Host "open clicked"
