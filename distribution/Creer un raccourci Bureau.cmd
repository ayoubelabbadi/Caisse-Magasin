@echo off
set "CAISSE_PORTABLE_DIR=%~dp0"
powershell -NoProfile -Command "$shell = New-Object -ComObject WScript.Shell; $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Caisse magasin.lnk')); $link.TargetPath = Join-Path $env:CAISSE_PORTABLE_DIR 'Caisse.Desktop.exe'; $link.WorkingDirectory = $env:CAISSE_PORTABLE_DIR; $link.IconLocation = $link.TargetPath + ',0'; $link.Description = 'Caisse du magasin'; $link.Save()"
if errorlevel 1 (
    echo Impossible de creer le raccourci.
) else (
    echo Raccourci Caisse magasin cree sur le Bureau. Conservez ce dossier a son emplacement.
)
pause
