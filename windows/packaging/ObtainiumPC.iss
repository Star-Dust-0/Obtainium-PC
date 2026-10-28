; Obtainium-PC installer (Inno Setup 6).
;
; Installs machine-wide to C:\Program Files\Obtainium-PC (administrator
; elevation, as normal for that location). The .NET and Windows App SDK
; runtimes ship inside the app folder, so the installer is a plain file copy.
;
; Identity/update compatibility: AppId below is FIXED for the product's
; lifetime. Newer installers (and the in-app update, which downloads and runs
; one through the shell so Windows asks for elevation) install over the same
; directory after closing a running app, keeping the user's store and settings
; under %LOCALAPPDATA%\ObtainiumPC.
;
; Migration: an earlier per-user install (under %LOCALAPPDATA%\Programs) is
; removed first, its settings and app list are kept (they live outside it).
;
; Version is NOT stamped here: CI passes /DMyAppVersion from the csproj so
; this script never carries a version bump of its own.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef BuildDir
  #define BuildDir "..\ObtainiumPC\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
#endif

[Setup]
AppId={{B05727EB-4A60-4AF2-952E-200A57D2DECD}
AppName=Obtainium-PC
AppVersion={#MyAppVersion}
AppVerName=Obtainium-PC {#MyAppVersion}
AppPublisher=Mohamed Elnaggar
AppPublisherURL=https://github.com/StarsDust01/Obtainium-PC
AppSupportURL=https://github.com/StarsDust01/Obtainium-PC/issues
DefaultDirName={autopf}\Obtainium-PC
DefaultGroupName=Obtainium-PC
PrivilegesRequired=admin
OutputDir=..\..\installer-out
OutputBaseFilename=ObtainiumPC-Setup-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
UninstallDisplayName=Obtainium-PC
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=..\ObtainiumPC\Assets\AppIcon.ico
UninstallDisplayIcon={app}\ObtainiumPC.exe

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Obtainium-PC"; Filename: "{app}\ObtainiumPC.exe"

[Run]
; Relaunch after an interactive install/update (the app hands its own newer
; installer to this wizard, which closes the running app first).
Filename: "{app}\ObtainiumPC.exe"; Description: "Launch Obtainium-PC"; Flags: nowait postinstall skipifsilent

[Code]
// Remove a previous per-user install (HKCU uninstall entry of the same AppId)
// before installing machine-wide, so there is no duplicate entry or folder.
// User data in %LOCALAPPDATA%\ObtainiumPC is not part of that install and stays.
procedure RemovePerUserInstall();
var
  Key, Cmd, Exe, Params: String;
  Code: Integer;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B05727EB-4A60-4AF2-952E-200A57D2DECD}_is1';
  if RegQueryStringValue(HKCU, Key, 'UninstallString', Cmd) then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ObtainiumPC.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Exe := RemoveQuotes(Cmd);
    if FileExists(Exe) then
      Exec(Exe, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then RemovePerUserInstall();
end;
