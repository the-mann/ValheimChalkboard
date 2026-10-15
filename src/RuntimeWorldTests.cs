using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ValheimWhiteboard
{
    internal static class RuntimeWorldTests
    {
        const string Tag = "chalkboard_disposable_qa_fixture";
        static List<ZDO> Fixtures() => ((Dictionary<ZDOID,ZDO>)AccessTools.Field(typeof(ZDOMan), "m_objectsByID")
            .GetValue(ZDOMan.instance)).Values.Where(z => z.GetInt(Tag,0) != 0).ToList();
        public static void Placement(Action<bool,string> check)
        {
            var vanilla = Jotunn.Managers.PrefabManager.Instance.GetPrefab("sign");
            for (int kind = 1; kind <= 3; kind++)
            {
                GameObject piece = null;
                try
                {
                    var template = Jotunn.Managers.PrefabManager.Instance.GetPrefab(BoardSchema.LegacyNames[kind-1]);
                    piece = UnityEngine.Object.Instantiate(template, new Vector3(4000+kind*10,100,4000), Quaternion.identity);
                    var record = piece.GetComponent<ZNetView>().GetZDO();
                    check(record != null && record.GetPrefab() == BoardSchema.SignHash && BoardSchema.Kind(record) == kind,
                        "actual Unity placement creates marked vanilla sign from the first Awake: " + kind);
                    var decoration = piece.GetComponent<SignDecoration>();
                    AccessTools.Method(typeof(SignDecoration), "Update").Invoke(decoration,null);
                    check(piece.GetComponent<Sign>() != null && piece.GetComponent<DrawingBoard>() != null &&
                        piece.transform.Find("ChalkboardDecoration") != null,
                        "actual marked sign builds its local drawing decoration: " + kind);
                    check(piece.GetComponentsInChildren<Collider>(true).Length == vanilla.GetComponentsInChildren<Collider>(true).Length,
                        "constructed decoration adds no invisible solid colliders: " + kind);
                }
                finally { if (piece != null) ZNetScene.instance.Destroy(piece); }
            }
        }
        public static void Seed(Action<bool,string> check)
        {
            check(Fixtures().Count == 0, "disposable world has no previous migration fixtures");
            for (int kind = 1; kind <= 3; kind++)
            {
                int prefab = BoardSchema.LegacyNames[kind-1].GetStableHashCode();
                var record = ZDOMan.instance.CreateNewZDO(new Vector3(5000+kind*100,100,5000), prefab);
                record.SetPrefab(prefab); // CreateNewZDO uses the hash for portal indexing, not the stored prefab.
                record.Persistent = true;
                record.Set(Tag,kind); record.Set("health",73f); record.Set("creator",123456L);
                var pixels = new byte[BoardData.ByteCount]; BoardData.SetPixel(pixels,kind,kind, (byte)(kind+1));
                record.Set(BoardSchema.DataKey,Convert.ToBase64String(pixels)); record.Set(BoardSchema.RevisionKey,kind*10);
            }
            MigrationPersistence.Save();
            check(Fixtures().Count == 3 && Fixtures().All(z => BoardSchema.LegacyKind(z.GetPrefab()) != 0),
                "three unloaded legacy boards committed with the actual world save pipeline");
        }
        public static void Verify(Action<bool,string> check)
        {
            var fixtures = Fixtures();
            check(fixtures.Count == 3, "actual restarted world contains all three unloaded migration fixtures");
            foreach (var record in fixtures)
            {
                int kind = record.GetInt(Tag,0);
                check(BoardSchema.Kind(record) == kind && record.GetPrefab() == BoardSchema.SignHash,
                    "restarted world migrated fixture to vanilla sign: " + kind);
                check(record.GetInt(BoardSchema.RevisionKey,0) == kind*10 &&
                    BoardData.TryDecode(record.GetString(BoardSchema.DataKey,""), out var pixels) && BoardData.Pixel(pixels,kind,kind) == kind+1 &&
                    record.GetFloat("health",0) == 73f && record.GetLong("creator",0) == 123456L &&
                    record.GetPosition().x == 5000+kind*100,
                    "actual disk load/migration retains drawing, revision, health, creator and position: " + kind);
            }
            check(BoardMigration.Persisted, "migrated world committed before network entry");
        }
    }
}
