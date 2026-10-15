using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

// Test-only observer. No reference to Chalkboard or Jotunn and no Harmony patches.
// Reads a COPY of the migrated world after stock Valheim loading has completed.
[BepInPlugin("marcus.valheim.chalkboard.qa.stock", "Stock world QA observer", "0.1.0")]
public sealed class StockWorldProbe : BaseUnityPlugin
{
    bool done;
    float deadline;
    void Awake() { deadline = Time.realtimeSinceStartup + 180; }
    void Update()
    {
        if (done) return;
        if (ZNet.instance == null || !ZNet.instance.IsServer() || ZNetScene.instance == null || ZoneSystem.instance == null || !ZoneSystem.instance.LocationsGenerated)
        {
            if (Time.realtimeSinceStartup > deadline) Finish("FAIL stock world did not become ready",1);
            return;
        }
        try
        {
            var messages = new List<string>();
            void Check(bool ok,string name) { if (!ok) throw new InvalidOperationException(name); messages.Add("PASS " + name); Logger.LogInfo("PASS " + name); }
            var plugins = BepInEx.Bootstrap.Chainloader.PluginInfos;
            Check(plugins.Count == 1 && plugins.ContainsKey("marcus.valheim.chalkboard.qa.stock"), "only read-only QA observer loaded; Chalkboard and Jotunn absent");
            var records = ((Dictionary<ZDOID,ZDO>)typeof(ZDOMan).GetField("m_objectsByID",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ZDOMan.instance))
                .Values.Where(z => z.GetInt("chalkboard_disposable_qa_fixture",0) != 0).ToList();
            Check(records.Count == 3, "stock world load retained all three previously migrated boards");
            foreach (var record in records)
            {
                int kind = record.GetInt("chalkboard_disposable_qa_fixture",0);
                var prefab = ZNetScene.instance.GetPrefab(record.GetPrefab());
                Check(record.GetPrefab() == "sign".GetStableHashCode() && prefab != null && prefab.GetComponent<Sign>() != null,
                    "stock engine resolves migrated board as vanilla sign: " + kind);
                var pixels = Convert.FromBase64String(record.GetString("whiteboard_pixels_v1",""));
                int pixel = kind*128+kind;
                Check(pixels.Length == 4608 && ((pixels[pixel/2] >> ((pixel&1)*4)) & 15) == kind+1 &&
                    record.GetInt("whiteboard_revision_v1",0) == kind*10 && record.GetInt("chalkboard_kind_v2",0) == kind,
                    "stock disk loader retains opaque drawing metadata: " + kind);
            }
            Finish(string.Join(Environment.NewLine,messages)+Environment.NewLine+"PASS stock-world suite; 8 checks, no Chalkboard/Jotunn loaded",0);
        }
        catch (Exception ex) { Finish("FAIL " + ex,1); }
    }
    void Finish(string report,int code)
    {
        done = true;
        Directory.CreateDirectory(Paths.ConfigPath);
        File.WriteAllText(Path.Combine(Paths.ConfigPath,"stock-world-report.txt"),report);
        Logger.LogInfo(report); Application.Quit(code);
    }
}
