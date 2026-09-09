$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget\packages'
if (Get-Process -Name 'Caisse.Desktop' -ErrorAction SilentlyContinue) {
    Write-Host 'Fermez la fenetre Caisse deja ouverte, puis relancez ce script.'
    exit 1
}
dotnet run --project src/Caisse.Desktop/Caisse.Desktop.csproj -c Release
exit $LASTEXITCODE
