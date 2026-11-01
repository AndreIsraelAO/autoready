@echo off
rem Compila ReadyUp.exe con el compilador de C# que ya trae Windows (.NET Framework 4). No hace falta instalar nada.
cd /d "%~dp0"
if not exist dist mkdir dist
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize /target:winexe /platform:x64 ^
  /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /out:dist\ReadyUp.exe src\*.cs
if %errorlevel%==0 (echo Listo: dist\ReadyUp.exe) else (echo Error al compilar & exit /b 1)
