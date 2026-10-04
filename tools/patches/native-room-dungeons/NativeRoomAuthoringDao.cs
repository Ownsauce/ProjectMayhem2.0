using System;
using System.IO;
using System.Linq;
using System.Text;
using WorldGen.Dungeons;

namespace WorldGen.Authoring
{
    /// <summary>Storage boundary for authored room data; generators consume a published catalog instead.</summary>
    public interface INativeRoomAuthoringDao
    {
        /// <summary>Returns null when this source has no authored settings.</summary>
        NativeRoomOverrides Load(int sourcePlayfield);
        /// <summary>Saves the aggregate only if its revision still matches storage.</summary>
        void Save(NativeRoomOverrides settings);
    }

    public sealed class NativeRoomAuthoringConflictException : IOException
    {
        public NativeRoomAuthoringConflictException()
            : base("Room settings changed since they were loaded. Reload before saving; your edits were not written.") { }
    }

    /// <summary>
    /// Local file provider. Serialization is supplied by the host so the DAO has no Unity or SQL dependency.
    /// A database/API provider can implement the same aggregate contract later.
    /// </summary>
    public sealed class NativeRoomAuthoringFileDao : INativeRoomAuthoringDao
    {
        private readonly Func<int, string> pathForSource;
        private readonly Func<string, NativeRoomOverrides> deserialize;
        private readonly Func<NativeRoomOverrides, string> serialize;

        public NativeRoomAuthoringFileDao(Func<int, string> pathForSource,
            Func<string, NativeRoomOverrides> deserialize, Func<NativeRoomOverrides, string> serialize)
        {
            this.pathForSource = pathForSource ?? throw new ArgumentNullException(nameof(pathForSource));
            this.deserialize = deserialize ?? throw new ArgumentNullException(nameof(deserialize));
            this.serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
        }

        public NativeRoomOverrides Load(int sourcePlayfield)
        {
            string path = PathFor(sourcePlayfield);
            if (!File.Exists(path)) return null;
            return Read(path, sourcePlayfield);
        }

        public void Save(NativeRoomOverrides settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            Validate(settings, settings.SourcePlayfield);
            string path = PathFor(settings.SourcePlayfield);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Coordinates cooperating authors/processes; the lock file contains no user data.
            using var gate = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            long storedRevision = File.Exists(path) ? Read(path, settings.SourcePlayfield).Revision : 0;
            if (storedRevision != settings.Revision) throw new NativeRoomAuthoringConflictException();
            var next = deserialize(serialize(settings));
            Validate(next, settings.SourcePlayfield);
            next.Revision = checked(storedRevision + 1);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(serialize(next));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                settings.Revision = next.Revision;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private string PathFor(int source)
        {
            if (source <= 0) throw new ArgumentOutOfRangeException(nameof(source));
            return Path.GetFullPath(pathForSource(source));
        }

        private NativeRoomOverrides Read(string path, int source)
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Room settings exceed the size limit.");
            var settings = deserialize(File.ReadAllText(path));
            Validate(settings, source);
            return settings;
        }

        private static void Validate(NativeRoomOverrides settings, int source)
        {
            if (settings == null || settings.Version != 1 || settings.SourcePlayfield != source || settings.Revision < 0
                || settings.Rooms == null || settings.Rooms.Length > 512 || settings.Rooms.Any(r => r == null || r.SourceIndex < 0)
                || settings.Rooms.Select(r => r.SourceIndex).Distinct().Count() != settings.Rooms.Length)
                throw new InvalidDataException("Invalid authored room data.");
        }
    }
}
