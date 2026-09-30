[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Load only the verifier's path function: these checks never deploy files,
# inspect the registry, or alter drive mappings.
$verifier = Join-Path $PSScriptRoot '..\..\tools\Verify-Deployment.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile([IO.Path]::GetFullPath($verifier), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-DeploymentCanonicalPath' }, $false)
if ($null -eq $function) { throw 'Deployment canonical path function missing.' }
. ([scriptblock]::Create($function.Extent.Text))

# Model the information provided by the OS filesystem provider; no real drive
# is created and no fixture path needs to exist.
function Get-PSDrive {
    param($Name, $PSProvider, $ErrorAction)
    if ($Name -eq 'Q') { return [pscustomobject]@{ DisplayRoot = '\\foresight-fixture\library' } }
    return [pscustomobject]@{ DisplayRoot = $null }
}
function Assert-PathEquality {
    param([string]$Left, [string]$Right, [bool]$Expected, [string]$Name)
    $equal = [string]::Equals((Get-DeploymentCanonicalPath $Left), (Get-DeploymentCanonicalPath $Right), [StringComparison]::OrdinalIgnoreCase)
    if ($equal -ne $Expected) { throw "Failed: $Name" }
    Write-Output "PASS: $Name"
}
Assert-PathEquality 'Q:\Chrono Trigger\Chrono Trigger.exe' '\\foresight-fixture\library\Chrono Trigger\Chrono Trigger.exe' $true 'mapped drive and UNC alias match'
Assert-PathEquality 'q:\Chrono Trigger\Chrono Trigger.exe' '\\FORESIGHT-FIXTURE\LIBRARY\Chrono Trigger\Chrono Trigger.exe' $true 'alias comparison ignores case'
Assert-PathEquality 'Q:\Chrono Trigger\..\Chrono Trigger\Chrono Trigger.exe' '\\foresight-fixture\library\Chrono Trigger\Chrono Trigger.exe' $true 'dot segments normalize'
Assert-PathEquality 'Q:\Different Install\Chrono Trigger.exe' '\\foresight-fixture\library\Chrono Trigger\Chrono Trigger.exe' $false 'another install remains distinct'
Assert-PathEquality 'Q:\Chrono Trigger\Different.exe' '\\foresight-fixture\library\Chrono Trigger\Chrono Trigger.exe' $false 'another executable remains distinct'
Assert-PathEquality 'C:\ForesightFixture\Chrono Trigger.exe' 'C:\ForesightFixture\Chrono Trigger.exe' $true 'local path remains valid'
Write-Output 'All 6 deployment path checks passed.'
