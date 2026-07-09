using System;

namespace Elderholt
{
    // What a client needs from the host process: a clock, a chat sink, and a way
    // to schedule delayed work (used for banter timing; the latency pipe itself
    // is also built on the host's scheduler). Implemented by ElderholtBootstrap.
    public interface IClientHost
    {
        double NowMs { get; }
        void PushChat(string from, string text, bool isYou);
        void Schedule(float delaySeconds, Action action);
    }

    // A received snapshot stamped with its arrival time, for interpolation.
    public struct TimedSnap
    {
        public double at;
        public Snapshot snap;
    }
}
