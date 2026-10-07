# Produces the README screenshots (docs/images) from a fresh portable instance, using UI Automation only.
# Usage (from the repository root, after building Debug): powershell -ExecutionPolicy Bypass -File tools/docs-screenshots.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$root = Split-Path $PSScriptRoot
$bin = Join-Path $root 'src\PhraseFlow.App\bin\Debug\net10.0-windows'
$images = Join-Path $root 'docs\images'
$capture = Join-Path $PSScriptRoot 'capture-window.ps1'
New-Item -ItemType Directory -Force $images | Out-Null
New-Item -ItemType File -Force (Join-Path $bin 'portable.txt') | Out-Null
Remove-Item -Recurse -Force (Join-Path $bin 'Data') -ErrorAction SilentlyContinue

$app = Start-Process -FilePath (Join-Path $bin 'PhraseFlow.exe') -PassThru
function Find-Window([string]$name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $app.Id)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    foreach ($i in 1..50) {
        $w = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($w) { return $w }
        Start-Sleep -Milliseconds 200
    }
    throw "Window '$name' not found"
}
function Find-ByName($parent, [string]$name) {
    $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
}
function Shot([string]$title, [string]$file) {
    Start-Sleep -Milliseconds 800
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File $capture -ProcessId $app.Id -Output (Join-Path $images $file) -TitleContains $title | Out-Null
}

try {
    $main = Find-Window 'PhraseFlow'
    Start-Sleep -Seconds 2
    (Find-ByName $main 'Filter snippets').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue(';call')
    Start-Sleep -Milliseconds 600
    $grid = Find-ByName $main 'Snippet list'
    $row = $grid.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)))
    $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Shot 'PhraseFlow' 'main.png'

    $buttons = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    ($buttons | Where-Object { $_.Current.HelpText -like 'Search all snippets*' } | Select-Object -First 1).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $picker = Find-Window 'PhraseFlow search'
    (Find-ByName $picker 'Search snippets').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('meeting')
    Shot 'PhraseFlow search' 'picker.png'
    $picker.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}
finally {
    Start-Process -FilePath (Join-Path $bin 'PhraseFlow.exe') -ArgumentList '--exit' -Wait
    $null = $app.WaitForExit(8000)
}
Get-ChildItem $images | Select-Object Name, Length
