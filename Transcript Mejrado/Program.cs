using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

public class Program
{
    private static string ApiKey;
    private static string BaseDirectory;
    private static string FilePath;
    private static string LogFilePath;
    private static bool EnableLogging;

    private static readonly HttpClient httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(10) // Extiende el timeout para peticiones más largas
    };

    public static async Task Main(string[] args)
    {
        try
        {
            // Cargar configuración
            LoadConfiguration();

            // Definir la ruta del log
            LogFilePath = Path.Combine(BaseDirectory, "log.txt");

            if (EnableLogging)
                await LogAsync("Programa iniciado.");

            Console.WriteLine("Iniciando el proceso de transcripción...");

            // Validar el archivo (extensión y tamaño)
            if (!IsValidFile(FilePath))
            {
                Console.WriteLine("Archivo no válido (extensión/tamaño). Saliendo...");
                return;
            }

            // Subir el archivo completo
            if (EnableLogging)
                await LogAsync("Iniciando la subida del archivo completo...");
            Console.WriteLine("Subiendo el archivo completo...");

            string audioUrl = await UploadAudioFileAsync(FilePath);
            if (string.IsNullOrEmpty(audioUrl) || !Uri.IsWellFormedUriString(audioUrl, UriKind.Absolute))
            {
                if (EnableLogging)
                    await LogAsync("El URL de subida es inválido o está vacío.");
                Console.WriteLine("El URL de subida es inválido o está vacío.");
                return;
            }

            if (EnableLogging)
                await LogAsync($"Archivo subido exitosamente. URL: {audioUrl}");
            Console.WriteLine("Archivo subido exitosamente.");

            // Solicitar transcripción
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

            // Esperar resultado
            if (EnableLogging)
                await LogAsync("Esperando la finalización de la transcripción...");
            Console.WriteLine("Esperando la finalización de la transcripción...");

            string transcriptText = await GetTranscriptionResultAsync(transcriptId);
            if (!string.IsNullOrEmpty(transcriptText))
            {
                string baseName = Path.GetFileNameWithoutExtension(FilePath);
                string uniqueFileName = Path.Combine(BaseDirectory, $"{baseName}_transcript_{Guid.NewGuid()}.txt");

                System.IO.File.WriteAllText(uniqueFileName, transcriptText);
                if (EnableLogging)
                    await LogAsync($"Transcripción completada. Guardada en: {uniqueFileName}");
                Console.WriteLine($"Transcripción completa. Guardada en: {uniqueFileName}");
            }
            else
            {
                if (EnableLogging)
                    await LogAsync("No se pudo recuperar la transcripción.");
                Console.WriteLine("No se pudo recuperar la transcripción.");
            }

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
            Console.WriteLine("Error: ApiKey no está configurada en appsettings.json.");
            throw new ArgumentNullException(nameof(ApiKey));
        }
        if (string.IsNullOrEmpty(BaseDirectory))
        {
            Console.WriteLine("Error: BaseDirectory no está configurada en appsettings.json.");
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

    private static async Task<string> UploadAudioFileAsync(string filePath)
    {
        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var response = await httpClient.PostAsync("https://api.assemblyai.com/v2/upload", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                await LogAsync($"Error al subir el archivo completo. StatusCode: {response.StatusCode}, Respuesta: {errorResponse}");
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(jsonResponse);
            return result.upload_url;
        }
        catch (Exception ex)
        {
            await LogAsync($"Excepción durante la subida completa: {ex.Message}");
            Console.WriteLine($"Excepción en UploadAudioFileAsync: {ex.Message}");
            return null;
        }
    }

    private static async Task<string> RequestTranscriptionAsync(string audioUrl)
    {
        var requestBody = new
        {
            audio_url = audioUrl,
            language_code = "es",
            speaker_labels = false,
            iab_categories = false,
            auto_highlights = false
        };

        var json = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("https://api.assemblyai.com/v2/transcript", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorResponse = await response.Content.ReadAsStringAsync();
            await LogAsync($"Error al solicitar la transcripción. StatusCode: {response.StatusCode}, Respuesta: {errorResponse}");
            Console.WriteLine($"Error al solicitar la transcripción. Detalles: {errorResponse}");
            return null;
        }

        var jsonResponse = await response.Content.ReadAsStringAsync();
        dynamic result = JsonConvert.DeserializeObject(jsonResponse);
        return result.id;
    }

    private static async Task<string> GetTranscriptionResultAsync(string transcriptId)
    {
        int maxWaitMinutes = 60;
        DateTime startTime = DateTime.UtcNow;

        int delay = 5000;
        int maxDelay = 30000;
        int currentDelay = delay;

        while (true)
        {
            double elapsedMinutes = (DateTime.UtcNow - startTime).TotalMinutes;
            if (elapsedMinutes > maxWaitMinutes)
            {
                Console.WriteLine("Tiempo máximo de espera excedido. Cancelando...");
                if (EnableLogging)
                    await LogAsync("Tiempo máximo de espera excedido.");
                return null;
            }

            var response = await httpClient.GetAsync($"https://api.assemblyai.com/v2/transcript/{transcriptId}");
            if (!response.IsSuccessStatusCode)
            {
                if (EnableLogging)
                    await LogAsync("Error al obtener resultado de la transcripción.");
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
                        await LogAsync($"Transcripción falló o error: {errorMessage}");
                    Console.WriteLine($"Transcripción falló o marcó error: {errorMessage}");
                    return null;

                default:
                    Console.WriteLine($"Estado: {status}. Esperando {currentDelay / 1000} seg...");
                    await Task.Delay(currentDelay);
                    currentDelay = Math.Min(currentDelay * 2, maxDelay);
                    break;
            }
        }
    }

    private static async Task LogAsync(string message)
    {
        if (!EnableLogging) return;

        string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";
        try
        {
            System.IO.File.AppendAllText(Path.Combine(BaseDirectory, "log.txt"), logMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al escribir en el log: {ex.Message}");
        }
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

        long maxSize = 2L * 1024L * 1024L * 1024L; // 2 GB
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
}
