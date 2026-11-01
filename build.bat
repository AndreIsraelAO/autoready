@echo off
rem Compila ReadyUp.exe con el compilador de C# que ya trae Windows (.NET Framework 4). No hace falta instalar nada.
rem Uso: build.bat [version]   ej: build.bat 0.2.0   (sin version = 0.0.0, build de desarrollo)
setlocal
cd /d "%~dp0"
set VERSION=%~1
if "%VERSION%"=="" set VERSION=0.0.0
if not exist dist mkdir dist
if not exist obj mkdir obj
> obj\Version.cs echo [assembly: System.Reflection.AssemblyVersion("%VERSION%.0")]
>> obj\Version.cs echo [assembly: System.Reflection.AssemblyInformationalVersion("%VERSION%")]
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize /target:winexe /platform:x64 ^
  /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /win32icon:assets\readyup.ico ^
  /resource:assets\tray-16.png,ReadyUp.tray-16.png /resource:assets\tray-32.png,ReadyUp.tray-32.png ^
  /out:dist\ReadyUp.exe src\*.cs obj\Version.cs
if %errorlevel%==0 (echo Listo: dist\ReadyUp.exe v%VERSION%) else (echo Error al compilar & exit /b 1)
