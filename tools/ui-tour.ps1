# Drives the PhraseFlow UI with UI Automation (no keyboard or mouse input) and captures screenshots.
# Usage: ui-tour.ps1 -ProcessId 1234 -OutputDirectory artifacts
param(
    [Parameter(Mandatory)] [int] $ProcessId,
    [Parameter(Mandatory)] [string] $OutputDirectory
)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$capture = Join-Path $PSScriptRoot 'capture-window.ps1'

function Get-Window {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $ProcessId)
    foreach ($i in 1..20) {
        $w = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($w) { return $w }
        Start-Sleep -Milliseconds 300
    }
    throw "Window not found"
}

function Find-ByName($parent, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Select-Item($element) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
}

function Set-Text($element, [string]$text) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($text)
}

function Shot([string]$name) {
    Start-Sleep -Milliseconds 700
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File $capture -ProcessId $ProcessId -Output (Join-Path $OutputDirectory "$name.png") -TitleContains 'PhraseFlow' | Out-Null
    Write-Output "captured $name"
}

$window = Get-Window
Shot 'snippets'

Set-Text (Find-ByName $window 'Filter snippets') ';hello'
Start-Sleep -Milliseconds 500
$grid = Find-ByName $window 'Snippet list'
$rowCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
$row = $grid.FindFirst([System.Windows.Automation.TreeScope]::Children, $rowCond)
if ($row) { Select-Item $row }
Shot 'editor'

Set-Text (Find-ByName $window 'Filter snippets') ''
foreach ($tab in 'Settings', 'Placeholders', 'About') {
    Select-Item (Find-ByName $window $tab)
    Shot $tab.ToLowerInvariant()
}
Select-Item (Find-ByName $window 'Snippets')

