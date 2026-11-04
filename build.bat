@echo off
setlocal
cd /d "%~dp0"

rem Usage: build.bat            -> build + copy into the game's BepInEx\plugins folder
rem        build.bat nocopy     -> build only
rem Output: dist\CardPullOut.dll and dist\CardPullOut-CRACK-vX.Y.Z.zip (ready-to-extract release)
set "GAMEDIR=C:\Program Files (x86)\Steam\steamapps\common\CRACK"
set "VERSION=1.0.0"

dotnet build CardPullOut.csproj -c Release -o build "-p:GameDir=%GAMEDIR%" "-p:Version=%VERSION%" || exit /b 1

if exist dist rmdir /s /q dist
mkdir dist\pkg\BepInEx\plugins
copy /y build\CardPullOut.dll dist\CardPullOut.dll >nul
copy /y build\CardPullOut.dll dist\pkg\BepInEx\plugins\CardPullOut.dll >nul
rem Windows' bsdtar writes zip entries with forward slashes (Compress-Archive in PS 5 uses backslashes)
tar -a -cf "dist\CardPullOut-CRACK-v%VERSION%.zip" -C dist\pkg BepInEx || exit /b 1
rmdir /s /q dist\pkg

if /i "%~1"=="nocopy" goto done
if not exist "%GAMEDIR%\BepInEx\plugins" (
  echo BepInEx not installed in "%GAMEDIR%" - skipping install.
  goto done
)
copy /y build\CardPullOut.dll "%GAMEDIR%\BepInEx\plugins\CardPullOut.dll" >nul && echo Installed to "%GAMEDIR%\BepInEx\plugins"

:done
echo.
echo Build OK: dist\CardPullOut.dll, dist\CardPullOut-CRACK-v%VERSION%.zip
