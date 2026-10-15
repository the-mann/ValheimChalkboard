using System;

namespace ValheimWhiteboard
{
    public static class BoardSchema
    {
        public const string KindKey = "chalkboard_kind_v2", SchemaKey = "chalkboard_schema";
        public const string DataKey = "whiteboard_pixels_v1", RevisionKey = "whiteboard_revision_v1";
        public const int Version = 2;
        public static readonly int SignHash = "sign".GetStableHashCode();
        public static readonly string[] LegacyNames = { "piece_whiteboard", "piece_whiteboard_medium", "piece_whiteboard_large" };
        public static readonly string Blank = Convert.ToBase64String(new byte[BoardData.ByteCount]);
        public static bool ValidKind(int kind) => kind >= 1 && kind <= 3;
        public static int Kind(ZDO zdo) => zdo != null && zdo.GetPrefab() == SignHash
            && zdo.GetInt(SchemaKey, 0) == Version && ValidKind(zdo.GetInt(KindKey, 0)) ? zdo.GetInt(KindKey, 0) : 0;
        public static float Scale(int kind) => kind == 2 ? 1.5f : kind == 3 ? 2f : 1f;
        public static string Name(int kind) => "Chalkboard (" + (kind == 2 ? "Medium" : kind == 3 ? "Large" : "Small") + ")";
        public static int LegacyKind(int prefab)
        { for (int i = 0; i < LegacyNames.Length; i++) if (prefab == LegacyNames[i].GetStableHashCode()) return i + 1; return 0; }
        public static void Initialize(ZDO zdo, int kind)
        {
            if (zdo == null || !ValidKind(kind)) throw new ArgumentException("Invalid chalkboard record");
            // Only convert an actual legacy record, or mark a vanilla sign. Never a third-party piece.
            if (zdo.GetPrefab() != SignHash && LegacyKind(zdo.GetPrefab()) == 0) throw new InvalidOperationException("Not a sign or legacy board");
            zdo.SetPrefab(SignHash);
            zdo.Set(SchemaKey, Version); zdo.Set(KindKey, kind);
            if (string.IsNullOrEmpty(zdo.GetString(ZDOVars.s_text, ""))) zdo.Set(ZDOVars.s_text, "Chalkboard");
            if (!BoardData.TryDecode(zdo.GetString(DataKey, ""), out _)) zdo.Set(DataKey, Blank);
        }
    }
}
