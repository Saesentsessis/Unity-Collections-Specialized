# Unity Collections Specialized — Installer

Custom-made third-party package — **not affiliated with or endorsed by Unity Technologies.**

This folder is the source for `Unity-Collections-Specialized-Installer.unitypackage`, a one-click helper that configures OpenUPM in a Unity project and adds `[com.saesentsessis.unity-collections-specialized](https://github.com/Saesentsessis/Unity-Collections-Specialized)` as a dependency.


|                       |                                                                                                                                  |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| **Package ID**        | `com.saesentsessis.unity-collections-specialized`                                                                                |
| **Installer version** | Defined in `Installer.cs` (`Version` constant)                                                                                   |
| **Repository**        | [https://github.com/Saesentsessis/Unity-Collections-Specialized](https://github.com/Saesentsessis/Unity-Collections-Specialized) |


## For end users

1. Download `Unity-Collections-Specialized-Installer.unitypackage` from [GitHub Releases](https://github.com/Saesentsessis/Unity-Collections-Specialized/releases).
2. In your Unity project, use **Assets → Import Package → Custom Package…** and select the file.
3. On import, the installer runs automatically and updates `Packages/manifest.json`:
  - Adds the **OpenUPM** scoped registry (`https://package.openupm.com`) if missing
  - Adds required registry scopes
  - Adds or updates the package dependency at the installer version (never downgrades an existing newer version)

After import, open **Window → Package Manager** and resolve packages. The main package should appear under **Packages: Unity Collections Specialized**.

> Prefer the command line? You can install without the installer: `openupm add com.saesentsessis.unity-collections-specialized`



## For maintainers

### Export the installer locally

Open the `[Installer/](../../../)` Unity project, then either:

- **Menu:** use the project’s export workflow, or
- **CI / batch:** call `PackageExporter.ExportPackage` (see `[PackageExporter.cs](PackageExporter.cs)`)

Output path: `Installer/build/Unity-Collections-Specialized-Installer.unitypackage`

### CI release

The `[release.yml](../../../../.github/workflows/release.yml)` workflow builds this `.unitypackage` automatically, runs EditMode tests in this project, and attaches the artifact to GitHub Releases alongside the signed UPM tarball.

### Bump the installer version

When cutting a release, sync the version from the **repository root**:

```powershell
.\commands\bump-version.ps1 -NewVersion "0.1.0"
```

This updates `Installer.cs`, `package.json`, and README download URLs together.

### Tests

EditMode tests in `[Tests/](Tests/)` cover manifest merging and version comparison logic:

- `ManifestInstallerTests` — scoped registry and dependency injection
- `VersionComparisonTests` — prevents downgrades when a project already has a newer package version

Run them via **Test Runner** in the Installer Unity project.

## Related documentation

- [Repository README](../../../../README.md)
- [Package README](../../../../Unity-Collections-Specialized/Assets/root/README.md)
- [Deploy to OpenUPM](../../../../docs/Deploy-OpenUPM.md)
- [Release manual steps](../../../../docs/RELEASE-MANUAL-STEPS.md)



## License

MIT — see `[Unity-Collections-Specialized/Assets/root/LICENSE](../../../../Unity-Collections-Specialized/Assets/root/LICENSE)`.