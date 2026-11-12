[Setup]
AppId={{6EA022B1-87BA-4789-9350-207885B9F439}
AppName=Usage Viewer
AppVersion=1.0.15
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
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "startup"; Description: "Iniciar Usage Viewer al iniciar sesión en Windows"; Flags: unchecked

[Files]
Source: "..\.build\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Usage Viewer"; Filename: "{app}\UsageViewer.exe"
Name: "{userstartup}\Usage Viewer"; Filename: "{app}\UsageViewer.exe"; Parameters: "--background"; Tasks: startup

[InstallDelete]
Type: files; Name: "{userstartup}\Usage Viewer.lnk"; Tasks: not startup

[Run]
Filename: "{app}\UsageViewer.exe"; Description: "Abrir Usage Viewer"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\UsageViewer"
