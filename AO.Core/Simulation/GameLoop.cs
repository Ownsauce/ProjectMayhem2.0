using AO.Core.World;

namespace AO.Core.Simulation
{
    public sealed class GameLoop
    {
        public const int TicksPerSecond = 30;
        public const float TickDelta = 1f / TicksPerSecond;

        private int _tick;
        public int Tick => _tick;

        public void TickOnce(WorldState world)
        {
            world.Update(TickDelta);
            _tick++;
        }
    }
}
