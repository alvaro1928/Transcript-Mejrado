using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AssemblyAI;
using AssemblyAI.Transcripts;
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
        Timeout = TimeSpan.FromMinutes(10) // 10 minutos de timeout global
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

            var client = new AssemblyAIClient(ApiKey);

            // Subir archivo local
            Console.WriteLine("Subiendo el archivo local...");
            if (EnableLogging)
                await LogAsync("Subiendo el archivo local...");

            var uploadedFile = await UploadAudioFileAsync(FilePath);
            if (string.IsNullOrEmpty(uploadedFile))
            {
                Console.WriteLine("Error: No se pudo subir el archivo.");
                return;
            }

            Console.WriteLine($"Archivo subido exitosamente. URL: {uploadedFile}");
            if (EnableLogging)
                await LogAsync($"Archivo subido exitosamente. URL: {uploadedFile}");

            // Iniciar transcripción
            Console.WriteLine("Iniciando la transcripción...");
            if (EnableLogging)
                await LogAsync("Iniciando la transcripción...");

            var transcriptId = await RequestTranscriptionAsync(uploadedFile);
            if (string.IsNullOrEmpty(transcriptId))
            {
                Console.WriteLine("Error al solicitar la transcripción.");
                return;
            }

            Console.WriteLine($"Transcripción solicitada exitosamente. ID: {transcriptId}");
            if (EnableLogging)
                await LogAsync($"Transcripción solicitada exitosamente. ID: {transcriptId}");

            // Obtener el resultado de la transcripción
            string transcriptText = await GetTranscriptionResultAsync(transcriptId);
            if (string.IsNullOrEmpty(transcriptText))
            {
                Console.WriteLine("Error: No se pudo obtener la transcripción.");
                return;
            }

            Console.WriteLine("Transcripción completada:");
            Console.WriteLine(transcriptText);

            if (EnableLogging)
                await LogAsync("Transcripción completada con éxito.");

            // Guardar resultados en un archivo
            string outputFilePath = Path.Combine(BaseDirectory, "transcription_output.txt");
            await File.WriteAllTextAsync(outputFilePath, transcriptText);

            Console.WriteLine($"Transcripción guardada en: {outputFilePath}");
            if (EnableLogging)
                await LogAsync($"Transcripción guardada en: {outputFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            if (EnableLogging)
                await LogAsync($"Error: {ex.Message}");
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

        ApiKey = configuration["ApiKey"];
        BaseDirectory = configuration["BaseDirectory"];
        string inputFileName = configuration["InputFileName"];
        EnableLogging = bool.TryParse(configuration["EnableLogging"], out bool enableLogging) && enableLogging;

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
        if (string.IsNullOrEmpty(inputFileName))
        {
            Console.WriteLine("Error: InputFileName no está configurado en appsettings.json.");
            throw new ArgumentNullException(nameof(inputFileName));
        }

        BaseDirectory = Path.GetFullPath(BaseDirectory);
        FilePath = Path.GetFullPath(Path.Combine(BaseDirectory, inputFileName));

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
    }

    private static async Task<string> UploadAudioFileAsync(string filePath)
    {
        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var response = await httpClient.PostAsync("https://api.assemblyai.com/v2/upload", content, cancellationTokenSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                await LogAsync($"Error al subir el archivo. StatusCode: {response.StatusCode}, Respuesta: {errorResponse}");
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(jsonResponse);
            return result.upload_url;
        }
        catch (TaskCanceledException ex)
        {
            await LogAsync("La subida fue cancelada debido a un timeout.");
            Console.WriteLine("Error: La subida fue cancelada debido a un timeout.");
            return null;
        }
        catch (Exception ex)
        {
            await LogAsync($"Excepción durante la subida: {ex.Message}");
            Console.WriteLine($"Error: {ex.Message}");
            return null;
        }
    }

    private static async Task<string> RequestTranscriptionAsync(string audioUrl)
    {
        var requestBody = new
        {
            audio_url = audioUrl,
            language_code = "es",
            speaker_labels = false
        };

        var json = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var response = await httpClient.PostAsync("https://api.assemblyai.com/v2/transcript", content, cancellationTokenSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                await LogAsync($"Error al solicitar la transcripción. StatusCode: {response.StatusCode}, Respuesta: {errorResponse}");
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject(jsonResponse);
            return result.id;
        }
        catch (TaskCanceledException ex)
        {
            await LogAsync("La solicitud de transcripción fue cancelada debido a un timeout.");
            Console.WriteLine("Error: La solicitud de transcripción fue cancelada debido a un timeout.");
            return null;
        }
        catch (Exception ex)
        {
            await LogAsync($"Excepción durante la solicitud de transcripción: {ex.Message}");
            Console.WriteLine($"Error: {ex.Message}");
            return null;
        }
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

            try
            {
                var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var response = await httpClient.GetAsync($"https://api.assemblyai.com/v2/transcript/{transcriptId}", cancellationTokenSource.Token);

                if (!response.IsSuccessStatusCode)
                {
                    if (EnableLogging)
                        await LogAsync("Error al obtener resultado de la transcripción.");
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
                        await LogAsync($"Transcripción falló. Error: {result.error}");
                    return null;
                }
                else
                {
                    Console.WriteLine($"Estado: {status}. Esperando {currentDelay / 1000} segundos...");
                    await Task.Delay(currentDelay);
                    currentDelay = Math.Min(currentDelay * 2, maxDelay);
                }
            }
            catch (TaskCanceledException ex)
            {
                await LogAsync("La solicitud de resultado de transcripción fue cancelada por timeout.");
                Console.WriteLine("Error: La solicitud de resultado fue cancelada por timeout.");
                return null;
            }
            catch (Exception ex)
            {
                await LogAsync($"Excepción durante la solicitud de resultado: {ex.Message}");
                Console.WriteLine($"Error: {ex.Message}");
                return null;
            }
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
