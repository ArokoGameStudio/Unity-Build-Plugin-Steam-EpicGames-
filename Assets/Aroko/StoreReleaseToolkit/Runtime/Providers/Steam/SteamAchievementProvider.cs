#if AROKO_SRT_STEAM_BUILD && (!AROKO_SRT_EPIC_BUILD || AROKO_SRT_SELECT_STEAM)
using System;
using System.Collections.Generic;
using System.IO;
using Steamworks;
using UnityEngine;

namespace Aroko.StoreRelease.Runtime.Steam
{
    internal sealed class SteamAchievementProvider : MonoBehaviour,
        IStoreAchievementProvider
    {
        private sealed class PendingSubmission
        {
            public string AchievementId;
            public Action<bool> OnCompleted;
        }

        private readonly Queue<PendingSubmission> pendingSubmissions =
            new Queue<PendingSubmission>();

        private bool apiInitialized;
        private bool ownsSteamApi;
        private bool statsReady;
        private string receiptNamespace = "steam-v1-uninitialized";
        private Callback<UserStatsStored_t> userStatsStoredCallback;
        private PendingSubmission activeSubmission;

        public string ReceiptNamespace => receiptNamespace;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var host = new GameObject("Steam Achievement Provider");
            DontDestroyOnLoad(host);
            host.AddComponent<SteamAchievementProvider>();
        }

        private void Awake()
        {
            StoreAchievements.RegisterProvider(this);
        }

        public void Initialize(
            Action<bool, IReadOnlyCollection<string>> onCompleted)
        {
            if (apiInitialized && statsReady)
            {
                onCompleted?.Invoke(true, Array.Empty<string>());
                return;
            }

#if !UNITY_EDITOR
            if (TryReadBuildAppId(out AppId_t buildAppId) &&
                SteamAPI.RestartAppIfNecessary(buildAppId))
            {
                Application.Quit();
                return;
            }
#endif

            if (!Packsize.Test() || !DllCheck.Test())
            {
                Debug.LogError(
                    "Steamworks compatibility checks failed. " +
                    "Achievements will remain queued.");
                onCompleted?.Invoke(false, Array.Empty<string>());
                return;
            }

            AppId_t activeAppId = AppId_t.Invalid;
            if (TryGetInitializedAppId(out activeAppId))
            {
                apiInitialized = true;
                ownsSteamApi = false;
            }
            else
            {
                try
                {
                    apiInitialized = SteamAPI.Init();
                    ownsSteamApi = apiInitialized;
                }
                catch (DllNotFoundException exception)
                {
                    Debug.LogError(
                        "Steamworks could not load its native library. " +
                        "Achievements will remain queued.\n" + exception);
                    onCompleted?.Invoke(false, Array.Empty<string>());
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        "Steamworks initialization failed. " +
                        "Achievements will remain queued.\n" + exception);
                    onCompleted?.Invoke(false, Array.Empty<string>());
                    return;
                }

                if (apiInitialized)
                {
                    activeAppId = SteamUtils.GetAppID();
                }
            }

            if (!apiInitialized)
            {
                Debug.LogWarning(
                    "Steam is unavailable. Achievements will remain queued.");
                onCompleted?.Invoke(false, Array.Empty<string>());
                return;
            }

            receiptNamespace = "steam-v1-app-" + activeAppId.m_AppId;
            if (TryReadBuildAppId(out AppId_t expectedAppId) &&
                activeAppId != expectedAppId)
            {
                Debug.LogWarning(
                    "Steam initialized with App ID " + activeAppId +
                    ", but this build expects " + expectedAppId + ".");
            }

            userStatsStoredCallback =
                Callback<UserStatsStored_t>.Create(OnUserStatsStored);
            statsReady = true;
            onCompleted?.Invoke(true, Array.Empty<string>());
        }

        public void Submit(string achievementId, Action<bool> onCompleted)
        {
            if (!apiInitialized || !statsReady)
            {
                onCompleted?.Invoke(false);
                return;
            }

            pendingSubmissions.Enqueue(new PendingSubmission
            {
                AchievementId = achievementId,
                OnCompleted = onCompleted
            });
            ProcessNextSubmission();
        }

        private void ProcessNextSubmission()
        {
            if (activeSubmission != null || pendingSubmissions.Count == 0)
            {
                return;
            }

            activeSubmission = pendingSubmissions.Dequeue();
            try
            {
                bool set = SteamUserStats.SetAchievement(
                    activeSubmission.AchievementId);
                bool accepted = set && SteamUserStats.StoreStats();
                if (accepted)
                {
                    return;
                }

                Debug.LogWarning(
                    "Steam achievement '" + activeSubmission.AchievementId +
                    "' could not be submitted and will be retried.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Steam achievement '" + activeSubmission.AchievementId +
                    "' could not be submitted and will be retried.\n" +
                    exception);
            }

            CompleteActiveSubmission(false);
        }

        private void OnUserStatsStored(UserStatsStored_t result)
        {
            if (activeSubmission == null ||
                new CGameID(result.m_nGameID).AppID() != SteamUtils.GetAppID())
            {
                return;
            }

            bool succeeded = result.m_eResult == EResult.k_EResultOK;
            if (!succeeded)
            {
                Debug.LogWarning(
                    "Steam rejected achievement '" +
                    activeSubmission.AchievementId + "' (" +
                    result.m_eResult + "); it will be retried.");
            }

            CompleteActiveSubmission(succeeded);
        }

        private void CompleteActiveSubmission(bool succeeded)
        {
            PendingSubmission completed = activeSubmission;
            activeSubmission = null;
            completed?.OnCompleted?.Invoke(succeeded);
            ProcessNextSubmission();
        }

        private static bool TryReadBuildAppId(out AppId_t appId)
        {
            appId = AppId_t.Invalid;
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "steam_appid.txt"));
            if (!File.Exists(path) ||
                !uint.TryParse(File.ReadAllText(path).Trim(), out uint value) ||
                value == 0)
            {
                return false;
            }

            appId = new AppId_t(value);
            return true;
        }

        private static bool TryGetInitializedAppId(out AppId_t appId)
        {
            try
            {
                appId = SteamUtils.GetAppID();
                return appId != AppId_t.Invalid;
            }
            catch
            {
                appId = AppId_t.Invalid;
                return false;
            }
        }

        private void Update()
        {
            if (apiInitialized)
            {
                SteamAPI.RunCallbacks();
            }
        }

        private void OnDestroy()
        {
            statsReady = false;
            userStatsStoredCallback?.Dispose();
            userStatsStoredCallback = null;
            PendingSubmission active = activeSubmission;
            activeSubmission = null;
            active?.OnCompleted?.Invoke(false);
            while (pendingSubmissions.Count > 0)
            {
                pendingSubmissions.Dequeue().OnCompleted?.Invoke(false);
            }

            if (apiInitialized && ownsSteamApi)
            {
                SteamAPI.Shutdown();
            }

            apiInitialized = false;
            ownsSteamApi = false;
        }
    }
}
#endif
