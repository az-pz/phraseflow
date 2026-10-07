# Opens the picker through UI Automation and captures it (no keyboard input). Usage: picker-shot.ps1 -ProcessId 1234 -OutputDirectory artifacts
param(
    [Parameter(Mandatory)] [int] $ProcessId,
    [Parameter(Mandatory)] [string] $OutputDirectory
)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$capture = Join-Path $PSScriptRoot 'capture-window.ps1'

function Get-ProcessWindow([string]$name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $ProcessId)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    foreach ($i in 1..30) {
        $w = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($w) { return $w }
        Start-Sleep -Milliseconds 200
    }
    throw "Window '$name' not found"
}

$main = Get-ProcessWindow 'PhraseFlow'
$buttonCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$button = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCond) | Where-Object { $_.Current.HelpText -like 'Search all snippets*' -or $_.Current.Name -like '*Search snippets*' } | Select-Object -First 1
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

$picker = Get-ProcessWindow 'PhraseFlow search'
Start-Sleep -Milliseconds 600
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $capture -ProcessId $ProcessId -Output (Join-Path $OutputDirectory 'picker.png') -TitleContains 'PhraseFlow search'

$queryCond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'Search snippets')
$query = $picker.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $queryCond)
$query.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('meeting')
Start-Sleep -Milliseconds 600
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $capture -ProcessId $ProcessId -Output (Join-Path $OutputDirectory 'picker-search.png') -TitleContains 'PhraseFlow search'
$picker.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
