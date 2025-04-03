namespace RAGSystem.Models
{
    public class QueryRequest
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();  // 新增這一行
        public string Query { get; set; }
        public float Temperature { get; set; } = 0.2f;
        public float TopP { get; set; } = 0.2f;
    }
}