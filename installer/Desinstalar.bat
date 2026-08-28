@echo off
setlocal enabledelayedexpansion
echo.
echo ============================================================
echo    PHDNavisTools - Desinstalador
echo ============================================================
echo.

set "PLUGIN=PHDNavisTools"
set "BASE=%APPDATA%\Autodesk"
set "REMOVED=0"

for %%V in (2025 2026 2027) do (
    set "DEST=!BASE!\Navisworks Manage %%V\Plugins\%PLUGIN%"
    if exist "!DEST!\" (
        echo Removendo de Navisworks Manage %%V...
        rmdir /S /Q "!DEST!"
        echo   OK
        set "REMOVED=1"
    )
)

echo.
if "!REMOVED!"=="1" (
    echo Plugin removido com sucesso!
    echo Reinicie o Navisworks.
) else (
    echo Nenhuma instalacao do PHDNavisTools encontrada.
)
echo.
pause
