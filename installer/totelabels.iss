; Inno Setup script for Tote-ally Labeled. Build with build\make-installer.ps1, which publishes the app into ..\dist
; and passes the version in (iscc /DAppVer=...). Produces Tote-ally-Labeled-Setup-<version>.exe in .\Output.
;
; The app is published self-contained, so the PC needs no .NET runtime. It does need the WebView2 runtime,
; which every current Windows 10 and 11 has; setup downloads it from Microsoft only when it is missing.
; Settings and the logo live per user in %LOCALAPPDATA%\ToteLabels, so they survive upgrades and uninstall.

#ifndef AppVer
  #define AppVer "0.1.0.0"
#endif
#define AppName "Tote-ally Labeled"
#define AppExe "ToteLabels.exe"
; The app's own web server port; other devices on the network reach it here.
#define Port "8683"
#define FirewallRule "Tote-ally Labeled (TCP 8683)"
; Rules for the old port 5050, which other software also uses, under the app's current and former names.
; Removed on install so an upgrade doesn't leave that port open.
#define OldFirewallRule "Tote-ally Labeled (TCP 5050)"
#define OlderFirewallRule "Tote Labels (TCP 5050)"

[Setup]
AppId={{0F9F01C9-5559-4451-9F1F-945E1AACBB9B}
AppName={#AppName}
AppVersion={#AppVer}
AppPublisher=Kurt Jacobson
DefaultDirName={autopf}\Tote-ally Labeled
DefaultGroupName=Tote-ally Labeled
DisableProgramGroupPage=yes
; Admin, for Program Files and the firewall rule.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=Tote-ally-Labeled-Setup-{#AppVer}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\tote.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; The app holds this mutex while it runs, so setup and uninstall ask for it to be closed first.
AppMutex=ToteLabels
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "firewall"; Description: "Allow phones and other computers on this network to use Tote-ally Labeled (opens TCP port {#Port} in Windows Firewall)"; GroupDescription: "Network:"

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[InstallDelete]
; A clean app folder on upgrade, so files a newer version no longer ships do not linger.
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; As the signed-in user, not the elevated setup, so the app uses that user's settings folder.
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"""; Flags: runhidden; RunOnceId: "Firewall"

[Code]
const
  { Edge Update records the WebView2 runtime here: per-machine in the 32-bit view, or per-user under HKCU. }
  WebView2Key = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  { Microsoft's fixed link to the WebView2 bootstrapper. }
  WebView2Url = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  WebView2File = 'MicrosoftEdgeWebview2Setup.exe';

var
  DownloadPage: TDownloadWizardPage;

function HasWebView2: Boolean;
var
  pv: String;
begin
  { An uninstalled runtime leaves the key behind with version 0.0.0.0. }
  Result := (RegQueryStringValue(HKLM32, WebView2Key, 'pv', pv) or RegQueryStringValue(HKCU, WebView2Key, 'pv', pv))
            and (pv <> '') and (pv <> '0.0.0.0');
end;

{ Download and install WebView2 when it is missing. Runs from the Ready page, so a failure leaves the user
  there to retry or cancel instead of half way into an install. "Present" is checked again afterwards: the
  bootstrapper's exit code says it ran, the registry says it worked. }
function InstallWebView2: Boolean;
var
  rc: Integer;
begin
  Result := True;
  if HasWebView2 then Exit;

  DownloadPage.Clear;
  DownloadPage.Add(WebView2Url, WebView2File, '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      if not DownloadPage.AbortedByUser then
        SuppressibleMsgBox('Could not download the Microsoft Edge WebView2 Runtime:' + #13#10#13#10 +
          AddPeriod(GetExceptionMessage) + #13#10#13#10 +
          'Check the internet connection and click Install again.', mbError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;

    DownloadPage.SetText('Installing the Microsoft Edge WebView2 Runtime...', '');
    if not Exec(ExpandConstant('{tmp}\' + WebView2File), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, rc) then
      rc := -1;
    Log(Format('%s /silent /install -> %d', [WebView2File, rc]));
    if not HasWebView2 then
    begin
      SuppressibleMsgBox('The Microsoft Edge WebView2 Runtime did not install. Install it from Microsoft''s website, then run this setup again.',
        mbError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

{ One firewall rule, replaced rather than added: netsh adds a duplicate every time, and an upgrade is every time.
  Private and domain networks only, and only from the local subnet. }
procedure AddFirewallRule;
var
  rc: Integer;
begin
  Exec(ExpandConstant('{sys}\netsh.exe'), 'advfirewall firewall delete rule name="{#FirewallRule}"', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\netsh.exe'),
       'advfirewall firewall add rule name="{#FirewallRule}" dir=in action=allow protocol=TCP localport={#Port} profile=private,domain remoteip=localsubnet',
       '', SW_HIDE, ewWaitUntilTerminated, rc);
  Log(Format('firewall rule "{#FirewallRule}" -> %d', [rc]));
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpReady then
    Result := InstallWebView2;
end;

{ The Ready page says what is about to be downloaded, so nothing is fetched unannounced. }
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo,
  MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo;
  if MemoTasksInfo <> '' then Result := Result + NewLine + NewLine + MemoTasksInfo;
  if not HasWebView2 then
    Result := Result + NewLine + NewLine + 'Download and install from Microsoft first:' + NewLine + Space +
              'Microsoft Edge WebView2 Runtime';
end;

procedure RemoveOldFirewallRules;
var
  rc: Integer;
begin
  Exec(ExpandConstant('{sys}\netsh.exe'), 'advfirewall firewall delete rule name="{#OldFirewallRule}"', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\netsh.exe'), 'advfirewall firewall delete rule name="{#OlderFirewallRule}"', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    RemoveOldFirewallRules;
    if WizardIsTaskSelected('firewall') then
      AddFirewallRule;
  end;
end;
