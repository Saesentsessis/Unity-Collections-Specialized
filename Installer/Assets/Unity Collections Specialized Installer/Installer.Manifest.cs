/*
┌────────────────────────────────────────────────────────────────────────────┐
│  Unity Collections Specialized                                             │
│  Custom-made third-party package — not affiliated with or endorsed by      │
│  Unity Technologies.                                                       │
│  Repository: https://github.com/Saesentsessis/Unity-Collections-Specialized│
└────────────────────────────────────────────────────────────────────────────┘
*/
#nullable enable
using System.IO;
using UnityEngine;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.Collections.Specialized.Installer.Tests")]
namespace Unity.Collections.Specialized.Installer
{
    public static partial class Installer
    {
        static string ManifestPath => Path.Combine(Application.dataPath, "../Packages/manifest.json");

        // Property names
        public const string Dependencies = "dependencies";

        /// <summary>
        /// Determines if the version should be updated. Only update if installer version is higher than current version.
        /// </summary>
        /// <param name="currentVersion">Current package version string</param>
        /// <param name="installerVersion">Installer version string</param>
        /// <returns>True if version should be updated (installer version is higher), false otherwise</returns>

        internal static bool ShouldUpdateVersion(string currentVersion, string installerVersion)
        {
            if (string.IsNullOrEmpty(currentVersion))
                return true; // No current version, should install

            if (string.IsNullOrEmpty(installerVersion))
                return false; // No installer version, don't change

            try
            {
                // Try to parse as System.Version (semantic versioning)
                var current = new System.Version(currentVersion);
                var installer = new System.Version(installerVersion);

                // Only update if installer version is higher than current version
                return installer > current;
            }
            catch (System.Exception)
            {
                Debug.LogWarning($"Failed to parse versions '{currentVersion}' or '{installerVersion}' as System.Version.");
                // If version parsing fails, fall back to string comparison
                // This ensures we don't break if version format is unexpected
                return string.Compare(installerVersion, currentVersion, System.StringComparison.OrdinalIgnoreCase) > 0;
            }
        }

        public static void AddScopedRegistryIfNeeded(string manifestPath, int indent = 2)
        {
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"{manifestPath} not found!");
                return;
            }
            var jsonText = File.ReadAllText(manifestPath)
                .Replace("{ }", "{\n}")
                .Replace("{}", "{\n}")
                .Replace("[ ]", "[\n]")
                .Replace("[]", "[\n]");

            var manifestJson = JSONObject.Parse(jsonText);
            if (manifestJson == null)
            {
                Debug.LogError($"Failed to parse {manifestPath} as JSON.");
                return;
            }

            var modified = false;

            // --- Package Dependency (Version-aware installation)
            // Only update version if installer version is higher than current version
            // This prevents downgrades when users manually update to newer versions
            var dependencies = manifestJson[Dependencies];
            if (dependencies == null)
            {
                manifestJson[Dependencies] = dependencies = new JSONObject();
                modified = true;
            }

            // Only update version if installer version is higher than current version
            var currentVersion = dependencies[PackageId];
            if (currentVersion == null || ShouldUpdateVersion(currentVersion, Version))
            {
                dependencies[PackageId] = Version;
                modified = true;
            }

            // --- Write changes back to manifest
            if (modified)
                File.WriteAllText(manifestPath, manifestJson.ToString(indent).Replace("\" : ", "\": "));
        }
    }
}