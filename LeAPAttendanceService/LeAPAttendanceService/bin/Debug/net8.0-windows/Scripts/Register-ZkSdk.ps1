param(
    [string]$SdkPath = (Join-Path $PSScriptRoot "..\\Sdk\\TSDK")
)

$ErrorActionPreference = "Stop"

# Re-launch the whole script with elevation so both the DLL copy and COM registration can succeed.
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).
    IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    $escapedPath = '"' + $PSCommandPath + '"'
    $escapedSdkPath = '"' + $SdkPath + '"'
    Start-Process -FilePath "powershell.exe" -ArgumentList "-ExecutionPolicy Bypass -File $escapedPath -SdkPath $escapedSdkPath" -Verb RunAs -Wait
    exit $LASTEXITCODE
}

Write-Host "Using SDK folder: $SdkPath"

if (-not (Test-Path $SdkPath)) {
    throw "SDK folder was not found. Expected path: $SdkPath"
}

# ZKTeco's older SDK is usually 32-bit, so the DLLs are commonly installed into SysWOW64.
$targetPath = "C:\\Windows\\SysWOW64"

Write-Host "Copying DLL files to $targetPath ..."
Copy-Item (Join-Path $SdkPath "*.dll") $targetPath -Force

$zkemkeeperPath = Join-Path $targetPath "zkemkeeper.dll"
if (-not (Test-Path $zkemkeeperPath)) {
    throw "zkemkeeper.dll was not copied successfully."
}

Write-Host "Registering zkemkeeper.dll ..."
Start-Process -FilePath "regsvr32.exe" -ArgumentList "/s `"$zkemkeeperPath`"" -Wait

Write-Host "ZKTeco SDK registration completed."
