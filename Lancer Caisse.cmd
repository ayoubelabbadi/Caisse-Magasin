@echo off
cd /d "%~dp0"
powershell -NoProfile -Command "if (Get-Process -Name 'Caisse.Desktop' -ErrorAction SilentlyContinue) { Write-Host 'Fermez la fenetre Caisse deja ouverte, puis relancez ce fichier.'; exit 1 }"
if errorlevel 1 (
    pause
    exit /b 1
)
if exist "dist\Caisse-Windows-x64\Caisse.Desktop.exe" (
    start "" "dist\Caisse-Windows-x64\Caisse.Desktop.exe"
    exit /b
)
if exist "publish\Caisse.Desktop.exe" (
    start "" "publish\Caisse.Desktop.exe"
    exit /b
)
set "DOTNET_CLI_HOME=%~dp0.dotnet-home"
set "NUGET_PACKAGES=%~dp0.nuget\packages"
dotnet run --project "src\Caisse.Desktop\Caisse.Desktop.csproj" -c Release
if errorlevel 1 pause
