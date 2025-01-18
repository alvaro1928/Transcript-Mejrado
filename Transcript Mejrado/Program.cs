using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
// Si usas la validación con ffprobe, descomenta la siguiente línea
// using System.Diagnostics;

public class Program
{
    private static string ApiKey;
    private static string BaseDirectory;
    private static string FilePath;
    private static string LogFilePath;
    private static bool EnableLogging;

    // HttpClient con un tiempo de espera extendido.
    private static readonly HttpClient httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public static async Task Main(string[] args)
    {
        try
        {
            // 1. Cargar configuración
            LoadConfiguration();

            // 2. Definir ruta del log
            LogFilePath = Path.Combine(BaseDirectory, "log.txt");

            if (EnableLogging)
                await LogAsync("Programa iniciado.");

            Console.WriteLine("Iniciando el proceso de transcripción...");

            // 3. Validar el archivo antes de subir
            if (!IsValidFile(FilePath))
            {
                Console.WriteLine("Archivo no válido. Saliendo...");
                return;
            }

            // (OPCIONAL) Validar con ffprobe si quieres asegurarte de que es un medio reproducible.
            /*
            if (!IsMediaFileValid(FilePath))
            {
                Console.WriteLine("El archivo no es un medio válido según ffprobe. Saliendo...");
                return;
            }
            */

            // 4. Subir el archivo a AssemblyAI
            if (EnableLogging)
                await LogAsync("Iniciando la subida del archivo...");
            Console.WriteLine("Subiendo el archivo de audio/video...");

            string audioUrl = await UploadAudioFileAsync(FilePath);
            if (string.IsNullOrEmpty(audioUrl))
            {
                if (EnableLogging)
                    await LogAsync("Error al subir el archivo.");
                Console.WriteLine("Error al subir el archivo.");
                return;
            }
            if (EnableLogging)
                await LogAsync("Archivo subido exitosamente.");
            Console.WriteLine("Archivo subido exitosamente.");

            // 5. Solicitar transcripción
            if (EnableLogging)
                await LogAsync("Solicitando transcripción...");
            Console.WriteLine("Solicitando la transcripción...");
            string transcriptId = await RequestTranscriptionAsync(audioUrl);
            if (string.IsNullOrEmpty(transcriptId))
            {
                if (EnableLogging)
                    await LogAsync("Error al solicitar la transcripción.");
                Console.WriteLine("Error al solicitar la transcripción.");
                return;
            }
            if (EnableLogging)
                await LogAsync($"Transcripción solicitada exitosamente. ID: {transcriptId}");
            Console.WriteLine($"Transcripción solicitada exitosamente. ID: {transcriptId}");

            // 6. Obtener resultado, con tiempo máximo de espera
            if (EnableLogging)
                await LogAsync("Esperando la finalización de la transcripción...");
            Console.WriteLine("Esperando la finalización de la transcripción...");

            string transcriptText = await GetTranscriptionResultAsync(transcriptId);
            if (!string.IsNullOrEmpty(transcriptText))
            {
                // Guardamos el resultado en un archivo de texto
                string baseName = Path.GetFileNameWithoutExtension(FilePath);
                string uniqueFileName = Path.Combine(BaseDirectory, $"{baseName}_transcript_{Guid.NewGuid()}.txt");

                await File.WriteAllTextAsync(uniqueFileName, transcriptText);
                if (EnableLogging)
                    await LogAsync($"Transcripción completada. Guardada en: {uniqueFileName}");
                Console.WriteLine($"Transcripción completa. Guardada en: {uniqueFileName}");
            }
            else
            {
                if (EnableLogging)
                    await LogAsync("No se pudo recuperar la transcripción. Retornó null o error.");
                Console.WriteLine("No se pudo recuperar la transcripción.");
            }

            // 7. Mantener la consola abierta
            Console.WriteLine("Proceso completado. Presione cualquier tecla para salir...");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                await LogAsync($"Error inesperado: {ex.Message}");
            Console.WriteLine($"Error inesperado: {ex.Message}");

            Console.WriteLine("Presione cualquier tecla para salir...");
            Console.ReadKey();
        }
        finally
        {
            if (EnableLogging)
                await LogAsync("Programa finalizado.");
        }
    }

