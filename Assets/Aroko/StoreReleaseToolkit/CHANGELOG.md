# Changelog

## 1.0.0 - Portable build-only release

- Added `Aroko.StoreRelease.Runtime.StoreAchievements.Unlock(string)` with persistent pending delivery and automatic retries.
- Confirmed Steam delivery through the asynchronous stats result and isolated receipts by Steam App ID or Epic deployment.
- Added optional Steamworks.NET and EOS achievement-provider assemblies selected by vendor-package and store-build defines.
- Kept achievement identifiers project-owned and removed game-specific runtime/provider dependencies.
- Removed storefront uploading, developer-account authentication, credential storage, delivery manifests, and vendor delivery-tool integrations.
- Simplified the dashboard to Setup, Build, Diagnostics, and API pages.
- Restricted the CLI to `validate` and `build`.
- Kept SDK download/detection, store-specific packaging, inactive-SDK isolation, profiles, and build validation.
- Added an explicit export manifest and portability validation that excludes tests, generated settings/configuration, credentials, vendor binaries, and unrelated project assets.
- Updated documentation and tests for portable package import and local-build handoff.
