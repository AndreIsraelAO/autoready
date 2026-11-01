@echo off
rem Compila AutoReady.exe con el compilador de C# que ya trae Windows (.NET Framework 4). No hace falta instalar nada.
rem Uso: build.bat [version]   ej: build.bat 0.2.0   (sin version = 0.0.0, build de desarrollo)
setlocal
cd /d "%~dp0"
set VERSION=%~1
if "%VERSION%"=="" set VERSION=0.0.0
if not exist dist mkdir dist
if not exist obj mkdir obj
> obj\Version.cs echo [assembly: System.Reflection.AssemblyVersion("%VERSION%.0")]
>> obj\Version.cs echo [assembly: System.Reflection.AssemblyInformationalVersion("%VERSION%")]
>> obj\Version.cs echo [assembly: System.Reflection.AssemblyFileVersion("%VERSION%.0")]
set NET=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
rem Notificaciones nativas: metadatos de Windows (WinRT) que vienen con Windows 10/11.
set WINMD=%WINDIR%\System32\WinMetadata
"%NET%\csc.exe" /nologo /optimize /target:winexe /platform:x64 /codepage:65001 ^
  /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xaml.dll ^
  /r:"%NET%\WPF\PresentationCore.dll" /r:"%NET%\WPF\PresentationFramework.dll" /r:"%NET%\WPF\WindowsBase.dll" ^
  /r:"%NET%\System.Runtime.dll" /r:"%NET%\System.Runtime.WindowsRuntime.dll" /r:"%NET%\System.Runtime.InteropServices.WindowsRuntime.dll" ^
  /r:"%WINMD%\Windows.Foundation.winmd" /r:"%WINMD%\Windows.Data.winmd" /r:"%WINMD%\Windows.UI.winmd" ^
  /win32icon:assets\autoready.ico ^
  /resource:assets\tray-16.png,AutoReady.tray-16.png /resource:assets\tray-32.png,AutoReady.tray-32.png ^
  /resource:assets\logo-64.png,AutoReady.logo-64.png ^
  /out:dist\AutoReady.exe src\*.cs obj\Version.cs
if %errorlevel%==0 (echo Listo: dist\AutoReady.exe v%VERSION%) else (echo Error al compilar & exit /b 1)
