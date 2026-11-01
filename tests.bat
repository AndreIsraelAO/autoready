@echo off
rem Pruebas automaticas: compila el codigo de la app (sin Program.cs) junto con tests\*.cs y las corre.
rem Uso: tests.bat   (exit 0 = todo bien). Corre tambien en GitHub Actions en cada push y antes de cada release.
setlocal
cd /d "%~dp0"
if not exist obj mkdir obj
if exist obj\Version.cs goto haveversion
> obj\Version.cs echo [assembly: System.Reflection.AssemblyVersion("0.0.0.0")]
:haveversion
set NET=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WINMD=%WINDIR%\System32\WinMetadata
set SRC=
for %%f in (src\*.cs) do if /i not "%%~nxf"=="Program.cs" call set SRC=%%SRC%% "%%f"
"%NET%\csc.exe" /nologo /target:exe /platform:x64 /codepage:65001 ^
  /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xaml.dll ^
  /r:"%NET%\WPF\PresentationCore.dll" /r:"%NET%\WPF\PresentationFramework.dll" /r:"%NET%\WPF\WindowsBase.dll" ^
  /r:"%NET%\WPF\UIAutomationProvider.dll" /r:"%NET%\WPF\UIAutomationTypes.dll" ^
  /r:"%NET%\System.Runtime.dll" /r:"%NET%\System.Runtime.WindowsRuntime.dll" /r:"%NET%\System.Runtime.InteropServices.WindowsRuntime.dll" ^
  /r:"%WINMD%\Windows.Foundation.winmd" /r:"%WINMD%\Windows.Data.winmd" /r:"%WINMD%\Windows.UI.winmd" ^
  /resource:assets\tray-16.png,AutoReady.tray-16.png /resource:assets\tray-32.png,AutoReady.tray-32.png ^
  /resource:assets\logo-64.png,AutoReady.logo-64.png ^
  /out:obj\AutoReadyTests.exe %SRC% tests\*.cs obj\Version.cs
if errorlevel 1 (echo Error al compilar las pruebas & exit /b 1)
obj\AutoReadyTests.exe tests\fixtures
