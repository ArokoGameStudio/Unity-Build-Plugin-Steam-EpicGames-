using System;
using System.Collections.Generic;
using System.Linq;
using Aroko.StoreRelease.Editor.Configuration;

namespace Aroko.StoreRelease.Editor.Dashboard
{
    internal sealed class StoreReleaseDashboardContext
    {
        public StoreReleaseDashboardContext()
        {
            ProjectSettings = StoreReleaseProjectSettings.instance;
            if (ProjectSettings.EnsureDefaults())
            {
                ProjectSettings.SaveSettings();
            }

            Version = ProjectSettings.DefaultVersion;
            RefreshSetupReadiness();
        }

        public StoreReleaseProjectSettings ProjectSettings { get; }
        public string Version { get; set; }
        public StoreOperationReport LastReport { get; set; }
        public string LastOperationName { get; private set; } = string.Empty;
        public string LastOperationScope { get; private set; } = string.Empty;
        public string LastOperationTarget { get; private set; } = string.Empty;
        public DateTime LastOperationTime { get; private set; }
        public Action<int> RequestNavigation { get; set; }
        public Action RequestRepaint { get; set; }
        public bool IsSetupReady { get; private set; }
        public string SetupBlockingReason { get; private set; } = string.Empty;

        public void RefreshSetupReadiness(
            IReadOnlyList<StoreSdkStatus> sdkStatuses = null)
        {
            List<StoreValidationIssue> issues =
                StoreConfigurationValidator.ValidateAll(ProjectSettings);
            StoreValidationIssue configurationError = issues.FirstOrDefault(
                issue => issue.Severity == StoreValidationSeverity.Error);
            if (configurationError != null)
            {
                IsSetupReady = false;
                SetupBlockingReason = configurationError.Message;
                return;
            }

            IReadOnlyList<StoreSdkStatus> statuses =
                sdkStatuses ?? StoreSdkDetector.DetectAll();
            if (!StoreSdkDetector.AreRequiredSdksReady(
                    ProjectSettings.Profiles,
                    statuses,
                    out string sdkBlockingReason))
            {
                IsSetupReady = false;
                SetupBlockingReason = sdkBlockingReason;
                return;
            }

            IsSetupReady = ActiveProfile != null;
            SetupBlockingReason = IsSetupReady
                ? string.Empty
                : "Select an active build profile.";
        }

        public void SetLastReport(
            StoreOperationReport report,
            string operationName,
            string operationScope,
            string operationTarget)
        {
            LastReport = report;
            LastOperationName = operationName ?? string.Empty;
            LastOperationScope = operationScope ?? string.Empty;
            LastOperationTarget = operationTarget ?? string.Empty;
            LastOperationTime = DateTime.Now;
        }

        public void NavigateTo(int pageIndex)
        {
            RequestNavigation?.Invoke(pageIndex);
        }

        public void Repaint()
        {
            RequestRepaint?.Invoke();
        }

        public StoreBuildProfile ActiveProfile
        {
            get => ProjectSettings.GetProfile(ProjectSettings.ActiveProfileId);
            set
            {
                if (value != null)
                {
                    ProjectSettings.ActiveProfileId = value.Id;
                    ProjectSettings.SaveSettings();
                }
            }
        }
    }
}
