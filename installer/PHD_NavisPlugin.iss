; PHD Eng. Digital — Navisworks Plugin Installer
; Inno Setup 6+ required (https://jrsoftware.org/isdl.php)

#define AppName       "PHD Navisworks Plugin"
#define AppPublisher  "PHD Eng. Digital"
#define AppVersion    "1.1.0"
#define AppId         "{B7C4D2E1-5F3A-4B8C-9D1E-2A6F7B3C4D5E}"
#define BuildDir      "..\bin\Release\net48"
#define NavisYear     "2026"
#define PluginFolder  "NavisworksIfcExporter"

[Setup]
AppId={{#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/jaribeirovargas/navisworks-ifc-exporter
AppSupportURL=https://github.com/jaribeirovargas/navisworks-ifc-exporter/issues
AppUpdatesURL=https://github.com/jaribeirovargas/navisworks-ifc-exporter/releases

; Instalação por usuário — sem UAC, sem admin
; Navisworks Manage usa a pasta "Navisworks Manage YYYY", não "Navisworks YYYY"
DefaultDirName={userappdata}\Autodesk\Navisworks Manage {#NavisYear}\Plugins\{#PluginFolder}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=

; Saída
OutputDir=output
OutputBaseFilename=PHD_NavisPlugin_{#AppVersion}_Setup
WizardImageFile=assets\wizard_banner.bmp
WizardSmallImageFile=assets\wizard_small.bmp
WizardStyle=modern
; SetupIconFile=assets\phd_icon.ico   ← descomente após adicionar um .ico em installer/assets/

Compression=lzma2/ultra64
SolidCompression=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Installer

; Não cria atalhos no menu iniciar (plugin carregado pelo Navisworks automaticamente)
DisableProgramGroupPage=yes

; Não pede pasta de destino — instalamos em caminho fixo por usuário
DisableDirPage=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
brazilianportuguese.WelcomeLabel2=Este assistente irá instalar o [name/ver] no seu computador.%n%nO plugin aparecerá automaticamente na aba "PHD Eng. Digital" no Navisworks Manage {#NavisYear}.%n%nFeche o Navisworks antes de continuar.
english.WelcomeLabel2=This will install [name/ver] on your computer.%n%nThe plugin will appear automatically in the "PHD Eng. Digital" tab in Navisworks Manage {#NavisYear}.%n%nClose Navisworks before continuing.

[Files]
; Plugin principal
Source: "{#BuildDir}\NavisworksIfcExporter.dll";   DestDir: "{app}"; Flags: ignoreversion

; ExcelDataReader (leitura de planilhas sem dependência do Office)
Source: "{#BuildDir}\ExcelDataReader.dll";          DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\ExcelDataReader.DataSet.dll";  DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Nenhum atalho — plugin integrado ao Navisworks

[Code]
// Verifica se o Navisworks 2026 está instalado antes de prosseguir
function NavisworksInstalled(): Boolean;
var
  Path: string;
begin
  Result := RegQueryStringValue(HKCU, 'Software\Autodesk\Navisworks\2026', 'InstallPath', Path)
         or RegQueryStringValue(HKLM, 'Software\Autodesk\Navisworks\2026', 'InstallPath', Path)
         or DirExists('C:\Program Files\Autodesk\Navisworks Manage 2026')
         or DirExists('C:\Program Files\Autodesk\Navisworks Simulate 2026');
end;

function InitializeSetup(): Boolean;
begin
  if not NavisworksInstalled() then
  begin
    if MsgBox('Navisworks Manage/Simulate 2026 não foi detectado neste computador.' + #13#10 +
              'O plugin pode não funcionar corretamente.' + #13#10 + #13#10 +
              'Deseja continuar mesmo assim?',
              mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
      Exit;
    end;
  end;
  Result := True;
end;

// Remove instalações anteriores do plugin (caminho atual e caminho legado incorreto)
procedure CurStepChanged(CurStep: TSetupStep);
var
  OldPath: string;
begin
  if CurStep = ssInstall then
  begin
    // Caminho correto atual
    OldPath := ExpandConstant('{userappdata}\Autodesk\Navisworks Manage 2026\Plugins\{#PluginFolder}');
    if DirExists(OldPath) then
      DelTree(OldPath, True, True, True);
    // Caminho legado incorreto (v1.0.0 instalava aqui por engano)
    OldPath := ExpandConstant('{userappdata}\Autodesk\Navisworks 2026\Plugins\{#PluginFolder}');
    if DirExists(OldPath) then
      DelTree(OldPath, True, True, True);
  end;
end;
