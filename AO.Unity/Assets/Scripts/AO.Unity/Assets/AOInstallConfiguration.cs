using System;
using System.Collections.Generic;
using System.IO;
using AO.Assets.ResourceDatabase;
using UnityEngine;

namespace AO.Unity.Assets
{
    public static class AOInstallConfiguration
    {
        private const string InstallPathPreference = "ProjectMayhem.AOInstallPath.v1";

        public static string CacheRoot => Path.Combine(
            Application.persistentDataPath, "AOAssetCache");

        public static AOInstallValidation GetConfiguredInstall()
        {
            string configured = PlayerPrefs.GetString(InstallPathPreference, string.Empty);
            AOInstallValidation validation = AOInstallLocator.Validate(configured);
            if (validation.IsValid)
                return validation;

            string environmentPath = Environment.GetEnvironmentVariable(
                "PROJECTMAYHEM_AO_INSTALL");
            var hints = new List<string>();
            if (!string.IsNullOrWhiteSpace(environmentPath)) hints.Add(environmentPath);
            AOInstallValidation discovered = AOInstallLocator.FindFirstValid(hints);
            if (discovered.IsValid)
                Save(discovered.RootPath);
            return discovered;
        }

        public static AOInstallValidation SetInstallPath(string path)
        {
            AOInstallValidation validation = AOInstallLocator.Validate(path);
            if (validation.IsValid)
                Save(validation.RootPath);
            return validation;
        }

        public static void ClearInstallPath()
        {
            PlayerPrefs.DeleteKey(InstallPathPreference);
            PlayerPrefs.Save();
        }

        private static void Save(string path)
        {
            PlayerPrefs.SetString(InstallPathPreference, path);
            PlayerPrefs.Save();
        }
    }
}
