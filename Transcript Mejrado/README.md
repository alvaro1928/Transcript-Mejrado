# Transcript Application

# Transcript Application

## Tabla de Contenidos

- [Descripción](#descripción)
- [Prerequisitos](#prerequisitos)
- [Instalación](#instalación)
- [Configuración](#configuración)
  - [appsettings.json](#appsettingsjson)
  - [Ajuste de Rutas](#ajuste-de-rutas)
  - [Registro de Logs](#registro-de-logs)
  - [Uso de la API Key](#uso-de-la-api-key)
  - [Manejo de API Keys Expiradas](#manejo-de-api-keys-expiradas)
- [Bibliotecas Utilizadas](#bibliotecas-utilizadas)
- [Uso](#uso)
  - [Ejecutar la Aplicación](#ejecutar-la-aplicación)
  - [Verificar Logs](#verificar-logs)
- [Solución de Problemas](#solución-de-problemas)
- [Licencia](#licencia)
- [Contacto](#contacto)

---

## Descripción

La **Transcript Application** es una aplicación de consola en C# diseñada para automatizar la transcripción de archivos de audio o video utilizando la [AssemblyAI](https://www.assemblyai.com/) API. La aplicación lee configuraciones desde un archivo externo `appsettings.json`, soporta el registro de logs y asegura que los archivos de transcripción tengan nombres únicos para evitar sobreescrituras.

---

## Prerequisitos

Antes de configurar la aplicación, asegúrate de tener lo siguiente:

- **.NET 8.0 SDK**: [Descargar .NET](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Visual Studio 2022** (o posterior) o cualquier IDE preferido para C#
- **Cuenta en AssemblyAI**: Regístrate en [AssemblyAI](https://www.assemblyai.com/) para obtener una clave de API

---

## Instalación

1. **Clonar el Repositorio**

   ```bash
   git clone https://github.com/tu-usuario/transcript-app.git
   cd transcript-app
    dotnet restore


## Uso

Cómo utilizar la aplicación una vez instalada.

## Configuración

La aplicación depende de un archivo appsettings.json para su configuración. Este archivo contiene ajustes como claves de API, rutas de directorios y preferencias de logging.

appsettings.json
Crea un archivo appsettings.json en el directorio raíz de tu proyecto con el siguiente contenido:

json
Copiar código
{
  "AssemblyAI": {
    "ApiKey": "TU_CLAVE_DE_API_AQUI",
    "BaseDirectory": "Clases/",
    "InputFileName": "01-202624.mp4",
    "EnableLogging": true
  }
}
Importante: Reemplaza "TU_CLAVE_DE_API_AQUI" con tu clave de API real de AssemblyAI.

## Contribuciones

Guía sobre cómo otros pueden contribuir a tu proyecto.

## Licencia

Información sobre la licencia del proyecto.

## Contacto

Información de contacto para preguntas o soporte.

- **Nombre**: Alvaro Contreras
- **Email**: alvarocontreras35@gmail.com
- **GitHub**: [tu-usuario](https://github.com/alvaro1928)
