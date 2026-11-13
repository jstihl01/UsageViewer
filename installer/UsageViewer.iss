#ifndef AppVersion
  #error El build debe proporcionar AppVersion.
#endif
#ifndef PublishDir
  #error El build debe proporcionar PublishDir.
#endif
#ifndef CleanupInclude
  #error El build debe proporcionar CleanupInclude.
#endif
[Setup]
AppId={{6EA022B1-87BA-4789-9350-207885B9F439}
AppName=Usage Viewer
; Inno Setup 6.7.4 uses an x86 setup stub; the payload and permitted OS are x64.
AppVersion={#AppVersion}
AppPublisher=jstihl01
AppPublisherURL=https://github.com/jstihl01/UsageViewer
AppCopyright=Copyright (c) 2026 jstihl01
SetupIconFile=..\src\Assets\UsageViewer.ico
DefaultDirName={localappdata}\Programs\Usage Viewer
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.22000
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
OutputDir=..\dist
OutputBaseFilename=UsageViewer-Setup-x64
UninstallDisplayIcon={app}\UsageViewer.exe
WizardStyle=modern
#ifdef Development
Compression=lzma2/fast
#else
Compression=lzma2/max
#endif
SolidCompression=yes
CloseApplications=yes
RestartApplications=yes
AppMutex=Local\UsageViewer-Installer
#ifdef Signed
SignTool=UsageViewerSign
SignedUninstaller=yes
#endif

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
[Tasks]
Name: "startup"; Description: "Iniciar Usage Viewer al iniciar sesión en Windows"; Flags: unchecked
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{userprograms}\Usage Viewer"; Filename: "{app}\UsageViewer.exe"
Name: "{userstartup}\Usage Viewer"; Filename: "{app}\UsageViewer.exe"; Parameters: "--background"; Tasks: startup
[InstallDelete]
Type: files; Name: "{userstartup}\Usage Viewer.lnk"; Tasks: not startup
[Run]
Filename: "{app}\UsageViewer.exe"; Description: "Abrir Usage Viewer"; Flags: nowait postinstall skipifsilent
Filename: "{app}\UsageViewer.exe"; Parameters: "--background"; Flags: nowait runhidden; Check: RestartSilentUpgrade
[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\UsageViewer"

[Code]
var
  WasRunning: Boolean;
  PreviousPath: String;

function GetCurrentProcess(): THandle;
  external 'GetCurrentProcess@kernel32.dll stdcall';
function OpenProcessToken(ProcessHandle: THandle; Access: Cardinal; var Token: THandle): Boolean;
  external 'OpenProcessToken@advapi32.dll stdcall';
function GetTokenInformation(Token: THandle; Kind: Cardinal; Buffer: String; Size: Cardinal; var Needed: Cardinal): Boolean;
  external 'GetTokenInformation@advapi32.dll stdcall';
procedure CopyPointer(var Destination: LongWord; Source: String; Count: Cardinal);
  external 'RtlMoveMemory@kernel32.dll stdcall';
function ConvertSidToStringSid(Sid: LongWord; var Text: LongWord): Boolean;
  external 'ConvertSidToStringSidW@advapi32.dll stdcall';
function TextLength(Text: LongWord): Integer;
  external 'lstrlenW@kernel32.dll stdcall';
procedure CopyText(Destination: String; Source: LongWord; Count: Integer);
  external 'lstrcpynW@kernel32.dll stdcall';
function LocalFree(Memory: LongWord): LongWord;
  external 'LocalFree@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function CurrentSid(): String;
var
  Token: THandle;
  Buffer: String;
  Needed, Sid, Text: LongWord;
begin
  Result := '';
  if not OpenProcessToken(GetCurrentProcess(), $0008, Token) then exit;
  try
    Buffer := StringOfChar(#0, 512);
    if GetTokenInformation(Token, 1, Buffer, 1024, Needed) then begin
      CopyPointer(Sid, Buffer, 4);
      if ConvertSidToStringSid(Sid, Text) then begin
        try
          SetLength(Result, TextLength(Text));
          CopyText(Result, Text, Length(Result) + 1);
        finally
          LocalFree(Text);
        end;
      end;
    end;
  finally
    CloseHandle(Token);
  end;
end;

function InitializeSetup(): Boolean;
var
  Sid, ProgramFile: String;
  MajorMinor, BuildRevision: Cardinal;
  Code: Integer;
begin
  Result := True;
  Sid := CurrentSid();
  WasRunning := (Sid <> '') and CheckForMutexes('Local\UsageViewer-' + Sid);
  if not RegQueryStringValue(HKCU64,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6EA022B1-87BA-4789-9350-207885B9F439}_is1',
    'InstallLocation', PreviousPath) then PreviousPath := '';
  ProgramFile := AddBackslash(PreviousPath) + 'UsageViewer.exe';
  if WasRunning and FileExists(ProgramFile) and GetVersionNumbers(ProgramFile, MajorMinor, BuildRevision) then begin
    if ((MajorMinor shr 16) > 1) or (((MajorMinor shr 16) = 1) and ((MajorMinor and $FFFF) >= 1)) then begin
      Result := Exec(ProgramFile, '--shutdown', PreviousPath, SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
      if not Result then MsgBox('No se pudo cerrar Usage Viewer. Ciérralo desde su icono y vuelve a intentarlo.', mbError, MB_OK);
    end;
  end;
end;

function RestartSilentUpgrade(): Boolean;
begin
  Result := WizardSilent and WasRunning;
end;

procedure DeleteLegacyIfHashMatches(RelativeName, ExpectedHash: String);
var
  Target: String;
begin
  Target := AddBackslash(ExpandConstant('{app}')) + RelativeName;
  if FileExists(Target) and (CompareText(GetSHA256OfFile(Target), ExpectedHash) = 0) then begin
    if not DeleteFile(Target) then Log('No se pudo retirar un componente antiguo: ' + RelativeName);
  end;
end;

procedure CurStepChanged(Step: TSetupStep);
begin
  if Step = ssInstall then begin
#include CleanupInclude
  end;
end;

function InitializeUninstall(): Boolean;
var
  Code: Integer;
begin
  Result := Exec(ExpandConstant('{app}\UsageViewer.exe'), '--uninstall-cleanup',
    ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  if not Result then MsgBox('No se pudieron cerrar la aplicación y limpiar sus credenciales. La desinstalación se ha detenido.', mbError, MB_OK);
end;
