# Releasing

1. Update `CHANGELOG.md`, bump `<Version>` in `apps/windows/Directory.Build.props` and `versionName`/`versionCode` in `apps/android/app/build.gradle.kts`.
2. `git tag vX.Y.Z && git push --tags` — the Release workflow builds and attaches both binaries.

## Store-grade signing
- **Android:** the release build type is signed with the debug key so demo APKs install directly. For Play Store, create an upload keystore, store it as an encrypted CI secret, and replace `signingConfig` in `app/build.gradle.kts`.
- **Windows:** the exe is unsigned (SmartScreen will warn). Sign with an Authenticode certificate (`signtool`) or Azure Trusted Signing in the Release workflow.
