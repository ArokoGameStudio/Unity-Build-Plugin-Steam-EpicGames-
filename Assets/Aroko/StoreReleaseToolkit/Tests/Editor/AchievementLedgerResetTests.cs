using System.Collections.Generic;
using Aroko.StoreRelease.Runtime;
using NUnit.Framework;

namespace Aroko.StoreRelease.Tests.Editor
{
    public sealed class AchievementLedgerResetTests
    {
        private const string Receipt = "steam-test-account";
        private const string Edge = "EDGE_OF_THE_WORLD";
        private const string Europe = "EUROPEAN_WANDERER";
        private const string Memory = "MEMORY_COLLECTOR";

        [Test]
        public void StoreResetDropsHistoricalAwardsAndAllowsGenuineArrivalToEarnAgain()
        {
            var ledger = CreateLedger();
            foreach (string id in new[] { Edge, Europe, Memory })
            {
                ledger.RecordEarned(id);
                ledger.MarkDelivered(Receipt, id);
            }

            // The old local receipt blocks the unlock even when Steam is locked.
            ledger.RecordEarned(Edge);
            Assert.That(ledger.GetPending(Receipt), Is.Empty);

            ledger.ReconcileDelivered(Receipt, new string[0]);
            Assert.That(ledger.GetPending(Receipt), Is.Empty,
                "Startup must not replay awards from the old reset game.");

            ledger.RecordEarned(Edge);
            Assert.That(ledger.GetPending(Receipt), Is.EquivalentTo(new[] { Edge }));
            ledger.MarkDelivered(Receipt, Edge);
            Assert.That(ledger.GetPending(Receipt), Is.Empty);
        }

        [Test]
        public void FullGameResetClearsConfirmedAndPendingLocalProgress()
        {
            var ledger = CreateLedger();
            ledger.RecordEarned(Edge);
            ledger.MarkDelivered(Receipt, Edge);
            ledger.RecordEarned(Europe);

            ledger.ClearEarned();
            Assert.That(ledger.GetPending(Receipt), Is.Empty);
            ledger.RecordEarned(Edge);
            Assert.That(ledger.GetPending(Receipt), Is.Empty,
                "Full Game Reset should retain store receipts.");

            ledger.ClearEarned();
            ledger.ReconcileDelivered(Receipt, new string[0]);
            Assert.That(ledger.GetPending(Receipt), Is.Empty);
            ledger.RecordEarned(Edge);
            Assert.That(ledger.GetPending(Receipt), Is.EquivalentTo(new[] { Edge }));
        }

        [Test]
        public void SteamSnapshotDoesNotCreateLocalProgressAfterFullReset()
        {
            var ledger = CreateLedger();
            ledger.ReconcileDelivered(Receipt, new[] { Edge, Europe, Memory });
            ledger.ReconcileDelivered(Receipt, new string[0]);
            Assert.That(ledger.GetPending(Receipt), Is.Empty);
        }

        [Test]
        public void ReconciliationPreservesUnconfirmedOfflineUnlocks()
        {
            var ledger = CreateLedger();
            ledger.RecordEarned(Edge);
            ledger.RecordEarned(Europe);
            ledger.MarkDelivered(Receipt, Europe);

            ledger.ReconcileDelivered(Receipt, new[] { Europe });
            Assert.That(ledger.GetPending(Receipt), Is.EquivalentTo(new[] { Edge }));
            ledger.ReconcileDelivered(Receipt, new[] { Edge, Europe });
            Assert.That(ledger.GetPending(Receipt), Is.Empty);
        }

        [Test]
        public void LegacyAppReceiptDoesNotReplayForNewSteamAccount()
        {
            var ledger = CreateLedger();
            const string oldAppReceipt = "steam-v1-app-4892020";
            const string newAccountReceipt = "steam-v2-app-4892020-user-test";
            ledger.RecordEarned(Edge);
            ledger.MarkDelivered(oldAppReceipt, Edge);

            ledger.ReconcileDelivered(
                newAccountReceipt,
                new string[0],
                oldAppReceipt);
            Assert.That(ledger.GetPending(newAccountReceipt), Is.Empty,
                "An old app-wide receipt must not replay before current save progress is checked.");

            ledger.RecordEarned(Edge);
            Assert.That(ledger.GetPending(newAccountReceipt), Is.EquivalentTo(new[] { Edge }));
        }

        private static AchievementLedger CreateLedger()
        {
            return new AchievementLedger(new MemoryStore(), "isolated-test");
        }

        private sealed class MemoryStore : IAchievementStore
        {
            private readonly Dictionary<string, string> values =
                new Dictionary<string, string>();

            public string GetString(string key, string defaultValue)
            {
                return values.TryGetValue(key, out string value) ? value : defaultValue;
            }

            public void SetString(string key, string value) { values[key] = value; }
            public void Save() { }
        }
    }
}
