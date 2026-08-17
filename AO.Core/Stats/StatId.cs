using System;

namespace AO.Core.Stats
{
    /// <summary>
    /// Lightweight numeric identity for a stat.
    /// Shared across requirements, modifiers, and effects.
    /// </summary>
    public readonly struct StatId : IEquatable<StatId>
    {
        public int Value { get; }

        public StatId(int value)
        {
            Value = value;
        }

        public bool Equals(StatId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is StatId other && Equals(other);
        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString();

        public static implicit operator int(StatId id) => id.Value;
        public static implicit operator StatId(int value) => new(value);

        public static bool operator ==(StatId left, StatId right) => left.Equals(right);
        public static bool operator !=(StatId left, StatId right) => !left.Equals(right);
    }
}
