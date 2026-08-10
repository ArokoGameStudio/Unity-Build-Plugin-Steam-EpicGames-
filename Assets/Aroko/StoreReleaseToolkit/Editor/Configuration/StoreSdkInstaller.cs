using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Aroko.StoreRelease.Editor.Configuration
{
    internal static class StoreSdkInstaller
    {
        private static AddRequest request;
        private static string activePackageName = string.Empty;
        private static string statusMessage = string.Empty;
        private static bool lastOperationFailed;

        public static event Action Changed;

        public static bool IsBusy => request != null && !request.IsCompleted;
        public static string ActivePackageName => activePackageName;
        public static string StatusMessage => statusMessage;
        public static bool LastOperationFailed => lastOperationFailed;

        public static bool Install(string packageName)
        {
            if (IsBusy)
            {
                statusMessage = "Another SDK installation is already running.";
                lastOperationFailed = true;
                Changed?.Invoke();
                return false;
            }

            string installUrl = StoreSdkDetector.GetInstallUrl(packageName);
            if (string.IsNullOrWhiteSpace(installUrl))
            {
                statusMessage = "No automatic installer is configured for " + packageName + ".";
                lastOperationFailed = true;
                Changed?.Invoke();
                return false;
            }

            try
            {
                activePackageName = packageName;
                statusMessage = "Downloading and installing " + packageName + "...";
                lastOperationFailed = false;
                request = Client.Add(installUrl);
                EditorApplication.update -= Poll;
                EditorApplication.update += Poll;
                Changed?.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                request = null;
                statusMessage = "Installation could not start: " + exception.Message;
                lastOperationFailed = true;
                Changed?.Invoke();
                return false;
            }
        }

        private static void Poll()
        {
            if (request == null || !request.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= Poll;
            if (request.Status == StatusCode.Success)
            {
                string displayName = request.Result?.displayName;
                statusMessage = string.IsNullOrWhiteSpace(displayName)
                    ? activePackageName + " installed successfully."
                    : displayName + " installed successfully.";
                lastOperationFailed = false;
            }
            else
            {
                string error = request.Error?.message;
                statusMessage = "Installation failed" +
                                (string.IsNullOrWhiteSpace(error) ? "." : ": " + error);
                lastOperationFailed = true;
            }

            request = null;
            Changed?.Invoke();
        }
    }
}
