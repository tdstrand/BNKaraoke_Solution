namespace BNKaraoke.DJ.Models
{
    [System.Flags]
    public enum SingerStatusFlags
    {
        None     = 0,
        LoggedIn = 1 << 0,
        Joined   = 1 << 1,
        OnBreak  = 1 << 2,
        Muted    = 1 << 3,
    }
}
