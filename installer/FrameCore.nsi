; FrameCore installer (NSIS 3, native x64 stub).
; Build:  makensis -DVERSION=1.0.0 -DSOURCE_DIR=..\publish\FrameCore -DOUT_FILE=..\artifacts\FrameCore-Setup-1.0.0-x64.exe installer\FrameCore.nsi
; Silent install:   FrameCore-Setup.exe /S [/D=C:\Path]
; Silent uninstall: "%ProgramFiles%\FrameCore\Uninstall.exe" /S   (user data is kept unless /PURGE is given)
;
; The installer only copies the application. It never applies an optimisation, never creates a
; scheduled task or a service, never installs a driver and never bundles third-party software.

Unicode true
Target amd64-unicode
ManifestDPIAware true
ManifestSupportedOS Win10
SetCompressor /SOLID lzma
SetCompressorDictSize 64

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef SOURCE_DIR
  !define SOURCE_DIR "..\publish\FrameCore"
!endif
!ifndef OUT_FILE
  !define OUT_FILE "..\artifacts\FrameCore-Setup-${VERSION}-x64.exe"
!endif

!define APP_NAME      "FrameCore"
!define APP_EXE       "FrameCore.exe"
!define APP_PUBLISHER "FrameCore Project"
!define APP_URL       "https://github.com/jojobeculo/Pc-optimizer"
!define UNINST_KEY    "Software\Microsoft\Windows\CurrentVersion\Uninstall\FrameCore"
!define APP_KEY       "Software\FrameCore"

Name "${APP_NAME} ${VERSION}"
OutFile "${OUT_FILE}"
InstallDir "$PROGRAMFILES64\${APP_NAME}"
InstallDirRegKey HKLM "${APP_KEY}" "InstallDir"
RequestExecutionLevel admin
ShowInstDetails show
ShowUninstDetails show
BrandingText "${APP_NAME} ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=0 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=0 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=0 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=0 "FileDescription" "${APP_NAME} installer"
VIAddVersionKey /LANG=0 "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey /LANG=0 "LegalCopyright" "Copyright (c) 2026 FrameCore contributors (MIT)"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "FileFunc.nsh"

!define MUI_ICON   "..\src\FrameCore.UI\Assets\framecore.ico"
!define MUI_UNICON "..\src\FrameCore.UI\Assets\framecore.ico"
!define MUI_ABORTWARNING
!define MUI_LANGDLL_REGISTRY_ROOT "HKLM"
!define MUI_LANGDLL_REGISTRY_KEY "${APP_KEY}"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!if /FileExists "${SOURCE_DIR}/docs/GUIDE-UTILISATION.html"
  !define GUIDE_FILE "docs\GUIDE-UTILISATION.html"
!else
  !define GUIDE_FILE "docs\GUIDE-UTILISATION.md"
!endif
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\${GUIDE_FILE}"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_SHOWREADME_TEXT "$(TXT_OPEN_GUIDE)"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE $(LIC_NOTICE)
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_COMPONENTS
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "French"
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_RESERVEFILE_LANGDLL

LicenseLangString LIC_NOTICE ${LANG_FRENCH}  "notice-fr.txt"
LicenseLangString LIC_NOTICE ${LANG_ENGLISH} "notice-en.txt"

