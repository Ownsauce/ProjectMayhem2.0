using System;
using System.Linq;
using AO.Client.Backends.AORebirth;

static class MovementActionsTests
{
    static void Expect(byte[] actual, params byte[] expected)
    {
        if (!actual.SequenceEqual(expected)) throw new Exception("Unexpected actions: " + string.Join(",", actual));
    }
    static int Apply(int flags, byte[] actions)
    {
        foreach (byte action in actions)
            flags = action switch
            {
                1 => flags | 1, 2 => flags & ~1,
                3 => flags | 2, 4 => flags & ~2,
                5 => flags | 4, 6 => flags & ~4,
                7 => flags | 8, 8 => flags & ~8,
                21 => 0, 22 => flags, _ => throw new Exception("Invalid action")
            };
        return flags;
    }
    static void Main()
    {
        var sequence = new AORebirthMovementActionSequence();
        Expect(sequence.Build(1, 0), 21, 1);
        Expect(sequence.Build(1, 0), 22);
        Expect(sequence.Build(-1, 0), 2, 3);
        Expect(sequence.Build(-1, 1), 5);
        Expect(sequence.Build(0, -1), 4, 6, 7);
        Expect(sequence.Build(0, 0), 21);
        Expect(sequence.Build(0, 0), 21);
        sequence.Reset();
        Expect(sequence.Build(-1, 0), 21, 3);
        // Exhaust every transition against the server's independent flag semantics,
        // including diagonal movement and stale flags after an authoritative correction.
        foreach (int f in new[] { -1, 0, 1 }) foreach (int r in new[] { -1, 0, 1 })
        foreach (int nextF in new[] { -1, 0, 1 }) foreach (int nextR in new[] { -1, 0, 1 })
        {
            sequence.Reset();
            int flags = Apply(15, sequence.Build(f, r));
            flags = Apply(flags, sequence.Build(nextF, nextR));
            int expected = (nextF > 0 ? 1 : nextF < 0 ? 2 : 0) | (nextR > 0 ? 4 : nextR < 0 ? 8 : 0);
            if (flags != expected) throw new Exception("Direction transition retained stale server flags.");
        }
        Console.WriteLine("PASS movement actions: all 81 direction transitions, idle, update, correction reset");
    }
}
