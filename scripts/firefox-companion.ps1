#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Register','Unregister')][string]$Action
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Windows required.' }
$repo = Split-Path -Parent $PSScriptRoot
$name = 'com.branchwatch.firefox_probe'
$ExtensionId = 'branchwatch-companion@branchwatch.local'
$Browser = 'Firefox'
$vendor = 'Mozilla'
$key = "HKCU:\Software\$vendor\NativeMessagingHosts\$name"
$root = Join-Path $env:LOCALAPPDATA "BranchWatch\browser-companion\$Browser"
$manifest = Join-Path $root 'host.json'

if ($Action -eq 'Unregister') {
    if (Test-Path -LiteralPath $key) {
        $existing = (Get-Item -LiteralPath $key).GetValue('')
        if ($existing -ne $manifest) { throw 'Refusing to remove a registration owned by another installation.' }
        Remove-Item -LiteralPath $key
    }
    Write-Host "Registration removed. Remove the unpacked extension in $Browser. Local probe files remain at $root."
    return
}

if (Test-Path -LiteralPath $key) { throw 'Host already registered; unregister explicitly before replacing it.' }
$rid = switch ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()) {
    'X64' { 'win-x64' }
    'Arm64' { 'win-arm64' }
    default { throw 'Unsupported architecture.' }
}
$project = Join-Path $repo 'tools\FirefoxCompanionHost\FirefoxCompanionHost.csproj'
dotnet publish $project -c Release -r $rid --self-contained true -p:PublishSingleFile=true -o $root
if ($LASTEXITCODE -ne 0) { throw 'Native host build failed.' }
$exe = Join-Path $root 'FirefoxCompanionHost.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Native host executable not found.' }
@{
    name=$name;description='BranchWatch Firefox correlation diagnostic';path=$exe;type='stdio'
    allowed_extensions=@($ExtensionId)
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifest -Encoding utf8NoBOM
New-Item -Path $key -Force | Out-Null
Set-Item -LiteralPath $key -Value $manifest
Write-Host "Registered $Browser Companion for $ExtensionId. Open the extension popup and check connection."
