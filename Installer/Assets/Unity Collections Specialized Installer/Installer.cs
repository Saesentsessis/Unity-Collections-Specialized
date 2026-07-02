/*
┌────────────────────────────────────────────────────────────────────────────┐
│  Unity Collections Specialized                                             │
│  Custom-made third-party package — not affiliated with or endorsed by      │
│  Unity Technologies.                                                       │
│  Repository: https://github.com/Saesentsessis/Unity-Collections-Specialized│
└────────────────────────────────────────────────────────────────────────────┘
*/

using UnityEditor;

namespace Unity.Collections.Specialized.Installer
{
    [InitializeOnLoad]
    public static partial class Installer
    {
        public const string PackageId = "com.saesentsessis.unity-collections-specialized";
        public const string Version = "0.1.0";

        static Installer()
        {
            if (Application.isBatchMode) return;
            AddScopedRegistryIfNeeded(ManifestPath);
        }
    }
}