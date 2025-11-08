using System.ComponentModel.DataAnnotations;

namespace BNKaraoke.Api.Models;

public class ReorderSuggestionResponse
{
    public List<ReorderSuggestion> Suggestions { get; set; } = new();
}

public class ReorderSuggestion
{
    public int QueueId { get; set; }
    public string SingerName { get; set; } = string.Empty;
    public int CurrentPosition { get; set; }
    public int SuggestedPosition { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double Score { get; set; }
}

public class ApplyReorderRequest
{
    [Required]
    public int EventId { get; set; }

    [Required]
    [MinLength(1)]
    public List<ApplyReorderItem> Reorder { get; set; } = new();
}

public class ApplyReorderItem
{
    [Required]
    public int QueueId { get; set; }

    [Required]
    public int SuggestedPosition { get; set; }
}
