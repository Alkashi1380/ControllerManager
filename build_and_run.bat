@echo off
title ControllerManager - Build & Run
echo ===================================================
echo   ControllerManager (.NET 8 + WebView2 Tray App)
echo ===================================================
echo.
echo [1] Lancer l'application (dotnet run)
echo [2] Compiler la version Release (dotnet publish)
echo [3] Ouvrir uniquement le Dashboard Web (UI/index.html)
echo.
set /p choice="Votre choix (1/2/3) : "

if "%choice%"=="1" (
    dotnet run
    pause
) else if "%choice%"=="2" (
    dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
    echo.
    echo Executable genere dans le dossier ./publish !
    pause
) else if "%choice%"=="3" (
    start "" "UI\index.html"
)
