using System.Collections.Generic;
using Aroko.StoreRelease.Editor.Configuration;
using EditorStorePlatform = Aroko.StoreRelease.Editor.Configuration.StorePlatform;

namespace Aroko.StoreRelease.Editor.Build
{
    public interface IStoreEditorAdapter
    {
        EditorStorePlatform Store { get; }

        void Validate(
            StoreBuildRequest request,
            StoreReleaseProjectSettings settings,
            IList<StoreValidationIssue> issues);

        void Postprocess(
            StoreBuildRequest request,
            StoreReleaseProjectSettings settings,
            string executablePath);
    }
}
