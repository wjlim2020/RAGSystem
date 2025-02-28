using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

[ApiController]
[Route("api/[controller]")]
public class RagController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<RagController> _logger;

    // ✅ Inject ApplicationDbContext in the constructor
    public RagController(ApplicationDbContext context, IOllamaService ollamaService, ILogger<RagController> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _ollamaService = ollamaService ?? throw new ArgumentNullException(nameof(ollamaService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    //public RagController(IOllamaService ollamaService)
    //{
    //    _ollamaService = ollamaService;
    //}

    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] string query)
    {
        try
        {
            var responseJson = await _ollamaService.QueryAsync(query);

            // 🔹 Format response into readable text
            string formattedResponse = ExtractResponses(responseJson);

            return Ok(new { message = formattedResponse });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error processing query: {Message}", ex.Message);
            return StatusCode(500, new { message = "Error processing query." });
        }
    }

    // ✅ Function to format DeepSeek response
    private string ExtractResponses(string jsonString)
    {
        try
        {
            if (!jsonString.Trim().StartsWith("["))
            {
                jsonString = "[" + jsonString.Replace("}\n{", "},{") + "]";
            }

            var responseList = JsonSerializer.Deserialize<List<ResponseData>>(jsonString);

            return responseList != null && responseList.Any()
                ? string.Join("", responseList.Select(r => r.Response))
                : "No valid response";
        }
        catch
        {
            return "Error processing JSON.";
        }
    }


[HttpPost("upload")]
    public async Task<IActionResult> UploadFile([FromForm] UploadFileDto uploadFileDto)
    {
        if (uploadFileDto.File == null || uploadFileDto.File.Length == 0)
            return BadRequest("No file uploaded.");

        try
        {
            using var reader = new StreamReader(uploadFileDto.File.OpenReadStream());
            var content = await reader.ReadToEndAsync();

            // 🔹 Generate embeddings using the API
            var embeddingArray = await _ollamaService.GenerateEmbeddingAsync(content);

            // ✅ Convert float[] to comma-separated string
            var embeddingString = string.Join(",", embeddingArray.Select(f => f.ToString(CultureInfo.InvariantCulture)));

            var document = new Document
            {
                FileName = uploadFileDto.File.FileName,
                Content = content,
                Embedding = embeddingArray,
                CreatedAt = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            return Ok(new { message = "File uploaded successfully.", documentId = document.Id });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to upload file: {ex.Message}");
        }
    }



    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] int documentId)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null || document.Embedding == null || document.Embedding.Length == 0)
            return NotFound("Document not found or has no embeddings.");

        return Ok(new { documentId, embedding = document.Embedding });
    }

    // ✅ Define ResponseData Model
    public class ResponseData
    {
        [JsonPropertyName("response")]
        public string Response { get; set; }
    }
}

