#if AROKO_SRT_EPIC_BUILD && (!AROKO_SRT_STEAM_BUILD || AROKO_SRT_SELECT_EPIC)
using System;
using System.Collections.Generic;
using System.IO;
using Epic.OnlineServices;
using Epic.OnlineServices.Achievements;
using Epic.OnlineServices.Auth;
using Epic.OnlineServices.Connect;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace Aroko.StoreRelease.Runtime.Epic
{
    internal sealed class EpicAchievementProvider : MonoBehaviour,
        IStoreAchievementProvider
    {
        private Action<bool, IReadOnlyCollection<string>> initializationCallback;
        private ProductUserId productUserId;
        private AchievementsInterface achievements;
        private bool ready;
        private bool providerStarted;
        private string receiptNamespace = "epic-v1-uninitialized";

        public string ReceiptNamespace => receiptNamespace;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            string configDirectory = Path.Combine(
                Application.streamingAssetsPath,
                "EOS");
            if (!File.Exists(Path.Combine(
                    configDirectory,
                    "eos_product_config.json")) ||
                !File.Exists(Path.Combine(
                    configDirectory,
                    "eos_windows_config.json")))
            {
                Debug.LogWarning(
                    "EOS configuration is missing. " +
                    "Achievements will remain queued until a configured build is launched.");
                return;
            }

            var host = new GameObject("Epic Achievement Provider");
            DontDestroyOnLoad(host);
            host.AddComponent<EpicAchievementProvider>();
        }

        private void Start()
        {
            TryStartProvider();
        }

        private void Update()
        {
            if (!providerStarted)
            {
                TryStartProvider();
            }
        }

        private void TryStartProvider()
        {
            if (providerStarted ||
                EOSManagerPlatformSpecificsSingleton.Instance == null)
            {
                return;
            }

            EOSManager manager = UnityEngine.Object.FindFirstObjectByType<EOSManager>();
            if (manager == null)
            {
                manager = gameObject.AddComponent<EOSManager>();
            }

            providerStarted = true;
            StoreAchievements.RegisterProvider(this);
        }

        public void Initialize(
            Action<bool, IReadOnlyCollection<string>> onCompleted)
        {
            if (ready)
            {
                onCompleted?.Invoke(true, Array.Empty<string>());
                return;
            }

            initializationCallback = onCompleted;
            EOSManager.EOSSingleton.EpicLauncherArgs args =
                EOSManager.EOSSingleton.GetCommandLineArgsFromEpicLauncher();
            if (!ConfigureReceiptNamespace())
            {
                CompleteInitialization(false, Array.Empty<string>());
                return;
            }

            ProductUserId existingUser = EOSManager.Instance.GetProductUserId();
            if (existingUser != null)
            {
                BeginAchievementQuery(existingUser);
                return;
            }

            if (string.IsNullOrWhiteSpace(args.authPassword))
            {
                Debug.LogWarning(
                    "Epic Launcher authentication was not supplied. " +
                    "Achievements will remain queued.");
                CompleteInitialization(false, Array.Empty<string>());
                return;
            }

            EOSManager.Instance.StartLoginWithLoginTypeAndToken(
                LoginCredentialType.ExchangeCode,
                args.authLogin,
                args.authPassword,
                OnAuthLogin);
        }

        public void Submit(string achievementId, Action<bool> onCompleted)
        {
            if (!ready || productUserId == null || achievements == null)
            {
                onCompleted?.Invoke(false);
                return;
            }

            var options = new UnlockAchievementsOptions
            {
                UserId = productUserId,
                AchievementIds = new Utf8String[] { achievementId }
            };

            achievements.UnlockAchievements(
                ref options,
                null,
                (ref OnUnlockAchievementsCompleteCallbackInfo data) =>
                {
                    bool succeeded = data.ResultCode == Result.Success;
                    if (!succeeded)
                    {
                        Debug.LogWarning(
                            "Epic achievement '" + achievementId +
                            "' could not be submitted (" + data.ResultCode +
                            ") and will be retried.");
                    }

                    onCompleted?.Invoke(succeeded);
                });
        }

        private void OnAuthLogin(
            global::Epic.OnlineServices.Auth.LoginCallbackInfo data)
        {
            if (data.ResultCode != Result.Success || data.LocalUserId == null)
            {
                Debug.LogWarning(
                    "Epic Launcher authentication failed (" + data.ResultCode +
                    "). Achievements will remain queued.");
                CompleteInitialization(false, Array.Empty<string>());
                return;
            }

            EOSManager.Instance.StartConnectLoginWithEpicAccount(
                data.LocalUserId,
                OnConnectLogin);
        }

        private void OnConnectLogin(
            global::Epic.OnlineServices.Connect.LoginCallbackInfo data)
        {
            if (data.ResultCode == Result.InvalidUser &&
                data.ContinuanceToken != null)
            {
                EOSManager.Instance.CreateConnectUserWithContinuanceToken(
                    data.ContinuanceToken,
                    OnConnectUserCreated);
                return;
            }

            if (data.ResultCode != Result.Success || data.LocalUserId == null)
            {
                Debug.LogWarning(
                    "Epic Connect login failed (" + data.ResultCode +
                    "). Achievements will remain queued.");
                CompleteInitialization(false, Array.Empty<string>());
                return;
            }

            BeginAchievementQuery(data.LocalUserId);
        }

        private void OnConnectUserCreated(CreateUserCallbackInfo data)
        {
            if (data.ResultCode != Result.Success || data.LocalUserId == null)
            {
                Debug.LogWarning(
                    "Epic Connect user creation failed (" + data.ResultCode +
                    "). Achievements will remain queued.");
                CompleteInitialization(false, Array.Empty<string>());
                return;
            }

            BeginAchievementQuery(data.LocalUserId);
        }

        private void BeginAchievementQuery(ProductUserId userId)
        {
            productUserId = userId;
            achievements = EOSManager.Instance.GetEOSAchievementInterface();
            if (achievements == null)
            {
                Debug.LogWarning(
                    "Epic achievements are unavailable. " +
                    "Unlocks will remain queued.");
                CompleteInitialization(false, Array.Empty<string>());
                return;
            }

            var definitionOptions = new QueryDefinitionsOptions
            {
                LocalUserId = productUserId
            };
            achievements.QueryDefinitions(
                ref definitionOptions,
                null,
                (ref OnQueryDefinitionsCompleteCallbackInfo _) =>
                    QueryPlayerAchievements());
        }

        private void QueryPlayerAchievements()
        {
            var options = new QueryPlayerAchievementsOptions
            {
                LocalUserId = productUserId,
                TargetUserId = productUserId
            };

            achievements.QueryPlayerAchievements(
                ref options,
                null,
                (ref OnQueryPlayerAchievementsCompleteCallbackInfo data) =>
                {
                    var unlocked = new List<string>();
                    if (data.ResultCode == Result.Success)
                    {
                        CollectUnlockedAchievements(unlocked);
                    }
                    else
                    {
                        Debug.LogWarning(
                            "Epic achievement state query failed (" +
                            data.ResultCode +
                            "); pending unlocks will still be attempted.");
                    }

                    CompleteInitialization(true, unlocked);
                });
        }

        private bool ConfigureReceiptNamespace()
        {
            try
            {
                receiptNamespace =
                    "epic-v1-product-" + EOSManager.Instance.GetProductId() +
                    "-sandbox-" + EOSManager.Instance.GetSandboxId() +
                    "-deployment-" + EOSManager.Instance.GetDeploymentID();
                return true;
            }
            catch (Exception exception)
            {
                receiptNamespace = "epic-v1-uninitialized";
                Debug.LogWarning(
                    "Epic environment identifiers could not be read; " +
                    "achievement delivery will remain queued.\n" + exception);
                return false;
            }
        }

        private void CompleteInitialization(
            bool succeeded,
            IReadOnlyCollection<string> unlocked)
        {
            ready = succeeded;
            Action<bool, IReadOnlyCollection<string>> callback =
                initializationCallback;
            initializationCallback = null;
            callback?.Invoke(
                succeeded,
                unlocked ?? Array.Empty<string>());
        }

        private void CollectUnlockedAchievements(List<string> unlocked)
        {
            var countOptions = new GetPlayerAchievementCountOptions
            {
                UserId = productUserId
            };
            uint count = achievements.GetPlayerAchievementCount(ref countOptions);
            var copyOptions = new CopyPlayerAchievementByIndexOptions
            {
                LocalUserId = productUserId,
                TargetUserId = productUserId
            };

            for (uint index = 0; index < count; index++)
            {
                copyOptions.AchievementIndex = index;
                Result result = achievements.CopyPlayerAchievementByIndex(
                    ref copyOptions,
                    out PlayerAchievement? achievement);
                if (result == Result.Success &&
                    achievement.HasValue &&
                    achievement.Value.Progress >= 1.0)
                {
                    unlocked.Add(achievement.Value.AchievementId);
                }
            }
        }
    }
}
#endif
