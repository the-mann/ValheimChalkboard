using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using ValheimWhiteboard;

class Program
{
    static int count;
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Test(string name,Action body){body();count++;Console.WriteLine("PASS "+name);}
    static void Invoke(object obj,string method)=>obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,null);
    static Player PlayerFor(long peer,long id)
    {
        var o=new GameObject();var p=o.AddComponent<Player>();p.ID=id;
        o.AddComponent<ZNetView>().Data.Owner=peer;Player.All.Add(p);return p;
    }
    static (ZDO record,Player a,Player b,Player vanilla) Setup()
    {
        ZNet.UID=100;ZNet.instance=new ZNet();ZDOMan.instance=new ZDOMan();ZRoutedRpc.instance=new ZRoutedRpc();
        Player.All.Clear();PrivateArea.Reset();DrawingEditor.Results.Clear();Time.unscaledTime=0;
        foreach(long peer in new long[]{101,102,103})ZNet.instance.Peers[peer]=new ZNetPeer{m_uid=peer};
        var a=PlayerFor(101,1001);var b=PlayerFor(102,1002);var vanilla=PlayerFor(103,1003);Player.m_localPlayer=a;
        var record=new ZDO{Owner=103};BoardSchema.Initialize(record,1);ZDOMan.instance.Add(record);
        BoardNetwork.Reset();BoardNetwork.Register();BoardMigration.EnsureReady();
        ZRoutedRpc.instance.Deliver(BoardNetwork.Hello,101,BoardNetwork.Protocol);
        ZRoutedRpc.instance.Deliver(BoardNetwork.Hello,102,BoardNetwork.Protocol);
        return(record,a,b,vanilla);
    }
    static string Image(int x,int y,byte c){var p=new byte[BoardData.ByteCount];BoardData.SetPixel(p,x,y,c);return Convert.ToBase64String(p);}
    static void Save(long sender,ZDO record,int revision,string encoded,int request=1)=>ZRoutedRpc.instance.Deliver(BoardNetwork.Save,sender,record.m_uid,request,revision,encoded);
    static (long target,string method,object[] args) Reply()=>ZRoutedRpc.instance.Sent.Last(x=>x.method==BoardNetwork.Result);
    static bool Accepted()=> (bool)Reply().args[2];
    static int Revision(ZDO z)=>z.GetInt(BoardSchema.RevisionKey,0);
    static byte[] Pixels(ZDO z)=>Convert.FromBase64String(z.GetString(BoardSchema.DataKey));
    static DrawingBoard Attach(ZDO record)
    {var o=new GameObject();o.AddComponent<ZNetView>().Data=record;var b=o.AddComponent<DrawingBoard>();Invoke(b,"Awake");Invoke(b,"Start");return b;}
    static void Main()
    {
        Test("placement template advertises vanilla sign before record creation",()=>{
            Setup();foreach(int kind in new[]{1,2,3}){var o=new GameObject();o.AddComponent<BoardTemplate>().Kind=kind;var n=o.AddComponent<ZNetView>();string name=BoardSchema.LegacyNames[kind-1];BoardPatches.NetworkPrefab(n,ref name);Assert(name=="sign","Custom prefab escaped");n.Data.Prefab=name.GetStableHashCode();BoardPatches.InitializeBacking(n);Assert(BoardSchema.Kind(n.Data)==kind,"Size marker absent");}
        });
        Test("ordinary signs and invalid templates keep original network identity",()=>{
            Setup();var o=new GameObject();var n=o.AddComponent<ZNetView>();string name="sign";BoardPatches.NetworkPrefab(n,ref name);BoardPatches.InitializeBacking(n);Assert(name=="sign"&&BoardSchema.Kind(n.Data)==0&&n.Data.Values.Count==0,"Ordinary sign altered");o.AddComponent<BoardTemplate>().Kind=99;name="other_piece";BoardPatches.NetworkPrefab(n,ref name);Assert(name=="other_piece","Invalid template decorated");
        });
        Test("placement ghosts do not create persistent chalkboard metadata",()=>{
            Setup();var o=new GameObject();o.AddComponent<BoardTemplate>().Kind=2;var n=o.AddComponent<ZNetView>();n.Valid=false;BoardPatches.InitializeBacking(n);Assert(n.Data.Values.Count==0,"Ghost mutated persistent data");
        });
        Test("schema rejects decorating unrelated network prefabs",()=>{
            Setup();var other=new ZDO{Prefab="other_mod_piece".GetStableHashCode()};bool rejected=false;try{BoardSchema.Initialize(other,1);}catch(InvalidOperationException){rejected=true;}Assert(rejected&&other.Values.Count==0,"Third-party piece changed");
        });
        Test("migration preserves IDs drawing creator health and placement",()=>{
            Setup();for(int kind=1;kind<=3;kind++){var z=new ZDO{Prefab=BoardSchema.LegacyNames[kind-1].GetStableHashCode(),Position=new Vector3(8,9,10),Owner=102};z.Set(BoardSchema.DataKey,Image(10,20,7));z.Set(BoardSchema.RevisionKey,14);z.Set("creator",1234);z.Set("health",87);var id=z.m_uid;int backups=0;int n=BoardMigration.Convert(new[]{z},records=>{backups++;Assert(records[0].GetPrefab()!=BoardSchema.SignHash,"Backup occurred after mutation");});Assert(n==1&&backups==1&&BoardSchema.Kind(z)==kind&&z.m_uid.Equals(id)&&Revision(z)==14&&BoardData.Pixel(Pixels(z),10,20)==7&&z.GetInt("creator")==1234&&z.GetInt("health")==87&&z.Position.x==8&&z.Owner==102,"Migration lost state");}
        });
        Test("duplicate migration inputs are backed up and converted exactly once",()=>{
            Setup();var z=new ZDO{Prefab=BoardSchema.LegacyNames[0].GetStableHashCode()};int backed=0;
            int count=BoardMigration.Convert(new[]{z,z},records=>backed=records.Count);
            Assert(count==1&&backed==1&&BoardSchema.Kind(z)==1,"Duplicate record was migrated repeatedly");
        });
        Test("failed world commit keeps migrated host closed and retry commits once",()=>{
            Setup();var z=new ZDO{Prefab=BoardSchema.LegacyNames[0].GetStableHashCode()};ZDOMan.instance.Add(z);BoardMigration.Reset();
            BoardMigration.EnsureReady();Assert(BoardMigration.Ready&&!BoardMigration.Persisted,"Legacy migration skipped durability gate");
            bool failed=false;try{BoardMigration.EnsurePersisted(()=>throw new IOException("disk full"));}catch(IOException){failed=true;}
            Assert(failed&&!BoardMigration.Persisted,"Failed commit opened host");int commits=0;
            BoardMigration.EnsurePersisted(()=>commits++);BoardMigration.EnsurePersisted(()=>commits++);
            Assert(commits==1&&BoardMigration.Persisted,"Commit retry or idempotence failed");
        });
        Test("migration is idempotent and leaves unrelated signs untouched",()=>{
            var f=Setup();var ordinary=new ZDO();ordinary.Set(ZDOVars.s_text,"My real sign");var other=new ZDO{Prefab="other_mod".GetStableHashCode()};int backups=0;Assert(BoardMigration.Convert(new[]{f.record,ordinary,other},_=>backups++)==0&&backups==0,"Unexpected conversion");Assert(ordinary.GetString(ZDOVars.s_text)=="My real sign"&&ordinary.Values.Count==1&&other.Values.Count==0,"Unrelated data changed");
        });
        Test("failed backup leaves all legacy records unmodified",()=>{
            Setup();var legacy=new ZDO{Prefab=BoardSchema.LegacyNames[0].GetStableHashCode()};bool failed=false;try{BoardMigration.Convert(new[]{legacy},_=>throw new IOException("Disk failure"));}catch(IOException){failed=true;}Assert(failed&&BoardSchema.LegacyKind(legacy.Prefab)==1&&legacy.Values.Count==0,"Migration proceeded without backup");
        });
        Test("world-load migration includes unloaded records and writes recoverable snapshot",()=>{
            Setup();var legacy=new ZDO{Prefab=BoardSchema.LegacyNames[2].GetStableHashCode()};legacy.Set(BoardSchema.DataKey,Image(5,6,4));legacy.Set(BoardSchema.RevisionKey,3);ZDOMan.instance.Add(legacy);BoardMigration.Reset();BoardPatches.WorldLoaded();Assert(BoardMigration.Ready&&BoardSchema.Kind(legacy)==3,"Unloaded board not migrated");var file=Directory.GetFiles(Path.Combine(BepInEx.Paths.ConfigPath,"ValheimChalkboard","migration-backups")).OrderBy(x=>x).Last();using var reader=new BinaryReader(File.OpenRead(file));Assert(reader.ReadString()=="ChalkboardMigrationBackupV2"&&reader.ReadInt32()==1,"Backup header invalid");Assert(reader.ReadString()==legacy.m_uid.ToString(),"Backup ID lost");reader.ReadInt64();reader.ReadSingle();reader.ReadSingle();reader.ReadSingle();int len=reader.ReadInt32();var p=new ZPackage();p.Set(reader.ReadBytes(len));var old=new ZDO();old.Deserialize(p);Assert(BoardSchema.LegacyKind(old.Prefab)==3&&Revision(old)==3&&BoardData.Pixel(Pixels(old),5,6)==4,"Backup did not preserve original");
        });
        Test("host opening and peer entry fail closed when migration cannot back up",()=>{
            Setup();var legacy=new ZDO{Prefab=BoardSchema.LegacyNames[0].GetStableHashCode()};ZDOMan.instance.Add(legacy);BoardMigration.Reset();string saved=BepInEx.Paths.ConfigPath;Directory.CreateDirectory(saved);string obstruction=Path.Combine(saved,"not-a-directory");File.WriteAllText(obstruction,"test");BepInEx.Paths.ConfigPath=obstruction;try{Assert(!BoardPatches.BeforeOpen()&&!BoardPatches.BeforePeerInfo()&&!BoardMigration.Ready&&BoardSchema.LegacyKind(legacy.Prefab)==1,"Failed migration opened session");}finally{BepInEx.Paths.ConfigPath=saved;}
        });
        Test("modded writer saves while backing sign belongs to vanilla peer",()=>{
            var f=Setup();Assert(f.record.Owner==103,"Fixture is not vanilla owned");Save(101,f.record,0,Image(1,1,2));Assert(Accepted()&&Revision(f.record)==1&&f.record.Owner==100,"Host save failed");Assert(Reply().target==101&&ZRoutedRpc.instance.Sent.All(x=>x.target!=103),"Custom RPC leaked to vanilla");
        });
        Test("two peers save sequentially through host RPC",()=>{
            var f=Setup();Save(101,f.record,0,Image(1,1,2));Save(102,f.record,1,Image(2,2,3));Assert(Accepted()&&Revision(f.record)==2&&Reply().target==102,"Second peer failed");
        });
        Test("stale peer cannot replace accepted drawing",()=>{
            var f=Setup();string a=Image(1,1,2);Save(101,f.record,0,a);Save(102,f.record,0,Image(2,2,3));Assert(!Accepted()&&Revision(f.record)==1&&f.record.GetString(BoardSchema.DataKey)==a,"Stale write accepted");Assert((int)Reply().args[3]==1&&(string)Reply().args[4]==a,"Reject did not return canonical snapshot");
        });
        Test("rebase retry preserves two independent peer strokes",()=>{
            var f=Setup();var blank=Pixels(f.record);var draft=Convert.FromBase64String(Image(2,2,3));Save(101,f.record,0,Image(1,1,2));Save(102,f.record,0,Convert.ToBase64String(draft));var remote=Convert.FromBase64String((string)Reply().args[4]);var merged=BoardData.MergeDraft(remote,blank,draft);Save(102,f.record,(int)Reply().args[3],Convert.ToBase64String(merged),2);Assert(Accepted()&&BoardData.Pixel(Pixels(f.record),1,1)==2&&BoardData.Pixel(Pixels(f.record),2,2)==3,"Stroke lost");
        });
        Test("late vanilla owner update cannot roll back drawing revision",()=>{
            var f=Setup();var delayed=f.record.Snapshot();Save(101,f.record,0,Image(7,8,6));delayed.Set(ZDOVars.s_text,"Vanilla edited this label");delayed.Set("health",42);var packet=new ZPackage();delayed.Serialize(packet);f.record.Owner=103;f.record.Deserialize(packet);BoardPatches.IncomingRecord(f.record);Assert(Revision(f.record)==1&&BoardData.Pixel(Pixels(f.record),7,8)==6&&BoardSchema.Kind(f.record)==1,"Canonical data reverted");Assert(f.record.GetString(ZDOVars.s_text)=="Vanilla edited this label"&&f.record.GetInt("health")==42,"Vanilla text/health overwritten");
        });
        Test("vanilla owner removing opaque fields is repaired before disk save",()=>{
            var f=Setup();Save(101,f.record,0,Image(3,4,5));f.record.Values.Remove(BoardSchema.KindKey);f.record.Values.Remove(BoardSchema.DataKey);f.record.Set(BoardSchema.RevisionKey,0);f.record.Owner=103;BoardPatches.BeforeSaveSnapshot();Assert(BoardSchema.Kind(f.record)==1&&Revision(f.record)==1&&BoardData.Pixel(Pixels(f.record),3,4)==5,"Disk save would lose drawing");
        });
        Test("vanilla text change does not remove bitmap or marker",()=>{
            var f=Setup();Save(101,f.record,0,Image(3,4,5));f.record.Set(ZDOVars.s_text,"Custom vanilla text");BoardPatches.IncomingRecord(f.record);Assert(f.record.GetString(ZDOVars.s_text)=="Custom vanilla text"&&BoardSchema.Kind(f.record)==1&&BoardData.Pixel(Pixels(f.record),3,4)==5,"Vanilla label destroyed board");
        });
        Test("world readback seeds host canonical state after restart",()=>{
            var f=Setup();Save(101,f.record,0,Image(20,30,7));var persisted=f.record.Snapshot();ZDOMan.instance=new ZDOMan();ZDOMan.instance.Add(persisted);BoardNetwork.Reset();BoardNetwork.Register();BoardMigration.EnsureReady();ZRoutedRpc.instance.Deliver(BoardNetwork.Hello,102,BoardNetwork.Protocol);Save(102,persisted,1,Image(21,30,6));Assert(Accepted()&&Revision(persisted)==2,"Reloaded revision failed");
        });
        Test("dismantled backing signs are never resurrected",()=>{
            var f=Setup();var id=f.record.m_uid;ZDOMan.instance.Remove(id);BoardNetwork.Tick();Save(101,f.record,0,Image(0,0,1));Assert(!Accepted()&&ZDOMan.instance.GetZDO(id)==null,"Destroyed piece resurrected");
        });
        Test("unknown sender and vanilla peer without capabilities receive no custom replies",()=>{
            var f=Setup();int before=ZRoutedRpc.instance.Sent.Count;Save(999,f.record,0,Image(0,0,1));Save(103,f.record,0,Image(0,0,1));Assert(ZRoutedRpc.instance.Sent.Count==before&&Revision(f.record)==0,"Unnegotiated peer received mod messages");
        });
        Test("incompatible schema handshake disables saving",()=>{
            var f=Setup();ZRoutedRpc.instance.Deliver(BoardNetwork.Hello,101,99);Assert((int)ZRoutedRpc.instance.Sent.Last().args[0]==0,"Schema mismatch approved");int before=ZRoutedRpc.instance.Sent.Count;Save(101,f.record,0,Image(0,0,1));Assert(Revision(f.record)==0&&before==ZRoutedRpc.instance.Sent.Count,"Mismatch saved");
        });
        Test("disconnected negotiated peer cannot save",()=>{
            var f=Setup();ZNet.instance.Peers.Remove(102);int before=ZRoutedRpc.instance.Sent.Count;Save(102,f.record,0,Image(0,0,1));Assert(Revision(f.record)==0&&before==ZRoutedRpc.instance.Sent.Count,"Disconnected peer saved");
        });
        Test("ordinary sign cannot be written through drawing endpoint",()=>{
            Setup();var ordinary=new ZDO();ordinary.Set(ZDOVars.s_text,"Keep me");ZDOMan.instance.Add(ordinary);Save(101,ordinary,0,Image(0,0,1));Assert(!Accepted()&&ordinary.Values.Count==1&&ordinary.GetString(ZDOVars.s_text)=="Keep me","Ordinary sign modified");
        });
        Test("out of range and dead writers rejected",()=>{
            var f=Setup();f.b.transform.position=new Vector3(6,0,0);Save(102,f.record,0,Image(0,0,1));Assert(!Accepted(),"Distant writer saved");f.b.transform.position=Vector3.zero;f.b.Dead=true;Save(102,f.record,0,Image(0,0,1));Assert(!Accepted()&&Revision(f.record)==0,"Dead writer saved");
        });
        Test("five-metre interaction boundary allowed",()=>{
            var f=Setup();f.b.transform.position=new Vector3(5,0,0);Save(102,f.record,0,Image(0,0,1));Assert(Accepted(),"Valid boundary rejected");
        });
        Test("ward validation uses writer identity not host identity",()=>{
            var f=Setup();var o=new GameObject();var w=o.AddComponent<PrivateArea>();o.AddComponent<Piece>().Creator=f.a.ID;PrivateArea.Add(w);Save(102,f.record,0,Image(0,0,1));Assert(!Accepted(),"Remote inherited local ward access");w.Permitted.Add(f.b.ID);Save(102,f.record,0,Image(0,0,1));Assert(Accepted(),"Permitted remote denied");
        });
        Test("disabled and overlapping wards match vanilla permissions",()=>{
            var f=Setup();PrivateArea.Add(new PrivateArea{Enabled=false});Save(102,f.record,0,Image(0,0,1));Assert(Accepted(),"Disabled ward blocked");var blocked=new PrivateArea();PrivateArea.Add(blocked);var allowed=new PrivateArea();allowed.Permitted.Add(f.b.ID);PrivateArea.Add(allowed);Save(102,f.record,1,Image(1,1,2));Assert(Accepted(),"Permitted overlapping ward denied");
        });
        Test("bad payloads and future revisions do not mutate canonical state",()=>{
            foreach(var bad in new[]{"",new string('!',6144),Convert.ToBase64String(Enumerable.Repeat((byte)0x88,BoardData.ByteCount).ToArray())}){var f=Setup();Save(101,f.record,0,bad);Assert(!Accepted()&&Revision(f.record)==0,"Invalid payload accepted");}var g=Setup();Save(101,g.record,20,Image(0,0,1));Assert(!Accepted()&&Revision(g.record)==0,"Future revision accepted");
        });
        Test("replayed request cannot increment revision twice",()=>{
            var f=Setup();var data=Image(0,0,1);Save(101,f.record,0,data);Save(101,f.record,0,data);Assert(!Accepted()&&Revision(f.record)==1,"Replay changed state");
        });
        Test("non-host ignores direct save RPC",()=>{
            var f=Setup();ZNet.instance.Server=false;Save(101,f.record,0,Image(0,0,1));Assert(Revision(f.record)==0,"Non-host accepted write");
        });
        Test("client sends saves to host never to vanilla sign owner",()=>{
            var f=Setup();var board=Attach(f.record);ZNet.instance.Server=false;ZNet.UID=101;ZRoutedRpc.instance.Deliver(BoardNetwork.HelloResult,100,BoardNetwork.Protocol);board.Submit(0,Convert.FromBase64String(Image(0,0,1)));var wire=ZRoutedRpc.instance.Sent.Last();Assert(wire.method==BoardNetwork.Save&&wire.target==100&&wire.target!=f.record.Owner,"Client routed to vanilla owner");
        });
        Test("forged hello and acknowledgements cannot update client",()=>{
            var f=Setup();var board=Attach(f.record);ZNet.instance.Server=false;ZNet.UID=101;ZRoutedRpc.instance.Deliver(BoardNetwork.HelloResult,999,BoardNetwork.Protocol);Assert(!BoardNetwork.Ready,"Spoofed hello accepted");ZRoutedRpc.instance.Deliver(BoardNetwork.HelloResult,100,BoardNetwork.Protocol);board.Submit(0,Convert.FromBase64String(Image(0,0,1)));int request=(int)ZRoutedRpc.instance.Sent.Last().args[1];ZRoutedRpc.instance.Deliver(BoardNetwork.Result,999,f.record.m_uid,request,true,8,Image(2,2,6));Assert(board.Revision==0&&DrawingEditor.Results.Count==0,"Spoofed result accepted");
        });
        Test("ack snapshot is usable before replication and obsolete request replies ignored",()=>{
            var f=Setup();var board=Attach(f.record);ZNet.instance.Server=false;ZNet.UID=101;ZRoutedRpc.instance.Deliver(BoardNetwork.HelloResult,100,BoardNetwork.Protocol);board.Submit(0,Convert.FromBase64String(Image(0,0,1)));int old=(int)ZRoutedRpc.instance.Sent.Last().args[1];board.Submit(0,Convert.FromBase64String(Image(3,3,7)));int current=(int)ZRoutedRpc.instance.Sent.Last().args[1];ZRoutedRpc.instance.Deliver(BoardNetwork.Result,100,f.record.m_uid,old,true,1,Image(0,0,1));Assert(DrawingEditor.Results.Count==0,"Old request completed new editor");ZRoutedRpc.instance.Deliver(BoardNetwork.Result,100,f.record.m_uid,current,false,7,Image(4,4,5));Assert(board.Revision==7&&BoardData.Pixel(board.ReadPixels(),4,4)==5&&Revision(f.record)==0,"Snapshot depends on replication");f.record.Set(BoardSchema.RevisionKey,6);Assert(board.Revision==7,"Late ZDO regressed revision");
        });
        Test("session reset clears previous capabilities and authoritative state",()=>{
            var f=Setup();BoardNetwork.Reset();BoardNetwork.Register();BoardMigration.EnsureReady();int before=ZRoutedRpc.instance.Sent.Count;Save(101,f.record,0,Image(0,0,1));Assert(ZRoutedRpc.instance.Sent.Count==before,"Previous session capability leaked");
        });
        Test("codec covers all pixels and merge includes erasing without mutation",()=>{
            var p=new byte[BoardData.ByteCount];for(int y=0;y<BoardData.Height;y++)for(int x=0;x<BoardData.Width;x++)BoardData.SetPixel(p,x,y,(byte)((x+y)%8));Assert(BoardData.TryDecode(Convert.ToBase64String(p),out var q)&&p.SequenceEqual(q),"Codec roundtrip failed");var d=(byte[])p.Clone();BoardData.SetPixel(d,1,1,0);var m=BoardData.MergeDraft(p,p,d);Assert(BoardData.Pixel(m,1,1)==0&&BoardData.Pixel(p,1,1)==2,"Eraser merge failed");BoardData.SetPixel(d,-1,0,1);BoardData.SetPixel(d,128,0,1);BoardData.SetPixel(d,0,0,99);Assert(BoardData.Pixel(d,0,0)==0,"Invalid color changed data");
        });
        Test("autosave waits for stroke completion and debounce",()=>{
            var d=new AutosaveDraft(new byte[BoardData.ByteCount],0);d.Changed(1f);
            Assert(!d.ShouldSubmit(2f,true)&&!d.ShouldSubmit(1.3f,false)&&d.ShouldSubmit(1.5f,false),"Stroke debounce broken");
            Assert(d.BeginSubmit(d.Baseline,0,2f)&&!d.BeginSubmit(d.Baseline,0,2f)&&!d.ShouldSubmit(4f,false),"Duplicate pending submit");
        });
        Test("Done waits for host acknowledgement and retains baseline",()=>{
            var f=Setup();var initial=Pixels(f.record);var d=new AutosaveDraft(initial,0);
            BoardData.SetPixel(d.Pixels,8,9,4);d.Changed(0);Assert(!d.RequestClose(),"Dirty Done closed early");
            d.BeginSubmit(initial,0,1);Save(101,f.record,d.Revision,Convert.ToBase64String(d.Pixels));
            Assert(Accepted()&&d.Acknowledge(true,Revision(f.record),1.1f)&&!d.Dirty&&!d.Pending&&d.Revision==1,"Done failed to await accepted save");
            Assert(BoardData.Pixel(Pixels(f.record),8,9)==4&&d.Baseline.SequenceEqual(d.Pixels)&&!ReferenceEquals(d.Baseline,d.Pixels),"Accepted baseline not preserved independently");
        });
        Test("editor retries stale save with remote drawing and local eraser intact",()=>{
            var f=Setup();Save(101,f.record,0,Image(2,2,5));var d=new AutosaveDraft(Pixels(f.record),1);
            BoardData.SetPixel(d.Pixels,2,2,0);BoardData.SetPixel(d.Pixels,9,9,6);d.Changed(0);d.BeginSubmit(d.Baseline,1,1);
            var remote=Pixels(f.record);BoardData.SetPixel(remote,20,20,7);Save(102,f.record,1,Convert.ToBase64String(remote));
            Save(101,f.record,d.Revision,Convert.ToBase64String(d.Pixels));Assert(!Accepted(),"Stale editor accepted");
            d.Acknowledge(false,Revision(f.record),1.1f);Assert(d.ShouldSubmit(2f,false),"Rejected draft did not retry");
            d.BeginSubmit(Pixels(f.record),Revision(f.record),2);Save(101,f.record,d.Revision,Convert.ToBase64String(d.Pixels));d.Acknowledge(Accepted(),Revision(f.record),2.1f);
            Assert(!d.Dirty&&BoardData.Pixel(Pixels(f.record),2,2)==0&&BoardData.Pixel(Pixels(f.record),9,9)==6&&BoardData.Pixel(Pixels(f.record),20,20)==7,"Editor rebase lost stroke/eraser");
        });
        Test("timeout and rejection preserve draft and stop automatic retries",()=>{
            var d=new AutosaveDraft(new byte[BoardData.ByteCount],0);BoardData.SetPixel(d.Pixels,4,5,2);d.Changed(0);d.RequestClose();d.BeginSubmit(d.Baseline,0,1);
            Assert(!d.CheckTimeout(11f)&&d.CheckTimeout(11.01f)&&d.Dirty&&!d.Pending&&!d.Closing&&d.Failures==3&&!d.ShouldSubmit(20,false),"Timeout discarded or retried draft");
            Assert(!d.Acknowledge(true,1,20)&&d.Dirty&&d.Revision==0,"Late acknowledgement cleared timed-out draft");
            d.Failures=0;for(int i=0;i<3;i++){d.BeginSubmit(d.Baseline,0,21+i);d.Acknowledge(false,0,21+i);}
            Assert(d.Failures==3&&!d.ShouldSubmit(30,false)&&BoardData.Pixel(d.Pixels,4,5)==2,"Rejection retry limit lost draft");
            Assert(!d.RequestClose()&&d.Failures==0,"Explicit Done could not retry draft");
        });
        Console.WriteLine($"{count} managed integration tests passed. This suite simulates engine/storage/transport; headless engine and real-client acceptance are separate layers.");
    }
}
