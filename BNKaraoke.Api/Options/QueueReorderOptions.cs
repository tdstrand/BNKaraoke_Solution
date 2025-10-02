namespace BNKaraoke.Api.Options
{
    public class QueueReorderOptions
    {
        public double SolverTimeSeconds { get; set; } = 2.0;

        public string MaturePolicyDefault { get; set; } = "Defer";

        public int PlanTtlSeconds { get; set; } = 600;

        public int DefaultMovementCap { get; set; } = 4;

        public int ConfirmationThreshold { get; set; } = 6;

        public int FrozenHeadCount { get; set; } = 2;
    }
}
