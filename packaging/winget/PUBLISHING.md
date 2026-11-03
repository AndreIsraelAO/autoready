# Publicar AutoReady en winget

Los manifiestos finales de cada versión viven en `manifests/h/hercase/AutoReady/<versión>/` (misma estructura que
`microsoft/winget-pkgs`). Los `hercase.AutoReady*.yaml` sueltos de esta carpeta son las plantillas con `<VERSION>` y `<SHA256>`.

Estado: `0.5.2` generado y validado con `winget validate` (cliente 1.29). **No se publicó nada.**

## Qué trae cada manifiesto
- Instalador `exe` (el propio `AutoReady.exe` se instala solo), `Scope: user`, sin admin.
- Switches silenciosos: `--install --quiet` (`Installer.IsQuietInstall` en `src/Installer.cs`, llamado desde `src/Program.cs`):
  instala en `%LOCALAPPDATA%\Programs\AutoReady`, sin acceso directo en el escritorio, y arranca la app.
- `ProductCode: AutoReady` = nombre de la clave de desinstalación en HKCU, para que winget reconozca la instalación.
- Desinstalar: winget usa `QuietUninstallString` (`--uninstall --quiet`), que conserva la configuración de `%APPDATA%`.
- Locales: `en-US` (default) y `es-AR`. `ManifestVersion 1.6.0` (el repo ya tiene esquemas hasta 1.28.0; 1.6.0 sigue aceptado,
  y `wingetcreate` usa el más nuevo si preferís regenerar).

## Antes de mandar el PR
1. Release publicada y no borrador. Verificar el hash (de un directorio vacío, sin ejecutar el exe):
   `gh release download vX.Y.Z -R hercase/autoready -p AutoReady.exe -p AutoReady.exe.sha256`
   y comparar `Get-FileHash .\AutoReady.exe` con el `.sha256`. Opcional: `gh attestation verify AutoReady.exe -R hercase/autoready`.
2. `winget validate --manifest <carpeta de la versión>`.
3. Prueba de instalación real (como administrador, una vez):
   `winget settings --enable LocalManifestFiles`, luego
   `winget install --manifest <carpeta> --silent`, comprobar que aparece en Inicio y en Configuración > Aplicaciones,
   y `winget uninstall hercase.AutoReady --silent`. Mejor en una VM/Windows Sandbox: el exe no está firmado (issue #8) y
   SmartScreen/Defender pueden marcarlo; si el bot de winget-pkgs lo marca, hay que responder en el PR.
4. Que el repo tenga el LICENSE (MIT) público y que `InstallerUrl` responda (la URL no puede cambiar después del hash).

## Opción A: wingetcreate (recomendada)
Requiere `winget install Microsoft.WingetCreate` y un token de GitHub (`wingetcreate token --store`, scope `public_repo`).
- Primera vez: `wingetcreate new https://github.com/hercase/autoready/releases/download/vX.Y.Z/AutoReady.exe`
  (completar identificador `hercase.AutoReady`, los switches `--install --quiet`, tipo `exe`, scope `user`),
  o partir de la carpeta ya generada y hacer `wingetcreate submit <carpeta>`.
- Versiones siguientes: `wingetcreate update hercase.AutoReady --version X.Y.Z --urls https://github.com/hercase/autoready/releases/download/vX.Y.Z/AutoReady.exe --submit`
  (recalcula el SHA-256 y abre el PR solo).

## Opción B: PR manual
1. Fork de `microsoft/winget-pkgs` y clon (`git clone --depth 1 <tu fork>`).
2. Rama `hercase.AutoReady-X.Y.Z`.
3. Copiar la carpeta `manifests/h/hercase/AutoReady/X.Y.Z/` a la misma ruta del clon.
4. `winget validate --manifest <esa carpeta>`, commit `New package: hercase.AutoReady version X.Y.Z` (en las versiones
   siguientes: `New version: hercase.AutoReady version X.Y.Z`), push y PR contra `master`.
5. Completar el checklist de la plantilla del PR. Esperar la validación automática (Azure pipeline); si falla, el bot deja el
   motivo (hash, URL, instalación, antivirus). Corregir con commits en la misma rama.

## En cada release nueva
1. Publicar el tag `vX.Y.Z` (el workflow sube `AutoReady.exe` + `.sha256`).
2. Copiar la carpeta de la versión anterior a `manifests/h/hercase/AutoReady/X.Y.Z/` y cambiar en los 4 archivos
   `PackageVersion`; en el installer `InstallerUrl`, `InstallerSha256` (en mayúsculas, del `.sha256` publicado) y `ReleaseDate`;
   en los locales `ReleaseNotesUrl`.
3. Validar y probar como arriba, y mandar el PR (o `wingetcreate update ... --submit`).
4. Solo versiones que traigan `--install --quiet` (0.5.2 en adelante); las anteriores no se pueden empaquetar.
