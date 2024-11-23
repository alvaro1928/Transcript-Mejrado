using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;

public class Program
{
    private static string ApiKey;
    private static string BaseDirectory;
    private static string FilePath;
    private static string LogFilePath;
    private static bool EnableLogging;

    // Reutiliza HttpClient con un tiempo de espera extendido
    private static readonly HttpClient httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public static async Task Main(string[] args)
    {
        try
        {
            // Cargar configuración desde appsettings.json
            LoadConfiguration();

            // Definir la ruta del archivo de log **después** de cargar la configuración
            LogFilePath = Path.Combine(BaseDirectory, "log.txt");

            if (EnableLogging)
                await LogAsync("Programa iniciado.");

            Console.WriteLine("Iniciando el proceso de transcripción...");

            // Paso 1: Subir el archivo local a AssemblyAI y obtener la URL de acceso
            if (EnableLogging)
                await LogAsync("Iniciando la subida del archivo...");
            Console.WriteLine("Subiendo el archivo de audio...");
            string audioUrl = await UploadAudioFileAsync(FilePath);
            if (string.IsNullOrEmpty(audioUrl))
            {
                if (EnableLogging)
                    await LogAsync("Error al subir el archivo de audio.");
                Console.WriteLine("Error al subir el archivo de audio.");
                return;
            }
            if (EnableLogging)
                await LogAsync("Archivo subido exitosamente.");
            Console.WriteLine("Archivo subido exitosamente.");

            // Paso 2: Solicitar transcripción con la URL generada
            if (EnableLogging)
                await LogAsync("Solicitando transcripción...");
            Console.WriteLine("Solicitando la transcripción...");
            var transcriptId = await RequestTranscriptionAsync(audioUrl);
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

            // Paso 3: Obtener el resultado de la transcripción
            if (EnableLogging)
                await LogAsync("Esperando la finalización de la transcripción...");
            Console.WriteLine("Esperando la finalización de la transcripción...");
            string transcriptText = await GetTranscriptionResultAsync(transcriptId);
            if (!string.IsNullOrEmpty(transcriptText))
            {
                string videoFileName = Path.GetFileNameWithoutExtension(FilePath); // Obtiene el nombre del video sin la extensión
                string uniqueFileName = Path.Combine(BaseDirectory, $"{videoFileName}_transcript_{Guid.NewGuid()}.txt");

                await File.WriteAllTextAsync(uniqueFileName, transcriptText);
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

            // Mantener la consola abierta al finalizar
            Console.WriteLine("Proceso completado. Presione cualquier tecla para salir...");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            if (EnableLogging)
                await LogAsync($"Error inesperado: {ex.Message}");
            Console.WriteLine($"Error inesperado: {ex.Message}");

            // Mantener la consola abierta en caso de error
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
            .SetBasePath(AppContext.BaseDirectory) // Usar el directorio base de la aplicación
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        ApiKey = configuration["AssemblyAI:ApiKey"];
        BaseDirectory = configuration["AssemblyAI:BaseDirectory"];
        FilePath = Path.Combine(BaseDirectory, configuration["AssemblyAI:InputFileName"]);
        EnableLogging = bool.Parse(configuration["AssemblyAI:EnableLogging"]);

        // Validaciones
        if (string.IsNullOrEmpty(ApiKey))
        {
            Console.WriteLine("Error: ApiKey no está configurada en appsettings.json.");
            throw new ArgumentNullException("ApiKey", "La clave de API no puede ser nula o vacía.");
        }

        if (string.IsNullOrEmpty(BaseDirectory))
        {
            Console.WriteLine("Error: BaseDirectory no está configurada en appsettings.json.");
            throw new ArgumentNullException("BaseDirectory", "BaseDirectory no puede ser nula o vacía.");
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
        httpClient.DefaultRequestHeaders.Clear(); // Limpiar cualquier encabezado existente
        httpClient.DefaultRequestHeaders.Add("Authorization", ApiKey);
    }

    private static async Task<string> UploadAudioFileAsync(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        var content = new StreamContent(fileStream);
        content.Headers.Add("Content-Type", "application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.assemblyai.com/v2/upload")
        {
            Content = content
        };

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            if (EnableLogging)
                await LogAsync("Error al subir el archivo a AssemblyAI.");
            return null;
        }

        var jsonResponse = await response.Content.ReadAsStringAsync();
        dynamic result = JsonConvert.DeserializeObject(jsonResponse);
        return result.upload_url;
    }

    private static async Task<string> RequestTranscriptionAsync(string audioUrl)
    {
        var requestBody = new
        {
            audio_url = audioUrl,
            language_code = "es" // Código para español
        };
        var json = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("https://api.assemblyai.com/v2/transcript", content);

        if (!response.IsSuccessStatusCode)
        {
            if (EnableLogging)
                await LogAsync("Error al solicitar la transcripción.");
            return null;
        }

        var jsonResponse = await response.Content.ReadAsStringAsync();
        dynamic result = JsonConvert.DeserializeObject(jsonResponse);
        return result.id;
    }

    private static async Task<string> GetTranscriptionResultAsync(string transcriptId)
    {
        int delay = 5000; // Tiempo de espera inicial (5 segundos)
        int maxDelay = 30000; // Máximo tiempo de espera entre intentos (30 segundos)
        int currentDelay = delay;

        while (true)
        {
            var response = await httpClient.GetAsync($"https://api.assemblyai.com/v2/transcript/{transcriptId}");
            if (!response.IsSuccessStatusCode)
            {
                if (EnableLogging)
                    await LogAsync("Error al obtener el resultado de la transcripción.");
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(jsonResponse);
            string status = result.status;

            if (status == "completed")
            {
                return result.text;
            }
            else if (status == "failed")
            {
                if (EnableLogging)
                    await LogAsync("La transcripción falló.");
                return null;
            }

            // Espera antes de verificar de nuevo
            Console.WriteLine($"Estado: {status}. Esperando {currentDelay / 1000} segundos...");
            await Task.Delay(currentDelay);
            currentDelay = Math.Min(currentDelay * 2, maxDelay);
        }
    }

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
