namespace Game.Contracts
{
    // Cross-module event types. One file per publishing module: <Module>Events.cs holds the events that module publishes
    // (harness_lint). Additive only: add new structs, never change one that landed (other modules may already depend on it),
    // and give each a name no other contracts type has (submit.ps1 / land.ps1 refuse both).

    /// <summary>Published by Smoke each time the spinner completes a full turn.</summary>
    public readonly struct SpinnerLap
    {
        public readonly int Lap;
        public SpinnerLap(int lap) { Lap = lap; }
    }

    /// <summary>Published by Smoke when the player reverses the spin (Space).</summary>
    public readonly struct SpinDirectionChanged
    {
        public readonly int Direction; // +1 / -1
        public SpinDirectionChanged(int direction) { Direction = direction; }
    }
}
