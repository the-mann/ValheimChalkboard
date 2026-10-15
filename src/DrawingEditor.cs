using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimWhiteboard
{
    public sealed class DrawingEditor : MonoBehaviour
    {
        private static DrawingEditor instance;
        private DrawingBoard board;
        private Texture2D preview;
        private AutosaveDraft draft;
        private byte[] pixels { get => draft.Pixels; set => draft.Pixels = value; }
        private readonly Stack<byte[]> undo = new Stack<byte[]>();
        private int revision => draft.Revision;
        private int radius = 1;
        private int failures { get => draft.Failures; set => draft.Failures = value; }
        private byte color = 1;
        private bool drawing, inputBlocked;
        private bool pending => draft.Pending;
        private bool dirty => draft.Dirty;
        private bool closing => draft.Closing;
        private Vector2Int previous;
        private CursorLockMode oldLock;
        private bool oldVisible;
        private string status = "Your marks are kept automatically.";
        private GameObject uiRoot;
        private RectTransform panel, slate, tools, frame;
        private Text statusText, toolHint;
        private readonly List<Button> editingButtons = new List<Button>();
        private readonly List<Outline> chalkSelection = new List<Outline>();
        private readonly List<Outline> sizeSelection = new List<Outline>();
        private Button retry;
        private static readonly Color Gold = new Color(.91f,.72f,.34f,1);
        private static readonly Color Parchment = new Color(.88f,.84f,.72f,1);

        public static void Open(DrawingBoard target)
        {
            if (instance != null) return;
            instance = new GameObject("ChalkboardEditor").AddComponent<DrawingEditor>();
            instance.board = target;
            instance.draft = new AutosaveDraft(target.ReadPixels(), target.Revision);
            instance.preview = DrawingBoard.NewTexture();
            instance.Refresh();
            instance.oldLock = Cursor.lockState;
            instance.oldVisible = Cursor.visible;
            try { instance.CreateUI(); }
            catch { Destroy(instance.gameObject); throw; }
            GUIManager.BlockInput(true);
            instance.inputBlocked=true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private RectTransform Rect(GameObject obj, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            var r = obj.GetComponent<RectTransform>();
            r.anchorMin=min; r.anchorMax=max; r.pivot=new Vector2(.5f,.5f);
            r.anchoredPosition=position; r.sizeDelta=size;
            return r;
        }
        private GameObject Fill(string name,Transform parent,Color tint)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));
            go.transform.SetParent(parent,false);
            var r=Rect(go,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            r.offsetMin=Vector2.zero; r.offsetMax=Vector2.zero;
            go.GetComponent<Image>().color=tint;
            return go;
        }
        private Text TextAt(string value,Transform parent,float x,float y,float width,float height,int fontSize=20,bool heading=false)
        {
            var manager=GUIManager.Instance;
            var go=manager.CreateText(value,parent,new Vector2(0,1),new Vector2(0,1),new Vector2(x,-y),heading?manager.NorseBold:manager.AveriaSerif,fontSize,heading?Gold:Parchment,false,Color.black,width,height,false);
            var text=go.GetComponent<Text>(); text.alignment=TextAnchor.MiddleLeft; text.raycastTarget=false;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;
            return text;
        }
        private Button ButtonAt(string name,Transform parent,float x,float y,float width,float height,System.Action action,bool editing=true)
        {
            var go=GUIManager.Instance.CreateButton(name,parent,new Vector2(0,1),new Vector2(0,1),new Vector2(x,-y),width,height);
            var b=go.GetComponent<Button>(); b.onClick.AddListener(()=>action());
            var text=go.GetComponentInChildren<Text>();
            if(text!=null) { text.font=GUIManager.Instance.AveriaSerifBold; text.fontSize=19; text.color=Parchment; }
            if(editing) editingButtons.Add(b);
            return b;
        }
        private Outline Selection(Button button)
        {
            var outline=button.gameObject.AddComponent<Outline>();
            outline.effectColor=Gold; outline.effectDistance=new Vector2(2,-2);
            return outline;
        }
        private void CreateUI()
        {
            uiRoot=new GameObject("ChalkboardCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=uiRoot.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting=true; canvas.sortingOrder=32760;
            uiRoot.GetComponent<CanvasScaler>().uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;
            // The same component used by Valheim's native menus handles resolution and UI scale.
            uiRoot.AddComponent<GuiScaler>();
            Fill("DimmedWorld",uiRoot.transform,new Color(.025f,.032f,.028f,.97f));
            var wood=GUIManager.Instance.CreateWoodpanel(uiRoot.transform,new Vector2(.025f,.025f),new Vector2(.975f,.975f),Vector2.zero,0,0,false);
            panel=wood.GetComponent<RectTransform>(); panel.offsetMin=Vector2.zero;panel.offsetMax=Vector2.zero;
            TextAt("CHALKBOARD",panel,180,42,290,52,38,true);
            TextAt(board.DisplayName+"  /  Sketches & plans",panel,490,42,300,40,18);
            var done=ButtonAt("Done  [Esc]",panel,0,42,154,42,RequestClose,false);
            var doneRect=done.GetComponent<RectTransform>();doneRect.anchorMin=doneRect.anchorMax=new Vector2(1,1);doneRect.anchoredPosition=new Vector2(-112,-42);
            var divider=Fill("HeaderRule",panel,new Color(.61f,.43f,.20f,.7f));
            Rect(divider,new Vector2(0,1),new Vector2(1,1),new Vector2(0,-79),new Vector2(-56,2));
            tools=new GameObject("ChalkTools",typeof(RectTransform)).GetComponent<RectTransform>();tools.SetParent(panel,false);
            Rect(tools.gameObject,new Vector2(1,1),new Vector2(1,1),new Vector2(-125,-96),new Vector2(190,360));tools.pivot=new Vector2(.5f,1);
            TextAt("CHALK",tools,94,20,174,32,27,true);
            for(byte i=1;i<DrawingBoard.Palette.Length;i++)
            {
                byte selected=i;int slot=i-1;
                var b=ButtonAt("",tools,28+(slot%4)*45,65+(slot/4)*50,38,38,()=>color=selected);
                var mark=Fill("ChalkStick",b.transform,DrawingBoard.Palette[i]);
                Rect(mark,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(23,7));
                mark.transform.localRotation=Quaternion.Euler(0,0,-12);
                mark.GetComponent<Image>().raycastTarget=false;
                chalkSelection.Add(Selection(b));
            }
            var wipe=ButtonAt("Wipe",tools,160,115,38,38,()=>color=0);
            var wt=wipe.GetComponentInChildren<Text>();if(wt!=null)wt.fontSize=12;
            chalkSelection.Insert(0,Selection(wipe));
            TextAt("STROKE",tools,94,169,174,32,25,true);
            for(int i=1;i<=6;i++)
            {
                int selected=i;
                var b=ButtonAt(i.ToString(),tools,18+(i-1)*30,213,26,36,()=>radius=selected);
                var t=b.GetComponentInChildren<Text>();if(t!=null)t.fontSize=16;
                sizeSelection.Add(Selection(b));
            }
            ButtonAt("Undo stroke",tools,94,273,174,42,()=>{if(undo.Count>0){pixels=undo.Pop();Changed();}});
            ButtonAt("Wipe board",tools,94,325,174,42,()=>{Snapshot();pixels=new byte[DrawingBoard.ByteCount];Changed();});
            toolHint=TextAt("Draw with the mouse.\nLift the chalk to sync.",tools,94,392,174,68,18);
            var frameObject=GUIManager.Instance.CreateWoodpanel(panel,new Vector2(0,1),new Vector2(0,1),Vector2.zero,100,100,false);
            frame=frameObject.GetComponent<RectTransform>();
            var canvasObject=new GameObject("Slate",typeof(RectTransform),typeof(RawImage));canvasObject.transform.SetParent(frame,false);
            slate=canvasObject.GetComponent<RectTransform>();
            Rect(canvasObject,Vector2.zero,Vector2.one,Vector2.zero,new Vector2(-22,-22));
            var raw=canvasObject.GetComponent<RawImage>();raw.texture=preview;raw.raycastTarget=true;
            statusText=TextAt(status,panel,0,0,100,38,18);
            var sr=statusText.rectTransform;sr.anchorMin=new Vector2(0,0);sr.anchorMax=new Vector2(1,0);sr.anchoredPosition=new Vector2(-75,28);sr.sizeDelta=new Vector2(-220,38);
            retry=ButtonAt("Retry",panel,0,0,100,36,()=>{failures=0;Flush();},false);
            var rr=retry.GetComponent<RectTransform>();rr.anchorMin=rr.anchorMax=new Vector2(1,0);rr.anchoredPosition=new Vector2(-85,28);
            retry.gameObject.SetActive(false);
            Layout();
        }
        private void Layout()
        {
            var size=panel.rect.size;
            float availableWidth=Mathf.Max(32,size.x-284), availableHeight=Mathf.Max(32,size.y-166);
            float width=Mathf.Min(availableWidth,availableHeight*16f/9f);
            frame.sizeDelta=new Vector2(width,width*9f/16f);
            frame.anchoredPosition=new Vector2(34+availableWidth/2,-92-availableHeight/2);
            statusText.text=status;
            toolHint.gameObject.SetActive(size.y>=620);
            foreach(var b in editingButtons)b.interactable=!pending&&!closing;
            for(int i=0;i<chalkSelection.Count;i++)chalkSelection[i].enabled=i==color;
            for(int i=0;i<sizeSelection.Count;i++)sizeSelection[i].enabled=i+1==radius;
            retry.gameObject.SetActive(failures>=3);
        }
        private void Update()
        {
            if(board==null || Player.m_localPlayer==null) {Close();return;}
            if(Player.m_localPlayer.IsDead() || !board.CanEdit()) {Close();return;}
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            Layout();
            if(Input.GetKeyDown(KeyCode.Escape)) {RequestClose();return;}
            if(!pending&&!closing)
            {
                bool inside=RectTransformUtility.RectangleContainsScreenPoint(slate,Input.mousePosition,null);
                if(Input.GetMouseButtonDown(0)&&inside)
                {Snapshot();drawing=true;previous=Point();Dab(previous);Changed();}
                else if(Input.GetMouseButton(0)&&drawing)
                {if(inside){var point=Point();Line(previous,point);previous=point;Changed();}else drawing=false;}
                if(Input.GetMouseButtonUp(0))drawing=false;
            }
            if(draft.CheckTimeout(Time.unscaledTime))
            {status="Connection lost. Your draft is still here — Retry.";}
            if(draft.ShouldSubmit(Time.unscaledTime,drawing))Flush();
        }
        private Vector2Int Point()
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(slate,Input.mousePosition,null,out var local);
            var rect=slate.rect;
            return new Vector2Int(Mathf.Clamp((int)((local.x-rect.xMin)/rect.width*DrawingBoard.Width),0,DrawingBoard.Width-1),Mathf.Clamp((int)((rect.yMax-local.y)/rect.height*DrawingBoard.Height),0,DrawingBoard.Height-1));
        }
        private void Flush()
        {
            if(pending||!dirty||board==null)return;
            if(!draft.BeginSubmit(board.ReadPixels(),board.Revision,Time.unscaledTime))return;
            Refresh();status="Keeping your marks…";board.Submit(revision,pixels);
        }
        private void RequestClose()
        {drawing=false;if(draft.RequestClose()){Close();return;}if(!pending)Flush();}
        private void Dab(Vector2Int point)
        {for(int y=-radius+1;y<radius;y++)for(int x=-radius+1;x<radius;x++)if(x*x+y*y<=radius*radius)DrawingBoard.SetPixel(pixels,point.x+x,point.y+y,color);}
        private void Line(Vector2Int a,Vector2Int b)
        {int count=Mathf.Max(Mathf.Abs(a.x-b.x),Mathf.Abs(a.y-b.y));for(int i=0;i<=count;i++)Dab(new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(a.x,b.x,count==0?0:(float)i/count)),Mathf.RoundToInt(Mathf.Lerp(a.y,b.y,count==0?0:(float)i/count))));}
        private void Snapshot(){if(undo.Count>=30)undo.Clear();undo.Push((byte[])pixels.Clone());}
        private void Changed(){draft.Changed(Time.unscaledTime);status="Drawing…";Refresh();}
        private void Refresh()=>DrawingBoard.PaintTexture(preview,pixels);
        public static void Report(DrawingBoard target,bool accepted,int newRevision)
        {
            if(instance==null||instance.board!=target||!instance.pending)return;
            bool close=instance.draft.Acknowledge(accepted,newRevision,Time.unscaledTime);
            if(accepted)
            {instance.status="Your marks are kept.";if(close)instance.Close();}
            else
            {instance.status=instance.failures>=3?"Could not sync. Your draft is still here — Retry.":"Another hand is drawing. Trying again…";}
        }
        public static void BoardDestroyed(DrawingBoard target){if(instance!=null&&instance.board==target)instance.Close();}
        private void Close()=>Destroy(gameObject);
        private void OnDestroy()
        {
            if(draft!=null&&dirty&&!pending&&board!=null&&board.CanEdit())board.Submit(revision,pixels);
            if(inputBlocked)GUIManager.BlockInput(false);Cursor.lockState=oldLock;Cursor.visible=oldVisible;
            if(uiRoot!=null)Destroy(uiRoot);if(preview!=null)Destroy(preview);if(instance==this)instance=null;
        }
    }
}
