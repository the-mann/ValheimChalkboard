// Minimal test doubles for compiling the production DrawingBoard without proprietary game DLLs.
// They model RPC registration, authoritative ZDO state and player identity, not Unity or transport.
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public class Object { protected static void Destroy(object o) {} }
 public class Component : Object {
  public GameObject gameObject = new GameObject(); public Transform transform => gameObject.transform;
  public T GetComponent<T>() where T:class => gameObject.GetComponent<T>();
 }
 public class MonoBehaviour : Component {}
 public class GameObject { public int layer; public Transform transform=new Transform(); private Dictionary<Type,object> components=new(); public GameObject(string n="") {} public T GetComponent<T>() where T:class => components.TryGetValue(typeof(T),out var v)?(T)v:null; public T AddComponent<T>() where T:new() { var v=new T(); components[typeof(T)]=v; if(v is Component c)c.gameObject=this; return v; } }
 public class Transform { public Vector3 position,localPosition; public void SetParent(Transform p,bool b){} }
 public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} public static Vector3 zero=>new(0,0,0); public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z)); }
 public struct Vector2 { public Vector2(float x,float y){} }
 public struct Color32 { public Color32(byte r,byte g,byte b,byte a){} }
 public struct Color { public static Color white=>new(); }
 public class Material { public object mainTexture; public Material(Material m){} }
 public class Texture2D { public FilterMode filterMode; public TextureWrapMode wrapMode; public Texture2D(int w,int h,TextureFormat f,bool b){} public void SetPixels32(Color32[] c){} public void Apply(bool b){} }
 public enum FilterMode {Point} public enum TextureWrapMode {Clamp} public enum TextureFormat {RGBA32}
 public class Mesh { public Vector3[] vertices; public Vector2[] uv; public Color[] colors; public int[] triangles; public void RecalculateNormals(){} }
 public class MeshFilter:Component {public Mesh sharedMesh;} public class MeshRenderer:Component {public Material sharedMaterial;}
 public static class Time {public static float unscaledTime;}
}
public interface Hoverable {float GetHoverOffset(); string GetHoverName(); string GetHoverText();}
public interface Interactable {bool Interact(Humanoid user,bool hold,bool alt);bool UseItem(Humanoid user,ItemDrop.ItemData item);}
public class Humanoid:UnityEngine.Component {}
public class ItemDrop {public class ItemData {}}
public class Localization {public static Localization instance=new();public string Localize(string s)=>s;}
public class ZDO {
 public long Owner=100; public Dictionary<string,object> Values=new();
 public long GetOwner()=>Owner; public int GetInt(string k,int d=0)=>Values.TryGetValue(k,out var v)?(int)v:d;
 public string GetString(string k,string d="")=>Values.TryGetValue(k,out var v)?(string)v:d;
 public void Set(string k,int v)=>Values[k]=v; public void Set(string k,string v)=>Values[k]=v;
}
public class ZNetView:UnityEngine.Component {
 public bool Valid=true,Owner=true; public ZDO Data=new(); public Dictionary<string,Delegate> Rpc=new();
 public List<(long target,string method,object[] args)> Replies=new();
 public bool IsValid()=>Valid; public bool IsOwner()=>Owner; public ZDO GetZDO()=>Data;
 public void Register<T,U>(string n,Action<long,T,U> a)=>Rpc[n]=a;
 public void InvokeRPC(string n,params object[] args)=>InvokeRPC(100,n,args);
 public void InvokeRPC(long target,string n,params object[] args)=>Replies.Add((target,n,args));
 public void Deliver(string name,long sender,params object[] args) {var call=new object[args.Length+1];call[0]=sender;Array.Copy(args,0,call,1,args.Length);Rpc[name].DynamicInvoke(call);}
}
public class Player:Humanoid {
 public static Player m_localPlayer; public static List<Player> All=new(); public bool Dead; public long ID;
 public static List<Player> GetAllPlayers()=>All; public bool IsDead()=>Dead; public long GetPlayerID()=>ID;
}
public class Piece:UnityEngine.Component {public long Creator;public long GetCreator()=>Creator;}
public class PrivateArea:UnityEngine.Component {
 private static List<PrivateArea> m_allAreas=new(); public bool Enabled=true,Inside=true;public HashSet<long> Permitted=new();
 private bool IsEnabled()=>Enabled;private bool IsInside(UnityEngine.Vector3 p,float r)=>Inside;private bool IsPermitted(long id)=>Permitted.Contains(id);
 public static bool CheckAccess(UnityEngine.Vector3 p)=>true;
 public static void Reset()=>m_allAreas.Clear();public static void Add(PrivateArea p)=>m_allAreas.Add(p);
}
namespace ValheimWhiteboard {public static class DrawingEditor {
 public static List<(bool accepted,int revision)> Results=new();public static void Open(DrawingBoard b){}
 public static void Report(DrawingBoard b,bool a,int r)=>Results.Add((a,r));public static void BoardDestroyed(DrawingBoard b){}
}}
