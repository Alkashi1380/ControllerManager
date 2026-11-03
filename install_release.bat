@echo off
title Installation Finale - Gamepad OS Manager

echo ========================================================
echo   INSTALLATION FINALE - GAMEPAD OS MANAGER (RELEASE)
echo ========================================================
echo.

echo [1] Compilation de la version Release optimisee...
dotnet publish -c Release -r win-x64 --self-contained false -o Release_Finale

echo.
echo [2] Copie de l'interface HTML...
powershell -Command "New-Item -ItemType Directory -Force -Path 'Release_Finale/UI' | Out-Null; Copy-Item -Force 'UI/index.html' 'Release_Finale/UI/index.html'"

echo.
echo [3] Creation du raccourci sur le Bureau...
powershell -Command "$s=(New-Object -COM WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Desktop') + '/Gamepad OS Manager.lnk'); $s.TargetPath=(Resolve-Path 'Release_Finale/ControllerManager.exe').Path; $s.WorkingDirectory=(Resolve-Path 'Release_Finale').Path; $s.Save()"

echo.
echo ========================================================
echo   TERMINE ! Raccourci cree sur le Bureau.
echo ========================================================
echo.
echo Lancement de l'application...
cd Release_Finale
start ControllerManager.exe
pause