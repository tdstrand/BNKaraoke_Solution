namespace BNKaraoke.Api.Models;

public class PinChangeHistory
{
    public int Id { get; set; }
    public string Pin { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
}