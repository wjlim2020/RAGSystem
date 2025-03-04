using System.Net.Http.Json;
using RAGSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public interface IOllamaService
{
    Task<string> QueryAsync(string query, float temperature, float topP);
    Task<float[]> GenerateEmbeddingAsync(string content);
}
public class OllamaService : IOllamaService
{
    private readonly ApplicationDbContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;


    public OllamaService(ApplicationDbContext context, HttpClient httpClient, ILogger<OllamaService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    //public async Task<string> QueryAsync(string query)
    //{
    //    try
    //    {
    //        _logger.LogInformation("Sending query to Ollama: {Query}", query);

    //        // Send a POST request to the Ollama API with the full URL and correct body
    //        //var response = await _httpClient.PostAsJsonAsync("http://localhost:11434/api/generate", new { model = "deepseek-r1:1.5b", prompt = query });

    //        // In OllamaService.cs
    //        var response = await _httpClient.PostAsJsonAsync("api/generate", new
    //        {
    //            model = "deepseek-r1:1.5b",
    //            prompt = query
    //        });

    //        // Ensure the request was successful
    //        response.EnsureSuccessStatusCode();

    //        // Read and return the response content
    //        var result = await response.Content.ReadAsStringAsync();
    //        _logger.LogInformation("Received response from Ollama: {Result}", result);

    //        return result;
    //    }
    //    catch (HttpRequestException ex)
    //    {
    //        _logger.LogError(ex, "Failed to query Ollama: {Message}", ex.Message);
    //        throw new Exception("Failed to query Ollama. Please check the API URL and ensure the service is running.", ex);
    //    }
    //}


    public async Task<string> QueryAsync(string query, float temperature, float topP)
    {
        try
        {
            _logger.LogInformation("Retrieving database context for query: {Query}", query);

            // 🔹 Retrieve the most relevant document based on search
            var document = await _context.Documents
                .OrderByDescending(d => d.CreatedAt)  // Modify this to use vector similarity if needed
                .FirstOrDefaultAsync();

            if (document == null)
            {
                _logger.LogWarning("No relevant document found in the database.");
                return "No relevant data available in the database.";
            }

            // 🔹 Construct prompt with database context
            string fullPrompt = $"Context: {document.Content}\n\nUser Query: {query}\n\nAnswer strictly based on the given context.";

            _logger.LogInformation("Sending query to DeepSeek with database context...");

            var response = await _httpClient.PostAsJsonAsync("http://localhost:11434/api/generate", new
            {
                model = "deepseek-r1:1.5b",
                //model = "deepseek-r1:7b",
                prompt = fullPrompt,
                temperature = temperature,
                top_p = topP,
            });

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Received response from DeepSeek: {Result}", result);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to query DeepSeek: {Message}", ex.Message);
            throw new Exception("Failed to query DeepSeek. Please check the API URL and ensure the service is running.", ex);
        }
    }


    public async Task<float[]> GenerateEmbeddingAsync(string content)
    {
        try
        {
            _logger.LogInformation("Sending request to Ollama for embedding...");

            var response = await _httpClient.PostAsJsonAsync("http://localhost:11434/api/embeddings", new
            {
                model = "nomic-embed-text:latest",
                prompt = content
            });

            string responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"Ollama API Response: {responseContent}");

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();

            return result?.Embedding ?? throw new Exception("Failed to parse embedding response.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to query Ollama: {Message}", ex.Message);
            throw new Exception("Failed to generate embedding. Check API connection.", ex);
        }
    }

    // Response model for embedding API
    public class EmbeddingResponse
    {
        public float[] Embedding { get; set; }
    }


}