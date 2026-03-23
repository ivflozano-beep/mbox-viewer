# MboxViewer

Aplicacion de escritorio para abrir archivos `.mbox` con una experiencia inspirada en Gmail.

## Caracteristicas

- Apertura local de archivos MBOX
- Interfaz de tres paneles estilo cliente de correo moderno
- Busqueda instantanea por remitente, asunto, cuerpo y etiquetas
- Filtros de no leidos y destacados
- Acciones locales de archivar, eliminar y marcar mensajes
- Panel de lectura con encabezados y contenido limpio

## Stack

- .NET 8
- WPF
- MVVM ligero sin dependencias externas

## Ejecutar

```powershell
$env:DOTNET_CLI_HOME='d:\Proyectos VSCode\MBOX app\.dotnet'
$env:HOME='d:\Proyectos VSCode\MBOX app\.dotnet'
dotnet build MboxViewer.sln
dotnet run --project .\MboxViewer.Desktop\MboxViewer.Desktop.csproj
```