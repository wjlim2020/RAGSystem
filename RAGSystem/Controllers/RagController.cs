﻿using DocumentFormat.OpenXml.Packaging; // Word & PPT
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml; // Excel (EPPlus)
using RAGSystem.Models;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;


[ApiController]
[Route("api/[controller]")]
public class RagController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<RagController> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    // ✅ Inject ApplicationDbContext in the constructor
    public RagController(
        ApplicationDbContext context,
        IOllamaService ollamaService,
        ILogger<RagController> logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _ollamaService = ollamaService ?? throw new ArgumentNullException(nameof(ollamaService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient;
        _configuration = configuration;
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
            // Provide default values for temperature and topP
            float defaultTemperature = 0.4f;
            float defaultTopP = 0.8f;

            var responseJson = await _ollamaService.QueryAsync(query, defaultTemperature, defaultTopP);

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

    //[HttpPost("query-v2")]
    //public async Task<IActionResult> QueryWithMemory([FromBody] QueryRequest request)
    //{
    //    try
    //    {
    //        // 🔍 撈對話歷史
    //        var historyList = await _context.ChatHistories
    //            .Where(c => c.SessionId == request.SessionId)
    //            .OrderByDescending(c => c.Timestamp)
    //            .Take(5)
    //            .ToListAsync();

    //        historyList.Reverse(); // 調整順序：舊 → 新

    //        var sb = new StringBuilder();
    //        sb.AppendLine("Use the following conversation history to answer the user's question.\n");

    //        foreach (var h in historyList)
    //        {
    //            sb.AppendLine($"User: {h.UserQuery}");
    //            sb.AppendLine($"Assistant: {h.Answer}");
    //        }

    //        sb.AppendLine($"User: {request.Query}");
    //        sb.AppendLine("Assistant:");

    //        string prompt = sb.ToString();

    //        // ✅ 設定你要的預設參數
    //        float defaultTemperature = 0.7f;
    //        float defaultTopP = 0.2f;

    //        // ✅ 呼叫 Ollama 查詢
    //        var result = await _ollamaService.QueryAsync(
    //            prompt,
    //            defaultTemperature,
    //            defaultTopP
    //        );

    //        var formattedAnswer = ExtractResponses(result);

    //        // ✅ 儲存對話
    //        _context.ChatHistories.Add(new ChatHistory
    //        {
    //            SessionId = request.SessionId,
    //            UserQuery = request.Query,
    //            Answer = formattedAnswer
    //        });
    //        await _context.SaveChangesAsync();

    //        return Ok(new { message = formattedAnswer, sessionId = request.SessionId });
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Query failed");
    //        return StatusCode(500, new { message = "Query failed", detail = ex.Message });
    //    }
    //}
    [HttpPost("query-v2")]
    public async Task<IActionResult> QueryWithMemory([FromBody] QueryRequest request)
    {
        try
        {
            // 🔍 撈對話歷史
            var historyList = await _context.ChatHistories
                .Where(c => c.SessionId == request.SessionId)
                .OrderByDescending(c => c.Timestamp)
                .Take(1)
                .ToListAsync();

            historyList.Reverse(); // 調整順序：舊 → 新

            var historyPrompt = new StringBuilder();
            historyPrompt.AppendLine("Use the following conversation history to answer the user's question.\n");

            foreach (var h in historyList)
            {
                historyPrompt.AppendLine($"User: {h.UserQuery}");
                historyPrompt.AppendLine($"Assistant: {h.Answer}");
            }

            // ✅ 呼叫 embedding API，取得查詢向量
            var embedding = await _ollamaService.GenerateEmbeddingAsync(request.Query);

            // ✅ 呼叫 Qdrant 查詢相關內容
            var qdrantRequest = new
            {
                embedding = embedding,
                top_k = 10
            };

            string qdrantUrl = _ollamaService.QdrantFastApiUrl + "/search";
            var qdrantResponse = await _httpClient.PostAsJsonAsync(qdrantUrl, qdrantRequest);
            qdrantResponse.EnsureSuccessStatusCode();
            var qdrantResult = await qdrantResponse.Content.ReadFromJsonAsync<OllamaService.QdrantSearchResponse>();

            var contextPrompt = new StringBuilder();
            foreach (var doc in qdrantResult.Results)
            {
                contextPrompt.AppendLine($"[Context from {doc.FileName}]\n{doc.Content}\n");
            }

            // ✅ 最終 prompt 結合歷史、向量上下文與用戶提問
            var fullPrompt = new StringBuilder();
            fullPrompt.AppendLine(historyPrompt.ToString());
            fullPrompt.AppendLine(contextPrompt.ToString());
            fullPrompt.AppendLine($"\nUser: {request.Query}");
            fullPrompt.AppendLine("Assistant:");

            var result = await _ollamaService.QueryAsync(fullPrompt.ToString(), request.Temperature, request.TopP);
            var formattedAnswer = ExtractResponses(result);

            // ✅ 儲存對話
            _context.ChatHistories.Add(new ChatHistory
            {
                SessionId = request.SessionId,
                UserQuery = request.Query,
                Answer = formattedAnswer
            });
            await _context.SaveChangesAsync();

            return Ok(new { message = formattedAnswer, sessionId = request.SessionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Query-v2 failed");
            return StatusCode(500, new { message = "Query-v2 failed", detail = ex.Message });
        }
    }


    [HttpPost("query-v3")]
    public async IAsyncEnumerable<string> QueryWithMemoryStream(
        [FromBody] QueryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IHttpResponseBodyFeature? httpResponseBodyFeature = HttpContext.Features.Get<IHttpResponseBodyFeature>();
        httpResponseBodyFeature?.DisableBuffering();

        var historyList = await _context.ChatHistories
            .Where(c => c.SessionId == request.SessionId)
            .OrderByDescending(c => c.Timestamp)
            .Take(1)
            .ToListAsync();
        historyList.Reverse();

        var historyPrompt = new StringBuilder();
        historyPrompt.AppendLine("Use the following conversation history to answer the user's question.\n");

        foreach (var h in historyList)
        {
            historyPrompt.AppendLine($"User: {h.UserQuery}");
            historyPrompt.AppendLine($"Assistant: {h.Answer}");
        }

        var embedding = await _ollamaService.GenerateEmbeddingAsync(request.Query);
        var qdrantRequest = new
        {
            embedding = embedding,
            top_k = 10
        };

        string qdrantUrl = _ollamaService.QdrantFastApiUrl + "/search";
        var qdrantResponse = await _httpClient.PostAsJsonAsync(qdrantUrl, qdrantRequest);
        qdrantResponse.EnsureSuccessStatusCode();
        var qdrantResult = await qdrantResponse.Content.ReadFromJsonAsync<OllamaService.QdrantSearchResponse>();

        var contextPrompt = new StringBuilder();
        foreach (var doc in qdrantResult.Results)
        {
            contextPrompt.AppendLine($"[Context from {doc.FileName}]\n{doc.Content}\n");
        }

        // ✅ 最終 prompt 結合歷史、向量上下文與用戶提問
        var fullPrompt = new StringBuilder();
        fullPrompt.AppendLine(historyPrompt.ToString());
        fullPrompt.AppendLine(contextPrompt.ToString());
        fullPrompt.AppendLine($"\nUser: {request.Query}");
        fullPrompt.AppendLine("Assistant:");

        string fullResponse = string.Empty;

        await foreach (var chunk in _ollamaService.QueryStreamAsync(fullPrompt.ToString(), request.Temperature, request.TopP, cancellationToken))
        {
            if (chunk == null) continue;

            fullResponse += chunk;
            yield return chunk;
        }

        // ✅ 儲存對話
        _context.ChatHistories.Add(new ChatHistory
        {
            SessionId = request.SessionId,
            UserQuery = request.Query,
            Answer = fullResponse
        });
        await _context.SaveChangesAsync();
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile([FromForm] UploadFileDto uploadFileDto)
    {
        if (uploadFileDto.File == null || uploadFileDto.File.Length == 0)
            return BadRequest("No file uploaded.");

        try
        {
            // 🔹 Extract text content from Office files
            string content = ExtractTextFromOfficeFile(uploadFileDto.File);
            if (string.IsNullOrEmpty(content))
                return BadRequest("Unable to extract content from file.");

            // 🔹 Generate Embedding for the content
            var embeddingArray = await _ollamaService.GenerateEmbeddingAsync(content);
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

            // 🔹 呼叫 Qdrant Gateway 插入向量資料
            await _ollamaService.InsertToQdrantAsync(document.Id.ToString(), embeddingArray, document.FileName, document.Content);


            return Ok(new { message = "File uploaded successfully.", documentId = document.Id });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to upload file: {ex.Message}");
        }
    }




    //[HttpPost("search")]
    //public async Task<IActionResult> Search([FromBody] int documentId)
    //{
    //    var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId);

    //    if (document == null || document.Embedding == null || document.Embedding.Length == 0)
    //        return NotFound("Document not found or has no embeddings.");

    //    return Ok(new { documentId, embedding = document.Embedding });
    //}

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] string query)
    {
        try
        {
            // 🔹 產生 embedding
            var embedding = await _ollamaService.GenerateEmbeddingAsync(query);
            Console.WriteLine("Done");
            // 🔹 呼叫 Qdrant Gateway 查相似文件
            var json = await _ollamaService.SearchQdrantAsync(embedding);

            return Ok(new { message = "Search success", results = json });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search error: {Message}", ex.Message);
            return StatusCode(500, new { message = "Search failed", detail = ex.ToString() });
        }

    }



    // ✅ Define ResponseData Model
    public class ResponseData
    {
        [JsonPropertyName("response")]
        public string Response { get; set; }
    }



    private string ExtractTextFromOfficeFile(IFormFile file)
    {
        string extension = Path.GetExtension(file.FileName).ToLower();

        using var stream = file.OpenReadStream();

        if (extension == ".docx" || extension == ".doc")
        {
            return ExtractTextFromWord(stream);
        }
        else if (extension == ".xlsx" || extension == ".xls")
        {
            return ExtractTextFromExcel(stream);
        }
        else if (extension == ".pptx" || extension == ".ppt")
        {
            return ExtractTextFromPowerPoint(stream);
        }
        else if (extension == ".pdf")
        {
            return ExtractTextFromPdf(stream);
        }

        return null;
    }

    // 🔹 解析 Word 文件 (.docx)
    private string ExtractTextFromWord(Stream stream)
    {
        using WordprocessingDocument doc = WordprocessingDocument.Open(stream, false);
        StringBuilder sb = new();
        foreach (var text in doc.MainDocumentPart.Document.Body.Descendants<Text>())
        {
            sb.AppendLine(text.Text);
        }
        return sb.ToString();
    }

    // 🔹 解析 Excel 文件 (.xlsx)
    private string ExtractTextFromExcel(Stream stream)
    {
        using var package = new ExcelPackage(stream);
        StringBuilder sb = new();
        foreach (var worksheet in package.Workbook.Worksheets)
        {
            int rowCount = worksheet.Dimension.Rows;
            int colCount = worksheet.Dimension.Columns;
            for (int row = 1; row <= rowCount; row++)
            {
                for (int col = 1; col <= colCount; col++)
                {
                    sb.Append(worksheet.Cells[row, col].Text + "\t");
                }
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    // 🔹 解析 PowerPoint 文件 (.pptx)
    private string ExtractTextFromPowerPoint(Stream stream)
    {
        using PresentationDocument ppt = PresentationDocument.Open(stream, false);
        StringBuilder sb = new();
        foreach (var slide in ppt.PresentationPart.SlideParts)
        {
            foreach (var text in slide.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>())
            {
                sb.AppendLine(text.Text);
            }
        }
        return sb.ToString();
    }

    // 🟢 Extract text from PDF
    private string ExtractTextFromPdf(Stream stream)
    {
        StringBuilder sb = new();

        using PdfDocument document = PdfDocument.Open(stream);
        foreach (Page page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return sb.ToString();
    }
}

