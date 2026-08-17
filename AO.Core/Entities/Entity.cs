using AO.Core.Math;

namespace AO.Core.Entities
{
    public abstract class Entity
    {
        public int Id { get; init; }
        public Vector3 Position;

        public abstract void Update(float deltaTime);
    }
}
