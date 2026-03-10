; Script generated for DropDetect - Optimized for Production
#define MyAppName "DropDetect"
#define MyAppVersion "1.2.2"
#define MyAppPublisher "DropDetect Solo Project"
#define MyAppExeName "Detectvbdc12.2.exe"
#define MyAppAssocName "DropDetect Analysis File"
#define MyAppAssocExt ".ddp"
#define MyAppAssocKey "DropDetectFile"

[Setup]
; Unique ID สำหรับระบุตัวตนของโปรแกรม (คงเดิมเพื่อการอัปเกรด)
AppId={{5D85CE94-FBBE-4D88-B3FD-0834B71D00EB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
; บังคับใช้โหมด 64-bit
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
; ตำแหน่งไฟล์ Installer ที่สร้างเสร็จแล้ว
OutputDir=bin\Release\Installer
OutputBaseFilename=DropDetect_Setup_v1.2.2
SetupIconFile=Assets\appaivbdc.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; บังคับขอสิทธิ์ Admin เฉพาะตอนติดตั้ง
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; ดึงไฟล์จากโฟลเดอร์ publish ที่เรา build ไว้
Source: "bin\Release\net8.0\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net8.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; ลบไฟล์ขยะที่อาจเกิดขึ้นในโฟลเดอร์ติดตั้ง
Type: filesandordirs; Name: "{app}\*"

[Code]
// ส่วนของ Pascal Script เพื่อตรวจสอบความปลอดภัยหรือจัดการข้อมูลเพิ่มเติม (ถ้าจำเป็นในอนาคต)
