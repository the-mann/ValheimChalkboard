using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ValheimWhiteboard
{
    // Fixed-size palette canvas: 4,608 bytes per board, independent of stroke count.
    public sealed class DrawingBoard : MonoBehaviour, Hoverable, Interactable
    {
        public const int Width = 128, Height = 72;
        public const int ByteCount = Width * Height / 2;
        public static readonly Color32[] Palette = {
            new Color32(29,42,36,255), new Color32(235,231,211,255),
            new Color32(219,133,121,255), new Color32(139,180,203,255),
            new Color32(160,191,139,255), new Color32(229,210,138,255),
            new Color32(188,161,200,255), new Color32(225,176,125,255)
        };
        private const string DataKey = "whiteboard_pixels_v1";
        private const string RevisionKey = "whiteboard_revision_v1";
        public string DisplayName = "Chalkboard";
        public Transform Surface;
        public Material SurfaceMaterial;
        private ZNetView view;
        private Texture2D texture;
        private Material material;
        private Mesh drawingMesh;
        private int seenRevision = -1;
        private float nextPoll;
        private const BindingFlags WardFlags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static readonly FieldInfo Wards = typeof(PrivateArea).GetField("m_allAreas", WardFlags);
        private static readonly MethodInfo WardEnabled = typeof(PrivateArea).GetMethod("IsEnabled", WardFlags);
        private static readonly MethodInfo WardInside = typeof(PrivateArea).GetMethod("IsInside", WardFlags);
        private static readonly MethodInfo WardPermitted = typeof(PrivateArea).GetMethod("IsPermitted", WardFlags);

        private bool HasWardAccess(Player player)
        {
            // Match vanilla overlapping-ward behavior, using the requesting player's identity.
            if (Wards == null || WardEnabled == null || WardInside == null || WardPermitted == null) return false;
            bool protectedArea = false;
            foreach (var ward in (List<PrivateArea>)Wards.GetValue(null))
            {
                if (ward == null || !(bool)WardEnabled.Invoke(ward, null)
                    || !(bool)WardInside.Invoke(ward, new object[] { transform.position, 0f })) continue;
                protectedArea = true;
                var piece = ward.GetComponent<Piece>();
                if ((piece != null && piece.GetCreator() == player.GetPlayerID())
                    || (bool)WardPermitted.Invoke(ward, new object[] { player.GetPlayerID() })) return true;
            }
            return !protectedArea;
        }

        private void Awake()
        {
            view = GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return; // placement ghost / prefab
            view.Register<int, string>("WB_Save", SaveRPC);
            view.Register<bool, int>("WB_Result", ResultRPC);
            if (Surface != null)
            {
                if (SurfaceMaterial != null)
                {
                    texture = NewTexture();
                    material = new Material(SurfaceMaterial) { mainTexture = texture };
                    var face = new GameObject("Drawing");
                    face.layer = gameObject.layer;
                    face.transform.SetParent(Surface, false);
                    face.transform.localPosition = new Vector3(0, 0, 0f);
                    var mesh = drawingMesh = new Mesh();
                    mesh.vertices = new[] { new Vector3(-1,-.575f,0), new Vector3(1,-.575f,0),
                                            new Vector3(1,.575f,0), new Vector3(-1,.575f,0) };
                    mesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
                    mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                    mesh.triangles = new[] { 0,2,1,0,3,2 };
                    mesh.RecalculateNormals();
                    face.AddComponent<MeshFilter>().sharedMesh = mesh;
                    face.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
            }
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .25f;
            Refresh();
        }

        private void Refresh()
        {
            if (view == null || !view.IsValid()) return;
            int revision = view.GetZDO().GetInt(RevisionKey, 0);
            if (revision == seenRevision) return;
            seenRevision = revision;
            if (texture != null) PaintTexture(texture, ReadPixels());
        }

        public int Revision => view.GetZDO().GetInt(RevisionKey, 0);
        public byte[] ReadPixels()
        {
            string data = view.GetZDO().GetString(DataKey, "");
            if (TryDecode(data, out var pixels)) return pixels;
            return new byte[ByteCount];
        }

        public bool CanEdit()
        {
            var player = Player.m_localPlayer;
            return player != null && view != null && view.IsValid()
                && Vector3.Distance(player.transform.position, transform.position) <= 5f
                && PrivateArea.CheckAccess(transform.position);
        }

        public float GetHoverOffset() => 0f;
        public string GetHoverName() => DisplayName;
        public string GetHoverText() => Localization.instance.Localize(DisplayName + "\n[<color=yellow><b>$KEY_Use</b></color>] Draw");
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !CanEdit()) return false;
            DrawingEditor.Open(this);
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public void Submit(int baseRevision, byte[] pixels)
        {
            if (!CanEdit()) { DrawingEditor.Report(this, false, Revision); return; }
            view.InvokeRPC("WB_Save", baseRevision, Convert.ToBase64String(pixels));
        }

        private void SaveRPC(long sender, int baseRevision, string encoded)
        {
            if (!view.IsOwner()) return;
            // Verify the requesting peer has a live player near this board.
            bool nearby = false;
            foreach (var player in Player.GetAllPlayers())
            {
                var playerView = player.GetComponent<ZNetView>();
                if (playerView != null && playerView.IsValid()
                    && playerView.GetZDO().GetOwner() == sender
                    && !player.IsDead()
                    && Vector3.Distance(player.transform.position, transform.position) <= 5f
                    && HasWardAccess(player))
                { nearby = true; break; }
            }
            int revision = Revision;
            bool accepted = nearby && baseRevision == revision && TryDecode(encoded, out _);
            if (accepted)
            {
                view.GetZDO().Set(DataKey, encoded);
                view.GetZDO().Set(RevisionKey, ++revision);
                Refresh();
            }
            view.InvokeRPC(sender, "WB_Result", accepted, revision);
        }

        private void ResultRPC(long sender, bool accepted, int revision)
        {
            if (view.IsValid() && sender == view.GetZDO().GetOwner())
                DrawingEditor.Report(this, accepted, revision);
        }

        public static bool TryDecode(string encoded, out byte[] pixels)
        {
            pixels = null;
            if (encoded == null || encoded.Length != ByteCount / 3 * 4) return false;
            try { pixels = Convert.FromBase64String(encoded); }
            catch (FormatException) { return false; }
            if (pixels.Length != ByteCount) return false;
            foreach (byte value in pixels)
                if ((value & 15) >= Palette.Length || (value >> 4) >= Palette.Length) return false;
            return true;
        }

        public static byte Pixel(byte[] pixels, int x, int y)
        {
            int i = y * Width + x;
            return (byte)((pixels[i / 2] >> ((i & 1) * 4)) & 15);
        }
        public static void SetPixel(byte[] pixels, int x, int y, byte color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            int i = y * Width + x, shift = (i & 1) * 4;
            pixels[i / 2] = (byte)((pixels[i / 2] & ~(15 << shift)) | (color << shift));
        }
        public static byte[] MergeDraft(byte[] saved, byte[] baseline, byte[] draft)
        {
            var merged = (byte[])saved.Clone();
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
                if (Pixel(draft, x, y) != Pixel(baseline, x, y))
                    SetPixel(merged, x, y, Pixel(draft, x, y));
            return merged;
        }

        public static Texture2D NewTexture() => new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        public static void PaintTexture(Texture2D target, byte[] pixels)
        {
            var colors = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    colors[(Height - 1 - y) * Width + x] = Palette[Pixel(pixels, x, y)];
            target.SetPixels32(colors);
            target.Apply(false);
        }
        private void OnDestroy()
        {
            DrawingEditor.BoardDestroyed(this);
            if (texture != null) Destroy(texture);
            if (material != null) Destroy(material);
            if (drawingMesh != null) Destroy(drawingMesh);
        }
    }
}




