using AO.Core.Entities;

namespace AO.Core.World
{
    public sealed class WorldState
    {
        public readonly EntityManager Entities = new();

        public void Update(float deltaTime)
        {
            Entities.Update(deltaTime);
        }
    }
}
