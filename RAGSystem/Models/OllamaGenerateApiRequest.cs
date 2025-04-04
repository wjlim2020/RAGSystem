using System.Text.Json.Serialization;

namespace RAGSystem.Models;

public class OllamaGenerateApiRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "deepseek-r1:1.5b";
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;
    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;
    [JsonPropertyName("top_p")]
    public double TopP { get; set; } = 0.9;
}