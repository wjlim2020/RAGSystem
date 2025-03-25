using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using DocumentFormat.OpenXml.Packaging; // Word & PPT
using DocumentFormat.OpenXml.Wordprocessing;
using OfficeOpenXml; // Excel (EPPlus)
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;


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
            // Provide default values for temperature and topP
            float defaultTemperature = 0.2f;
            float defaultTopP = 0.2f;

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

