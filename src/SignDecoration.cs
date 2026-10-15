using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ValheimWhiteboard
{
    // A local decoration, never a network prefab. Normal and unmodded sign behavior remains intact.
    public sealed class SignDecoration : MonoBehaviour
    {
        private int decoratedKind;
        private float nextPoll;
        private GameObject visual;
        private DrawingBoard drawing;
        private readonly List<MeshRenderer> original = new List<MeshRenderer>();
        private readonly List<Material> materials = new List<Material>();
        private GameObject textObject;
        private bool textWasActive;
        private void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .25f;
            var view = GetComponent<ZNetView>();
            var template = GetComponent<BoardTemplate>();
            int kind = view != null && view.IsValid() ? BoardSchema.Kind(view.GetZDO()) : template != null ? template.Kind : 0;
            if (kind == decoratedKind) return;
            Clear();
            if (BoardSchema.ValidKind(kind)) Decorate(kind);
        }
        private void Decorate(int kind)
        {
            var text = GetComponentInChildren<TMP_Text>(true);
            if (text == null) return;
            Material vanilla = null;
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.GetComponent<TMP_Text>() != null) continue;
                if (vanilla == null) vanilla = renderer.sharedMaterial;
                if (renderer.enabled) { original.Add(renderer); renderer.enabled = false; }
            }
            if (vanilla == null) { Clear(); return; }
            textObject = text.gameObject; textWasActive = textObject.activeSelf; textObject.SetActive(false);
            var writing = new Material(vanilla); materials.Add(writing);
            writing.mainTexture = Texture2D.whiteTexture; writing.mainTextureScale = Vector2.one; writing.mainTextureOffset = Vector2.zero;
            if (writing.HasProperty("_Color")) writing.SetColor("_Color", Color.white);
            if (writing.HasProperty("_BumpMap")) writing.SetTexture("_BumpMap", null);
            writing.DisableKeyword("_NORMALMAP");
            var slate = new Material(writing); materials.Add(slate);
            if (slate.HasProperty("_Color")) slate.SetColor("_Color", DrawingBoard.Palette[0]);
            var wood = new Material(vanilla); materials.Add(wood);
            if (wood.HasProperty("_Color")) wood.SetColor("_Color", new Color(.65f,.48f,.30f));
            visual = new GameObject("ChalkboardDecoration"); visual.layer = gameObject.layer;
            visual.transform.SetParent(transform, false); visual.transform.position = text.transform.position;
            visual.transform.rotation = text.transform.rotation;
            visual.transform.localScale = new Vector3(BoardSchema.Scale(kind), BoardSchema.Scale(kind), 1);
            Part("WritingSurface", new Vector3(0,0,.045f), new Vector3(2,1.15f,.06f), slate);
            Part("Top", new Vector3(0,.60f,.045f), new Vector3(2.16f,.09f,.13f), wood);
            Part("Bottom", new Vector3(0,-.60f,.045f), new Vector3(2.16f,.09f,.13f), wood);
            foreach (float x in new[] {-1.025f,1.025f}) Part("Side",new Vector3(x,0,.045f),new Vector3(.09f,1.15f,.13f),wood);
            Part("ChalkTray",new Vector3(0,-.65f,-.06f),new Vector3(2.16f,.08f,.25f),wood);
            drawing = gameObject.AddComponent<DrawingBoard>();
            drawing.Configure(visual.transform, writing, BoardSchema.Name(kind));
            decoratedKind = kind;
        }
        private void Part(string name, Vector3 position, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = name; part.layer = gameObject.layer;
            part.transform.SetParent(visual.transform,false); part.transform.localPosition = position; part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            // Local artwork must never create a solid obstacle invisible to vanilla players.
            DestroyImmediate(part.GetComponent<Collider>());
        }
        private void Clear()
        {
            decoratedKind = 0;
            if (drawing != null) Destroy(drawing); drawing = null;
            if (visual != null) Destroy(visual); visual = null;
            foreach (var renderer in original) if (renderer != null) renderer.enabled = true;
            original.Clear();
            if (textObject != null) textObject.SetActive(textWasActive); textObject = null;
            foreach (var material in materials) if (material != null) Destroy(material);
            materials.Clear();
        }
        private void OnDestroy() => Clear();
    }
}
