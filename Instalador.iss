; Script do Inno Setup para o instalador do L2 Utilitários (antigo
; "Manutenção da Impressora"). Gera um instalador que coloca o programa
; em Arquivos de Programas, cria atalho na área de trabalho e no menu
; Iniciar, e remove o agendamento automático (se houver) ao desinstalar.

#define MyAppName "L2 Utilitários"
#define MyAppVersion "1.4.0"
#define MyAppExeName "ManutencaoImpressora.exe"

[Setup]
; Identificador fixo: garante que trocar o nome do app não crie uma
; segunda entrada em "Adicionar ou remover programas" nas próximas versões.
AppId={{A7E2B8F4-6C1D-4E3A-9B7F-2D8C5A91F3E6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\ManutencaoImpressora
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=icone.ico
OutputDir=instalador
OutputBaseFilename=ManutencaoImpressora_Instalador
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho na área de trabalho"; GroupDescription: "Atalhos adicionais:"

[Files]
Source: "publicado\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir o programa agora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "schtasks.exe"; Parameters: "/delete /tn ""ManutencaoImpressora"" /f"; Flags: runhidden; RunOnceId: "RemoverTarefaAgendada"
