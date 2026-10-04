namespace ZoneEngine_New.Core.WorldGeneration
{
    using System;
    using System.Globalization;
    using ZoneEngine_New.Core.Commands;
    using ZoneEngine_New.Core.Playfield;
    using ZoneEngine_New.Core.GameData;

    /// <summary>Original-client baseline commands; do not route Unity recipes into AO's ACG protocol.</summary>
    internal static class NativeClientDungeonCommands
    {
        private static bool GenerateAcg(GmCommandContext context, ProceduralInstanceService instances)
        {
            int? target = null;
            if (context.Args.Length == 5 && int.TryParse(context.Args[4], out int parsed)) target = parsed;
            if ((context.Args.Length != 4 && context.Args.Length != 5) || (context.Args.Length == 5 && target == null)
                || !int.TryParse(context.Args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed) || seed < 0
                || !int.TryParse(context.Args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                GmCommandFeedback.Send(context.Session, context.Player,
                    "Usage: .worldgen native-client-acg <seed> <3-40 target rooms> <instance-id> [client-pf]");
                return true;
            }
            if (context.Player.Playfield is not Playfield current)
                throw new InvalidOperationException("Not on a transferable playfield.");
            var manager = current.GetRequiredService<PlayfieldManager>();
            var data = current.GetRequiredService<IGameData>();
            var instance = instances.GenerateNativeAcg(context.Args[3], seed, count, current.Identity.Instance,
                data.RootPath, target, id => manager.TryGet(id, out _));
            GmCommandFeedback.Send(context.Session, context.Player,
                $"Created native ACG dungeon {context.Args[3]}: pf={instance.PlayfieldId}, style={instance.Layout.Manifest.Parameters["nativeStyle"]}, rooms={instance.Layout.Manifest.Parameters["roomCount"]}. Enter with .worldenter {context.Args[3]}; return with .worldexit.");
            return true;
        }

        public static bool TryExecute(GmCommandContext context, ProceduralInstanceService instances)
        {
            if (context.Args.Length == 0) return false;
            bool acg = string.Equals(context.Args[0], "native-client-acg", StringComparison.OrdinalIgnoreCase);
            if (!acg && !string.Equals(context.Args[0], "native-client-copy", StringComparison.OrdinalIgnoreCase)) return false;
            if (acg) return GenerateAcg(context, instances);
            int? target = null;
            if (context.Args.Length == 5 && int.TryParse(context.Args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedTarget))
                target = parsedTarget;
            if ((context.Args.Length != 4 && context.Args.Length != 5) || (context.Args.Length == 5 && target == null)
                || !int.TryParse(context.Args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int source) || source <= 0
                || !ulong.TryParse(context.Args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong seed))
            {
                GmCommandFeedback.Send(context.Session, context.Player, "Usage: .worldgen native-client-copy <source-pf> <seed> <instance-id> [client-pf]");
                return true;
            }
            if (context.Player.Playfield is not Playfield current)
                throw new InvalidOperationException("Not on a transferable playfield.");
            var manager = current.GetRequiredService<PlayfieldManager>();
            var instance = instances.GenerateNativeCopy(context.Args[3], seed,
                current.Identity.Instance, source, originalClient: true, clientPlayfield: target,
                isLoaded: id => manager.TryGet(id, out _));
            GmCommandFeedback.Send(context.Session, context.Player,
                $"Created original-client source copy {context.Args[3]}: source={source}, pf={instance.PlayfieldId}. Enter with .worldenter {context.Args[3]}; return with .worldexit. Seed identifies this copy; source room arrangement is unchanged.");
            return true;
        }
    }
}
