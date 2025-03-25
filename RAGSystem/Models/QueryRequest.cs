namespace RAGSystem.Models
{
    public class QueryRequest
    {
        public string Query { get; set; }
        public float Temperature { get; set; } = 0.2f; // Default 0.7
        public float TopP { get; set; } = 0.2f;       // Default 0.9
    }
}
