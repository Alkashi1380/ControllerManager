@echo off
setlocal EnableDelayedExpansion
title ControllerManager (DualLock) - Build ^& Run

pushd "%~dp0" || exit /b 1
call :menu
set "result=!ERRORLEVEL!"
popd
exit /b !result!

:menu
where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERREUR] Le SDK .NET 8 n'est pas detecte dans le PATH.
    echo Telechargez .NET 8 SDK sur https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo ===================================================
echo   ControllerManager (.NET 8 + WebView2 Tray App)
echo ===================================================
echo.
echo [1] Lancer les tests unitaires + l'application (dotnet test ^& run)
echo [2] Compiler Release Framework-Dependent (Release_Finale)
echo [3] Compiler Release Standalone Autonome (artifacts\publish\win-x64)
echo [4] Preparer le ZIP portable et l'installateur
echo [5] Mode demo : Dashboard Web seul (sans service C#)
echo.
set /p choice="Votre choix (1/2/3/4/5) : "

if "%choice%"=="1" (
    echo [1/2] Verification des tests unitaires...
    dotnet test ControllerManager.Tests\ControllerManager.Tests.csproj -c Release --nologo
    if !ERRORLEVEL! neq 0 (
        echo [ERREUR] Echec des tests unitaires !
        pause
        exit /b !ERRORLEVEL!
    )
    echo [2/2] Demarrage de ControllerManager...
    dotnet run --project ControllerManager.csproj -c Release --no-restore
    set "runExit=!ERRORLEVEL!"
    pause
    exit /b !runExit!
) else if "%choice%"=="2" (
    dotnet test ControllerManager.Tests\ControllerManager.Tests.csproj -c Release --nologo
    if !ERRORLEVEL! neq 0 (
        echo [ERREUR] Echec des tests unitaires. Compilation annulee.
        pause
        exit /b !ERRORLEVEL!
    )
    dotnet publish ControllerManager.csproj -c Release -r win-x64 --self-contained false -o Release_Finale
    if !ERRORLEVEL! neq 0 (
        echo [ERREUR] Echec de la publication Release_Finale.
        pause
        exit /b !ERRORLEVEL!
    )
    echo.
    echo [OK] Executable genere avec succes dans Release_Finale\ControllerManager.exe
    pause
) else if "%choice%"=="3" (
    dotnet test ControllerManager.Tests\ControllerManager.Tests.csproj -c Release --nologo
    if !ERRORLEVEL! neq 0 (
        echo [ERREUR] Echec des tests unitaires. Compilation annulee.
        pause
        exit /b !ERRORLEVEL!
    )
    dotnet publish ControllerManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts\publish\win-x64
    if !ERRORLEVEL! neq 0 (
        echo [ERREUR] Echec de la publication autonome.
        pause
        exit /b !ERRORLEVEL!
    )
    echo.
    echo [OK] Release autonome prete dans artifacts\publish\win-x64 ^(prete pour Inno Setup^).
    pause
) else if "%choice%"=="4" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\prepare_build.ps1"
    set "buildExit=!ERRORLEVEL!"
    pause
    exit /b !buildExit!
) else if "%choice%"=="5" (
    start "" "UI\index.html"
) else (
    echo [ERREUR] Choix invalide.
    pause
    exit /b 1
)
exit /b 0
