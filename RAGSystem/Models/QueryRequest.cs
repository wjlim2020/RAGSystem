namespace RAGSystem.Models
{
    public class QueryRequest
    {
        public string Query { get; set; }
        public float Temperature { get; set; } = 0.7f; // Default 0.7
        public float TopP { get; set; } = 0.9f;       // Default 0.9
    }
}
