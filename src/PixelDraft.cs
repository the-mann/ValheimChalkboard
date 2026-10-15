using System;
using System.Collections.Generic;
namespace ValheimWhiteboard
{
    // Every changed pixel is a separate ordered operation, including erasing.
    public sealed class PixelDraft
    {
        public sealed class Edit { public int Request, Index; public byte Color; public float SentAt; }
        private readonly Queue<Edit> queued = new Queue<Edit>();
        private readonly Dictionary<int,Edit> pending = new Dictionary<int,Edit>();
        public byte[] Pixels { get; private set; }
        public bool Closing, Failed;
        public bool Dirty => queued.Count != 0 || pending.Count != 0;
        public int PendingCount => pending.Count;
        public PixelDraft(byte[] pixels) { Pixels=(byte[])pixels.Clone(); }
        public void Set(int x,int y,byte color)
        {
            if(x<0||y<0||x>=BoardData.Width||y>=BoardData.Height||color>=BoardData.Colors)return;
            if(BoardData.Pixel(Pixels,x,y)==color)return;
            BoardData.SetPixel(Pixels,x,y,color);
            queued.Enqueue(new Edit{Index=y*BoardData.Width+x,Color=color});
        }
        public void Replace(byte[] image,bool all=false)
        {
            for(int y=0;y<BoardData.Height;y++)for(int x=0;x<BoardData.Width;x++)
            {
                byte c=BoardData.Pixel(image,x,y);
                if(all&&BoardData.Pixel(Pixels,x,y)==c)queued.Enqueue(new Edit{Index=y*BoardData.Width+x,Color=c});
                else Set(x,y,c);
            }
        }
        public void Flush(float now,Func<int> next,Action<Edit> send)
        {
            if(Failed)return;
            int budget=256;
            while(queued.Count>0&&pending.Count<512&&budget-->0)
            {
                var edit=queued.Dequeue();edit.Request=next();edit.SentAt=now;pending.Add(edit.Request,edit);send(edit);
            }
        }
        public void Acknowledge(int request,bool accepted)
        {
            if(!pending.TryGetValue(request,out var edit))return;
            pending.Remove(request);
            if(!accepted){queued.Enqueue(edit);Failed=true;Closing=false;}
        }
        public void Observe(byte[] authoritative)
        {
            Pixels=(byte[])authoritative.Clone();
            // Overlay outstanding local operations in their original sequence.
            var overlay=new List<Edit>(pending.Values);overlay.Sort((a,b)=>a.Request.CompareTo(b.Request));
            foreach(var edit in overlay)BoardData.SetPixel(Pixels,edit.Index%BoardData.Width,edit.Index/BoardData.Width,edit.Color);
            foreach(var edit in queued)BoardData.SetPixel(Pixels,edit.Index%BoardData.Width,edit.Index/BoardData.Width,edit.Color);
        }
        public void CheckTimeout(float now)
        { foreach(var edit in pending.Values)if(now-edit.SentAt>10f){Failed=true;Closing=false;return;} }
        public void Retry()
        {
            var edits=new List<Edit>(pending.Values);edits.Sort((a,b)=>a.Request.CompareTo(b.Request));
            edits.AddRange(queued);pending.Clear();queued.Clear();foreach(var edit in edits)queued.Enqueue(edit);Failed=false;
        }
    }
}
