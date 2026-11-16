[Setup]
AppId={{8F4B9A21-5C7E-4D3A-9B12-6E8A4F102938}
AppName=Odoo 20 Point of Sale
AppVersion=1.0.0
AppPublisher=Odoo POS Systems
DefaultDirName={autopf}\OdooPOS
DefaultGroupName=Odoo 20 Point of Sale
OutputDir=.\Installer
OutputBaseFilename=OdooPOS_Setup_v1.0.0
SetupIconFile=Assets\app_logo.ico

; Uninstaller settings
Uninstallable=yes
UninstallDisplayName=Odoo 20 Point of Sale
UninstallDisplayIcon={app}\OdooPOS.exe
CloseApplications=yes

Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible x86
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Start Odoo POS automatically when Windows starts"; GroupDescription: "POS Terminal Options:"; Flags: unchecked

[Dirs]
Name: "{app}"; Permissions: users-modify
Name: "{app}\Backups"; Permissions: users-modify

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; Excludes: "*.db,*.db-shm,*.db-wal,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs; Permissions: users-modify

[Icons]
Name: "{group}\Odoo 20 Point of Sale"; Filename: "{app}\OdooPOS.exe"; WorkingDir: "{app}"; IconFilename: "{app}\OdooPOS.exe"
Name: "{group}\Uninstall Odoo 20 Point of Sale"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Odoo 20 Point of Sale"; Filename: "{app}\OdooPOS.exe"; WorkingDir: "{app}"; IconFilename: "{app}\OdooPOS.exe"; Tasks: desktopicon
Name: "{commonstartup}\Odoo 20 Point of Sale"; Filename: "{app}\OdooPOS.exe"; WorkingDir: "{app}"; IconFilename: "{app}\OdooPOS.exe"; Tasks: startupicon

; Clean up log files automatically on uninstall
[UninstallDelete]
Type: files; Name: "{app}\*.log"

[Run]
Filename: "{app}\OdooPOS.exe"; Description: "{cm:LaunchProgram,Odoo 20 Point of Sale}"; Flags: nowait postinstall skipifsilent

[Code]
// Ask during uninstall whether to also remove the local SQLite database (pos_data.db)
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if FileExists(ExpandConstant('{app}\pos_data.db')) then
    begin
      if MsgBox('Do you also want to delete the local POS database (pos_data.db)?' + #13#10 + #13#10 +
                'Click "No" if you are reinstalling or updating and want to keep your local POS data.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DeleteFile(ExpandConstant('{app}\pos_data.db'));
        DeleteFile(ExpandConstant('{app}\pos_data.db-wal'));
        DeleteFile(ExpandConstant('{app}\pos_data.db-shm'));
        DeleteFile(ExpandConstant('{app}\custom_program_logo.png'));
        RemoveDir(ExpandConstant('{app}'));
      end;
    end;
  end;
end;