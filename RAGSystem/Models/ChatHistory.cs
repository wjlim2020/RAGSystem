public class ChatHistory
{
    public int Id { get; set; }
    public string SessionId { get; set; }     // 前端送來的對話 ID
    public string UserQuery { get; set; }
    public string Answer { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
