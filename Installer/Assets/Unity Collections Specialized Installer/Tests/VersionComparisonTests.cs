/*
┌────────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                       │
│  Repository: GitHub (https://github.com/IvanMurzak/Unity-Package-Template) │
│  Copyright (c) 2025 Ivan Murzak                                            │
│  Licensed under the MIT License.                                           │
│  See the LICENSE file in the project root for more information.            │
└────────────────────────────────────────────────────────────────────────────┘
*/
using System.IO;
using NUnit.Framework;

namespace Unity.Collections.Specialized.Installer.Tests
{
    public class VersionComparisonTests
    {
        const string TestManifestPath = "Temp/Unity.Collections.Specialized.Installer.Tests/test_manifest.json";
        const string PackageId = "com.saesentsessis.unity-soa-generator";

        [SetUp]
        public void SetUp()
        {
            var dir = Path.GetDirectoryName(TestManifestPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(TestManifestPath))
                File.Delete(TestManifestPath);
        }

        [Test]
        public void ShouldUpdateVersion_PatchVersionHigher_ReturnsTrue()
        {
            // Act & Assert
            Assert.IsTrue(
                condition: Installer.ShouldUpdateVersion(
                    currentVersion: "1.5.1",
                    installerVersion: "1.5.2"
                ),
                message: "Should update when patch version is higher"
            );
        }

        [Test]
        public void ShouldUpdateVersion_PatchVersionLower_ReturnsFalse()
        {
            // Act & Assert
            Assert.IsFalse(
                condition: Installer.ShouldUpdateVersion(
                    currentVersion: "1.5.2",
                    installerVersion: "1.5.1"
                ),
                message: "Should not downgrade when patch version is lower"
            );
        }

        [Test]
        public void ShouldUpdateVersion_MinorVersionHigher_ReturnsTrue()
        {
            // Act & Assert
            Assert.IsTrue(
                condition: Installer.ShouldUpdateVersion(
                    currentVersion: "1.5.0",
                    installerVersion: "1.6.0"
                ),
                message: "Should update when minor version is higher"
            );
        }

        [Test]
        public void ShouldUpdateVersion_MinorVersionLower_ReturnsFalse()
        {
            // Act & Assert
            Assert.IsFalse(
                condition: Installer.ShouldUpdateVersion(
                    currentVersion: "1.6.0",
                    installerVersion: "1.5.0"
                ),
                message: "Should not downgrade when minor version is lower"
            );
        }

        [Test]
        public void ShouldUpdateVersion_MajorVersionHigher_ReturnsTrue()
        {
            // Act & Assert
            Assert.IsTrue(
                condition: Installer.ShouldUpdateVersion(
                    currentVersion: "1.5.0",
                    installerVersion: "2.0.0"
                ),
                message: "Should update when major version is higher"
            );
        }

        [Test]
        public void ShouldUpdateVersion_MajorVersionLower_ReturnsFalse()
        {
            // Act & Assert
            Assert.IsFalse(
                condition: Installer.ShouldUpdateVersion(
                    currentVersion: "2.0.0",
                    installerVersion: "1.5.0"
                ),
                message: "Should not downgrade when major version is lower"
            );
        }

        [Test]
        public void ShouldUpdateVersion_SameVersion_ReturnsFalse()
        {
            // Act & Assert
            Assert.IsFalse(
                condition: Installer.ShouldUpdateVersion(
                    currentVersion: "1.5.2",
                    installerVersion: "1.5.2"
                ),
                message: "Should not update when versions are the same"
            );
        }

        [Test]
        public void ShouldUpdateVersion_EmptyCurrentVersion_ReturnsTrue()
        {
            // Act & Assert
            Assert.IsTrue(
                condition: Installer.ShouldUpdateVersion(
                    currentVersion: "",
                    installerVersion: "1.5.2"
                ),
                message: "Should install when no current version exists"
            );
        }

        [Test]
        public void ShouldUpdateVersion_NullCurrentVersion_ReturnsTrue()
        {
            // Act & Assert
            Assert.IsTrue(
                condition: Installer.ShouldUpdateVersion(
                    currentVersion: null,
                    installerVersion: "1.5.2"
                ),
                message: "Should install when current version is null"
            );
        }
    }
}