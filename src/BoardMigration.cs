using System;
using System.Collections.Generic;
using System.Reflection;

namespace ValheimWhiteboard
{
    public static class BoardMigration
    {
        public static bool Ready { get; private set; }
        public static bool Persisted { get; private set; }
        public static void Reset() { Ready = false; Persisted = false; }
        public static void EnsurePersisted(Action persist)
        {
            EnsureReady();
            if (!Ready || Persisted) return;
            persist(); // The callback must fail if the synchronous world save did not commit.
            Persisted = true;
        }
        public static int Convert(IEnumerable<ZDO> records, Action<IReadOnlyList<ZDO>> backup)
        {
            var legacy = new List<ZDO>();
            var seen = new HashSet<ZDOID>();
            foreach (var record in records) if (BoardSchema.LegacyKind(record.GetPrefab()) != 0 && seen.Add(record.m_uid)) legacy.Add(record);
            // Backup is an all-or-nothing gate: no mutation until it succeeds.
            if (legacy.Count > 0) backup(legacy);
            foreach (var record in legacy) BoardSchema.Initialize(record, BoardSchema.LegacyKind(record.GetPrefab()));
            return legacy.Count;
        }
        public static void EnsureReady()
        {
            if (Ready || ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            // Includes unloaded sectors. Snapshot before mutating prefab hashes.
            var field = typeof(ZDOMan).GetField("m_objectsByID", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("World record index unavailable; migration cannot be verified");
            var index = field.GetValue(ZDOMan.instance) as Dictionary<ZDOID, ZDO>;
            if (index == null) throw new InvalidOperationException("Unexpected world record index");
            var records = new List<ZDO>(index.Values);
            int count = Convert(records, MigrationBackup.Write);
            int signs = 0;
            foreach (var record in records) if (BoardSchema.Kind(record) != 0) { BoardNetwork.Track(record); signs++; }
            Ready = true;
            Persisted = count == 0;
            WhiteboardPlugin.LogInfo("Vanilla sign migration complete: " + count + " legacy boards converted; " + signs + " chalkboard signs tracked.");
        }
    }
}
