namespace Game.Contracts
{
    // Cross-module event types. Additive only: add new files/structs, never change an existing one in place
    // (other modules may already depend on it). One file per publishing module.

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
