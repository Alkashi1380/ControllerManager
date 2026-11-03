@echo off
title ControllerManager - Build & Run
echo ===================================================
echo   ControllerManager (.NET 8 + WebView2 Tray App)
echo ===================================================
echo.
echo [1] Lancer l'application (dotnet run)
echo [2] Compiler la version Release (dotnet publish)
echo [3] Mode demo : Dashboard Web seul (sans le service C#, fonctions limitees)
echo.
set /p choice="Votre choix (1/2/3) : "

if "%choice%"=="1" (
    dotnet run
    pause
) else if "%choice%"=="2" (
    dotnet publish ControllerManager.csproj -c Release -r win-x64 --self-contained false -o Release_Finale
    echo.
    echo Executable genere dans le dossier Release_Finale !
    pause
) else if "%choice%"=="3" (
    start "" "UI\index.html"
)
