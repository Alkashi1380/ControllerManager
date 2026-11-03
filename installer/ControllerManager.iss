; Installateur ControllerManager (Inno Setup 6)
; Compilé par .github/workflows/release.yml :
;   ISCC /DAppVersion=1.2.3 /DSourceDir=<dossier de dotnet publish> installer\ControllerManager.iss

#define AppName "ControllerManager"
#define AppExe "ControllerManager.exe"
#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

[Setup]
; Identifiant fixe : ne jamais le changer, il relie les mises à jour à l'installation existante
AppId={{8F2C6B1E-3D47-4C1A-9E5B-6A7D2F0C9B14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Alkashi1380
AppPublisherURL=https://github.com/Alkashi1380/ControllerManager
AppSupportURL=https://github.com/Alkashi1380/ControllerManager/issues
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; Installation par utilisateur, sans droits administrateur (l'utilisateur peut choisir « pour tous »)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts\installer
OutputBaseFilename=ControllerManager-Setup-{#AppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Ferme l'appli en cours (verrou d'instance unique de Program.cs) avant de remplacer ses fichiers
AppMutex=ControllerManager.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Lancer ControllerManager au démarrage de Windows (discret, dans la barre des tâches)"; GroupDescription: "Démarrage :"

[Files]
Source: "{#SourceDir}\*"; Excludes: "*.pdb,*.xml"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Même valeur que l'option « Lancer au démarrage » du menu de l'appli
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "GamepadOSManager"; ValueData: """{app}\{#AppExe}"" --tray"; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  WebView2Key = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2Url = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

// Le tableau de bord utilise WebView2 (présent sur Windows 11 et la plupart des Windows 10 à jour)
function IsWebView2Installed: Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\' + Copy(WebView2Key, 10, MaxInt), 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM, WebView2Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, WebView2Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if (CurStep = ssPostInstall) and not IsWebView2Installed and not WizardSilent then
  begin
    if MsgBox('Le tableau de bord de ControllerManager a besoin de Microsoft Edge WebView2, qui n''est pas installé.' + #13#10#13#10 +
              'La manette, les profils et les raccourcis fonctionnent sans, mais pas la fenêtre de configuration.' + #13#10#13#10 +
              'Ouvrir la page de téléchargement de WebView2 (gratuit, Microsoft) ?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', WebView2Url, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    // Démarrage automatique : peut aussi avoir été activé depuis le menu de l'appli
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'GamepadOSManager');

  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    if MsgBox('Supprimer aussi vos profils de manette, réglages et journaux ?' + #13#10#13#10 +
              'Choisissez « Non » pour les retrouver si vous réinstallez ControllerManager.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\ControllerManager'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\ControllerManager'), True, True, True);
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\ControllerManager');
    end;
  end;
end;
