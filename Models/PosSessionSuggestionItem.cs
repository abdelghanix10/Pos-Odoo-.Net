namespace OdooPosLogin;

public class PosSessionSuggestionItem
{
    public string Name { get; set; } = string.Empty;
    public string ShopName { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public bool IsAllSessions { get; set; }
    public bool IsSelected { get; set; }
    public bool IsEmptyPlaceholder { get; set; }
}
