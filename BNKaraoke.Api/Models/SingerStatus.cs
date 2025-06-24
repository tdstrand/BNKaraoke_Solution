using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BNKaraoke.Api.Models
{
    public class SingerStatus
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EventId { get; set; }

        [Required]
        public string RequestorId { get; set; } = null!;

        [Required]
        public bool IsLoggedIn { get; set; }

        [Required]
        public bool IsJoined { get; set; }

        [Required]
        public bool IsOnBreak { get; set; }

        [Required]
        public DateTime UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("EventId")]
        public Event? Event { get; set; }

        [ForeignKey("RequestorId")]
        public ApplicationUser? Requestor { get; set; }
    }
}