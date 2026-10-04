using System;
using System.Collections.Generic;

namespace AO.Client.Backends.AORebirth
{
    /// <summary>CharDCMove actions are transitions, not a generic moving/stopped byte.</summary>
    public sealed class AORebirthMovementActionSequence
    {
        private int active;
        private bool resetPending = true;
        public void Reset() { active = 0; resetPending = true; }

        public byte[] Build(float forward, float right)
        {
            int desired = (forward > .1f ? 1 : forward < -.1f ? 2 : 0)
                | (right > .1f ? 4 : right < -.1f ? 8 : 0);
            var actions = new List<byte>();
            if (desired == 0)
            {
                active = 0;
                resetPending = false;
                return new byte[] { 0x15 }; // FullStop, including idle heartbeat/rotation.
            }
            if (resetPending) actions.Add(0x15);
            // Stop obsolete directions before starting replacements. Otherwise the
            // server retains both forward and backward (or both strafe) flags.
            int[] flags = { 1, 2, 4, 8 };
            byte[] starts = { 0x01, 0x03, 0x05, 0x07 };
            byte[] stops = { 0x02, 0x04, 0x06, 0x08 };
            for (int i = 0; i < flags.Length; i++)
                if ((active & flags[i]) != 0 && (desired & flags[i]) == 0) actions.Add(stops[i]);
            for (int i = 0; i < flags.Length; i++)
                if ((active & flags[i]) == 0 && (desired & flags[i]) != 0) actions.Add(starts[i]);
            if (actions.Count == 0) actions.Add(0x16); // Position/heading update preserves current flags.
            active = desired;
            resetPending = false;
            return actions.ToArray();
        }
    }
}
