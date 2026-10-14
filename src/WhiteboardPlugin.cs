using System;
using BepInEx;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using TMPro;
using UnityEngine;

namespace ValheimWhiteboard
{
    [BepInPlugin("marcus.valheim.whiteboard", "Valheim Chalkboard", "0.4.0")]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public sealed class WhiteboardPlugin : BaseUnityPlugin
    {
        private Material boardMaterial;
        private Material frameMaterial;

        private void Awake()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Register;
        }

        private void OnDestroy()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
        }

        private void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            RegisterBoard("piece_whiteboard", "Chalkboard (Small)", 1f, 4, 1);
            RegisterBoard("piece_whiteboard_medium", "Chalkboard (Medium)", 1.5f, 8, 2);
            RegisterBoard("piece_whiteboard_large", "Chalkboard (Large)", 2f, 16, 4);
        }

        private void RegisterBoard(string prefabName, string displayName, float size, int wood, int coal)
        {
            try
            {
                var config = new PieceConfig
                {
                    Name = displayName,
                    Description = "A large timber-framed slate for sketches and plans. Interact to draw.",
                    PieceTable = "Hammer",
                    Category = "Furniture",
                    Requirements = new[] { new RequirementConfig("Wood", wood, 0, true),
                                           new RequirementConfig("Coal", coal, 0, true) }
                };
                // Retain vanilla networking, durability and snap points.
                // Replace the sign interaction with our drawing component.
                var custom = new CustomPiece(prefabName, "sign", config);
                var prefab = custom.PiecePrefab;
                var text = prefab.GetComponentInChildren<TMP_Text>(true);
                if (prefab.GetComponent<Sign>() == null || text == null)
                    throw new InvalidOperationException("The vanilla sign layout has changed: Sign/TMP_Text missing.");

                Material vanillaMaterial = null;
                // Reuse a shipped shader: Unity's generic Unlit shaders are stripped from Valheim.
                foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.GetComponent<TMP_Text>() == null)
                    {
                        if (vanillaMaterial == null) vanillaMaterial = renderer.sharedMaterial;
                        renderer.enabled = false;
                    }

                if (vanillaMaterial == null) throw new InvalidOperationException("Vanilla sign material missing.");
                boardMaterial = new Material(vanillaMaterial);
                boardMaterial.mainTexture = Texture2D.whiteTexture;
                boardMaterial.mainTextureScale = Vector2.one;
                boardMaterial.mainTextureOffset = Vector2.zero;
                if (boardMaterial.HasProperty("_Color")) boardMaterial.SetColor("_Color", Color.white);
                if (boardMaterial.HasProperty("_BumpMap")) boardMaterial.SetTexture("_BumpMap", null);
                boardMaterial.DisableKeyword("_NORMALMAP");
                frameMaterial = new Material(vanillaMaterial);
                if (frameMaterial.HasProperty("_Color")) frameMaterial.SetColor("_Color", new Color(0.65f, 0.48f, 0.30f));
                // Text is a local XY plane facing negative Z. Build the board in
                // that same plane, slightly behind the lettering, at root scale.
                var visual = new GameObject("WhiteboardVisual");
                visual.transform.SetParent(prefab.transform, false);
                visual.layer = prefab.layer;
                visual.transform.position = text.transform.position;
                visual.transform.rotation = text.transform.rotation;
                visual.transform.localScale = new Vector3(size, size, 1f);
                var slateMaterial = new Material(boardMaterial);
                if (slateMaterial.HasProperty("_Color")) slateMaterial.SetColor("_Color", DrawingBoard.Palette[0]);
                Part(visual.transform, "WritingSurface", new Vector3(0, 0, 0.045f),
                     new Vector3(2.0f, 1.15f, 0.06f), slateMaterial, true);
                Part(visual.transform, "Top", new Vector3(0, 0.60f, 0.045f),
                     new Vector3(2.16f, 0.09f, 0.13f), frameMaterial, false);
                Part(visual.transform, "Bottom", new Vector3(0, -0.60f, 0.045f),
                     new Vector3(2.16f, 0.09f, 0.13f), frameMaterial, false);
                foreach (float x in new[] { -1.025f, 1.025f })
                    Part(visual.transform, "Side", new Vector3(x, 0, 0.045f),
                         new Vector3(0.09f, 1.15f, 0.13f), frameMaterial, false);

                Part(visual.transform, "ChalkTray", new Vector3(0, -0.65f, -0.06f),
                     new Vector3(2.16f, 0.08f, 0.25f), frameMaterial, false);
                UnityEngine.Object.DestroyImmediate(prefab.GetComponent<Sign>());
                text.gameObject.SetActive(false);
                var drawing = prefab.AddComponent<DrawingBoard>();
                drawing.Surface = visual.transform;
                drawing.DisplayName = displayName;
                drawing.SurfaceMaterial = boardMaterial;

                if (!PieceManager.Instance.AddPiece(custom))
                    throw new InvalidOperationException("Jotunn rejected the whiteboard prefab.");
                Logger.LogInfo(displayName + " registered in the Hammer menu: " + prefabName);
            }
            catch (Exception ex)
            {
                Logger.LogError(displayName + " registration failed: " + ex);
            }
        }

        private static void Part(Transform parent, string name, Vector3 position,
                                 Vector3 size, Material material, bool collider)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.layer = parent.gameObject.layer;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        }
    }
}