    /// <summary>
    /// Carga la configuración desde appsettings.json
    /// </summary>
    private static void LoadConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        ApiKey = configuration["AssemblyAI:ApiKey"];
        BaseDirectory = configuration["AssemblyAI:BaseDirectory"];
        FilePath = Path.Combine(BaseDirectory, configuration["AssemblyAI:InputFileName"]);
        EnableLogging = bool.Parse(configuration["AssemblyAI:EnableLogging"]);

        if (string.IsNullOrEmpty(ApiKey))
        {
            Console.WriteLine("Error: ApiKey no está configurada.");
            throw new ArgumentNullException(nameof(ApiKey), "La clave de API no puede ser nula o vacía.");
        }

        if (string.IsNullOrEmpty(BaseDirectory))
        {
            Console.WriteLine("Error: BaseDirectory no está configurada.");
            throw new ArgumentNullException(nameof(BaseDirectory), "BaseDirectory no puede ser nula o vacía.");
        }

        // Normalizar rutas
        BaseDirectory = Path.GetFullPath(BaseDirectory);
        FilePath = Path.GetFullPath(FilePath);

        // Crear el directorio si no existe
        if (!Directory.Exists(BaseDirectory))
        {
            Directory.CreateDirectory(BaseDirectory);
            Console.WriteLine($"BaseDirectory no existía. Se creó: {BaseDirectory}");
        }

        // Verificar que el archivo de entrada exista
        if (!File.Exists(FilePath))
        {
            Console.WriteLine($"Error: El archivo de entrada '{FilePath}' no existe.");
            throw new FileNotFoundException($"El archivo de entrada '{FilePath}' no se encontró.");
        }

