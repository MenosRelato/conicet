# CONICET

Análisis y estadísticas sobre el CONICET.

## Instalación

Para instalar el proyecto localmente, es necesario tener instalado 
[.NET 8](http://get.dot.net/) o posterior.

Una vez instalado, ejecutar el siguiente comando:

```
dotnet tool install -g dotnet-conicet --add-source https://menosrelato.blob.core.windows.net/nuget/index.json
```

Una vez instalado, puede ejecutarse con: `conicet`, lo resultara en una pantalla 
de ayuda con las opciones disponibles:

```
❯ conicet
USAGE:
    conicet [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help       Prints help information
    -v, --version    Prints version information

COMMANDS:
    scrap    Descarga artículos por área de conocimiento
    fetch    Refrescar metadata de un artículo específico
    index    Generar índice por área de conocimiento
    sync     Sincroniza todos los artículos de todas las categorías
    upload   Sube los datos descargados a Azure Blob storage
```

## Descarga de datos

Al descargar los datos (via `scrap`, `sync` o `fetch` de un artículo específico),
los datos se almacenan en un cache local ubicado en `%AppData%\MenosRelato\conicet`.

El comando `index` genera un archivo JSON con un indice de todos los artículos 
para una determinada categoría (o todas si se especifica `-a|--all`).

## Sitio Web

El sitio web se genera con [Jekyll](https://jekyllrb.com/) y se encuentra en 
la carpeta `docs`. Para generar el sitio web, es necesario tener instalado 
Ruby y Jekyll. Una vez instalados, ejecutar desde la carpeta `docs`:

```
jekyll serve --incremental
```

### Datos

Los datos descargados previamente, deben subirse a alguna cuenta de Azure 
Blob Storage, por ejemplo utilizando el [Explorador de Azure Storage](https://azure.microsoft.com/es-es/products/storage/storage-explorer/).

Alternativamente, el comando `upload <storage>` permite especificar el 
connection string de la cuenta de Azure Blob Storage de destino y subir
todos los datos automaticamente a un container de nombre `conicet`.

Posteriormente, se debe actualizar el archivo `docs/_config.toml` para que 
la propiedad `storage` coincida con la URL de su cuenta de blob storage.