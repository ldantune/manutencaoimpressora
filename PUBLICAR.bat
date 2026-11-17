@echo off
setlocal enabledelayedexpansion

rem Le a versao direto do .csproj, para nao duplicar o numero em outro lugar.
for /f "tokens=2 delims=<>" %%v in ('findstr /c:"<Version>" ManutencaoImpressora.csproj') do set VERSAO=%%v

if "%VERSAO%"=="" (
    echo Nao encontrei a tag ^<Version^> no ManutencaoImpressora.csproj.
    goto fim
)

echo Versao detectada no projeto: %VERSAO%
echo (As mudancas de codigo ja precisam estar commitadas antes de rodar isso.)
set /p CONFIRMA=Publicar a versao %VERSAO% agora (gera o instalador, cria a tag v%VERSAO% e sobe o release no GitHub)? (s/n):
if /i not "%CONFIRMA%"=="s" goto fim

call COMPILAR.bat
if errorlevel 1 goto fim

echo.
echo Criando a tag v%VERSAO%...
git tag -a v%VERSAO% -m "Versao %VERSAO%"
if errorlevel 1 goto fim

echo Enviando para o GitHub...
git push origin main --tags
if errorlevel 1 goto fim

set GH="C:\Program Files\GitHub CLI\gh.exe"
if not exist %GH% set GH=gh

echo Criando o release v%VERSAO% e anexando o instalador...
%GH% release create v%VERSAO% instalador\ManutencaoImpressora_Instalador.exe --title v%VERSAO% --notes "Versao %VERSAO%"

echo.
echo Pronto! Release v%VERSAO% publicado em:
echo https://github.com/ldantune/manutencaoimpressora/releases/tag/v%VERSAO%

:fim
pause
