@echo off
echo Gerando o executavel...
dotnet publish -c Release -r win-x64 --self-contained true -o publicado
if errorlevel 1 goto fim

echo.
echo Gerando o instalador...
set ISCC="%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not exist %ISCC% set ISCC="%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist %ISCC% (
    echo AVISO: Inno Setup nao encontrado. Instale com "winget install JRSoftware.InnoSetup" para gerar o instalador.
    goto fim
)
%ISCC% Instalador.iss

echo.
echo Pronto! O instalador esta em: %cd%\instalador\ManutencaoImpressora_Instalador.exe

:fim
pause
