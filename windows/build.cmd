@echo off
rem Builds "dist\Easy Uninstall.exe" with the C# compiler that is already part of Windows.
rem No Visual Studio or .NET SDK needed.   build.cmd       -> the exe
rem                                        build.cmd zip   -> also dist\Easy-Uninstall-windows.zip for a release
setlocal
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" ( echo .NET Framework 4.8 not found ^(it comes with Windows 10 and 11^). & exit /b 1 )

set /p VERSION=<..\VERSION
if "%VERSION%"=="" set VERSION=1.0.0
if not exist obj mkdir obj
if not exist dist mkdir dist

> obj\Version.cs  echo [assembly: System.Reflection.AssemblyVersion("%VERSION%.0")]
>>obj\Version.cs  echo [assembly: System.Reflection.AssemblyFileVersion("%VERSION%.0")]
>>obj\Version.cs  echo [assembly: System.Reflection.AssemblyTitle("Easy Uninstall")]
>>obj\Version.cs  echo [assembly: System.Reflection.AssemblyProduct("Easy Uninstall")]
>>obj\Version.cs  echo [assembly: System.Reflection.AssemblyCompany("Md. Shakil")]
>>obj\Version.cs  echo [assembly: System.Reflection.AssemblyCopyright("Creator: Md. Shakil (shakil.nextive@gmail.com)")]
>>obj\Version.cs  echo [assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

"%FW%\csc.exe" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /warn:3 ^
  /out:"dist\Easy Uninstall.exe" /win32icon:assets\app.ico /win32manifest:app.manifest ^
  /resource:assets\logo.png,logo.png /resource:assets\app.ico,app.ico ^
  /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:System.Xaml.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll ^
  src\*.cs obj\Version.cs
if errorlevel 1 ( echo Build failed. & exit /b 1 )
echo Built: %CD%\dist\Easy Uninstall.exe

if /i "%1"=="zip" (
  del /q "dist\Easy-Uninstall-windows.zip" 2>nul
  powershell -NoProfile -Command "Compress-Archive -Path 'dist\Easy Uninstall.exe' -DestinationPath 'dist\Easy-Uninstall-windows.zip'"
  echo Zip: %CD%\dist\Easy-Uninstall-windows.zip
)
