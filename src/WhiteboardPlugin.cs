using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace ValheimWhiteboard
{
    [BepInPlugin("marcus.valheim.whiteboard", "Valheim Chalkboard", "0.5.0")]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.ServerMustHaveMod, VersionStrictness.Minor)]
    public sealed class WhiteboardPlugin : BaseUnityPlugin
    {
        private static ManualLogSource log;
        private Harmony harmony;
        private bool faulted;
        private bool validateHeadless;
        private bool validated;
        private float validationDeadline;
        public static void LogInfo(string message) => log?.LogInfo(message);
        public static void LogError(string message) => log?.LogError(message);
        private void Awake()
        {
            log = Logger;
            validateHeadless = Array.IndexOf(Environment.GetCommandLineArgs(), "-chalkboard-validate") >= 0;
            validationDeadline = Time.realtimeSinceStartup + 180f;
            harmony = new Harmony("marcus.valheim.whiteboard"); harmony.PatchAll(typeof(WhiteboardPlugin).Assembly);
            PrefabManager.OnVanillaPrefabsAvailable += Register;
            new Terminal.ConsoleCommand("chalkboard_validate", "Run non-world-mutating engine smoke checks on a host.", args => { args.Context.AddString(RuntimeSmokeTests.Run()); }, onlyServer: true);
        }
        private void Update()
        {
            if (faulted) return;
            try
            {
                BoardNetwork.Tick();
                if (validateHeadless && !validated && GUIManager.IsHeadless())
                {
                    if (BoardMigration.Ready && BoardMigration.Persisted && ZNet.instance != null && ZNet.instance.IsServer() && ZNetScene.instance != null
                        && ZoneSystem.instance != null && ZoneSystem.instance.LocationsGenerated)
                    {
                        validated = true;
                        string report = RuntimeSmokeTests.Run();
                        Application.Quit(report.Contains("FAIL ") ? 1 : 0);
                    }
                    else if (Time.realtimeSinceStartup > validationDeadline)
                    { validated = true; LogError("FAIL headless validation timed out waiting for the disposable world."); Application.Quit(2); }
                }
            }
            catch (Exception ex) { faulted = true; LogError("Chalkboard initialization stopped: " + ex); if (validateHeadless) Application.Quit(3); }
        }
        private void OnDestroy()
        { PrefabManager.OnVanillaPrefabsAvailable -= Register; harmony?.UnpatchSelf(); }
        private void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            for (int kind = 1; kind <= 3; kind++) RegisterBoard(kind);
        }
        private void RegisterBoard(int kind)
        {
            try
            {
                var custom = new CustomPiece(BoardSchema.LegacyNames[kind - 1], "sign", new PieceConfig
                {
                    Name = BoardSchema.Name(kind), Description = "A drawing slate. Vanilla players see a wooden sign.",
                    PieceTable = "Hammer", Category = "Furniture"
                });
                var prefab = custom.PiecePrefab;
                // Local menu template only. The Harmony prefab-name hook creates a vanilla sign ZDO.
                prefab.AddComponent<BoardTemplate>().Kind = kind;
                // Identical recipe/refunds and solid collider for all clients, including vanilla.
                prefab.GetComponent<Piece>().m_resources = PrefabManager.Instance.GetPrefab("sign").GetComponent<Piece>().m_resources;
                if (!PieceManager.Instance.AddPiece(custom)) throw new InvalidOperationException("Piece registration failed");
                LogInfo(BoardSchema.Name(kind) + " registered; network backing: vanilla sign.");
            }
            catch (Exception ex) { LogError("Chalkboard registration failed: " + ex); }
        }
    }
}
