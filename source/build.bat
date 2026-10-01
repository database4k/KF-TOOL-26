@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the .NET Framework compiler at %CSC%
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:app.ico /resource:fonts\Questrial-Regular.ttf,Questrial.ttf /out:"KF TOOL 26.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)
echo.
echo Built "KF TOOL 26.exe"
pause
