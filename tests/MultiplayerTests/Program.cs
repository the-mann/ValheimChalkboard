using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using ValheimWhiteboard;
class Program {
 static int count;
 static void Assert(bool condition,string message) {if(!condition)throw new Exception(message);}
 static void Test(string name,Action body){body();count++;Console.WriteLine("PASS "+name);}
 static (DrawingBoard board,ZNetView net,Player a,Player b) Setup(){
  Player.All.Clear();PrivateArea.Reset();DrawingEditor.Results.Clear();
  var obj=new GameObject();var net=obj.AddComponent<ZNetView>();var board=obj.AddComponent<DrawingBoard>();
  typeof(DrawingBoard).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(board,null);
  Player Add(long peer,long id){var o=new GameObject();var p=o.AddComponent<Player>();p.ID=id;o.AddComponent<ZNetView>().Data.Owner=peer;Player.All.Add(p);return p;}
  var a=Add(101,1001);var b=Add(102,1002);Player.m_localPlayer=a;return(board,net,a,b);
 }
 static string Image(int x,int y,byte c){var p=new byte[DrawingBoard.ByteCount];DrawingBoard.SetPixel(p,x,y,c);return Convert.ToBase64String(p);}
 static bool Accepted(ZNetView n)=> (bool)n.Replies.Last().args[0];
 static void Send(ZNetView n,long peer,int rev,string data)=>n.Deliver("WB_Save",peer,rev,data);
 static void Main(){
  Test("two peers save sequentially through registered RPC",()=>{var f=Setup();Send(f.net,101,0,Image(1,1,2));Assert(Accepted(f.net)&&f.board.Revision==1,"A failed");Send(f.net,102,1,Image(2,2,3));Assert(Accepted(f.net)&&f.board.Revision==2,"B failed");Assert(f.net.Replies.Last().target==102,"Reply routed incorrectly");});
  Test("stale peer cannot overwrite accepted drawing",()=>{var f=Setup();var a=Image(1,1,2);Send(f.net,101,0,a);Send(f.net,102,0,Image(2,2,3));Assert(!Accepted(f.net)&&f.board.Revision==1,"Stale accepted");Assert(f.net.Data.GetString("whiteboard_pixels_v1")==a,"Drawing overwritten");});
  Test("merge and retry preserves both peers strokes",()=>{var f=Setup();var blank=f.board.ReadPixels();var draft=Convert.FromBase64String(Image(2,2,3));Send(f.net,101,0,Image(1,1,2));Send(f.net,102,0,Convert.ToBase64String(draft));var merged=DrawingBoard.MergeDraft(f.board.ReadPixels(),blank,draft);Send(f.net,102,1,Convert.ToBase64String(merged));Assert(Accepted(f.net)&&DrawingBoard.Pixel(f.board.ReadPixels(),1,1)==2&&DrawingBoard.Pixel(f.board.ReadPixels(),2,2)==3,"Merge lost drawing");});
  Test("unknown and spoofed sender rejected",()=>{var f=Setup();Send(f.net,999,0,Image(0,0,1));Assert(!Accepted(f.net)&&f.board.Revision==0,"Unknown accepted");});
  Test("distant player rejected",()=>{var f=Setup();f.b.transform.position=new Vector3(6,0,0);Send(f.net,102,0,Image(0,0,1));Assert(!Accepted(f.net),"Distant accepted");});
  Test("exactly five metres allowed",()=>{var f=Setup();f.b.transform.position=new Vector3(5,0,0);Send(f.net,102,0,Image(0,0,1));Assert(Accepted(f.net),"Boundary rejected");});
  Test("dead and disconnected players rejected",()=>{var f=Setup();f.b.Dead=true;Send(f.net,102,0,Image(0,0,1));Assert(!Accepted(f.net),"Dead accepted");f.b.Dead=false;Player.All.Remove(f.b);Send(f.net,102,0,Image(0,0,1));Assert(!Accepted(f.net),"Disconnected accepted");});
  Test("ward checks requesting identity rather than local player",()=>{var f=Setup();var o=new GameObject();var w=o.AddComponent<PrivateArea>();o.AddComponent<Piece>().Creator=f.a.ID;PrivateArea.Add(w);Send(f.net,102,0,Image(0,0,1));Assert(!Accepted(f.net),"Remote inherited host ward access");w.Permitted.Add(f.b.ID);Send(f.net,102,0,Image(0,0,1));Assert(Accepted(f.net),"Permitted remote denied");});
  Test("disabled wards do not block requests",()=>{var f=Setup();var w=new PrivateArea{Enabled=false};PrivateArea.Add(w);Send(f.net,102,0,Image(0,0,1));Assert(Accepted(f.net),"Disabled ward blocks");});
  Test("overlapping wards match vanilla any-permitted behavior",()=>{var f=Setup();PrivateArea.Add(new PrivateArea());var w=new PrivateArea();w.Permitted.Add(f.b.ID);PrivateArea.Add(w);Send(f.net,102,0,Image(0,0,1));Assert(Accepted(f.net),"Overlap denied");});
  Test("malformed and invalid palette payloads rejected without mutation",()=>{foreach(var bad in new[]{"",new string('!',6144),Convert.ToBase64String(Enumerable.Repeat((byte)0x88,DrawingBoard.ByteCount).ToArray())}){var f=Setup();Send(f.net,101,0,bad);Assert(!Accepted(f.net)&&f.board.Revision==0&&f.net.Data.Values.Count==0,"Bad payload changed state");}});
  Test("non-owner ignores writes during ownership handoff",()=>{var f=Setup();f.net.Owner=false;Send(f.net,101,0,Image(0,0,1));Assert(f.net.Replies.Count==0&&f.board.Revision==0,"Non-owner handled write");f.net.Owner=true;Send(f.net,101,0,Image(0,0,1));Assert(Accepted(f.net),"New owner did not save");});
  Test("forged save acknowledgements ignored",()=>{var f=Setup();f.net.Deliver("WB_Result",999,true,99);Assert(DrawingEditor.Results.Count==0,"Forged ack accepted");f.net.Deliver("WB_Result",f.net.Data.Owner,true,1);Assert(DrawingEditor.Results.Count==1,"Owner ack ignored");});
  Test("replayed successful request cannot increment revision twice",()=>{var f=Setup();var data=Image(0,0,1);Send(f.net,101,0,data);Send(f.net,101,0,data);Assert(!Accepted(f.net)&&f.board.Revision==1,"Replay mutated state");});
  Test("world data readback preserves drawing after component recreation",()=>{var f=Setup();Send(f.net,101,0,Image(20,30,7));var o=new GameObject();var n=o.AddComponent<ZNetView>();n.Data=f.net.Data;var b=o.AddComponent<DrawingBoard>();typeof(DrawingBoard).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(b,null);Assert(b.Revision==1&&DrawingBoard.Pixel(b.ReadPixels(),20,30)==7,"Readback failed");});
  Test("codec roundtrip and eraser merge",()=>{var p=new byte[DrawingBoard.ByteCount];for(int y=0;y<DrawingBoard.Height;y++)for(int x=0;x<DrawingBoard.Width;x++)DrawingBoard.SetPixel(p,x,y,(byte)((x+y)%8));Assert(DrawingBoard.TryDecode(Convert.ToBase64String(p),out var q)&&p.SequenceEqual(q),"Roundtrip failed");var d=(byte[])p.Clone();DrawingBoard.SetPixel(d,1,1,0);var m=DrawingBoard.MergeDraft(p,p,d);Assert(DrawingBoard.Pixel(m,1,1)==0&&DrawingBoard.Pixel(p,1,1)==2,"Erase merge failed");});
  Console.WriteLine($"{count} multiplayer/codec regression tests passed. Transport and Unity are simulated; live test plan still required.");
 }
}
