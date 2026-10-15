// Minimal engine doubles for managed integration tests. Production schema, migration,
// patches, RPC service, access control and drawing component are linked unchanged.
// Unity lifecycle and Valheim transport/serialization are simulated, not claimed as live tests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace UnityEngine {
 public class Object { public string name; protected static void Destroy(object o) {} }
 public class Component:Object {
  public GameObject gameObject=new GameObject();public Transform transform=>gameObject.transform;
  public T GetComponent<T>() where T:class=>gameObject.GetComponent<T>();
 }
 public class MonoBehaviour:Component {}
 public class GameObject {public int layer;public Transform transform=new Transform();private Dictionary<Type,object> components=new();public GameObject(string n=""){} public T GetComponent<T>() where T:class=>components.TryGetValue(typeof(T),out var v)?(T)v:null;public T AddComponent<T>() where T:new(){var v=new T();components[typeof(T)]=v;if(v is Component c)c.gameObject=this;return v;} }
 public class Transform {public Vector3 position,localPosition;public void SetParent(Transform p,bool b){} }
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 zero=>new(0,0,0);public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));}
 public struct Vector2 {public Vector2(float x,float y){} }
 public struct Color32 {public Color32(byte r,byte g,byte b,byte a){} }
 public struct Color {public static Color white=>new();}
 public class Material {public object mainTexture;public Material(Material m){} }
 public class Texture2D {public FilterMode filterMode;public TextureWrapMode wrapMode;public Texture2D(int w,int h,TextureFormat f,bool b){}public void SetPixels32(Color32[] c){}public void Apply(bool b){} }
 public enum FilterMode{Point}public enum TextureWrapMode{Clamp}public enum TextureFormat{RGBA32}
 public class Mesh {public Vector3[] vertices;public Vector2[] uv;public Color[] colors;public int[] triangles;public void RecalculateNormals(){} }
 public class MeshFilter:Component {public Mesh sharedMesh;}public class MeshRenderer:Component {public Material sharedMaterial;}
 public static class Time {public static float unscaledTime;}
}
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)]public class HarmonyPatch:Attribute{public Type Type;public string Method;public HarmonyPatch(){}public HarmonyPatch(Type t,string m){Type=t;Method=m;}}
 public class HarmonyPrefix:Attribute{}public class HarmonyPostfix:Attribute{}
}
namespace BepInEx {public static class Paths {public static string ConfigPath=Path.Combine(Environment.CurrentDirectory,"tests","MultiplayerTests","bin","migration-qa");}}
public interface Hoverable{float GetHoverOffset();string GetHoverName();string GetHoverText();}
public interface Interactable{bool Interact(Humanoid user,bool hold,bool alt);bool UseItem(Humanoid user,ItemDrop.ItemData item);}
public class Humanoid:UnityEngine.Component{}
public class Sign:UnityEngine.Component{}
public class ItemDrop{public class ItemData{}}
public class Localization{public static Localization instance=new();public string Localize(string s)=>s;}
public static class StringExtensionMethods{
 public static int GetStableHashCode(this string value){unchecked{int a=5381,b=a;for(int i=0;i<value.Length&&value[i]!='\0';i+=2){a=((a<<5)+a)^value[i];if(i==value.Length-1||value[i+1]=='\0')break;b=((b<<5)+b)^value[i+1];}return a+b*1566083941;}}
}
public static class ZDOVars{public static int s_text="text".GetStableHashCode(),s_dead="dead".GetStableHashCode(),s_playerID="playerID".GetStableHashCode();}
public readonly struct ZDOID:IEquatable<ZDOID>{public static readonly ZDOID None=new(0,0);public readonly long User;public readonly uint ID;public ZDOID(long u,uint id){User=u;ID=id;}public bool Equals(ZDOID b)=>User==b.User&&ID==b.ID;public override bool Equals(object o)=>o is ZDOID b&&Equals(b);public override int GetHashCode()=>HashCode.Combine(User,ID);public override string ToString()=>User+":"+ID;}
public class ZPackage{private byte[] bytes=Array.Empty<byte>();public byte[] GetArray()=>bytes;public void Set(byte[] b)=>bytes=b;}
public class ZDO{
 static uint next;public ZDOID m_uid=new(100,++next);public long Owner=100;public int Prefab="sign".GetStableHashCode();public UnityEngine.Vector3 Position;public Dictionary<string,object> Values=new();
 public bool GetBool(int k,bool d=false)=>Values.TryGetValue("#"+k,out var v)?(bool)v:d;public long GetLong(int k,long d=0)=>Values.TryGetValue("#"+k,out var v)?(long)v:d;public void Set(int k,bool v)=>Values["#"+k]=v;public void Set(int k,long v)=>Values["#"+k]=v;public int GetPrefab()=>Prefab;public void SetPrefab(int h)=>Prefab=h;public void SetOwner(long u)=>Owner=u;public long GetOwner()=>Owner;public UnityEngine.Vector3 GetPosition()=>Position;
 public int GetInt(string k,int d=0)=>Values.TryGetValue(k,out var v)?(int)v:d;public string GetString(string k,string d="")=>Values.TryGetValue(k,out var v)?(string)v:d;
 public string GetString(int k,string d="")=>GetString("#"+k,d);public void Set(int k,string v)=>Set("#"+k,v);public void Set(string k,int v)=>Values[k]=v;public void Set(string k,string v)=>Values[k]=v;
 public void Serialize(ZPackage p){using var m=new MemoryStream();using var w=new BinaryWriter(m);w.Write(Prefab);w.Write(Values.Count);foreach(var e in Values.OrderBy(x=>x.Key)){w.Write(e.Key);w.Write(e.Value is int);if(e.Value is int n)w.Write(n);else w.Write((string)e.Value);}p.Set(m.ToArray());}
 public void Deserialize(ZPackage p){using var m=new MemoryStream(p.GetArray());using var r=new BinaryReader(m);Prefab=r.ReadInt32();Values.Clear();int count=r.ReadInt32();for(int i=0;i<count;i++){var k=r.ReadString();Values[k]=r.ReadBoolean()?(object)r.ReadInt32():r.ReadString();}}
 public ZDO Snapshot(){var p=new ZPackage();Serialize(p);var b=new ZDO{m_uid=m_uid,Owner=Owner,Position=Position};b.Deserialize(p);return b;}
}
public class ZDOMan{
 public static ZDOMan instance=new();private Dictionary<ZDOID,ZDO> m_objectsByID=new();public List<ZDOID> Forced=new();
 public void Add(ZDO z)=>m_objectsByID[z.m_uid]=z;public ZDO GetZDO(ZDOID id)=>m_objectsByID.TryGetValue(id,out var v)?v:null;public void Remove(ZDOID id)=>m_objectsByID.Remove(id);public void ForceSendZDO(ZDOID id)=>Forced.Add(id);
}
public class ZNetPeer{public long m_uid;public ZDOID m_characterID;}
public class ZNet{
 public static ZNet instance=new();public bool Server=true;public static long UID=100;public Dictionary<long,ZNetPeer> Peers=new();public ZNetPeer ServerPeer=new(){m_uid=100};
 public static long GetUID()=>UID;public bool IsServer()=>Server;public ZNetPeer GetServerPeer()=>ServerPeer;public ZNetPeer GetPeer(long id)=>Peers.TryGetValue(id,out var p)?p:null;
}
public class ZRoutedRpc{
 public static ZRoutedRpc instance=new();public Dictionary<string,Delegate> Methods=new();public List<(long target,string method,object[] args)> Sent=new();
 public void Register<T>(string n,Action<long,T> a)=>Methods[n]=a;
 public void Register<T,U,V,B>(string n,Action<long,T,U,V,B> a)=>Methods[n]=a;
 public void Register<T,U,V,B,K>(string n,Action<long,T,U,V,B,K> a)=>Methods[n]=a;
 public void InvokeRoutedRPC(long target,string name,params object[] args){Sent.Add((target,name,args));if(target==ZNet.GetUID())Deliver(name,ZNet.GetUID(),args);}
 public void Deliver(string name,long sender,params object[] args){var call=new object[args.Length+1];call[0]=sender;Array.Copy(args,0,call,1,args.Length);Methods[name].DynamicInvoke(call);}
}
public class ZNetView:UnityEngine.Component{
 public bool Valid=true;public ZDO Data=new();public bool IsValid()=>Valid;public ZDO GetZDO()=>Data;
}
public class Player:Humanoid{
 public static Player m_localPlayer;public static List<Player> All=new();public bool Dead;public long ID;public static List<Player> GetAllPlayers()=>All;public bool IsDead()=>Dead;public long GetPlayerID()=>ID;
}
public class Piece:UnityEngine.Component{public long Creator;public long GetCreator()=>Creator;}
public class PrivateArea:UnityEngine.Component{
 private static List<PrivateArea> m_allAreas=new();public bool Enabled=true,Inside=true;public HashSet<long> Permitted=new();
 private bool IsEnabled()=>Enabled;private bool IsInside(UnityEngine.Vector3 p,float r)=>Inside;private bool IsPermitted(long id)=>Permitted.Contains(id);
 public static bool CheckAccess(UnityEngine.Vector3 p)=>true;public static void Reset()=>m_allAreas.Clear();public static void Add(PrivateArea p)=>m_allAreas.Add(p);
}
namespace ValheimWhiteboard{
 internal static class MigrationPersistence { internal static void Save() {} }
 public class SignDecoration:UnityEngine.MonoBehaviour{}
 public static class WhiteboardPlugin{public static List<string> Logs=new();public static void LogInfo(string s)=>Logs.Add(s);public static void LogError(string s)=>Logs.Add(s);}
 public static class DrawingEditor{public static List<(bool accepted,int revision)> Results=new();public static void Open(DrawingBoard b){}public static void Report(DrawingBoard b,bool a,int r)=>Results.Add((a,r));public static void BoardDestroyed(DrawingBoard b){}public static void ReportPixel(DrawingBoard b,int request,bool accepted){} }
}
