@echo off
setlocal enabledelayedexpansion

REM Le dossier source = là où tu es actuellement
set "SOURCE=%CD%"

REM Dossier créé sur le Bureau
set "DEST=%USERPROFILE%\Desktop\Coclico_tout"

mkdir "%DEST%" 2>nul

set /a compteur=0
set /a doublons=0

for /r "%SOURCE%" %%F in (*) do (
    set /a compteur+=1
    set "nom=%%~nxF"

    if exist "%DEST%\!nom!" (
        set /a doublons+=1
        set "nom=%%~nF_!doublons!%%~xF"
    )

    copy "%%F" "%DEST%\!nom!" >nul
)

echo.
echo ✅ !compteur! fichiers copies dans %DEST%
if !doublons! GTR 0 echo ⚠️ !doublons! doublons renommes
pause