using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers; // Para MediaTypeHeaderValue
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

// Alias para evitar conflicto con System.IO.File
using TFile = TagLib.File;

public class Program
{
    private static string ApiKey;
    private static string BaseDirectory;
    private static string FilePath;
    private static string LogFilePath;
    private static bool EnableLogging;

    // HttpClient con 10 min de timeout por chunk
    private static readonly HttpClient httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public static async Task Main(string[] args)
    {
        try
        {
            // 1) Cargar configuración
            LoadConfiguration();

            // 2) Definir la ruta del log
            LogFilePath = Path.Combine(BaseDirectory, "log.txt");

            if (EnableLogging)
                await LogAsync("Programa iniciado.");

            Console.WriteLine("Iniciando el proceso de transcripción...");

            // 3) Validar el archivo
            if (!IsValidFile(FilePath))
            {
                Console.WriteLine("Archivo no válido (extensión/tamaño). Saliendo...");
                return;
            }

            // 4) Validar con TagLib# (duración > 0)
            if (!IsMediaFileValidTagLib(FilePath))
            {
                Console.WriteLine("El archivo no es un medio válido según TagLib#. Saliendo...");
                return;
            }

            // 5) Subir el archivo a AssemblyAI en trozos (pero en la misma URL, generando 1 solo archivo)
            Console.WriteLine("Subiendo el archivo de audio/video...");
            if (EnableLogging)
                await LogAsync("Iniciando la subida del archivo...");

            string audioUrl = await UploadAudioFileAsync(FilePath);
            if (string.IsNullOrEmpty(audioUrl))
            {
                if (EnableLogging)
                    await LogAsync("Error al subir el archivo completo.");
                Console.WriteLine("Error al subir el archivo.");
                return;
            }
            Console.WriteLine("Archivo subido exitosamente.");
            if (EnableLogging)
                await LogAsync("Archivo subido exitosamente.");

            // 6) Solicitar transcripción
            Console.WriteLine("Solicitando la transcripción...");
            if (EnableLogging)
                await LogAsync("Solicitando transcripción...");
            string transcriptId = await RequestTranscriptionAsync(audioUrl);
            if (string.IsNullOrEmpty(transcriptId))
            {
                if (EnableLogging)
                    await LogAsync("Error al solicitar la transcripción.");
                Console.WriteLine("Error al solicitar la transcripción.");
                return;
            }
            Console.WriteLine($"Transcripción solicitada. ID: {transcriptId}");
            if (EnableLogging)
                await LogAsync($"Transcripción solicitada exitosamente. ID: {transcriptId}");

            // 7) Esperar resultado
            Console.WriteLine("Esperando la finalización de la transcripción...");
            if (EnableLogging)
                await LogAsync("Esperando la finalización de la transcripción...");
            string transcriptText = await GetTranscriptionResultAsync(transcriptId);
            if (!string.IsNullOrEmpty(transcriptText))
            {
                // Guardar el resultado
                string baseName = Path.GetFileNameWithoutExtension(FilePath);
                string uniqueFileName = Path.Combine(BaseDirectory, $"{baseName}_transcript_{Guid.NewGuid()}.txt");
                System.IO.File.WriteAllText(uniqueFileName, transcriptText);

                Console.WriteLine($"Transcripción completa. Guardada en: {uniqueFileName}");
                if (EnableLogging)
                    await LogAsync($"Transcripción completada. Guardada en: {uniqueFileName}");
            }
            else
            {
                Console.WriteLine("No se pudo recuperar la transcripción.");
                if (EnableLogging)
                    await LogAsync("No se pudo recuperar la transcripción (retornó null o error).");
            }

            Console.WriteLine("Proceso completado. Presione cualquier tecla para salir...");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error inesperado: {ex.Message}");
            if (EnableLogging)
                await LogAsync($"Error inesperado: {ex.Message}");

            Console.WriteLine("Presione cualquier tecla para salir...");
            Console.ReadKey();
        }
        finally
        {
            if (EnableLogging)
                await LogAsync("Programa finalizado.");
        }
    }

