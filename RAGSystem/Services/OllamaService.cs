using System.Text;
using System.Text.RegularExpressions;

public interface IOllamaService
{
    Task<string> QueryAsync(string query, float temperature, float topP);
    Task<float[]> GenerateEmbeddingAsync(string content);

    Task InsertToQdrantAsync(string id, float[] embedding, string fileName, string content);

    Task<string> SearchQdrantAsync(float[] embedding, int topK = 3);

    public string OllamaApiUrl { get; }
    public string QdrantApiUrl { get; }
    public string OllamaApiGenerateUrl { get; }
    public string OllamaApiEmbeddingUrl { get; }
    public string QdrantFastApiUrl { get; }
    public string QdrantDbUrl { get; }
}
public class OllamaService : IOllamaService
{
    private readonly ApplicationDbContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;
    private readonly IConfiguration _configuration;

    public OllamaService(
        ApplicationDbContext context,
        HttpClient httpClient,
        ILogger<OllamaService> logger,
        IConfiguration configuration)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
        _configuration = configuration;
    }

    public string OllamaApiUrl
    {
        get => _configuration["OllamaApi:BaseUrl"] + "api/";
    }
    public string QdrantApiUrl
    {
        get => _configuration["Qdrant:BaseUrl"]!;
    }
    public string OllamaApiGenerateUrl
    {
        get => OllamaApiUrl + "generate";
    }
    public string OllamaApiEmbeddingUrl
    {
        get => OllamaApiUrl + "embeddings";
    }
    public string QdrantFastApiUrl
    {
        get => QdrantApiUrl + ":" + _configuration["Qdrant:FastApiPort"];
    }
    public string QdrantDbUrl
    {
        get => QdrantApiUrl + ":" + _configuration["Qdrant:QdrantPort"];
    }

    public async Task<string> QueryAsync(string query, float temperature, float topP)
    {
        try
        {
            _logger.LogInformation("🔍 Generating embedding for query: {Query}", query);

            // 1️⃣ 呼叫 Ollama embedding API
            var queryEmbedding = await GenerateEmbeddingAsync(query);

            // 2️⃣ 傳送 embedding 至 Qdrant Gateway /search
            var qdrantRequest = new
            {
                embedding = queryEmbedding,
                top_k = 10
            };

            var qdrantSearchUrl = QdrantFastApiUrl + "/search";
            var qdrantResponse = await _httpClient.PostAsJsonAsync(qdrantSearchUrl, qdrantRequest);
            qdrantResponse.EnsureSuccessStatusCode();

            var qdrantResult = await qdrantResponse.Content.ReadFromJsonAsync<QdrantSearchResponse>();

            if (qdrantResult == null || qdrantResult.Results.Count == 0)
            {
                _logger.LogWarning("⚠️ Qdrant 沒有找到相關內容");
                return "No relevant documents found.";
            }

            // 3️⃣ 額外呼叫 Qdrant 原生 scroll，查出 chunkType = 名稱 的所有 spotName
            var scrollPayload = new
            {
                collection_name = "rag_collection",
                limit = 1000,
                with_payload = true,
                filter = new
                {
                    must = new[]
                    {
                    new
                    {
                        key = "chunkType",
                        match = new { value = "名稱" }
                    }
                }
                }
            };
            // ✅ 這是你漏掉的部分
            var scrollUrl = QdrantDbUrl + "/collections/rag_collection/points/scroll";
            var scrollResponse = await _httpClient.PostAsJsonAsync(scrollUrl, scrollPayload);
            scrollResponse.EnsureSuccessStatusCode();

            var scrollResult = await scrollResponse.Content.ReadFromJsonAsync<QdrantScrollResponse>();

            _logger.LogInformation("🌐 Scroll spot names from Qdrant: {Count} 項", scrollResult?.Points?.Count ?? 0);

            var allSpotNames = (scrollResult?.Points ?? [])
                .Select(p =>
                {
                    // 如果 payload 有 spotName 就用，否則 fallback 用 content 抓 **景點名稱**
                    if (!string.IsNullOrEmpty(p?.Payload?.SpotName))
                        return p.Payload.SpotName.Trim();

                    // 嘗試從 content 中用 **XX** 格式抓名稱
                    var match = Regex.Match(p?.Payload?.Content ?? "", @"景點名稱：\*\*(.*?)\*\*");
                    return match.Success ? match.Groups[1].Value.Trim() : null;
                })
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .ToList();

            //// 3️⃣ 組成 prompt
            //var promptBuilder = new StringBuilder();

            //// 🧠 自動收集所有 spotName（景點名稱）
            //var spotNames = qdrantResult.Results
            //    .Where(r => r.ChunkType == "名稱" || r.Content.Contains("景點名稱"))
            //    .Select(r => !string.IsNullOrEmpty(r.SpotName)
            //        ? r.SpotName.Trim()
            //        : Regex.Match(r.Content, @"\*\*(.*?)\*\*").Groups[1].Value?.Trim())
            //    .Where(name => !string.IsNullOrEmpty(name))
            //    .Distinct()
            //    .ToList();


            //if (spotNames.Any())
            //{
            //    promptBuilder.AppendLine("📍 目前收錄的景點包括：");
            //    foreach (var spot in spotNames)
            //    {
            //        promptBuilder.AppendLine($"- {spot}");
            //    }
            //    promptBuilder.AppendLine("\n---\n");
            //}

            //// 📚 加入每段 context 資料
            //foreach (var doc in qdrantResult.Results)
            //{
            //    promptBuilder.AppendLine($"[Context from {doc.FileName}]\n{doc.Content}\n");
            //}


            // 4️⃣ 組 Prompt
            var promptBuilder = new StringBuilder();

            if (allSpotNames.Any())
            {
                promptBuilder.AppendLine("📍 目前收錄的景點包括：");
                foreach (var spot in allSpotNames)
                {
                    promptBuilder.AppendLine($"- {spot}");
                }
                promptBuilder.AppendLine("\n---\n");
            }

            foreach (var doc in qdrantResult.Results)
            {
                promptBuilder.AppendLine($"[Context from {doc.FileName}]\n{doc.Content}\n");
            }


            string fullPrompt = $@"
你是一位專業的日本旅遊導覽員。請根據以下提供的旅遊資料，回答使用者的問題。請注意：

- 如果問題涉及「費用」、「開放時間」、「交通方式」，請務必明確列出具體數字。
- 若資料中有相關資訊但未直接明講，請根據上下文合理推理。
- 如果使用者詢問「有哪些景點」、「推薦哪些地點」等類型的問題，請列出所有景點的清單，讓使用者自行選擇下一步想了解的項目。
- 回答要精簡明確，可使用條列或表格格式。
- 如果資料中包含多個景點，請務必完整列出所有景點名稱，並使用條列式。
- 請依照以下格式回應：
    1. 景點名稱
    2. 開放時間
    3. 交通方式
    4. 推薦行程
    5. 預估費用
    （如有多個景點請依序列出）
-請務必只用繁體中文完整回答，不要使用任何英文詞彙（例如：ride、station、ticket），如果資料中原本為英文，請翻譯為繁體中文再顯示。
-請完整使用繁體中文回答，禁止回答任何英文詞彙（例如：ride、suggest、location 等），請全部翻譯為繁體中文。
-如果你發現自己使用了英文單詞，請立刻修正為繁體中文再重新回答。
請使用 <think> 包住分析過程，然後換行寫下使用者會看到的回覆。
以下是旅遊資料（可包含多個地點）：
{promptBuilder}

---

使用者問題：
{query}

請根據上方資料直接作答，不要杜撰內容。

";

            // 4️⃣ 傳送 prompt 至 DeepSeek 模型
            var response = await _httpClient.PostAsJsonAsync(OllamaApiGenerateUrl, new
            {
                model = "deepseek-r1:1.5b",  // 或 "deepseek-r1:7b",
                prompt = fullPrompt,
                temperature,
                top_p = topP,
            });

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("✅ 回應內容：{Result}", result);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "❌ 發送請求失敗: {Message}", ex.Message);
            throw new Exception("Failed to query DeepSeek or Qdrant. Please ensure all services are running.", ex);
        }
    }



    public async Task<float[]> GenerateEmbeddingAsync(string content)
    {
        try
        {
            _logger.LogInformation("Sending embedding request to Ollama: {content}", content);

            var response = await _httpClient.PostAsJsonAsync(OllamaApiEmbeddingUrl, new
            {
                model = "bge-m3",
                prompt = content
            });

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Ollama embedding response: {responseContent}", responseContent);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();

            if (result?.Embedding == null)
            {
                throw new Exception("Embedding is null.");
            }

            return result.Embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding request failed: {Message}", ex.Message);
            throw new Exception($"Embedding request failed: {ex.Message}", ex);
        }
    }



    public async Task InsertToQdrantAsync(string id, float[] embedding, string fileName, string content)
    {
        string insertUrl = QdrantFastApiUrl + "/insert";

        await _httpClient.PostAsJsonAsync(insertUrl, new
        //await _httpClient.PostAsJsonAsync("http://localhost:5002/clean_insert", new
        {
            id = Guid.NewGuid().ToString(),  // ✅ 自動產生 UUID 字串
            embedding = embedding,
            fileName = fileName,
            content = content
        });
    }

    public async Task<string> SearchQdrantAsync(float[] embedding, int topK = 2)
    {
        string searchUrl = QdrantFastApiUrl + "/search";
        var response = await _httpClient.PostAsJsonAsync(searchUrl, new
        {
            embedding = embedding,
            top_k = topK
        });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }




    // Response model for embedding API
    public class EmbeddingResponse
    {
        public float[] Embedding { get; set; }
    }

    //public class QdrantMatch
    //{
    //    public string FileName { get; set; }
    //    public string Content { get; set; }
    //}

    public class QdrantSearchResponse
    {
        public List<QdrantMatch> Results { get; set; } = new();
    }

    public class QdrantMatch
    {
        public string Id { get; set; }
        public double Score { get; set; }
        public string FileName { get; set; }
        public string Content { get; set; }

        // 加上這兩個對應 payload 欄位
        public string ChunkType { get; set; }
        public string SpotName { get; set; }
    }

    public class QdrantScrollResponse
    {
        public List<QdrantScrollPoint> Points { get; set; }
    }

    public class QdrantScrollPoint
    {
        public QdrantPayload Payload { get; set; }
    }

    public class QdrantPayload
    {
        public string SpotName { get; set; }
        public string ChunkType { get; set; }
        public string Content { get; set; } // ⬅️ 這個是 fallback 要用的
    }


}