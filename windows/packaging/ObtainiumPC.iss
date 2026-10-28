; Obtainium-PC per-user installer (Inno Setup 6).
;
; The app is an unpackaged (WindowsPackageType=None) self-contained WinUI 3
; build: the .NET 8 and Windows App SDK runtimes ship inside the app folder,
; so the installer is plain file copy - no machine-wide runtime setup, no
; admin rights, per-user install under %LOCALAPPDATA%\Programs.
;
; Identity/update compatibility: AppId below is FIXED for the product's
; lifetime. Newer installers (and any future in-app update that downloads and
; runs one) install over the same directory after closing a running app,
; keeping the user's store and settings under %LOCALAPPDATA%\ObtainiumPC.
; The uninstall entry lands in the per-user Uninstall registry key, which is
; also what the app's own installed-version detection reads.
;
; Version is NOT stamped here: CI passes /DMyAppVersion from the csproj so
; this script never carries a version bump of its own.

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
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
DefaultDirName={localappdata}\Programs\ObtainiumPC
DefaultGroupName=Obtainium-PC
PrivilegesRequired=lowest
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
