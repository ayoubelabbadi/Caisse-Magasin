$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget\packages'
dotnet publish src/Caisse.Desktop/Caisse.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -p:BaseOutputPath=bin-portable/ -o dist/Caisse-Windows-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath 'distribution\LIRE-MOI.txt' -Destination 'dist\Caisse-Windows-x64\LIRE-MOI.txt' -Force
Copy-Item -LiteralPath 'distribution\Creer un raccourci Bureau.cmd' -Destination 'dist\Caisse-Windows-x64\Creer un raccourci Bureau.cmd' -Force
Compress-Archive -Path 'dist\Caisse-Windows-x64\*' -DestinationPath 'dist\Caisse-Windows-x64.zip' -Force
Write-Host 'Version autonome : dist\Caisse-Windows-x64.zip'
