# 🎮 ControllerManager - Diagnostic Manettes & Tray App (Windows)

Outil hybride ultra-léger de diagnostic de manettes, surveillance de batterie et gestion automatique de profils par jeu.

## 📂 Contenu de l'archive

- **`UI/index.html`** : Le Dashboard Web 100 % autonome (HTML5 / Canvas 2D / Gamepad API / WebHID). Vous pouvez double-cliquer dessus pour l'utiliser directement dans Edge ou Chrome sans rien compiler.
- **Service natif Windows (.NET 8)** : mode *Tray-Only* (libération de la RAM à la fermeture de la fenêtre), détection du jeu actif (`GetForegroundWindow`), purge des conflits (`Steam` / `DS4Windows`) et suivi batterie.
  - `Program.cs` : point d'entrée (`--tray` pour démarrer minimisé).
  - `Tray/TrayApplicationContext.cs` : menu de la barre des tâches, dashboard WebView2, surveillance (batterie, jeu actif).
  - `Tray/TrayApplicationContext.SonyHid.cs` : lecture / écriture HID DualSense & DS4 (USB + Bluetooth avec CRC).
  - `Tray/TrayApplicationContext.InputMapping.cs` : boucle d'entrées, XInput, sticks / pavé tactile → souris & clavier.
  - `Tray/TrayApplicationContext.Profiles.cs` : chargement / sauvegarde des profils (`%APPDATA%\ControllerManager\profiles.json`).
  - `Tray/TrayApplicationContext.WebBridge.cs` : messages JSON entre le C# et `UI/index.html`.
  - `Hardware/` (XInput, CRC DualSense, `SonyReportParser` = décodage des trames), `Input/` (souris/clavier simulés, `KeyCombo` = raccourcis personnalisés), `Forms/` (HUD, icône), `Infrastructure/` (journal, détection administrateur), `Models/GamepadProfile.cs`.
- **`ControllerManager.Tests/`** : tests unitaires xUnit (trames DualSense réelles, CRC, raccourcis, élévation) — `dotnet test ControllerManager.Tests`.
- **`ControllerManager.csproj`** : Le fichier de projet .NET 8 (WinForms + WebView2 + HidSharp).
- **`profiles.json`** : Créé automatiquement dans `%APPDATA%\ControllerManager\` (profils par défaut générés au premier lancement).
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
   dotnet publish ControllerManager.csproj -c Release -r win-x64 --self-contained false -o Release_Finale
   ```
4. Pour lancer directement minimisé dans la barre des tâches au démarrage :
   ```bash
   ControllerManager.exe --tray
   ```

## ✨ Fonctions principales

- **Manettes** : DualSense / DualShock 4 en direct HID (USB et Bluetooth), Xbox via XInput (fonctionne en arrière-plan). Option *Toutes les manettes actives* pour utiliser PS et Xbox en même temps.
- **Profils par jeu** : bascule instantanée selon la fenêtre active, couleur de barre lumineuse, gâchettes adaptatives, sticks → souris / molette / ZQSD-WASD / flèches, pavé tactile → souris (1 doigt) et défilement (2 doigts).
- **Raccourcis** (onglet Profils) : chaque bouton ouvre un éditeur — action rapide, touche / combinaison capturée au clavier, maintenir, turbo, texte, macro (suite d'étapes), lancer une application, ouvrir un site. Bouton *Tester*.
- **Lancement d'appli avec profil** : un raccourci « Lancer une appli » mémorise le profil manette à activer (ou *Demander à chaque lancement*, fenêtre pilotable à la manette avec *Mémoriser ce choix*). Repli : profil associé à l'exe, sinon profil actuel.
- **Sticks** : même éditeur que les boutons, réglages propres à chaque stick et à chaque profil — natif, souris, molette, ZQSD/WASD, flèches ou *directions personnalisées* (une action par direction), vitesse, zone morte, inversion Y.
- **Matériel du profil** : gâchettes adaptatives PS5 et couleur de la barre lumineuse (nuancier ou couleur libre) enregistrées dans chaque profil. Les anciens profils sont migrés automatiquement (format v2).
- **Pause depuis la manette** : PS + Options (PS5) ou View + Menu (Xbox) ; l'icône devient grise.
- **Alertes** : batterie faible (20 %) et critique (10 %), logiciel en conflit détecté (DS4Windows, InputMapper, DSX), jeu lancé en administrateur (menu *Relancer en administrateur*).
- **Journal** : `%LOCALAPPDATA%\ControllerManager\logs\controllermanager.log` (menu Outils → *Ouvrir le dossier des journaux*).
