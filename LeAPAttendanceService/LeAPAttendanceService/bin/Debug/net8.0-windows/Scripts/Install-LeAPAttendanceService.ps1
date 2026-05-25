param(
    [string]$ServiceName = "LeAPAttendanceService",
    [string]$PublishFolder = (Join-Path $PSScriptRoot "..\\publish"),
    [string]$ExeName = "LeAPAttendanceService.exe"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).
    IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    $escapedPath = '\"' + $PSCommandPath + '\"'
    $escapedPublishFolder = '\"' + $PublishFolder + '\"'
    $escapedExeName = '\"' + $ExeName + '\"'
    $escapedServiceName = '\"' + $ServiceName + '\"'
    Start-Process -FilePath "powershell.exe" -ArgumentList "-ExecutionPolicy Bypass -File $escapedPath -ServiceName $escapedServiceName -PublishFolder $escapedPublishFolder -ExeName $escapedExeName" -Verb RunAs -Wait
    exit $LASTEXITCODE
}

$publishFolder = [System.IO.Path]::GetFullPath($PublishFolder)
$exePath = Join-Path $publishFolder $ExeName

if (-not (Test-Path $exePath)) {
    throw "Service executable not found: $exePath . Publish the project first."
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Stopping existing service $ServiceName ..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue

    Write-Host "Removing existing service $ServiceName ..."
    sc.exe delete $ServiceName | Out-Host
    Start-Sleep -Seconds 2
}

# We point BinaryPathName at the published EXE because Windows Services start executables directly.
Write-Host "Creating Windows service $ServiceName ..."
New-Service -Name $ServiceName -BinaryPathName \"`\"$exePath`\"\" -DisplayName $ServiceName -StartupType Automatic

Write-Host "Starting service $ServiceName ..."
Start-Service -Name $ServiceName

Write-Host "Windows service installed successfully."
Write-Host "Executable: $exePath"

