# Store Release Toolkit Showcase

This Unity 6000.0.58f2 project showcases the build-only Store Release Toolkit for Steam and Epic Games.

## Included

- Imported toolkit source: `Assets/Aroko/StoreReleaseToolkit`
- Portable package archive: `Assets/Store Release Toolkit 1.0.0.unitypackage`
- Store-specific Windows x64 build setup, validation, packaging, diagnostics, and achievement API
- No store upload commands, developer-account login, or credential storage

## Open the toolkit

In Unity, open **Tools > Store Release Toolkit**. Complete Setup before using Build or Diagnostics. Vendor SDKs can be installed from the toolkit's Download buttons.

## Achievement API

Use one project-owned achievement identifier for both stores:

```csharp
using Aroko.StoreRelease.Runtime;

StoreAchievements.Unlock("YOUR_ACHIEVEMENT_ID");
```

The active Steam or Epic build provider handles delivery automatically when its external SDK is installed.
