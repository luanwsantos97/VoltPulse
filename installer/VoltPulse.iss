; ============================================================================
; VoltPulse — Windows Installer Script (Inno Setup 6)
; Installs to C:\Program Files (x86)\VoltPulse by default, registers in Windows
; Registry / App Paths / Add-Remove Programs, creates Start Menu & Desktop
; shortcuts, installs PawnIO & WebView2 if missing, and supports UAC-free boot.
; ============================================================================

#ifndef AppVersion
  #define AppVersion "1.0.5"
#endif

#ifndef DistDir
  #define DistDir "..\dist\VoltPulse"
#endif

#ifndef OutputDir
  #define OutputDir "..\releases"
#endif

#define AppName "VoltPulse"
#define AppPublisher "VoltPulse Open Source (luanwsantos97)"
#define AppURL "https://github.com/luanwsantos97/VoltPulse"
#define AppExeName "VoltPulse.exe"

[Setup]
AppId={{8E92B6F4-4B7A-4F81-9F13-7B2B1C3D8A01}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={commonpf32}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowNoIcons=yes
LicenseFile=..\LICENSE
SetupIconFile=..\assets\voltpulse.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=VoltPulse-Setup-v{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Installer — PC Power, Voltage, Thermal & SSD Monitor

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
brazilianportuguese.TaskDesktop=Criar atalho na Área de Trabalho
brazilianportuguese.TaskStartup=Iniciar automaticamente com o Windows (minimizado, sem pop-up UAC)
brazilianportuguese.TaskPawnIO=Instalar driver PawnIO (necessário para sensores de CPU, placa-mãe e SSD)
brazilianportuguese.TaskWebView2=Verificar/Instalar Microsoft Edge WebView2 Runtime
brazilianportuguese.StatusPawnIO=Instalando driver assinado PawnIO...
brazilianportuguese.StatusWebView2=Verificando e instalando Microsoft Edge WebView2 Runtime...
brazilianportuguese.StatusStartup=Configurando inicialização automática no Agendador de Tarefas...
brazilianportuguese.LaunchApp=Executar o VoltPulse agora
brazilianportuguese.RemoveUserData=Deseja também remover o histórico de consumo (CSV) e configurações salvas?

english.TaskDesktop=Create a Desktop shortcut
english.TaskStartup=Start automatically with Windows (minimized, UAC-free)
english.TaskPawnIO=Install PawnIO signed driver (required for CPU, motherboard & SSD sensors)
english.TaskWebView2=Check/Install Microsoft Edge WebView2 Runtime
english.StatusPawnIO=Installing PawnIO signed driver...
english.StatusWebView2=Checking and installing Microsoft Edge WebView2 Runtime...
english.StatusStartup=Configuring automatic startup in Windows Task Scheduler...
english.LaunchApp=Launch VoltPulse now
english.RemoveUserData=Do you also want to delete saved energy history (CSV) and settings?

spanish.TaskDesktop=Crear acceso directo en el Escritorio
spanish.TaskStartup=Iniciar automáticamente con Windows (minimizado, sin UAC)
spanish.TaskPawnIO=Instalar controlador firmado PawnIO (para sensores de CPU, placa base y SSD)
spanish.TaskWebView2=Verificar/Instalar Microsoft Edge WebView2 Runtime
spanish.StatusPawnIO=Instalando controlador PawnIO...
spanish.StatusWebView2=Verificando e instalando Microsoft Edge WebView2 Runtime...
spanish.StatusStartup=Configurando inicio automático en el Programador de tareas...
spanish.LaunchApp=Ejecutar VoltPulse ahora
spanish.RemoveUserData=¿Desea eliminar también el historial de consumo (CSV) y la configuración?

french.TaskDesktop=Créer un raccourci sur le Bureau
french.TaskStartup=Démarrer automatiquement avec Windows (réduit, sans UAC)
french.TaskPawnIO=Installer le pilote signé PawnIO (capteurs CPU, carte mère et SSD)
french.TaskWebView2=Vérifier/Installer Microsoft Edge WebView2 Runtime
french.StatusPawnIO=Installation du pilote PawnIO...
french.StatusWebView2=Vérification et installation de Microsoft Edge WebView2 Runtime...
french.StatusStartup=Configuration du démarrage automatique...
french.LaunchApp=Lancer VoltPulse maintenant
french.RemoveUserData=Voulez-vous également supprimer l'historique d'énergie (CSV) et les paramètres ?

german.TaskDesktop=Desktop-Verknüpfung erstellen
german.TaskStartup=Automatisch mit Windows starten (minimiert, ohne UAC)
german.TaskPawnIO=Signierten PawnIO-Treiber installieren (für CPU-, Mainboard- & SSD-Sensoren)
german.TaskWebView2=Microsoft Edge WebView2 Runtime prüfen/installieren
german.StatusPawnIO=PawnIO-Treiber wird installiert...
german.StatusWebView2=Microsoft Edge WebView2 Runtime wird geprüft/installiert...
german.StatusStartup=Autostart in der Aufgabenplanung wird eingerichtet...
german.LaunchApp=VoltPulse jetzt starten
german.RemoveUserData=Möchten Sie auch die gespeicherten Verbrauchsdaten (CSV) und Einstellungen löschen?

italian.TaskDesktop=Crea collegamento sul Desktop
italian.TaskStartup=Avvia automaticamente con Windows (ridotto a icona, senza UAC)
italian.TaskPawnIO=Installa driver firmato PawnIO (per sensori CPU, scheda madre e SSD)
italian.TaskWebView2=Verifica/Installa Microsoft Edge WebView2 Runtime
italian.StatusPawnIO=Installazione del driver PawnIO...
italian.StatusWebView2=Verifica e installazione di Microsoft Edge WebView2 Runtime...
italian.StatusStartup=Configurazione avvio automatico...
italian.LaunchApp=Avvia VoltPulse ora
italian.RemoveUserData=Vuoi eliminare anche lo storico dei consumi (CSV) e le impostazioni?

japanese.TaskDesktop=デスクトップにショートカットを作成
japanese.TaskStartup=Windows 起動時に自動起動（最小化・UACなし）
japanese.TaskPawnIO=署名済み PawnIO ドライバをインストール（CPU・マザーボード・SSDセンサー用）
japanese.TaskWebView2=Microsoft Edge WebView2 Runtime の確認/インストール
japanese.StatusPawnIO=PawnIO ドライバをインストールしています...
japanese.StatusWebView2=Microsoft Edge WebView2 Runtime を確認・インストールしています...
japanese.StatusStartup=タスクスケジューラで自動起動を設定しています...
japanese.LaunchApp=今すぐ VoltPulse を起動
japanese.RemoveUserData=保存された消費電力履歴 (CSV) と設定も削除しますか？

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "{cm:TaskStartup}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "installpawnio"; Description: "{cm:TaskPawnIO}"; GroupDescription: "Dependencies / Dependências:"
Name: "installwebview2"; Description: "{cm:TaskWebView2}"; GroupDescription: "Dependencies / Dependências:"

[Files]
; Arquivos principais do VoltPulse (exe, DLLs, config, ui/, deps/)
Source: "{#DistDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.pt-BR.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Registro próprio da aplicação
Root: HKLM; Subkey: "SOFTWARE\{#AppName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\{#AppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"
Root: HKLM; Subkey: "SOFTWARE\{#AppName}"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"
Root: HKLM; Subkey: "SOFTWARE\{#AppName}"; ValueType: string; ValueName: "Publisher"; ValueData: "{#AppPublisher}"
Root: HKLM; Subkey: "SOFTWARE\{#AppName}"; ValueType: string; ValueName: "Repository"; ValueData: "{#AppURL}"

; App Paths (permite executar digitando "voltpulse" no Win+R)
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{#AppExeName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{#AppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName}"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{#AppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"

[Run]
; 1. Instala PawnIO silenciosamente caso ainda não esteja instalado
Filename: "{app}\deps\PawnIO_setup.exe"; Parameters: "-install -silent"; StatusMsg: "{cm:StatusPawnIO}"; Flags: runhidden waituntilterminated skipifdoesntexist; Tasks: installpawnio; Check: NeedsPawnIo

; 2. Baixa, valida assinatura Microsoft Authenticode e instala Edge WebView2 Runtime caso ainda não esteja instalado
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$d=Join-Path $env:TEMP ('VoltPulseWV2_'+[Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Force -Path $d | Out-Null; try {{ $p=Join-Path $d 'MicrosoftEdgeWebview2Setup.exe'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $p; $sig=Get-AuthenticodeSignature $p; if($sig.Status -eq 'Valid' -and $sig.SignerCertificate.Subject -match 'Microsoft'){{ Start-Process $p -ArgumentList '/silent /install' -WorkingDirectory $d -Wait } } finally {{ Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $d }"""; StatusMsg: "{cm:StatusWebView2}"; Flags: runhidden waituntilterminated; Tasks: installwebview2; Check: NeedsWebView2

; 3. Configura inicialização automática sem UAC (se o usuário marcou a opção)
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /TN ""VoltPulsePowerMonitor"" /TR ""\""{app}\{#AppExeName}\"" --minimized"" /SC ONLOGON /RL HIGHEST /F"; StatusMsg: "{cm:StatusStartup}"; Flags: runhidden waituntilterminated; Tasks: autostart

; 4. Executar VoltPulse ao concluir a instalação
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Encerra VoltPulse antes de desinstalar e remove tarefa agendada
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExeName}"; Flags: runhidden waituntilterminated; RunOnceId: "KillVoltPulse"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""VoltPulsePowerMonitor"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteStartupTask"

[Code]
function NeedsPawnIo: Boolean;
begin
  Result := not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

function NeedsWebView2: Boolean;
var
  Pv: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Pv) and (Pv <> '') then
    Result := False
  else if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Pv) and (Pv <> '') then
    Result := False
  else if RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Pv) and (Pv <> '') then
    Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Fecha instancias anteriores do VoltPulse / PcPowerMonitor antes de sobrescrever arquivos (caminho absoluto System32)
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM VoltPulse.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM PcPowerMonitor.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(400);
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\PcPowerMonitor');
    if DirExists(DataDir) then
    begin
      if MsgBox(CustomMessage('RemoveUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
