# Setup

## Requirements

- Unity 6000.0 LTS on Windows.
- Steamworks.NET 2025.162.1 for Steam-specific packaging and achievements.
- EOS Plugin for Unity 6.1.0 for Epic-specific packaging and achievements.

The Setup page detects each SDK. When one is missing, its **Download** button installs the supported package into the current project. Vendor SDKs are external dependencies and are not embedded in the exported toolkit package.

## Configure

Open **Window > Store Release Toolkit > Setup** and configure:

- default version, executable, and Windows icon;
- Steam App ID;
- the required EOS connection values for each Epic environment you plan to build;
- optional Epic launcher/encryption/bootstrapper values;
- enabled Build Settings scenes.

The dashboard creates project-local settings and EOS template files from scratch when required. Epic fields configure files included in the generated build. They are not used to authenticate a developer account.

## Achievements

Define achievement IDs in your game code and configure the same IDs in Steam and Epic. Call `Aroko.StoreRelease.Runtime.StoreAchievements.Unlock(id)`. Unlock requests persist locally and retry through the provider selected for the current build.

The Epic provider uses the launched player's Epic/EOS session. This is player authentication for runtime services, not developer-account login or build uploading.

## Build

Open **Build**, select the profile and version, confirm the output folder, then run **Validate Configuration** or **Build Windows x64**. The toolkit replaces the selected output folder, temporarily isolates the inactive vendor SDK, and restores editor state afterward.

The resulting folder is the complete toolkit output. Storefront delivery is intentionally outside the package.
