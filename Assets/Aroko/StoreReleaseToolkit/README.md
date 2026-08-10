# Store Release Toolkit

Store Release Toolkit configures, validates, and creates store-specific Windows x64 builds for Steam and Epic. It does not sign in to developer accounts, store developer credentials, generate delivery commands, or upload builds.

## Workflow

1. **Setup** - download or detect vendor SDKs and configure shared store values.
2. **Build** - select a profile and version, validate, and create the local build folder.
3. **Diagnostics** - review configuration and SDK blockers.
4. **API** - browse the supported runtime/editor APIs and scan their use in the project.

Open the dashboard from **Window > Store Release Toolkit**.

## Store-neutral achievements

Achievement identifiers belong to the game project. Define the same identifiers in the Steam and Epic developer portals, then call the portable runtime API:

```csharp
using Aroko.StoreRelease.Runtime;

public static class GameAchievementIds
{
    public const string FirstWin = "first-win";
}

StoreAchievements.Unlock(GameAchievementIds.FirstWin);
```

Unlocks are saved locally before delivery and retried automatically, including provider startup failures. Receipts are isolated by Steam App ID or Epic deployment. A Steam build uses the Steam provider; an Epic build uses the EOS provider. Epic player authentication at game runtime is separate from developer-account authentication, which the toolkit never performs.

## What remains store-specific

- Steam App ID validation and build-local `steam_appid.txt` generation.
- Epic EOS product, sandbox, deployment, launcher, encryption, and bootstrapper packaging.
- Store-specific achievement providers selected by build symbols.
- Per-build vendor scripting defines and inactive-SDK plugin isolation.
- Store/profile/channel output paths, enabled Build Settings scenes, icons, and development/release modes.

The produced folder is the handoff artifact. Any storefront delivery work happens outside this package.

## CI

Use `StoreReleaseCli.Run` with `-srtAction validate` or `-srtAction build`, plus `-srtProfile`, `-srtVersion`, and optional `-srtReport`.

See `Documentation/Setup.md`, `Documentation/API.md`, and `Documentation/CI.md` for details.
