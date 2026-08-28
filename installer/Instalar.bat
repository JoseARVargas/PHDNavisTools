@echo off
setlocal enabledelayedexpansion
echo.
echo ============================================================
echo    PHDNavisTools - Instalador
echo ============================================================
echo.

set "PLUGIN=PHDNavisTools"
set "BASE=%APPDATA%\Autodesk"
set "INSTALLED=0"

for %%V in (2025 2026 2027) do (
    set "NAVIS=!BASE!\Navisworks Manage %%V"
    if exist "!NAVIS!\" (
        set "DEST=!NAVIS!\Plugins\%PLUGIN%"
        echo Instalando para Navisworks Manage %%V...
        if not exist "!DEST!" mkdir "!DEST!"
        for %%F in ("%~dp0*.dll") do (
            copy /Y "%%F" "!DEST!\" > nul
            echo   + %%~nxF
        )
        echo   OK
        set "INSTALLED=1"
    )
)

echo.
if "!INSTALLED!"=="1" (
    echo Plugin instalado com sucesso!
    echo Reinicie o Navisworks para ativar o PHDNavisTools.
) else (
    echo AVISO: Nenhuma versao do Navisworks Manage 2025/2026/2027 encontrada.
)
echo.
pause
