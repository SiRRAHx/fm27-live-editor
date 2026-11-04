; Extra installer page: where FM27 is (the folder with fm.exe), so the app knows where to set up the
; in-game button. Optional; the app can find or ask for it later too. The answer is left in the
; registry and the app moves it into its own settings on first use.
!ifndef BUILD_UNINSTALLER
!include nsDialogs.nsh
!include LogicLib.nsh
!include FileFunc.nsh

Var FmDir
Var FmDirInput

!macro customPageAfterChangeDir
  Page custom FmDirPage FmDirPageLeave
!macroend

!macro FmTry dir
  ${If} $FmDir == ""
  ${AndIf} ${FileExists} "${dir}\fm.exe"
    StrCpy $FmDir "${dir}"
  ${EndIf}
!macroend

; The usual Steam, Epic and Game Pass places, on drives C to J.
Function FmGuess
  !insertmacro FmTry "$PROGRAMFILES32\Steam\steamapps\common\Football Manager 27"
  !insertmacro FmTry "$PROGRAMFILES64\Epic Games\Football Manager 27"
  StrCpy $1 0
  ${Do}
    StrCpy $2 "CDEFGHIJ" 1 $1
    ${If} $2 == ""
      ${Break}
    ${EndIf}
    !insertmacro FmTry "$2:\XboxGames\Football Manager 27\Content"
    !insertmacro FmTry "$2:\Games\Football Manager 27\Content"
    !insertmacro FmTry "$2:\SteamLibrary\steamapps\common\Football Manager 27"
    !insertmacro FmTry "$2:\Epic Games\Football Manager 27"
    IntOp $1 $1 + 1
  ${Loop}
FunctionEnd

Function FmDirPage
  ; An update from inside the app (--updated) keeps the folder already set, so skip the page.
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "--updated" $1
  ${IfNot} ${Errors}
    Abort
  ${EndIf}
  ${If} $FmDir == ""
    Call FmGuess
  ${EndIf}
  ; The page header (the UI library's header macro isn't loaded yet where this file is included).
  GetDlgItem $0 $HWNDPARENT 1037
  SendMessage $0 ${WM_SETTEXT} 0 "STR:Where is Football Manager 27?"
  GetDlgItem $0 $HWNDPARENT 1038
  SendMessage $0 ${WM_SETTEXT} 0 "STR:Pick the folder that has fm.exe in it."
  nsDialogs::Create 1018
  Pop $0
  ${NSD_CreateLabel} 0 0 100% 64u "The editor needs this to set up its optional in-game button. Usual places:$\r$\n$\r$\n  Steam:  Steam > steamapps > common > Football Manager 27$\r$\n  Epic:  Epic Games > Football Manager 27$\r$\n  Game Pass:  XboxGames > Football Manager 27 > Content$\r$\n$\r$\nIn Steam: right-click FM27 > Manage > Browse local files. In the Xbox app: FM27 > Manage > Files > Browse."
  Pop $0
  ${NSD_CreateText} 0 72u 78% 12u "$FmDir"
  Pop $FmDirInput
  ${NSD_CreateBrowseButton} 80% 71u 20% 14u "Browse..."
  Pop $0
  ${NSD_OnClick} $0 FmBrowse
  ${NSD_CreateLabel} 0 92u 100% 24u "Not sure? Leave it empty and carry on. You can set it later in the editor's Settings."
  Pop $0
  nsDialogs::Show
FunctionEnd

Function FmBrowse
  ${NSD_GetText} $FmDirInput $0
  nsDialogs::SelectFolderDialog "Pick the Football Manager 27 folder (the one with fm.exe in it)" $0
  Pop $0
  ${If} $0 != error
    ${NSD_SetText} $FmDirInput $0
  ${EndIf}
FunctionEnd

Function FmDirPageLeave
  ${NSD_GetText} $FmDirInput $FmDir
  ${If} $FmDir != ""
  ${AndIfNot} ${FileExists} "$FmDir\fm.exe"
    MessageBox MB_ICONEXCLAMATION|MB_OK "There's no fm.exe in that folder. Pick the folder that has fm.exe in it, or leave the box empty."
    Abort
  ${EndIf}
FunctionEnd

; An update started from inside the app (--updated) shows no installer at all: same folder,
; same settings. Editors from 0.3.4 on also pass --force-run, so the installer opens the editor
; again; older ones don't, so in that case it's opened here.
!macro preInit
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "--updated" $1
  ${IfNot} ${Errors}
    SetSilent silent
  ${EndIf}
!macroend

!macro customInstall
  ${If} $FmDir != ""
    WriteRegStr HKCU "Software\FM27 Live Editor" "GameFolder" "$FmDir"
  ${EndIf}
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "--updated" $1
  ${IfNot} ${Errors}
    ClearErrors
    ${GetOptions} $0 "--force-run" $1
    ${If} ${Errors}
      Exec '"$INSTDIR\${APP_EXECUTABLE_FILENAME}"'
    ${EndIf}
  ${EndIf}
!macroend
!endif

!macro customUnInstall
  DeleteRegKey HKCU "Software\FM27 Live Editor"
!macroend
