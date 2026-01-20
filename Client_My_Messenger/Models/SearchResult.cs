using Client_My_Messenger.Models;

public class SearchResult
{
    public string Type { get; set; } = string.Empty; // "chat", "user", "info"
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsJoined { get; set; }
    public double Similarity { get; set; }
    public int MemberCount { get; set; }
    public string ChatType { get; set; } = string.Empty;
    public UserObj User { get; set; }
    public Chat Chat { get; set; }
}