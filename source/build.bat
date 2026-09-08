@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo C# compiler not found.
  pause
  exit /b 1
)
if not exist "assets\EncodeKit.cs" (
  echo assets\EncodeKit.cs not found.
  pause
  exit /b 1
)
if not exist "assets\app.ico" (
  echo assets\app.ico not found.
  pause
  exit /b 1
)
"%CSC%" /nologo /optimize+ /target:winexe /out:EncodeKit.exe /win32icon:assets\app.ico /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll assets\EncodeKit.cs
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
echo Built EncodeKit.exe
pause
