@echo off
setlocal EnableDelayedExpansion
title Installation Finale - Gamepad OS Manager (DualLock)

echo ========================================================
echo   INSTALLATION FINALE - GAMEPAD OS MANAGER (RELEASE)
echo ========================================================
echo.

echo [0] Fermeture d'une eventuelle instance en cours...
taskkill /F /IM ControllerManager.exe >nul 2>&1

echo [1] Execution des tests unitaires de securite...
dotnet test ControllerManager.Tests\ControllerManager.Tests.csproj -c Release --nologo
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERREUR] Les tests unitaires ont echoue. Installation annulee.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2] Compilation de la version Release optimisee...
dotnet publish ControllerManager.csproj -c Release -r win-x64 --self-contained false -o Release_Finale
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERREUR] La compilation dotnet publish a echoue.
    pause
    exit /b %ERRORLEVEL%
)

if not exist "Release_Finale\ControllerManager.exe" (
    echo [ERREUR] Release_Finale\ControllerManager.exe introuvable.
    pause
    exit /b 1
)

echo.
echo [3] Verification des ressources UI et icone...
powershell -NoProfile -Command "New-Item -ItemType Directory -Force -Path 'Release_Finale/UI' | Out-Null; Copy-Item -Force 'UI/index.html' 'Release_Finale/UI/index.html'; if (Test-Path 'Assets/app.ico') { New-Item -ItemType Directory -Force -Path 'Release_Finale/Assets' | Out-Null; Copy-Item -Force 'Assets/app.ico' 'Release_Finale/Assets/app.ico' }"

echo.
echo [4] Creation du raccourci sur le Bureau...
powershell -NoProfile -Command "$s=(New-Object -COM WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Desktop') + '/Gamepad OS Manager.lnk'); $s.TargetPath=(Resolve-Path 'Release_Finale/ControllerManager.exe').Path; $s.WorkingDirectory=(Resolve-Path 'Release_Finale').Path; if (Test-Path 'Release_Finale/Assets/app.ico') { $s.IconLocation=(Resolve-Path 'Release_Finale/Assets/app.ico').Path }; $s.Save()"

echo.
echo ========================================================
echo   TERMINE ! Raccourci cree sur le Bureau.
echo ========================================================
echo.
echo Lancement de l'application...
pushd Release_Finale
start "" ControllerManager.exe
popd
pause