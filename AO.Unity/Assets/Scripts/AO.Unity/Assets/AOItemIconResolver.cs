using System;
using AO.Assets.ResourceDatabase;

namespace AO.Unity.Assets
{
    /// <summary>
    /// Lazily reads AO item icon image payloads directly from ResourceDatabase.
    /// The database index and file handles are shared instead of reopened for
    /// every inventory slot.
    /// </summary>
    public static class AOItemIconResolver
    {
        // AODB.Common.RDBObjects.IconTexture record type.
        private const int IconTextureResourceType = 1010008;
        private static readonly object Sync = new object();
        private static AOResourceDatabase _database;
        private static string _installKey = string.Empty;

        public static bool TryReadIcon(int iconId, out byte[] imageBytes)
        {
            imageBytes = null;
            if (iconId <= 0)
                return false;

            AOInstallValidation install = AOInstallConfiguration.GetConfiguredInstall();
            if (install == null || !install.IsValid)
                return false;

            lock (Sync)
            {
                string key = install.RootPath + "|" + install.DatabaseFingerprint;
                if (_database == null
                    || !string.Equals(_installKey, key, StringComparison.Ordinal))
                {
                    _database?.Dispose();
                    _database = new AOResourceDatabase(install.RootPath);
                    _installKey = key;
                }

                if (!_database.TryReadRaw(
                        IconTextureResourceType, iconId, out byte[] record)
                    || record == null || record.Length <= 12)
                    return false;

                // IconTexture records contain a 12-byte RDB object prefix before
                // the PNG/JPEG payload expected by Texture2D.LoadImage.
                imageBytes = new byte[record.Length - 12];
                Buffer.BlockCopy(record, 12, imageBytes, 0, imageBytes.Length);
                return imageBytes.Length > 0;
            }
        }
    }
}
