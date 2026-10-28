@echo off
rem Builds and runs tests\Tests.cs against the app's sources.
setlocal
cd /d "%~dp0"
if not exist dist mkdir dist
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /main:Tests /out:dist\Tests.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:Microsoft.CSharp.dll /r:System.Web.Extensions.dll /r:System.Xml.dll /r:System.Xml.Linq.dll src\*.cs tests\*.cs || exit /b 1
dist\Tests.exe
