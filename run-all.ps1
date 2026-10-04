# Starts all three modules in separate windows (Windows PowerShell).
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

dotnet build AssetHub.sln -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

foreach ($m in @("Identity", "Assets", "Booking")) {
    Start-Process powershell -ArgumentList "-NoExit", "-Command",
        "Set-Location '$root\src\AssetHub.$m'; dotnet run --no-build --no-launch-profile"
    if ($m -eq "Identity") { Start-Sleep -Seconds 4 }   # Identity first
}

Write-Host ""
Write-Host "Identity & Organization : http://localhost:5001"
Write-Host "Assets & Allocation     : http://localhost:5002"
Write-Host "Booking & Audit         : http://localhost:5003"
Write-Host "Default admin           : admin@assethub.local / Admin@123"
