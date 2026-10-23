@echo off
rem Builds dist\BlocPlus.exe (the app) and dist\BlocPlus-Setup.exe (same file; that name makes it install itself).
setlocal
cd /d "%~dp0"
if not exist dist mkdir dist
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /win32icon:assets\BlocPlus.ico /out:dist\BlocPlus.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:Microsoft.CSharp.dll /r:System.Web.Extensions.dll /resource:CHANGELOG.md,CHANGELOG.md BlocPlus.cs || exit /b 1
copy /y dist\BlocPlus.exe dist\BlocPlus-Setup.exe >nul
echo Built dist\BlocPlus.exe and dist\BlocPlus-Setup.exe
