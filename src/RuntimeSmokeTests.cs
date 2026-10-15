using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;

namespace ValheimWhiteboard
{
    // Runs INSIDE Unity against the actual game serializer and installed Harmony hooks.
    // Basic checks use unindexed ephemeral ZDOs. The explicit headless QA flag also
    // enables isolated socket/placement fixtures and optional disposable-world save tests.
    public static class RuntimeSmokeTests
    {
        public static string Run()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || !BoardMigration.Ready)
                throw new InvalidOperationException("Start a disposable local/host world before running chalkboard_validate.");
            var messages = new List<string>(); var scratch = new List<ZDO>();
            void Check(bool condition, string name)
            { if (!condition) throw new InvalidOperationException(name); messages.Add("PASS " + name); }
            ZDO Record()
            {
                uint suffix = 0xf0000000u + (uint)scratch.Count;
                while (ZDOMan.instance.GetZDO(new ZDOID(ZNet.GetUID(), suffix)) != null || scratch.Any(z => z.m_uid.Equals(new ZDOID(ZNet.GetUID(), suffix)))) suffix++;
                var record = new ZDO { m_uid = new ZDOID(ZNet.GetUID(), suffix) };
                AccessTools.Method(typeof(ZDO), "Init").Invoke(record, null);
                record.SetOwner(ZNet.GetUID()); scratch.Add(record); return record;
            }
            string report;
            try
            {
                foreach (var target in new[] {
                    AccessTools.Method(typeof(ZNetView), "GetPrefabName"), AccessTools.Method(typeof(ZNetView), "Awake"),
                    AccessTools.Method(typeof(Sign), "Awake"), AccessTools.Method(typeof(Sign), "Interact"),
                    AccessTools.Method(typeof(ZDO), "Deserialize"), AccessTools.Method(typeof(ZNet), "RPC_PeerInfo"),
                    AccessTools.Method(typeof(ZDOMan), "PrepareSave"), AccessTools.Method(typeof(ZNet), "ServerLoadWorld") })
                    Check(target != null && Harmony.GetPatchInfo(target)?.Owners.Contains("marcus.valheim.whiteboard") == true,
                        "Harmony hook installed: " + target?.DeclaringType?.Name + "." + target?.Name);
                var vanilla = PrefabManager.Instance.GetPrefab("sign");
                Check(vanilla != null && vanilla.GetComponent<Sign>() != null && vanilla.GetComponent<BoardTemplate>() == null,
                    "vanilla sign prefab remains undecorated and retains Sign");
                for (int kind = 1; kind <= 3; kind++)
                {
                    var template = PrefabManager.Instance.GetPrefab(BoardSchema.LegacyNames[kind - 1]);
                    Check(template != null && template.GetComponent<Sign>() != null && template.GetComponent<BoardTemplate>().Kind == kind,
                        "menu template retains vanilla Sign: " + BoardSchema.Name(kind));
                    string networkName = (string)AccessTools.Method(typeof(ZNetView), "GetPrefabName").Invoke(template.GetComponent<ZNetView>(), null);
                    Check(networkName == "sign", "actual patched GetPrefabName returns sign: " + kind);
                    var sourceRecipe = vanilla.GetComponent<Piece>().m_resources;
                    var recipe = template.GetComponent<Piece>().m_resources;
                    Check(recipe.Length == sourceRecipe.Length && recipe.Zip(sourceRecipe, (a,b) => a.m_amount == b.m_amount && a.m_resItem == b.m_resItem).All(x => x),
                        "vanilla recipe/refunds retained: " + kind);
                    Check(template.GetComponentsInChildren<UnityEngine.Collider>(true).Length == vanilla.GetComponentsInChildren<UnityEngine.Collider>(true).Length,
                        "template solid collider count matches vanilla: " + kind);
                }
                var old = Record(); old.SetPrefab(BoardSchema.LegacyNames[1].GetStableHashCode());
                var bytes = new byte[BoardData.ByteCount]; BoardData.SetPixel(bytes,30,40,7);
                string encoded = Convert.ToBase64String(bytes); old.Set(BoardSchema.DataKey,encoded); old.Set(BoardSchema.RevisionKey,12);
                bool backedUp = false;
                BoardMigration.Convert(new[] {old}, records => { backedUp = records[0].GetPrefab() == BoardSchema.LegacyNames[1].GetStableHashCode(); });
                Check(backedUp && BoardSchema.Kind(old) == 2 && old.GetString(BoardSchema.DataKey, "") == encoded && old.GetInt(BoardSchema.RevisionKey,0) == 12,
                    "migration preserves actual game ZDO metadata");
                var packet = new ZPackage(); old.Serialize(packet);
                var vanillaCopy = Record(); vanillaCopy.Deserialize(new ZPackage(packet.GetArray()));
                Check(BoardSchema.Kind(vanillaCopy) == 2 && vanillaCopy.GetString(BoardSchema.DataKey, "") == encoded && vanillaCopy.GetInt(BoardSchema.RevisionKey,0) == 12,
                    "actual game serializer preserves bitmap, marker and revision");
                vanillaCopy.Set(ZDOVars.s_text,"Vanilla label edit");
                var outgoing = new ZPackage(); vanillaCopy.Serialize(outgoing);
                var receiver = Record(); receiver.Deserialize(new ZPackage(outgoing.GetArray()));
                Check(receiver.GetString(ZDOVars.s_text, "") == "Vanilla label edit" && receiver.GetString(BoardSchema.DataKey, "") == encoded && BoardSchema.Kind(receiver) == 2,
                    "actual vanilla text update roundtrip retains opaque chalkboard fields");
                // Track only an ephemeral record; never insert it in ZDOMan or force-send it.
                Check(scratch.All(z => ZDOMan.instance.GetZDO(z.m_uid) == null), "QA records never entered world state");
                if (Jotunn.Managers.GUIManager.IsHeadless() && Array.IndexOf(Environment.GetCommandLineArgs(), "-chalkboard-validate") >= 0)
                {
                    RuntimeWireTests.Run(Check);
                    RuntimeWorldTests.Placement(Check);
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-chalkboard-world-seed") >= 0) RuntimeWorldTests.Seed(Check);
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-chalkboard-world-verify") >= 0) RuntimeWorldTests.Verify(Check);
                }
                messages.Add("PASS runtime smoke suite; " + messages.Count + " checks. Live mixed-client transport is a separate gate.");
            }
            catch (Exception ex) { messages.Add("FAIL " + ex); }
            finally { foreach (var record in scratch) record.Reset(); }
            report = string.Join(Environment.NewLine,messages);
            var folder = Path.Combine(BepInEx.Paths.ConfigPath,"ValheimChalkboard","validation"); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder,"runtime-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".txt");
            File.WriteAllText(path,report); WhiteboardPlugin.LogInfo(report + Environment.NewLine + "Runtime report: " + path);
            return report;
        }
    }
}
