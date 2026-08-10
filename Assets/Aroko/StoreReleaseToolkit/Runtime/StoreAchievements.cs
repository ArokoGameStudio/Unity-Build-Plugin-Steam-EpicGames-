using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Aroko.StoreRelease.Runtime
{
    /// <summary>
    /// Store-neutral achievement entry point. Calls are persisted immediately and
    /// delivered by the Steam or Epic provider compiled into the selected build.
    /// </summary>
    public static class StoreAchievements
    {
        private const string StorageKey = "Aroko.StoreRelease.Achievements.v1";
        private const string RetryHostName = "Store Release Achievement Queue";
        private const float DeliveryTimeoutSeconds = 60f;

        private static readonly AchievementLedger Ledger =
            new AchievementLedger(new PlayerPrefsAchievementStore(), StorageKey);
        private static readonly Dictionary<string, float> InFlight =
            new Dictionary<string, float>(StringComparer.Ordinal);

        private static IStoreAchievementProvider provider;
        private static bool providerReady;
        private static bool providerInitializing;

        /// <summary>
        /// Records an achievement and submits it to the active store provider.
        /// Calls made before the provider is ready remain queued and are retried.
        /// </summary>
        public static void Unlock(string achievementId)
        {
            string normalized = NormalizeAchievementId(achievementId);
            if (normalized.Length == 0)
            {
                Debug.LogWarning(
                    "StoreAchievements.Unlock ignored an empty achievement ID.");
                return;
            }

            Ledger.RecordEarned(normalized);
            FlushPending();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            provider = null;
            providerReady = false;
            providerInitializing = false;
            InFlight.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootstrapRetryQueue()
        {
            var retryHost = new GameObject(RetryHostName);
            UnityEngine.Object.DontDestroyOnLoad(retryHost);
            retryHost.AddComponent<AchievementRetryPump>();
        }

        internal static void RegisterProvider(IStoreAchievementProvider newProvider)
        {
            if (newProvider == null)
            {
                throw new ArgumentNullException(nameof(newProvider));
            }

            if (provider != null && !ReferenceEquals(provider, newProvider))
            {
                Debug.LogWarning(
                    "A store achievement provider is already registered; " +
                    "the duplicate provider was ignored.");
                return;
            }

            provider = newProvider;
            providerReady = false;
            providerInitializing = false;
            InFlight.Clear();
            TryInitializeProvider();
        }

        internal static void RetryPending()
        {
            ReleaseExpiredDeliveries();
            TryInitializeProvider();
            FlushPending();
        }

        private static void TryInitializeProvider()
        {
            if (provider == null || providerReady || providerInitializing)
            {
                return;
            }

            IStoreAchievementProvider initializingProvider = provider;
            providerInitializing = true;
            try
            {
                initializingProvider.Initialize((succeeded, unlockedAchievementIds) =>
                {
                    if (!ReferenceEquals(provider, initializingProvider))
                    {
                        return;
                    }

                    providerInitializing = false;
                    if (!succeeded)
                    {
                        providerReady = false;
                        return;
                    }

                    OnProviderReady(unlockedAchievementIds);
                });
            }
            catch (Exception exception)
            {
                providerInitializing = false;
                providerReady = false;
                Debug.LogWarning(
                    "The store achievement provider could not initialize; " +
                    "pending unlocks will be retried.\n" + exception);
            }
        }

        private static void OnProviderReady(
            IReadOnlyCollection<string> unlockedAchievementIds)
        {
            if (provider == null)
            {
                return;
            }

            if (unlockedAchievementIds != null)
            {
                foreach (string achievementId in unlockedAchievementIds)
                {
                    string normalized = NormalizeAchievementId(achievementId);
                    if (normalized.Length == 0)
                    {
                        continue;
                    }

                    Ledger.RecordEarned(normalized);
                    Ledger.MarkDelivered(provider.ReceiptNamespace, normalized);
                }
            }

            providerReady = true;
            FlushPending();
        }

        private static void FlushPending()
        {
            if (!providerReady || provider == null)
            {
                return;
            }

            string receiptNamespace = provider.ReceiptNamespace;
            string[] pending = Ledger.GetPending(receiptNamespace).ToArray();
            foreach (string achievementId in pending)
            {
                if (InFlight.ContainsKey(achievementId))
                {
                    continue;
                }

                InFlight[achievementId] = Time.realtimeSinceStartup;
                string capturedId = achievementId;
                try
                {
                    provider.Submit(capturedId, succeeded =>
                    {
                        InFlight.Remove(capturedId);
                        if (succeeded)
                        {
                            Ledger.MarkDelivered(receiptNamespace, capturedId);
                        }
                    });
                }
                catch (Exception exception)
                {
                    InFlight.Remove(capturedId);
                    Debug.LogWarning(
                        "Achievement '" + capturedId +
                        "' could not be submitted and will be retried.\n" +
                        exception);
                }
            }
        }

        private static void ReleaseExpiredDeliveries()
        {
            float now = Time.realtimeSinceStartup;
            string[] expired = InFlight
                .Where(pair => now - pair.Value >= DeliveryTimeoutSeconds)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (string achievementId in expired)
            {
                InFlight.Remove(achievementId);
            }
        }

        private static string NormalizeAchievementId(string achievementId)
        {
            return string.IsNullOrWhiteSpace(achievementId)
                ? string.Empty
                : achievementId.Trim();
        }
    }

    internal interface IStoreAchievementProvider
    {
        string ReceiptNamespace { get; }

        void Initialize(
            Action<bool, IReadOnlyCollection<string>> onCompleted);

        void Submit(string achievementId, Action<bool> onCompleted);
    }

    internal sealed class AchievementRetryPump : MonoBehaviour
    {
        private const float RetryIntervalSeconds = 20f;
        private float nextRetry;

        private void Update()
        {
            if (Time.unscaledTime < nextRetry)
            {
                return;
            }

            nextRetry = Time.unscaledTime + RetryIntervalSeconds;
            StoreAchievements.RetryPending();
        }
    }

    internal interface IAchievementStore
    {
        string GetString(string key, string defaultValue);
        void SetString(string key, string value);
        void Save();
    }

    internal sealed class PlayerPrefsAchievementStore : IAchievementStore
    {
        public string GetString(string key, string defaultValue)
        {
            return PlayerPrefs.GetString(key, defaultValue);
        }

        public void SetString(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
        }

        public void Save()
        {
            PlayerPrefs.Save();
        }
    }

    internal sealed class AchievementLedger
    {
        [Serializable]
        private sealed class StoredAchievementSet
        {
            public string[] values = Array.Empty<string>();
        }

        private readonly IAchievementStore store;
        private readonly string keyPrefix;

        public AchievementLedger(IAchievementStore store, string keyPrefix)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.keyPrefix = string.IsNullOrWhiteSpace(keyPrefix)
                ? throw new ArgumentException(
                    "A key prefix is required.", nameof(keyPrefix))
                : keyPrefix;
        }

        public void RecordEarned(string achievementId)
        {
            HashSet<string> earned = ReadSet(EarnedKey);
            if (earned.Add(achievementId))
            {
                WriteSet(EarnedKey, earned);
            }
        }

        public void MarkDelivered(
            string receiptNamespace,
            string achievementId)
        {
            HashSet<string> delivered = ReadSet(ReceiptKey(receiptNamespace));
            if (delivered.Add(achievementId))
            {
                WriteSet(ReceiptKey(receiptNamespace), delivered);
            }
        }

        public IEnumerable<string> GetPending(string receiptNamespace)
        {
            HashSet<string> delivered = ReadSet(ReceiptKey(receiptNamespace));
            return ReadSet(EarnedKey).Where(id => !delivered.Contains(id));
        }

        private string EarnedKey => keyPrefix + ".earned";

        private string ReceiptKey(string receiptNamespace)
        {
            if (string.IsNullOrWhiteSpace(receiptNamespace))
            {
                throw new ArgumentException(
                    "A receipt namespace is required.",
                    nameof(receiptNamespace));
            }

            return keyPrefix + ".receipt." + receiptNamespace;
        }

        private HashSet<string> ReadSet(string key)
        {
            string value = store.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(value))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            if (value[0] == '{')
            {
                try
                {
                    StoredAchievementSet payload =
                        JsonUtility.FromJson<StoredAchievementSet>(value);
                    return new HashSet<string>(
                        payload?.values ?? Array.Empty<string>(),
                        StringComparer.Ordinal);
                }
                catch
                {
                    return new HashSet<string>(StringComparer.Ordinal);
                }
            }

            // Read the pre-1.0 delimiter format so existing pending unlocks migrate
            // automatically the next time this set is written.
            return new HashSet<string>(
                value.Split(
                    new[] { '|' },
                    StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
        }

        private void WriteSet(string key, HashSet<string> values)
        {
            var payload = new StoredAchievementSet
            {
                values = values
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray()
            };
            store.SetString(key, JsonUtility.ToJson(payload));
            store.Save();
        }
    }
}
