using System;
using System.ComponentModel.DataAnnotations;

namespace BNKaraoke.Api.Models
{
    public class Song
    {
        public int Id { get; set; }
        [Required]
        public string Title { get; set; } = string.Empty;
        [Required]
        public string Artist { get; set; } = string.Empty;
        public string? Genre { get; set; }
        public string? Decade { get; set; }
        public float? Bpm { get; set; }
        public float? Danceability { get; set; }
        public float? Energy { get; set; }
        public string? Mood { get; set; }
        public int? Popularity { get; set; }
        public string? SpotifyId { get; set; }
        public string? YouTubeUrl { get; set; }
        [Required]
        public string Status { get; set; } = string.Empty;
        public string? MusicBrainzId { get; set; }
        public int? LastFmPlaycount { get; set; }
        public int? Valence { get; set; }
        public DateTime? RequestDate { get; set; }
        public string? RequestedBy { get; set; }
        public string? ApprovedBy { get; set; }
    }
}