LangString TXT_OPEN_GUIDE   ${LANG_FRENCH}  "Ouvrir le guide d'utilisation"
LangString TXT_OPEN_GUIDE   ${LANG_ENGLISH} "Open the user guide"
LangString TXT_NEED_X64     ${LANG_FRENCH}  "FrameCore nécessite une version 64 bits (x64) de Windows."
LangString TXT_NEED_X64     ${LANG_ENGLISH} "FrameCore requires a 64-bit (x64) edition of Windows."
LangString TXT_NEED_WIN10   ${LANG_FRENCH}  "FrameCore nécessite Windows 10 version 1809 ou ultérieure (Windows 11 recommandé)."
LangString TXT_NEED_WIN10   ${LANG_ENGLISH} "FrameCore requires Windows 10 version 1809 or later (Windows 11 recommended)."
LangString TXT_NOT_WIN11    ${LANG_FRENCH}  "Ce PC n'exécute pas Windows 11. FrameCore est conçu et testé pour Windows 11 : certaines optimisations ne seront pas proposées. Continuer ?"
LangString TXT_NOT_WIN11    ${LANG_ENGLISH} "This PC is not running Windows 11. FrameCore is designed for Windows 11; some optimisations will not be offered. Continue?"
LangString TXT_RUNNING      ${LANG_FRENCH}  "FrameCore est en cours d'exécution. Fermez-le puis cliquez sur Réessayer."
LangString TXT_RUNNING      ${LANG_ENGLISH} "FrameCore is running. Close it, then click Retry."
LangString TXT_PURGE_WARN   ${LANG_FRENCH}  "Vous avez choisi de supprimer les données de FrameCore (sauvegardes, journal des modifications, benchmarks).$\r$\n$\r$\nSi des optimisations sont encore appliquées, vous ne pourrez plus les annuler automatiquement. Il est recommandé de les restaurer d'abord depuis FrameCore (Mode Pro > Restauration).$\r$\n$\r$\nSupprimer quand même ces données ?"
LangString TXT_PURGE_WARN   ${LANG_ENGLISH} "You chose to delete FrameCore data (backups, change journal, benchmarks).$\r$\n$\r$\nIf optimisations are still applied you will no longer be able to undo them automatically. Restore them first from FrameCore (Pro mode > Restore).$\r$\n$\r$\nDelete the data anyway?"

LangString SEC_APP_NAME     ${LANG_FRENCH}  "FrameCore (requis)"
LangString SEC_APP_NAME     ${LANG_ENGLISH} "FrameCore (required)"
LangString SEC_START_NAME   ${LANG_FRENCH}  "Raccourci dans le menu Démarrer"
LangString SEC_START_NAME   ${LANG_ENGLISH} "Start menu shortcut"
LangString SEC_DESK_NAME    ${LANG_FRENCH}  "Raccourci sur le Bureau"
LangString SEC_DESK_NAME    ${LANG_ENGLISH} "Desktop shortcut"
LangString SEC_UNDATA_NAME  ${LANG_FRENCH}  "Supprimer aussi mes données (sauvegardes, historique)"
LangString SEC_UNDATA_NAME  ${LANG_ENGLISH} "Also delete my data (backups, history)"

LangString DESC_APP         ${LANG_FRENCH}  "Fichiers de l'application, catalogue d'optimisations, bibliothèque de jeux, langues et documentation."
LangString DESC_APP         ${LANG_ENGLISH} "Application files, optimisation catalog, game library, languages and documentation."
LangString DESC_START       ${LANG_FRENCH}  "Ajoute FrameCore au menu Démarrer."
LangString DESC_START       ${LANG_ENGLISH} "Adds FrameCore to the Start menu."
LangString DESC_DESK        ${LANG_FRENCH}  "Ajoute une icône FrameCore sur le Bureau."
LangString DESC_DESK        ${LANG_ENGLISH} "Adds a FrameCore icon to the desktop."

Var PurgeData

; ---------------------------------------------------------------------------------------------
Function .onInit
  !insertmacro MUI_LANGDLL_DISPLAY
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "$(TXT_NEED_X64)"
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "$(TXT_NEED_WIN10)"
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastBuild} 17763
    MessageBox MB_ICONSTOP "$(TXT_NEED_WIN10)"
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastBuild} 22000
    IfSilent +3
    MessageBox MB_ICONEXCLAMATION|MB_YESNO "$(TXT_NOT_WIN11)" IDYES +2
    Abort
  ${EndIf}
  SetRegView 64
