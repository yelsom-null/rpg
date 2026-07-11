using System;

namespace Elderholt
{
    // What a client needs from the host process: a clock and a way to schedule
    // delayed work (used by headless bot clients, e.g. the balance tester).
    // Implemented by ElderholtBootstrap.
    public interface IClientHost
    {
        double NowMs { get; }
        void Schedule(float delaySeconds, Action action);
    }

    // A received snapshot stamped with its arrival time, for interpolation.
    public struct TimedSnap
    {
        public double at;
        public Snapshot snap;
    }
}
