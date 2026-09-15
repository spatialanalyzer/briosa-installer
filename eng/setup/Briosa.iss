#ifndef PackageRoot
  #error PackageRoot is required
#endif
#ifndef ProductVersion
  #error ProductVersion is required
#endif
#ifndef OutputRoot
  #error OutputRoot is required
#endif
#ifndef SignUninstaller
  #define SignUninstaller "no"
#endif
#ifndef TestSuffix
  #define TestSuffix ""
#endif
#define ProductName "Briosa Installer" + TestSuffix
#define ProductId "{712B370D-F4A1-470E-B080-25A8F02BC531}" + TestSuffix

[Setup]
AppId={{#ProductId}
AppName={#ProductName}
AppVersion={#ProductVersion}
AppPublisher=David Lucas
AppPublisherURL=https://briosa.dev
AppSupportURL=https://briosa.dev/install
AppUpdatesURL=https://briosa.dev/install
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
WizardStyle=modern
SetupIconFile=..\..\src\Briosa.Installer.App\Assets\AppIcon\briosa.ico
UninstallDisplayIcon={app}\Briosa.Launcher.exe
UninstallDisplayName={#ProductName}
LicenseFile={#PackageRoot}\LICENSE.txt
OutputDir={#OutputRoot}
OutputBaseFilename=briosa-installer-{#ProductVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
SignedUninstaller={#SignUninstaller}
SignedUninstallerDir={#OutputRoot}\uninstaller
AppMutex=Local\BriosaInstallerApplication
CloseApplications=no
RestartApplications=no
VersionInfoVersion={#NumericVersion}
VersionInfoDescription=Briosa Installer Setup
VersionInfoProductName=Briosa Installer
VersionInfoProductVersion={#NumericVersion}
VersionInfoProductTextVersion={#ProductVersion}
UsePreviousAppDir=yes
AppendDefaultDirName=no

[Files]
Source: "{#PackageRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\Briosa.Launcher.exe"; WorkingDir: "{app}"; Comment: "Manage Briosa server packages"

[Run]
Filename: "{app}\Briosa.Launcher.exe"; Description: "Launch Briosa Installer"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  PreviousVersion: String;
  Previous: TArrayOfString;
  Current: TArrayOfString;
  Index: Integer;
begin
  Result := True;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#ProductId}_is1', 'DisplayVersion', PreviousVersion) then
  begin
    Previous := StringSplit(PreviousVersion, ['.', '-'], stExcludeEmpty);
    Current := StringSplit('{#ProductVersion}', ['.', '-'], stExcludeEmpty);
    if (GetArrayLength(Previous) >= 3) and (GetArrayLength(Current) >= 3) then
      for Index := 0 to 2 do
      begin
        if StrToIntDef(Previous[Index], 0) > StrToIntDef(Current[Index], 0) then
        begin
          SuppressibleMsgBox('A newer setup version of Briosa Installer is installed. Use Settings > Installer updates to review an intentional rollback.', mbError, MB_OK, IDOK);
          Result := False;
          Exit;
        end;
        if StrToIntDef(Previous[Index], 0) < StrToIntDef(Current[Index], 0) then Exit;
      end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Arguments: String;
begin
  if CurStep = ssPostInstall then
  begin
    Arguments := 'app prefer-bundled --version {#ProductVersion} --yes';
#if TestSuffix != ""
    Arguments := Arguments + ' --store "' + ExpandConstant('{app}\..\packages') + '"';
#endif
    if not Exec(ExpandConstant('{app}\Briosa.Installer.Cli.exe'),
      Arguments, ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      RaiseException('The application files were installed, but the selected installer version could not be updated. Close other Briosa Installer operations and run setup again. Existing packages and settings were preserved.');
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not UninstallSilent then
    Result := MsgBox('Uninstall Briosa Installer? Your settings, credentials, and downloaded server/installer packages will be kept. You can remove individual packages in the application before uninstalling.', mbConfirmation, MB_YESNO) = IDYES;
end;
