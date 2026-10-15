using System;
using System.Collections.Generic;

namespace ValheimWhiteboard
{
    // Only this service commits drawing revisions. A vanilla ZDO owner never needs our RPC handlers.
    public static class BoardNetwork
    {
        public const int Protocol = 3;
        public const string Hello = "ChalkboardV2_Hello", HelloResult = "ChalkboardV2_HelloResult";
        public const string Save = "ChalkboardV2_Save", Result = "ChalkboardV2_Result";
        public const string Pixel = "ChalkboardV3_Pixel", PixelResult = "ChalkboardV3_PixelResult";
        private static readonly HashSet<ZDOID> changed = new HashSet<ZDOID>();
        private sealed class HostState
        {
            public int Kind, Revision;
            public string Pixels;
            public HostState(ZDO record)
            { Kind = BoardSchema.Kind(record); Revision = Math.Max(0, record.GetInt(BoardSchema.RevisionKey, 0)); Pixels = record.GetString(BoardSchema.DataKey, ""); if (!BoardData.TryDecode(Pixels, out _)) Pixels = BoardSchema.Blank; }
        }
        private static readonly Dictionary<ZDOID, HostState> canonical = new Dictionary<ZDOID, HostState>();
        private static readonly Dictionary<long, ZNetPeer> capable = new Dictionary<long, ZNetPeer>();
        private static readonly Dictionary<ZDOID, DrawingBoard> boards = new Dictionary<ZDOID, DrawingBoard>();
        private static ZRoutedRpc router;
        private static bool handshake;
        private static float nextHello, nextReconcile;
        private static int sequence;
        public static bool Ready => ZNet.instance != null && (ZNet.instance.IsServer() ? BoardMigration.Ready : handshake);
        public static long Host => ZNet.instance.IsServer() ? ZNet.GetUID() : ZNet.instance.GetServerPeer()?.m_uid ?? 0;
        public static void Reset()
        { canonical.Clear(); changed.Clear(); capable.Clear(); boards.Clear(); router = null; handshake = false; nextHello = 0; nextReconcile = 0; sequence = 0; BoardMigration.Reset(); }
        public static void Register()
        {
            if (ZRoutedRpc.instance == null || router == ZRoutedRpc.instance) return;
            router = ZRoutedRpc.instance;
            router.Register<ZDOID,int,int,int>(Pixel, ReceivePixel);
            router.Register<ZDOID,int,bool,int,string>(PixelResult, ReceivePixelResult);
            router.Register<int>(Hello, ReceiveHello);
            router.Register<int>(HelloResult, ReceiveHelloResult);
            router.Register<ZDOID, int, int, string>(Save, ReceiveSave);
            router.Register<ZDOID, int, bool, int, string>(Result, ReceiveResult);
        }
        public static void Tick()
        {
            if (ZNet.instance == null || ZDOMan.instance == null) return;
            Register();
            if (ZNet.instance.IsServer())
            {
                BoardMigration.EnsureReady();
                if (UnityEngine.Time.unscaledTime < nextReconcile) return;
                nextReconcile = UnityEngine.Time.unscaledTime + .1f;
                foreach(var id in changed)ZDOMan.instance.ForceSendZDO(id);changed.Clear();
                Reconcile();
            }
            else if (!handshake && Host != 0 && UnityEngine.Time.unscaledTime >= nextHello)
            { nextHello = UnityEngine.Time.unscaledTime + 2f; router.InvokeRoutedRPC(Host, Hello, Protocol); }
        }
        public static void Reconcile()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            foreach (var id in new List<ZDOID>(canonical.Keys))
            {
                var record = ZDOMan.instance.GetZDO(id);
                if (record == null) canonical.Remove(id); else RestoreCanonical(record);
            }
        }
        private static bool Connected(long sender) => sender == ZNet.GetUID() && ZNet.instance.IsServer() || ZNet.instance.GetPeer(sender) != null;
        private static void ReceiveHello(long sender, int protocol)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || !Connected(sender)) return;
            if (protocol == Protocol) capable[sender] = ZNet.instance.GetPeer(sender); else capable.Remove(sender);
            router.InvokeRoutedRPC(sender, HelloResult, protocol == Protocol ? Protocol : 0);
        }
        private static void ReceiveHelloResult(long sender, int protocol)
        { if (ZNet.instance != null && sender == Host) handshake = protocol == Protocol; }
        public static void Track(ZDO record)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || BoardSchema.Kind(record) == 0 || canonical.ContainsKey(record.m_uid)) return;
            canonical.Add(record.m_uid, new HostState(record));
        }
        public static void RestoreCanonical(ZDO record)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || record == null || !canonical.TryGetValue(record.m_uid, out var state)) return;
            // A vanilla owner may send an older full ZDO after editing its text. Retain its text,
            // health, position and creator, but restore authoritative chalkboard fields only.
            if (record.GetPrefab() == BoardSchema.SignHash && BoardSchema.Kind(record) == state.Kind
                && record.GetInt(BoardSchema.RevisionKey, 0) == state.Revision && record.GetString(BoardSchema.DataKey, "") == state.Pixels) return;
            record.SetOwner(ZNet.GetUID());
            Apply(record, state);
            ZDOMan.instance.ForceSendZDO(record.m_uid);
        }
        private static void Apply(ZDO record, HostState state)
        {
            record.SetPrefab(BoardSchema.SignHash); record.Set(BoardSchema.SchemaKey, BoardSchema.Version);
            record.Set(BoardSchema.KindKey, state.Kind); record.Set(BoardSchema.DataKey, state.Pixels);
            record.Set(BoardSchema.RevisionKey, state.Revision);
        }
        public static int NextRequest() => ++sequence;
        public static void Submit(ZDOID id, int request, int revision, string pixels)
        {
            Register();
            if (!Ready || Host == 0) return;
            if (ZNet.instance.IsServer()) { capable[ZNet.GetUID()] = null; ReceiveSave(ZNet.GetUID(), id, request, revision, pixels); }
            else router.InvokeRoutedRPC(Host, Save, id, request, revision, pixels);
        }
        private static void ReceiveSave(long sender, ZDOID id, int request, int expectedRevision, string encoded)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || !BoardMigration.Ready || !Connected(sender)
                || !capable.TryGetValue(sender, out var negotiatedPeer)
                || (sender != ZNet.GetUID() && !ReferenceEquals(negotiatedPeer,ZNet.instance.GetPeer(sender)))) return;
            var record = ZDOMan.instance.GetZDO(id);
            if (record != null) RestoreCanonical(record);
            if (record == null || BoardSchema.Kind(record) == 0)
            { router.InvokeRoutedRPC(sender, Result, id, request, false, 0, BoardSchema.Blank); return; }
            Track(record);
            var state = canonical[id];
            bool accepted = state.Revision < int.MaxValue
                && BoardData.TryDecode(encoded, out _) && BoardAccess.CanWrite(sender, record.GetPosition());
            if (accepted)
            {
                state.Pixels = encoded; state.Revision++;
                record.SetOwner(ZNet.GetUID()); Apply(record, state);
                ZDOMan.instance.ForceSendZDO(id);
            }
            // Full authoritative snapshot makes retries independent of ZDO replication order.
            router.InvokeRoutedRPC(sender, Result, id, request, accepted, state.Revision, state.Pixels);
        }
        public static void SubmitPixel(ZDOID id,int request,int index,int color)
        {
            Register();if(!Ready||Host==0)return;
            if(ZNet.instance.IsServer()){capable[ZNet.GetUID()]=null;ReceivePixel(ZNet.GetUID(),id,request,index,color);}
            else router.InvokeRoutedRPC(Host,Pixel,id,request,index,color);
        }
        private static void ReceivePixel(long sender,ZDOID id,int request,int index,int color)
        {
            if(ZNet.instance==null||!ZNet.instance.IsServer()||!BoardMigration.Ready||!Connected(sender)
                ||!capable.TryGetValue(sender,out var peer)||(sender!=ZNet.GetUID()&&!ReferenceEquals(peer,ZNet.instance.GetPeer(sender))))return;
            var record=ZDOMan.instance.GetZDO(id);
            if(record!=null)RestoreCanonical(record);
            bool valid=record!=null&&BoardSchema.Kind(record)!=0&&index>=0&&index<BoardData.Width*BoardData.Height
                &&color>=0&&color<BoardData.Colors&&BoardAccess.CanWrite(sender,record.GetPosition());
            if(!valid){router.InvokeRoutedRPC(sender,PixelResult,id,request,false,0,"");return;}
            RestoreCanonical(record);Track(record);var state=canonical[id];
            if(state.Revision==int.MaxValue){router.InvokeRoutedRPC(sender,PixelResult,id,request,false,state.Revision,"");return;}
            var pixels=Convert.FromBase64String(state.Pixels);
            BoardData.SetPixel(pixels,index%BoardData.Width,index/BoardData.Width,(byte)color);
            state.Pixels=Convert.ToBase64String(pixels);state.Revision++;
            record.SetOwner(ZNet.GetUID());Apply(record,state);changed.Add(id);
            string delta=index+":"+color;
            // Target negotiated peers only; vanilla clients receive ordinary sign replication.
            foreach(var target in new List<long>(capable.Keys))
                if(Connected(target)&&(target==ZNet.GetUID()||ReferenceEquals(capable[target],ZNet.instance.GetPeer(target))))
                    router.InvokeRoutedRPC(target,PixelResult,id,target==sender?request:0,true,state.Revision,delta);
        }
        private static void ReceivePixelResult(long sender,ZDOID id,int request,bool accepted,int revision,string delta)
        {
            if(ZNet.instance==null||sender!=Host||revision<0)return;
            int index=0,color=0;
            if(accepted){var parts=delta.Split(':');if(parts.Length!=2||!int.TryParse(parts[0],out index)||!int.TryParse(parts[1],out color)
                ||index<0||index>=BoardData.Width*BoardData.Height||color<0||color>=BoardData.Colors)return;}
            if(boards.TryGetValue(id,out var board)&&board!=null)board.PixelAcknowledged(request,accepted,revision,index,(byte)color);
        }
        public static void Attach(DrawingBoard board, ZDOID id) => boards[id] = board;
        public static void Detach(DrawingBoard board, ZDOID id)
        { if (boards.TryGetValue(id, out var current) && current == board) boards.Remove(id); }
        private static void ReceiveResult(long sender, ZDOID id, int request, bool accepted, int revision, string snapshot)
        {
            if (ZNet.instance == null || sender != Host || !BoardData.TryDecode(snapshot, out _) || revision < 0) return;
            if (boards.TryGetValue(id, out var board) && board != null) board.Acknowledge(request, accepted, revision, snapshot);
        }
    }
}