        // Configura el encabezado de autorización para HttpClient
        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Add("Authorization", ApiKey);
    }

    /// <summary>
    /// Valida extensión y tamaño del archivo (básico).
    /// Ajusta según tus necesidades.
    /// </summary>
    private static bool IsValidFile(string filePath)
    {
        // Extensiones que consideramos válidas
        string[] validExtensions = { ".mp3", ".wav", ".m4a", ".flac", ".mp4", ".mov", ".webm" };
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        bool isExtensionValid = Array.Exists(validExtensions, ext => ext == extension);
        if (!isExtensionValid)
        {
            Console.WriteLine($"Extensión '{extension}' no es válida para transcripción.");
            return false;
        }

        // Ejemplo: límite 2 GB
        long maxSizeBytes = 2L * 1024 * 1024 * 1024;
        FileInfo fi = new FileInfo(filePath);

        if (fi.Length == 0)
        {
            Console.WriteLine("El archivo está vacío (0 bytes).");
            return false;
        }
        if (fi.Length > maxSizeBytes)
        {
            Console.WriteLine($"El archivo excede el tamaño máximo permitido de 2GB. (tamaño: {fi.Length} bytes)");
            return false;
        }

        return true;
    }

    /// <summary>
    /// (OPCIONAL) Valida que el archivo sea reproducible usando ffprobe (si deseas).
    /// Descomenta y ajusta la ruta de ffprobe si lo usas.
    /// </summary>
    /*
    private static bool IsMediaFileValid(string filePath)
    {
        // Debes instalar ffmpeg/ffprobe y colocar la ruta correcta a ffprobe.exe
        string ffprobePath = @"C:\ruta\hacia\ffprobe.exe"; 
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        // Si 'error' no está vacío, algo falló
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.WriteLine($"ffprobe error: {error}");
            return false;
        }

        // Si 'output' está vacío o no es un número, no se pudo leer duración
        if (!double.TryParse(output, out double duration) || duration <= 0)
        {
            Console.WriteLine("No se pudo determinar una duración válida (podría estar corrupto).");
            return false;
        }

        // Archivo válido (duración > 0)
        return true;
    }
    */

    /// <summary>
    /// Sube el archivo a AssemblyAI en trozos (chunked).
    /// Retorna la URL del archivo subido.
    /// </summary>
    private static async Task<string> UploadAudioFileAsync(string filePath)
    {
        const int chunkSize = 5 * 1024 * 1024; // 5 MB
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            byte[] buffer = new byte[chunkSize];
            int bytesRead;
            string uploadUrl = null;

            while ((bytesRead = await fs.ReadAsync(buffer, 0, chunkSize)) > 0)
            {
                using var content = new ByteArrayContent(buffer, 0, bytesRead);
                content.Headers.Add("Content-Type", "application/octet-stream");

                var response = await httpClient.PostAsync("https://api.assemblyai.com/v2/upload", content);

                if (!response.IsSuccessStatusCode)
                {
                    if (EnableLogging)
                        await LogAsync($"Error al subir un chunk. StatusCode: {response.StatusCode}");
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                dynamic result = JsonConvert.DeserializeObject(jsonResponse);

                // AssemblyAI retorna 'upload_url'
                uploadUrl = result.upload_url;
            }

            return uploadUrl;
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                await LogAsync($"Excepción durante la subida: {ex.Message}");
            Console.WriteLine($"Excepción durante la subida: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Crea una transcripción usando la 'uploadUrl' del archivo subido.
    /// </summary>
    private static async Task<string> RequestTranscriptionAsync(string audioUrl)
    {
        var requestBody = new
        {
            audio_url = audioUrl,
            // Cambia 'language_code' según tu idioma (es, en, etc.)
            language_code = "es"
        };

        var json = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("https://api.assemblyai.com/v2/transcript", content);
        if (!response.IsSuccessStatusCode)
        {
            if (EnableLogging)
                await LogAsync("Error al solicitar la transcripción (RequestTranscriptionAsync).");
            return null;
        }

        var jsonResponse = await response.Content.ReadAsStringAsync();
        dynamic result = JsonConvert.DeserializeObject(jsonResponse);
        return result.id;
    }

    /// <summary>
    /// Consulta el estado de la transcripción hasta completarse,
    /// con un tiempo máximo de espera (ej. 60 minutos).
    /// </summary>
    private static async Task<string> GetTranscriptionResultAsync(string transcriptId)
    {
        int maxWaitMinutes = 60;
        DateTime startTime = DateTime.UtcNow;

        int delay = 5000;   // 5s
        int maxDelay = 30000; // 30s
        int currentDelay = delay;

        while (true)
        {
            double elapsedMinutes = (DateTime.UtcNow - startTime).TotalMinutes;
            if (elapsedMinutes > maxWaitMinutes)
            {
                Console.WriteLine("Tiempo máximo de espera excedido. Cancelando transcripción...");
                if (EnableLogging)
                    await LogAsync("Tiempo máximo de espera excedido en GetTranscriptionResultAsync.");
                return null;
            }

            var response = await httpClient.GetAsync($"https://api.assemblyai.com/v2/transcript/{transcriptId}");
            if (!response.IsSuccessStatusCode)
            {
                if (EnableLogging)
                    await LogAsync("Error al obtener el resultado de la transcripción (StatusCode != 200).");
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(jsonResponse);
            string status = result.status;
            string errorMessage = result.error != null ? (string)result.error : "";

            switch (status)
            {
                case "completed":
                    return result.text;

                case "failed":
                case "error":
                    // Registrar el posible mensaje de error
                    if (EnableLogging)
                        await LogAsync($"Transcripción falló o error. status: {status}, mensaje: {errorMessage}");
                    Console.WriteLine($"Transcripción falló o marcó error: {errorMessage}");
                    return null;

                default:
                    // Estados: queued, processing, etc.
                    Console.WriteLine($"Estado: {status}. Esperando {currentDelay / 1000} segundos...");
                    await Task.Delay(currentDelay);
                    currentDelay = Math.Min(currentDelay * 2, maxDelay);
                    break;
            }
        }
    }

    /// <summary>
    /// Registra un mensaje en el archivo de log, si está habilitado.
    /// </summary>
    private static async Task LogAsync(string message)
    {
        if (!EnableLogging) return;

        string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";
        try
        {
            await File.AppendAllTextAsync(LogFilePath, logMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al escribir en el log: {ex.Message}");
        }
    }
}