    // -----------------------------------------------------------------
    // Métodos de configuración, validación y subida

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
            throw new ArgumentNullException(nameof(BaseDirectory));
        }

        BaseDirectory = Path.GetFullPath(BaseDirectory);
        FilePath = Path.GetFullPath(FilePath);

        if (!Directory.Exists(BaseDirectory))
        {
            Directory.CreateDirectory(BaseDirectory);
            Console.WriteLine($"BaseDirectory no existía. Se creó: {BaseDirectory}");
        }

        if (!System.IO.File.Exists(FilePath))
        {
            Console.WriteLine($"Error: El archivo de entrada '{FilePath}' no existe.");
            throw new FileNotFoundException($"El archivo de entrada '{FilePath}' no se encontró.");
        }

        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Add("Authorization", ApiKey);
    }

    private static bool IsValidFile(string filePath)
    {
        string[] validExtensions = { ".mp4", ".mov", ".webm", ".mp3", ".wav", ".m4a", ".flac" };
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (Array.IndexOf(validExtensions, extension) < 0)
        {
            Console.WriteLine($"Extensión '{extension}' no es válida para transcripción.");
            return false;
        }

        long maxSize = 2L * 1024L * 1024L * 1024L; // 2GB
        var fi = new FileInfo(filePath);

        if (fi.Length == 0)
        {
            Console.WriteLine("El archivo está vacío (0 bytes).");
            return false;
        }
        if (fi.Length > maxSize)
        {
            Console.WriteLine($"El archivo excede 2GB (tamaño: {fi.Length} bytes).");
            return false;
        }

        return true;
    }

    private static bool IsMediaFileValidTagLib(string filePath)
    {
        try
        {
            var tfile = TFile.Create(filePath);
            var duration = tfile.Properties.Duration;
            if (duration.TotalSeconds <= 0)
            {
                Console.WriteLine("TagLib#: Duración 0 (archivo corrupto o sin audio).");
                return false;
            }

            Console.WriteLine($"TagLib#: Duración = {duration}. Archivo con pista OK.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TagLib# no pudo leer el archivo. Error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sube el archivo a AssemblyAI en chunks de 5 MB, pero a la MISMA URL,
    /// con Content-Type="video/mp4". AssemblyAI reconstruye un solo .mp4.
    /// </summary>
    private static async Task<string> UploadAudioFileAsync(string filePath)
    {
        const int chunkSize = 5 * 1024 * 1024;
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            byte[] buffer = new byte[chunkSize];
            int bytesRead;
            string uploadUrl = null;

            while ((bytesRead = await fs.ReadAsync(buffer, 0, chunkSize)) > 0)
            {
                using var content = new ByteArrayContent(buffer, 0, bytesRead);
                // Importante: "video/mp4"
                content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

                var response = await httpClient.PostAsync("https://api.assemblyai.com/v2/upload", content);
                if (!response.IsSuccessStatusCode)
                {
                    if (EnableLogging)
                        await LogAsync($"Error al subir chunk. StatusCode: {response.StatusCode}");
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                dynamic result = JsonConvert.DeserializeObject(jsonResponse);
                // la última upload_url devuelta
                uploadUrl = result.upload_url;
            }

            return uploadUrl;
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                await LogAsync($"Excepción durante la subida: {ex.Message}");
            Console.WriteLine($"Excepción en UploadAudioFileAsync: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Solicita la transcripción. Retorna transcriptId.
    /// </summary>
    private static async Task<string> RequestTranscriptionAsync(string audioUrl)
    {
        var body = new
        {
            audio_url = audioUrl,
            language_code = "es" // Cambia según el idioma
        };
        var json = JsonConvert.SerializeObject(body);
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
    /// Espera hasta 60 min a que la transcripción finalice.
    /// </summary>
    private static async Task<string> GetTranscriptionResultAsync(string transcriptId)
    {
        int maxWaitMinutes = 60;
        DateTime startTime = DateTime.UtcNow;

        int delay = 5000;      // 5s
        int maxDelay = 30000;  // 30s
        int currentDelay = delay;

        while (true)
        {
            double elapsed = (DateTime.UtcNow - startTime).TotalMinutes;
            if (elapsed > maxWaitMinutes)
            {
                Console.WriteLine("Tiempo máximo de espera excedido. Cancelando...");
                if (EnableLogging)
                    await LogAsync("Tiempo máximo de espera excedido en GetTranscriptionResultAsync.");
                return null;
            }

            var response = await httpClient.GetAsync($"https://api.assemblyai.com/v2/transcript/{transcriptId}");
            if (!response.IsSuccessStatusCode)
            {
                if (EnableLogging)
                    await LogAsync("Error al obtener resultado de la transcripción (StatusCode != 200).");
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
                    if (EnableLogging)
                        await LogAsync($"Transcripción falló. status: {status}, mensaje: {errorMessage}");
                    Console.WriteLine($"Transcripción falló: {errorMessage}");
                    return null;

                default:
                    Console.WriteLine($"Estado: {status}. Esperando {currentDelay / 1000} seg...");
                    await Task.Delay(currentDelay);
                    currentDelay = Math.Min(currentDelay * 2, maxDelay);
                    break;
            }
        }
    }

    /// <summary>
    /// Registra un mensaje en el log si EnableLogging == true.
    /// </summary>
    private static async Task LogAsync(string message)
    {
        if (!EnableLogging) return;
        string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";
        try
        {
            System.IO.File.AppendAllText(LogFilePath, logMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al escribir en el log: {ex.Message}");
        }
    }
}
