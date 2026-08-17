using System.Collections.Generic;

namespace AO.Core.Entities
{
    public sealed class EntityManager
    {
        private readonly List<Entity> _entities = new();

        public void Add(Entity entity)
        {
            _entities.Add(entity);
        }

        public void Update(float deltaTime)
        {
            foreach (var entity in _entities)
            {
                entity.Update(deltaTime);
            }
        }
    }
}
