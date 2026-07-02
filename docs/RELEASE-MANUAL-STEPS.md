# Remaining manual steps for 0.1.0 release

Complete these steps in GitHub, Unity Cloud, Unity Editor, and OpenUPM before the
release workflow can succeed. Code and workflow changes in this repo are ready.

## 1. GitHub Actions secrets

In **https://github.com/Saesentsessis/Unity-Collections-Specialized/settings/secrets/actions**,
add:

| Secret | Value |
|--------|-------|
| `UNITY_EMAIL` | Unity ID email |
| `UNITY_PASSWORD` | Unity ID password |
| `UNITY_LICENSE` | Full text of `Unity_lic.ulf` |
| `UPM_ORG_ID` | Unity Cloud organization ID |
| `UPM_SERVICE_ACCOUNT_KEY_ID` | Service account key ID |
| `UPM_SERVICE_ACCOUNT_KEY_SECRET` | Service account key secret (shown once) |

Service account needs **Package Manager Package Signer** role. Details:
[`docs/openupm-signing.md`](openupm-signing.md) and root [`README.md`](../README.md).

## 2. One-time namespace claim (Unity Editor)

1. Open `Unity-Collections-Specialized/` in Unity 6.3+.
2. Package Manager → **Unity Collections Specialized** → **Export**.
3. **Authoring Org** → select the org matching `UPM_ORG_ID` → sign.

This cannot be automated; CI signing fails until this is done once.

## 3. OpenUPM registration

1. Submit the repo once: https://openupm.com/packages/add/
   - URL: `https://github.com/Saesentsessis/Unity-Collections-Specialized`
2. Open a PR to [openupm/openupm](https://github.com/openupm/openupm) using the listing in
   [`docs/openupm-listing.yml`](openupm-listing.yml).

## 4. Trigger release

After secrets and namespace claim are configured, push to `main` (or re-run the
**release** workflow). CI will:

- Run installer EditMode tests and export `.unitypackage`
- Build and sign `com.saesentsessis.unity-collections-specialized-0.1.0.tgz`
- Run 9 Unity test jobs
- Create GitHub Release tag `0.1.0` with both assets

## 5. Verify

- GitHub **Releases → 0.1.0**: both assets present; `.tgz` contains `package/.attestation.p7m`
- OpenUPM lists `0.1.0` (~30 min after release)
- Fresh Unity 6.3+ project: `openupm add com.saesentsessis.unity-collections-specialized`
