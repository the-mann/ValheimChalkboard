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
        Test("individual pixel arrivals merge different pixels and last same-pixel arrival wins",()=>{
            var f=Setup();
            void Pixel(long sender,int request,int index,int color)=>ZRoutedRpc.instance.Deliver(BoardNetwork.Pixel,sender,f.record.m_uid,request,index,color);
            Pixel(101,1,1,2);Pixel(102,2,2,3);Pixel(101,3,1,4);
            Assert(Revision(f.record)==3&&BoardData.Pixel(Pixels(f.record),1,0)==4&&BoardData.Pixel(Pixels(f.record),2,0)==3,"Pixel arrivals overwrote unrelated drawing");
            Pixel(102,4,1,0);Assert(BoardData.Pixel(Pixels(f.record),1,0)==0&&BoardData.Pixel(Pixels(f.record),2,0)==3,"Eraser overwrote unrelated pixel");
            Assert(ZRoutedRpc.instance.Sent.Where(x=>x.method==BoardNetwork.PixelResult).All(x=>x.target!=103),"Pixel event leaked to vanilla");
        });
        Test("pixel endpoint rejects unauthorized and malformed edits",()=>{
            var f=Setup();foreach(var edit in new[]{(-1,1),(9216,1),(0,-1),(0,8)})ZRoutedRpc.instance.Deliver(BoardNetwork.Pixel,101,f.record.m_uid,1,edit.Item1,edit.Item2);
            ZRoutedRpc.instance.Deliver(BoardNetwork.Pixel,103,f.record.m_uid,1,0,1);
            f.a.Dead=true;ZRoutedRpc.instance.Deliver(BoardNetwork.Pixel,101,f.record.m_uid,1,0,1);
            Assert(Revision(f.record)==0,"Invalid pixel request mutated board");
        });
        Test("pixel queue sends each edit while drawing and preserves received remote pixels",()=>{
            Setup();var d=new PixelDraft(new byte[BoardData.ByteCount]);d.Set(1,0,2);d.Set(2,0,3);d.Set(1,0,4);
            var sent=new System.Collections.Generic.List<PixelDraft.Edit>();int sequence=0;d.Flush(0,()=>++sequence,sent.Add);
            Assert(sent.Count==3&&sent[0].Index==1&&sent[2].Color==4,"Pixel edits were replaced by snapshot or coalesced");
            var remote=Convert.FromBase64String(Image(8,8,6));d.Observe(remote);
            Assert(BoardData.Pixel(d.Pixels,8,8)==6&&BoardData.Pixel(d.Pixels,1,0)==4,"Pending overlay destroyed remote pixel");
            foreach(var edit in sent)d.Acknowledge(edit.Request,true);Assert(!d.Dirty,"Acknowledgements left queue dirty");
        });
        Test("pixel queue timeout retries in order and wipe sends zero for every pixel",()=>{
            var d=new PixelDraft(new byte[BoardData.ByteCount]);d.Set(1,0,2);int n=0;d.Flush(0,()=>++n,e=>{});d.CheckTimeout(11);Assert(d.Failed&&d.Dirty,"Timeout lost pixel");
            d.Retry();var sent=new System.Collections.Generic.List<PixelDraft.Edit>();d.Flush(12,()=>++n,sent.Add);Assert(sent.Count==1&&sent[0].Color==2,"Retry lost pixel");d.Acknowledge(sent[0].Request,true);
            d.Replace(new byte[BoardData.ByteCount],true);int zeros=0;
            while(d.Dirty){sent.Clear();d.Flush(13,()=>++n,sent.Add);foreach(var e in sent){Assert(e.Color==0,"Wipe sent nonzero pixel");zeros++;d.Acknowledge(e.Request,true);}}
            Assert(zeros==9216,"Wipe did not clear every pixel independently");
        });
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
        Test("last server arrival wins regardless of base revision",()=>{
            var f=Setup();Save(101,f.record,0,Image(1,1,2));string last=Image(2,2,3);Save(102,f.record,0,last);
            Assert(Accepted()&&Revision(f.record)==2&&f.record.GetString(BoardSchema.DataKey)==last,"Last arrival did not win");
            Save(101,f.record,0,Image(3,3,4));Assert(Accepted()&&Revision(f.record)==3&&BoardData.Pixel(Pixels(f.record),3,3)==4,"Older base revision was rejected");
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
        Test("reconnected peer cannot inherit negotiation from a reused UID",()=>{
            var f=Setup();ZNet.instance.Peers[101]=new ZNetPeer{m_uid=101};int before=ZRoutedRpc.instance.Sent.Count;
            Save(101,f.record,0,Image(0,0,1));Assert(Revision(f.record)==0&&ZRoutedRpc.instance.Sent.Count==before,"New connection inherited old capability");
            ZRoutedRpc.instance.Deliver(BoardNetwork.Hello,101,BoardNetwork.Protocol);Save(101,f.record,0,Image(0,0,1));
            Assert(Accepted()&&Revision(f.record)==1,"Reconnect could not negotiate afresh");
        });
        Test("ordinary sign cannot be written through drawing endpoint",()=>{
            Setup();var ordinary=new ZDO();ordinary.Set(ZDOVars.s_text,"Keep me");ZDOMan.instance.Add(ordinary);Save(101,ordinary,0,Image(0,0,1));Assert(!Accepted()&&ordinary.Values.Count==1&&ordinary.GetString(ZDOVars.s_text)=="Keep me","Ordinary sign modified");
        });
        Test("dedicated server accepts peer character data with no instantiated players",()=>{
            var f=Setup();Player.All.Clear();var character=new ZDO{Owner=101,Prefab="Player".GetStableHashCode()};
            character.Set(ZDOVars.s_playerID,1001L);ZDOMan.instance.Add(character);ZNet.instance.Peers[101].m_characterID=character.m_uid;
            Save(101,f.record,0,Image(10,10,3));Assert(Accepted()&&Revision(f.record)==1,"Dedicated writer rejected without Player GameObject");
            character.Position=new Vector3(6,0,0);Save(101,f.record,1,Image(11,11,4));Assert(!Accepted(),"Distant replicated character accepted");
            character.Position=Vector3.zero;character.Set(ZDOVars.s_dead,true);Save(101,f.record,1,Image(11,11,4));Assert(!Accepted(),"Dead replicated character accepted");
            character.Set(ZDOVars.s_dead,false);character.Owner=102;Save(101,f.record,1,Image(11,11,4));Assert(!Accepted(),"Other peer character accepted");
            character.Owner=101;var ward=new PrivateArea();PrivateArea.Add(ward);Save(101,f.record,1,Image(11,11,4));Assert(!Accepted(),"Ward rejected character bypassed");
            ward.Permitted.Add(1001);Save(101,f.record,1,Image(11,11,4));Assert(Accepted(),"Permitted replicated character rejected");
        });
        Test("dedicated autosave completes instead of exhausting retries without a Player object",()=>{
            var f=Setup();Player.All.Clear();var character=new ZDO{Owner=101,Prefab="Player".GetStableHashCode()};
            character.Set(ZDOVars.s_playerID,1001L);ZDOMan.instance.Add(character);ZNet.instance.Peers[101].m_characterID=character.m_uid;
            var draft=new AutosaveDraft(Pixels(f.record),0);BoardData.SetPixel(draft.Pixels,12,13,6);draft.Changed(0);
            int attempts=0;
            while(draft.Dirty&&attempts<3){attempts++;draft.BeginSubmit(Pixels(f.record),Revision(f.record),attempts);Save(101,f.record,draft.Revision,Convert.ToBase64String(draft.Pixels),attempts);draft.Acknowledge(Accepted(),Revision(f.record),attempts);}
            Assert(attempts==1&&!draft.Dirty&&draft.Failures==0&&Revision(f.record)==1&&BoardData.Pixel(Pixels(f.record),12,13)==6,"Dedicated save exhausted retries and left draft unsynced");
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
        Test("bad payloads do not mutate canonical state",()=>{
            foreach(var bad in new[]{"",new string('!',6144),Convert.ToBase64String(Enumerable.Repeat((byte)0x88,BoardData.ByteCount).ToArray())}){var f=Setup();Save(101,f.record,0,bad);Assert(!Accepted()&&Revision(f.record)==0,"Invalid payload accepted");}
        });
        Test("repeated snapshot is accepted in server receive order",()=>{
            var f=Setup();var data=Image(0,0,1);Save(101,f.record,0,data);Save(101,f.record,0,data);Assert(Accepted()&&Revision(f.record)==2&&f.record.GetString(BoardSchema.DataKey)==data,"Repeated snapshot rejected");
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
        Test("autosave streams during a held stroke at bounded cadence",()=>{
            var d=new AutosaveDraft(new byte[BoardData.ByteCount],0);d.Changed(.01f);
            Assert(!d.ShouldSubmit(.05f,true)&&d.ShouldSubmit(.11f,true),"Streaming cadence broken");
            Assert(d.BeginSubmit(d.Baseline,0,.11f)&&!d.BeginSubmit(d.Baseline,0,.11f)&&!d.ShouldSubmit(4f,true),"Duplicate pending submit");
            BoardData.SetPixel(d.Pixels,2,3,4);d.Changed(.12f);d.Acknowledge(true,1,.13f);
            Assert(d.Dirty&&!d.Pending&&BoardData.Pixel(d.Baseline,2,3)==0&&BoardData.Pixel(d.Pixels,2,3)==4,"Ack discarded edits made in flight");
            Assert(!d.ShouldSubmit(.15f,true)&&d.ShouldSubmit(.22f,true),"Stream did not continue while drawing");
            d.RequestClose();d.BeginSubmit(d.Baseline,1,.23f);Assert(d.Acknowledge(true,2,.24f)&&!d.Dirty,"Final stream snapshot not acknowledged");
        });
        Test("closing with an in-flight frame waits for the final edited frame",()=>{
            var d=new AutosaveDraft(new byte[BoardData.ByteCount],0);d.Changed(0);d.BeginSubmit(d.Baseline,0,.1f);
            BoardData.SetPixel(d.Pixels,8,8,5);d.Changed(.11f);Assert(!d.RequestClose(),"Closed with stream pending");
            Assert(!d.Acknowledge(true,1,.12f)&&d.Closing&&d.Dirty&&d.ShouldSubmit(.12f,false),"Earlier ack closed or delayed final frame");
            d.BeginSubmit(d.Baseline,1,.12f);Assert(d.Acknowledge(true,2,.13f)&&!d.Dirty,"Final frame did not complete close");
        });
        Test("Done waits for host acknowledgement and retains baseline",()=>{
            var f=Setup();var initial=Pixels(f.record);var d=new AutosaveDraft(initial,0);
            BoardData.SetPixel(d.Pixels,8,9,4);d.Changed(0);Assert(!d.RequestClose(),"Dirty Done closed early");
            d.BeginSubmit(initial,0,1);Save(101,f.record,d.Revision,Convert.ToBase64String(d.Pixels));
            Assert(Accepted()&&d.Acknowledge(true,Revision(f.record),1.1f)&&!d.Dirty&&!d.Pending&&d.Revision==1,"Done failed to await accepted save");
            Assert(BoardData.Pixel(Pixels(f.record),8,9)==4&&d.Baseline.SequenceEqual(d.Pixels)&&!ReferenceEquals(d.Baseline,d.Pixels),"Accepted baseline not preserved independently");
        });
        Test("stream snapshot replaces concurrent image without rebasing",()=>{
            var f=Setup();Save(101,f.record,0,Image(2,2,5));var d=new AutosaveDraft(Pixels(f.record),1);
            BoardData.SetPixel(d.Pixels,2,2,0);BoardData.SetPixel(d.Pixels,9,9,6);d.Changed(0);
            Save(102,f.record,1,Image(20,20,7));d.BeginSubmit(Pixels(f.record),Revision(f.record),1);
            Save(101,f.record,d.Revision,Convert.ToBase64String(d.Pixels));d.Acknowledge(Accepted(),Revision(f.record),1.1f);
            Assert(!d.Dirty&&BoardData.Pixel(Pixels(f.record),2,2)==0&&BoardData.Pixel(Pixels(f.record),9,9)==6&&BoardData.Pixel(Pixels(f.record),20,20)==0,"Snapshot unexpectedly rebased");
        });
        Test("idle editor observes remote snapshots without replacing a local draft",()=>{
            var d=new AutosaveDraft(new byte[BoardData.ByteCount],0);var remote=Convert.FromBase64String(Image(1,1,3));
            Assert(d.Observe(remote,1)&&BoardData.Pixel(d.Pixels,1,1)==3,"Remote image not visible");
            d.Changed(1);Assert(!d.Observe(new byte[BoardData.ByteCount],2)&&BoardData.Pixel(d.Pixels,1,1)==3,"Remote image replaced local draft");
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
