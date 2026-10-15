using System;
using UnityEngine;

namespace ValheimWhiteboard
{
    public sealed class DrawingBoard : MonoBehaviour, Hoverable, Interactable
    {
        public const int Width = BoardData.Width, Height = BoardData.Height, ByteCount = BoardData.ByteCount;
        public static readonly Color32[] Palette = {
            new Color32(29,42,36,255), new Color32(235,231,211,255),
            new Color32(219,133,121,255), new Color32(139,180,203,255),
            new Color32(160,191,139,255), new Color32(229,210,138,255),
            new Color32(188,161,200,255), new Color32(225,176,125,255)
        };
        public string DisplayName = "Chalkboard";
        private Transform surface;
        private Material surfaceMaterial, material;
        private ZNetView view;
        private Texture2D texture;
        private Mesh drawingMesh;
        private int seenRevision = -1, replyRevision = -1, awaiting;
        private string replyPixels;
        private float nextPoll;
        private ZDOID id;
        private void Awake() => view = GetComponent<ZNetView>();
        public void Configure(Transform target, Material template, string name)
        { surface = target; surfaceMaterial = template; DisplayName = name; }
        private void Start()
        {
            if (view == null || !view.IsValid()) return;
            id = view.GetZDO().m_uid; BoardNetwork.Attach(this,id);
            if (surface != null && surfaceMaterial != null)
            {
                texture = NewTexture(); material = new Material(surfaceMaterial) { mainTexture = texture };
                var face = new GameObject("Drawing"); face.layer = gameObject.layer;
                face.transform.SetParent(surface,false); face.transform.localPosition = Vector3.zero;
                drawingMesh = new Mesh();
                drawingMesh.vertices = new[] {new Vector3(-1,-.575f,0),new Vector3(1,-.575f,0),new Vector3(1,.575f,0),new Vector3(-1,.575f,0)};
                drawingMesh.uv = new[] {new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
                drawingMesh.colors = new[] {Color.white,Color.white,Color.white,Color.white};
                drawingMesh.triangles = new[] {0,2,1,0,3,2}; drawingMesh.RecalculateNormals();
                face.AddComponent<MeshFilter>().sharedMesh = drawingMesh; face.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            Refresh();
        }
        private void Update()
        { if (Time.unscaledTime >= nextPoll) {nextPoll=Time.unscaledTime+.25f;Refresh();} }
        private void Refresh()
        {
            if (view == null || !view.IsValid()) return;
            int revision = Revision; if (revision == seenRevision) return; seenRevision = revision;
            if (texture != null) PaintTexture(texture,ReadPixels());
        }
        public int Revision => view != null && view.IsValid() ? Math.Max(replyRevision,view.GetZDO().GetInt(BoardSchema.RevisionKey,0)) : 0;
        public byte[] ReadPixels()
        {
            if (view == null || !view.IsValid()) return new byte[ByteCount];
            var data = view.GetZDO();
            string encoded = replyRevision >= data.GetInt(BoardSchema.RevisionKey,0) ? replyPixels : data.GetString(BoardSchema.DataKey,"");
            return TryDecode(encoded,out var pixels) ? pixels : new byte[ByteCount];
        }
        public bool CanEdit()
        {
            var player=Player.m_localPlayer;
            return BoardNetwork.Ready && player!=null && !player.IsDead() && view!=null && view.IsValid()
                && BoardSchema.Kind(view.GetZDO())!=0 && Vector3.Distance(player.transform.position,transform.position)<=5f
                && PrivateArea.CheckAccess(transform.position);
        }
        public float GetHoverOffset()=>0f;
        public string GetHoverName()=>DisplayName;
        public string GetHoverText()=>Localization.instance.Localize(DisplayName+ (BoardNetwork.Ready ? "\n[<color=yellow><b>$KEY_Use</b></color>] Draw" : "\nConnecting to chalkboard host…"));
        public bool Interact(Humanoid user,bool hold,bool alt)
        {if(hold||!CanEdit())return false;DrawingEditor.Open(this);return true;}
        public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
        public void Submit(int baseRevision,byte[] pixels)
        {
            if(!CanEdit()){DrawingEditor.Report(this,false,Revision);return;}
            awaiting=BoardNetwork.NextRequest();
            BoardNetwork.Submit(id,awaiting,baseRevision,Convert.ToBase64String(pixels));
        }
        public void Acknowledge(int request,bool accepted,int revision,string snapshot)
        {
            if(request!=awaiting||!TryDecode(snapshot,out _)||revision<replyRevision)return;
            replyRevision=revision;replyPixels=snapshot;Refresh();DrawingEditor.Report(this,accepted,revision);
        }
        public static bool TryDecode(string data,out byte[] pixels)=>BoardData.TryDecode(data,out pixels);
        public static byte Pixel(byte[] pixels,int x,int y)=>BoardData.Pixel(pixels,x,y);
        public static void SetPixel(byte[] pixels,int x,int y,byte color)=>BoardData.SetPixel(pixels,x,y,color);
        public static byte[] MergeDraft(byte[] saved,byte[] baseline,byte[] draft)=>BoardData.MergeDraft(saved,baseline,draft);
        public static Texture2D NewTexture()=>new Texture2D(Width,Height,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
        public static void PaintTexture(Texture2D target,byte[] pixels)
        {
            var colors=new Color32[Width*Height];
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)colors[(Height-1-y)*Width+x]=Palette[Pixel(pixels,x,y)];
            target.SetPixels32(colors);target.Apply(false);
        }
        private void OnDestroy()
        {
            BoardNetwork.Detach(this,id);DrawingEditor.BoardDestroyed(this);
            if(texture!=null)Destroy(texture);if(material!=null)Destroy(material);if(drawingMesh!=null)Destroy(drawingMesh);
        }
    }
}
