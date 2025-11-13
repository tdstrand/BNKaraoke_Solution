namespace BNKaraoke.DJ.Models
{
    public class HydrationSettings
    {
        public const int DefaultInitialSnapshotTimeoutMs = 3500;
        public const int DefaultMergeWindowMs = 4000;

        public int InitialSnapshotTimeoutMs { get; set; } = DefaultInitialSnapshotTimeoutMs;
        public int MergeWindowMs { get; set; } = DefaultMergeWindowMs;
        public bool EnableRestFallback { get; set; } = true;

        public void Normalize()
        {
            if (InitialSnapshotTimeoutMs <= 0)
            {
                InitialSnapshotTimeoutMs = DefaultInitialSnapshotTimeoutMs;
            }

            if (MergeWindowMs <= 0)
            {
                MergeWindowMs = DefaultMergeWindowMs;
            }
        }
    }
}
