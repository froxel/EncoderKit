@echo off
setlocal EnableExtensions

set "ASSETS=assets"
set "FFMPEG=%ASSETS%\ffmpeg.exe"
set "FFPROBE=%ASSETS%\ffprobe.exe"
set "SEVENZA=%ASSETS%\7za.exe"
set "FFZIP=%TEMP%\ffmpeg-latest-win64-gpl.zip"
set "FFURL=https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%ASSETS%" mkdir "%ASSETS%"

if not exist "%SEVENZA%" goto :no_7za
if not exist "%FFMPEG%" goto :need_ffmpeg
if not exist "%FFPROBE%" goto :need_ffmpeg
goto :have_ffmpeg

:no_7za
echo assets\7za.exe not found.
echo Place the standalone 7-Zip console 7za.exe in the assets folder.
pause
exit /b 1

:need_ffmpeg
echo ffmpeg.exe or ffprobe.exe missing. Downloading latest from GitHub...
where curl >nul 2>&1
if errorlevel 1 goto :no_curl

curl -L --fail --retry 3 -o "%FFZIP%" "%FFURL%"
if errorlevel 1 goto :dl_fail

echo Extracting ffmpeg.exe and ffprobe.exe...
"%SEVENZA%" e "%FFZIP%" -o"%ASSETS%" -y -r ffmpeg.exe ffprobe.exe
if errorlevel 1 goto :extract_fail
del /q "%FFZIP%" 2>nul

if not exist "%FFMPEG%" goto :extract_fail
if not exist "%FFPROBE%" goto :extract_fail
echo Installed:
echo   %FFMPEG%
echo   %FFPROBE%
goto :have_ffmpeg

:no_curl
echo curl.exe not found. Need Windows 10 version 1803 or later.
pause
exit /b 1

:dl_fail
echo Download failed.
pause
exit /b 1

:extract_fail
echo Could not extract ffmpeg.exe / ffprobe.exe.
del /q "%FFZIP%" 2>nul
pause
exit /b 1

:have_ffmpeg
if not exist "%CSC%" goto :no_csc
if not exist "%ASSETS%\EncodeKit.cs" goto :no_cs
if not exist "%ASSETS%\app.ico" goto :no_ico

"%CSC%" /nologo /optimize+ /target:winexe /out:EncodeKit.exe /win32icon:%ASSETS%\app.ico /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll %ASSETS%\EncodeKit.cs
if errorlevel 1 goto :build_fail
echo Built EncodeKit.exe
pause
exit /b 0

:no_csc
echo C# compiler not found.
pause
exit /b 1

:no_cs
echo assets\EncodeKit.cs not found.
pause
exit /b 1

:no_ico
echo assets\app.ico not found.
pause
exit /b 1

:build_fail
echo Build failed.
pause
exit /b 1