using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimWhiteboard
{
    // Present only on local build-menu templates, not required by the network prefab.
    public sealed class BoardTemplate : MonoBehaviour { public int Kind; }

    [HarmonyPatch]
    public static class BoardPatches
    {
        [HarmonyPatch(typeof(ZNet), "Awake"), HarmonyPrefix]
        public static void SessionStarting() => BoardNetwork.Reset();
        [HarmonyPatch(typeof(ZNet), "Awake"), HarmonyPostfix]
        public static void SessionStarted() => BoardNetwork.Register();
        [HarmonyPatch(typeof(ZNetView), "GetPrefabName"), HarmonyPostfix]
        public static void NetworkPrefab(ZNetView __instance, ref string __result)
        { var template = __instance.GetComponent<BoardTemplate>(); if (template != null && BoardSchema.ValidKind(template.Kind)) __result = "sign"; }
        [HarmonyPatch(typeof(ZNetView), "Awake"), HarmonyPostfix]
        public static void InitializeBacking(ZNetView __instance)
        {
            var template = __instance.GetComponent<BoardTemplate>();
            if (template != null && BoardSchema.ValidKind(template.Kind) && __instance.IsValid())
            { BoardSchema.Initialize(__instance.GetZDO(), template.Kind); BoardNetwork.Track(__instance.GetZDO()); }
        }
        [HarmonyPatch(typeof(Sign), "Awake"), HarmonyPostfix]
        public static void ObserveSign(Sign __instance)
        { if (__instance.GetComponent<SignDecoration>() == null) __instance.gameObject.AddComponent<SignDecoration>(); }
        [HarmonyPatch(typeof(Sign), "Interact"), HarmonyPrefix]
        public static bool Interact(Sign __instance, Humanoid __0, bool hold, bool alt, ref bool __result)
        {
            var board = __instance.GetComponent<DrawingBoard>();
            if (board == null) return true;
            __result = board.Interact(__0, hold, alt); return false;
        }
        [HarmonyPatch(typeof(Sign), "GetHoverText"), HarmonyPrefix]
        public static bool HoverText(Sign __instance, ref string __result)
        { var board = __instance.GetComponent<DrawingBoard>(); if (board == null) return true; __result = board.GetHoverText(); return false; }
        [HarmonyPatch(typeof(Sign), "GetHoverName"), HarmonyPrefix]
        public static bool HoverName(Sign __instance, ref string __result)
        { var board = __instance.GetComponent<DrawingBoard>(); if (board == null) return true; __result = board.GetHoverName(); return false; }
        [HarmonyPatch(typeof(ZNet), "ServerLoadWorld"), HarmonyPostfix]
        public static void WorldLoaded()
        {
            // Scan only after BOTH legacy and chunked world loading have completed.
            try { BoardMigration.EnsureReady(); }
            catch (Exception ex) { WhiteboardPlugin.LogError("Migration failed: " + ex); }
        }
        [HarmonyPatch(typeof(ZNet), "OpenServer"), HarmonyPrefix]
        public static bool BeforeOpen()
        {
            try { BoardMigration.EnsurePersisted(MigrationPersistence.Save); return BoardMigration.Ready && BoardMigration.Persisted; }
            catch (Exception ex) { WhiteboardPlugin.LogError("Migration failed; server remains closed: " + ex); return false; }
        }
        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo"), HarmonyPrefix]
        public static bool BeforePeerInfo()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return true;
            try { BoardMigration.EnsurePersisted(MigrationPersistence.Save); return BoardMigration.Ready && BoardMigration.Persisted; }
            catch (Exception ex) { WhiteboardPlugin.LogError("Migration failed; connection blocked: " + ex); return false; }
        }
        [HarmonyPatch(typeof(ZDO), "Deserialize"), HarmonyPostfix]
        public static void IncomingRecord(ZDO __instance) => BoardNetwork.RestoreCanonical(__instance);
        // PrepareSave runs on the main thread before extra-data maps are snapshotted.
        // ZDO.Save runs on a worker against clones: never mutate live data there.
        [HarmonyPatch(typeof(ZDOMan), "PrepareSave"), HarmonyPrefix]
        public static void BeforeSaveSnapshot() => BoardNetwork.Reconcile();
    }
}