FunctionEnd

; Refuse to overwrite files while the application (or its elevated worker) is running.
!macro WaitForAppClosed ID
  StrCpy $R9 0
  check_running_${ID}:
  ${If} ${FileExists} "$INSTDIR\${APP_EXE}"
    ClearErrors
    FileOpen $0 "$INSTDIR\${APP_EXE}" a
    ${If} ${Errors}
      ${If} ${Silent}
        IntOp $R9 $R9 + 1
        ${If} $R9 > 15
          SetErrorLevel 5
          Abort
        ${EndIf}
        Sleep 2000
        Goto check_running_${ID}
      ${EndIf}
      MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$(TXT_RUNNING)" IDRETRY check_running_${ID}
      Abort
    ${Else}
      FileClose $0
    ${EndIf}
  ${EndIf}
!macroend

Section "!$(SEC_APP_NAME)" SecApp
  SectionIn RO
  !insertmacro WaitForAppClosed inst

  ; Upgrade: remove the previous application files (user data lives in %LOCALAPPDATA%\FrameCore,
  ; never under the installation folder, so nothing personal is lost).
  RMDir /r "$INSTDIR\catalog"
  RMDir /r "$INSTDIR\lang"
  RMDir /r "$INSTDIR\docs"

  SetOutPath "$INSTDIR"
  File /r "${SOURCE_DIR}\*.*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKLM "${APP_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "${APP_KEY}" "Version" "${VERSION}"

  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr   HKLM "${UNINST_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr   HKLM "${UNINST_KEY}" "URLInfoAbout" "${APP_URL}"
  WriteRegStr   HKLM "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
  WriteRegStr   HKLM "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKLM "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr   HKLM "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "${UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd

Section "$(SEC_START_NAME)" SecStart
  SetShellVarContext all
  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
SectionEnd

Section "$(SEC_DESK_NAME)" SecDesk
  SetShellVarContext all
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApp}   "$(DESC_APP)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStart} "$(DESC_START)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesk}  "$(DESC_DESK)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ---------------------------------------------------------------------------------------------
Function un.onInit
  SetRegView 64
  !insertmacro MUI_UNGETLANGUAGE
  StrCpy $PurgeData "0"
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/PURGE" $1
  ${IfNot} ${Errors}
    StrCpy $PurgeData "1"
  ${EndIf}
FunctionEnd

Section "un.$(SEC_APP_NAME)" UnSecApp
  SectionIn RO
  !insertmacro WaitForAppClosed un
  SetShellVarContext all
  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  ; Only remove what the installer put there.
  RMDir /r "$INSTDIR\catalog"
  RMDir /r "$INSTDIR\lang"
  RMDir /r "$INSTDIR\docs"
  RMDir /r "$INSTDIR\runtimes"
  RMDir /r "$INSTDIR\en"
  RMDir /r "$INSTDIR\fr"
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\*.exe"
  Delete "$INSTDIR\*.json"
  Delete "$INSTDIR\*.pdb"
  Delete "$INSTDIR\*.md"
  Delete "$INSTDIR\*.txt"
  Delete "$INSTDIR\*.html"
  Delete "$INSTDIR\*.css"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "${UNINST_KEY}"
  DeleteRegKey HKLM "${APP_KEY}"
SectionEnd

Section /o "un.$(SEC_UNDATA_NAME)" UnSecData
  StrCpy $PurgeData "1"
SectionEnd

Section "-un.purge"
  ${If} $PurgeData == "1"
    IfSilent purge
    MessageBox MB_YESNO|MB_ICONEXCLAMATION "$(TXT_PURGE_WARN)" IDYES purge
    Goto done
    purge:
    ; %LOCALAPPDATA% of the account that launched the uninstaller.
    SetShellVarContext current
    RMDir /r "$LOCALAPPDATA\FrameCore"
    done:
  ${EndIf}
SectionEnd
