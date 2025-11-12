using System;

namespace BNKaraoke.DJ.Models
{
    /// <summary>
    /// Singer status snapshot included with V2 queue payloads.
    /// </summary>
    public class SingerStatusDto
    {
        public string UserId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public bool IsLoggedIn { get; set; }

        public bool IsJoined { get; set; }

        public bool IsOnBreak { get; set; }

        public SingerStatusFlags Flags { get; set; } = SingerStatusFlags.None;
    }
}
