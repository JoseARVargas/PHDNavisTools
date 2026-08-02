@echo off
setlocal
set "DEST=%APPDATA%\Autodesk\Navisworks Manage 2026\Plugins\PHDNavisTools"

echo.
echo === PHD Eng. Digital - Plugin Navisworks v1.1.0 ===
echo.

rem Remove versao anterior (limpa DLLs de versoes antigas)
if exist "%DEST%" (
    echo Removendo versao anterior...
    rmdir /S /Q "%DEST%"
)

rem Limpa nomes legados anteriores
set "LEGACY1=%APPDATA%\Autodesk\Navisworks 2026\Plugins\PHDNavisTools"
set "LEGACY2=%APPDATA%\Autodesk\Navisworks Manage 2026\Plugins\NavisworksIfcExporter"
if exist "%LEGACY1%" ( rmdir /S /Q "%LEGACY1%" )
if exist "%LEGACY2%" ( echo Removendo instalacao legada NavisworksIfcExporter... & rmdir /S /Q "%LEGACY2%" )

rem Remove DLL conflitante instalado em ProgramData (causa conflito de assembly identity)
set "CONFLICT=%ProgramData%\Autodesk\Rx_Navisworks Manage\Addins\2026\PHDNavisTools.dll"
if exist "%CONFLICT%" (
    echo Removendo DLL conflitante em ProgramData...
    del /F /Q "%CONFLICT%"
    if %errorlevel% neq 0 (
        echo [AVISO] Nao foi possivel remover o DLL em ProgramData.
        echo         Execute este script como Administrador para remover:
        echo         %CONFLICT%
        echo.
    )
)

echo Instalando v1.1.0 em:
echo   %DEST%
echo.

mkdir "%DEST%"

xcopy "PHDNavisTools.dll"   "%DEST%\" /Y /Q
xcopy "ExcelDataReader.dll"          "%DEST%\" /Y /Q
xcopy "ExcelDataReader.DataSet.dll"  "%DEST%\" /Y /Q

if %errorlevel% equ 0 (
    echo [OK] Plugin instalado com sucesso!
    echo.
    echo Reabra o Navisworks Manage 2026.
    echo A aba "PHD Eng. Digital" aparecera automaticamente.
) else (
    echo [ERRO] Feche o Navisworks e tente novamente.
)

echo.
pause
