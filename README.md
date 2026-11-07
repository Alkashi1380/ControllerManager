# 🎮 ControllerManager - Diagnostic Manettes & Tray App (Windows)

Outil hybride ultra-léger de diagnostic de manettes, surveillance de batterie et gestion automatique de profils par jeu.

## 📥 Installation

1. Ouvrez la page [**Releases**](https://github.com/Alkashi1380/ControllerManager/releases) et téléchargez `ControllerManager-Setup-x.y.z.exe`.
2. Lancez-le : installation **sans droits administrateur**, raccourci dans le menu Démarrer, options *icône sur le Bureau* et *lancement au démarrage de Windows*.
3. Aucune dépendance à installer : .NET est inclus. Seul **WebView2** (déjà présent sur Windows 11 et Windows 10 à jour) est nécessaire pour la fenêtre de configuration ; l'installateur propose de le télécharger s'il manque.

- **Mise à jour** : lancez simplement le nouvel installateur, l'appli en cours est fermée automatiquement et vos profils sont conservés.
- **Désinstallation** : Paramètres Windows → Applications. Vos profils (`%APPDATA%\ControllerManager`) sont conservés, sauf si vous choisissez de les supprimer.
- **Version portable** : `ControllerManager-x.y.z-portable-win-x64.zip`, à décompresser n'importe où.
- Empreintes de vérification : `SHA256SUMS.txt` dans chaque version.

## 🏷️ Publier une nouvelle version (développeur)

```bash
git tag v1.1.0
git push origin v1.1.0
```

Le workflow [`release.yml`](.github/workflows/release.yml) lance les tests, compile l'appli autonome, construit l'installateur ([`installer/ControllerManager.iss`](installer/ControllerManager.iss), Inno Setup) et crée la version GitHub avec les notes générées. Le workflow [`ci.yml`](.github/workflows/ci.yml) compile et teste chaque push.

## 📂 Contenu du dépôt

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
- **`scripts/prepare_build.ps1`** : prépare le ZIP portable, les sommes SHA-256 et l'installateur si Inno Setup 6 est disponible.

Les dossiers `bin/`, `obj/`, `Release_Finale/` et `artifacts/` sont générés localement et ignorés par Git. GitHub Actions publie les binaires dans les [Releases](https://github.com/Alkashi1380/ControllerManager/releases).

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

### Préparer un ZIP portable en local

Choisissez **4** dans `build_and_run.bat` ou exécutez depuis la racine du dépôt :

```powershell
powershell -NoProfile -File .\scripts\prepare_build.ps1 -SkipInstaller
```

Le ZIP et `SHA256SUMS.txt` sont écrits dans `artifacts/installer/`. Le script lit la version depuis `ControllerManager.csproj`, avec la possibilité de la préciser via `-Version x.y.z`. Pour générer aussi l’installateur avec Inno Setup 6, exécutez la commande sans `-SkipInstaller`.

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
