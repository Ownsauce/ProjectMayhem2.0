using AO.Core.Math;

namespace AO.Core.Entities
{
    public sealed class Character : Entity
    {
        public Vector3 Velocity;
        public float MoveSpeed = 6.0f;
        public Vector3 InputDirection;

        public override void Update(float deltaTime)
        {
            Velocity = InputDirection * MoveSpeed;
            Position += Velocity * deltaTime;
        }
    }
}
