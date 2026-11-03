# 🎮 ControllerManager - Diagnostic Manettes & Tray App (Windows)

Outil hybride ultra-léger de diagnostic de manettes, surveillance de batterie et gestion automatique de profils par jeu.

## 📂 Contenu de l'archive

- **`UI/index.html`** : Le Dashboard Web 100 % autonome (HTML5 / Canvas 2D / Gamepad API / WebHID). Vous pouvez double-cliquer dessus pour l'utiliser directement dans Edge ou Chrome sans rien compiler.
- **`Program.cs`** : Le service natif Windows (.NET 8) gérant le mode *Tray-Only* (libération de la RAM à la fermeture de la fenêtre), la détection du jeu actif (`GetForegroundWindow`), la détection de conflits (`Steam` + `DS4Windows`) et les alertes batterie.
- **`ControllerManager.csproj`** : Le fichier de projet .NET 8 (WinForms + WebView2 + HidSharp).
- **`profiles.json`** : Les profils de configuration par défaut.
- **`build_and_run.bat`** : Script rapide pour lancer ou compiler l'application en un clic.

## 🚀 Utilisation rapide

### Option 1 : Dashboard Web immédiat (Sans compilation)
Ouvrez simplement `UI/index.html` dans Microsoft Edge ou Google Chrome.

### Option 2 : Compiler l'application Windows (.NET 8)
Prérequis : [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installé.

1. Ouvrez un terminal dans ce dossier (ou lancez `build_and_run.bat`).
2. Pour tester en direct :
   ```bash
   dotnet run
   ```
3. Pour compiler un exécutable léger :
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained false
   ```
4. Pour lancer directement minimisé dans la barre des tâches au démarrage :
   ```bash
   ControllerManager.exe --tray
   ```
