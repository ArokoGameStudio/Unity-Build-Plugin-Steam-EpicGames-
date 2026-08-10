# CI

Invoke Unity in batch mode with `-executeMethod Aroko.StoreRelease.Editor.StoreReleaseCli.Run`.

Required arguments:

```text
-srtAction validate|build
-srtProfile <profile-id>
```

Optional arguments:

```text
-srtVersion <version>
-srtReport <absolute-or-relative-json-path>
```

Recommended pipeline:

1. Restore the Unity project and vendor SDK dependencies.
2. Compile editor assemblies.
3. Run EditMode tests.
4. Run `validate` for the intended profile and version.
5. Run `build` and archive the resulting local build folder.

The CLI has no account-authentication or storefront-delivery actions.
