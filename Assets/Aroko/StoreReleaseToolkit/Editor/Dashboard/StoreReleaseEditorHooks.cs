using System;
using Aroko.StoreRelease.Editor.Configuration;

namespace Aroko.StoreRelease.Editor.Dashboard
{
    /// <summary>
    /// Decouples the dashboard from build implementations so it remains usable
    /// while optional vendor SDK assemblies are unavailable.
    /// </summary>
    public static class StoreReleaseEditorHooks
    {
        public static Func<StoreBuildRequest, StoreOperationReport> ValidateHandler { get; set; }
        public static Func<StoreBuildRequest, StoreOperationReport> BuildHandler { get; set; }

        public static StoreOperationReport Validate(StoreBuildRequest request)
        {
            return Invoke(
                ValidateHandler,
                request,
                "SRT-HOOK-VALIDATE",
                "No build validator is registered.");
        }

        public static StoreOperationReport Build(StoreBuildRequest request)
        {
            return Invoke(
                BuildHandler,
                request,
                "SRT-HOOK-BUILD",
                "No build coordinator is registered.");
        }

        private static StoreOperationReport Invoke(
            Func<StoreBuildRequest, StoreOperationReport> handler,
            StoreBuildRequest request,
            string errorCode,
            string missingHandlerMessage)
        {
            if (handler == null)
            {
                return StoreOperationReport.Failure(errorCode, missingHandlerMessage);
            }

            try
            {
                return handler(request) ??
                       StoreOperationReport.Failure(
                           errorCode,
                           "The operation returned no report.");
            }
            catch (Exception exception)
            {
                return StoreOperationReport.Failure(errorCode, exception.Message);
            }
        }

    }
}
