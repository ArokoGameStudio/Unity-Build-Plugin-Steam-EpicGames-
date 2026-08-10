# Runtime and Editor API

## Achievement unlock

The runtime-facing API is store-neutral:

```csharp
using Aroko.StoreRelease.Runtime;

public static class GameAchievementIds
{
    public const string FirstWin = "first-win";
}

StoreAchievements.Unlock(GameAchievementIds.FirstWin);
```

Achievement IDs remain project-owned and should match the IDs configured in both store portals. The call records the unlock locally first. Pending unlocks persist between sessions and retry provider initialization and delivery automatically. Delivery receipts are isolated by Steam App ID or Epic product/sandbox/deployment, so testing one environment cannot suppress a later live unlock.

Steam and Epic providers are optional assemblies. They compile only when their vendor SDK package is installed, and their runtime implementation is included only in the matching store build.

## Build request

Editor-only build APIs live under `Aroko.StoreRelease.Editor`:

```csharp
var request = new StoreBuildRequest(profile, "1.2.3", "Builds/Steam/1.2.3");
```

`StoreBuildRequest` contains only a `StoreBuildProfile`, version, and optional output path.

## Validate and build

```csharp
StoreOperationReport validation = StoreBuildCoordinator.Validate(request);
StoreOperationReport build = StoreBuildCoordinator.Build(request);
```

`StoreOperationReport` exposes success state, validation issues, the resolved output path, and the local report path.

## Extension point

Implement `IStoreEditorAdapter` for a custom store-specific validator and postprocessor. Storefront delivery and developer-account authentication are not part of this API.
