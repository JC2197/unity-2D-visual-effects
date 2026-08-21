using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a sprite-based beam between a configurable start point and this transform position.
/// This is a generic version of the old lightning renderer so beam abilities can reuse it.
/// </summary>
namespace JoeConticello.VisualEffects
{
    public class BeamRenderer : MonoBehaviour
    {
        [Header("Animation Frames")]
        [Tooltip("Frames cycled in single-shot mode. Also used as loop body fallback.")]
        [SerializeField] private Sprite[] frames;

        [Header("Loop Mode Phases")]
        [Tooltip("Played once when beam starts (loop mode only). Leave empty to skip.")]
        [SerializeField] private Sprite[] startFrames;

        [Tooltip("Cycled continuously while beam is held (loop mode only). Falls back to Frames if empty.")]
        [SerializeField] private Sprite[] loopFrames;

        [Tooltip("Played once after TriggerEnd is called, before fade (loop mode only). Leave empty to skip.")]
        [SerializeField] private Sprite[] endFrames;

        [Header("Beam")]
        [Tooltip("World-space start point expressed as local offset from this object (target/end).")]
        [SerializeField] private Vector2 startOffset = new Vector2(0f, 10f);

        [Tooltip("Base tint. Alpha is driven by animation phases.")]
        [SerializeField] private Color tint = Color.white;

        [Header("Vertex Sprites")]
        [Tooltip("Sprite at beam origin/start. Leave empty to skip.")]
        [SerializeField] private Sprite[] vertexSpriteFrames;

        [Tooltip("Sprite at beam target/end. Leave empty to skip.")]
        [SerializeField] private Sprite[] targetVertexSpriteFrames;

        [Tooltip("Optional material for vertex sprites. Falls back to spriteMaterial.")]
        [SerializeField] private Material vertexMaterial;

        [Header("Rendering")]
        [Tooltip("Optional material for beam segments.")]
        [SerializeField] private Material spriteMaterial;

        [Tooltip("Sorting layer for beam rendering.")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrder = 10;

        [Tooltip("Shader tint property name. Use _Color for built-in sprite shaders.")]
        [SerializeField] private string tintPropertyName = "_Color";

        [Header("Animation")]
        [Tooltip("Frames per second for body animation during hold.")]
        [SerializeField] private float fps = 10f;

        [Tooltip("Number of frames to spend fading out. Each frame lasts 1/fps seconds.")]
        [SerializeField] private int fadeFrames = 5;

        [Header("Looping")]
        [Tooltip("Loop draw/hold/fade until externally destroyed.")]
        [SerializeField] private bool loopBeam = false;

        [Header("Point Mode")]
        [Tooltip("When enabled, draws between two named child points under this renderer.")]
        [SerializeField] private bool usePoints = false;

        [Tooltip("Name of child transform used as START point.")]
        [SerializeField] private string point1Name = "boltPoint1";

        [Tooltip("Name of child transform used as END point.")]
        [SerializeField] private string point2Name = "boltPoint2";

        [HideInInspector] public float alphaMultiplier = 1f;

        private readonly List<SpriteRenderer> pieces = new List<SpriteRenderer>();
        private SpriteRenderer originVertex;
        private SpriteRenderer targetVertex;
        private MaterialPropertyBlock mpb;

        private Transform beamRoot;
        private float cachedSegmentHeight;
        private int cachedSegmentCount;
        private bool endTriggered;
        private float angle;

        private void Start()
        {
            if (frames == null || frames.Length == 0 || frames[0] == null)
            {
                Debug.LogError($"[BeamRenderer] No frames assigned on {gameObject.name}.", this);
                Destroy(gameObject);
                return;
            }

            mpb = new MaterialPropertyBlock();

            if (usePoints)
                ResolvePoints();

            BuildPieces();
            StartCoroutine(BeamSequence());
        }

        private void BuildPieces()
        {
            float segmentHeight = frames[0].bounds.size.y;
            if (segmentHeight <= 0f)
            {
                Debug.LogWarning($"[BeamRenderer] frames[0].bounds.size.y is 0. Check Pixels Per Unit.", this);
                segmentHeight = 1f;
            }

            float strikeLength = startOffset.magnitude;
            int segmentCount = Mathf.Max(1, Mathf.CeilToInt(strikeLength / segmentHeight));

            beamRoot = new GameObject("BeamRoot").transform;
            beamRoot.SetParent(transform, false);
            beamRoot.localPosition = new Vector3(startOffset.x, startOffset.y, 0f);

            float angle = Mathf.Atan2(-startOffset.x, startOffset.y) * Mathf.Rad2Deg;
            beamRoot.localRotation = Quaternion.Euler(0f, 0f, angle);

            float actualLength = segmentCount * segmentHeight;
            float scaleY = actualLength > 0f ? strikeLength / actualLength : 1f;
            beamRoot.localScale = new Vector3(1f, scaleY, 1f);
            cachedSegmentHeight = segmentHeight;
            cachedSegmentCount = segmentCount;

            if (vertexSpriteFrames != null && vertexSpriteFrames.Length > 0 && vertexSpriteFrames[0] != null)
            {
                originVertex = SpawnVertex("VertexOrigin", transform, new Vector3(startOffset.x, startOffset.y, 0f), vertexSpriteFrames[0], angle);
            }

            if (targetVertexSpriteFrames != null && targetVertexSpriteFrames.Length > 0 && targetVertexSpriteFrames[0] != null)
            {
                targetVertex = SpawnVertex("VertexTarget", transform, Vector3.zero, targetVertexSpriteFrames[0], angle);
            }

            for (int i = 0; i < segmentCount; i++)
            {
                GameObject go = new GameObject($"Piece_{i}");
                go.transform.SetParent(beamRoot, false);
                go.transform.localPosition = new Vector3(0f, -(i + 0.5f) * segmentHeight, 0f);

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = frames[0];
                sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder = sortingOrder;

                if (spriteMaterial != null)
                    sr.material = spriteMaterial;

                ApplyColor(sr, WithAlpha(tint, 0f));
                pieces.Add(sr);
            }
        }

        private IEnumerator BeamSequence()
        {
            int total = pieces.Count;
            float frameDuration = 1f / Mathf.Max(fps, 0.001f);
            int effectiveFadeFrames = Mathf.Max(1, fadeFrames);

            Sprite[] loopBody = (loopFrames != null && loopFrames.Length > 0) ? loopFrames : frames;

            ShowAll();

            if (loopBeam)
            {
                // Start phase: play startFrames once.
                if (startFrames != null && startFrames.Length > 0)
                {
                    for (int i = 0; i < startFrames.Length; i++)
                    {
                        SetBodySprite(startFrames[i]);
                        SetVertexFrames(i);
                        yield return new WaitForSeconds(frameDuration);
                    }
                }

                // Loop phase: cycle loopBody until TriggerEnd() is called.
                int loopIndex = 0;
                while (!endTriggered)
                {
                    SetBodySprite(loopBody[loopIndex % loopBody.Length]);
                    SetVertexFrames(loopIndex);
                    loopIndex++;
                    yield return new WaitForSeconds(frameDuration);
                }

                // End phase: play endFrames once.
                if (endFrames != null && endFrames.Length > 0)
                {
                    for (int i = 0; i < endFrames.Length; i++)
                    {
                        SetBodySprite(endFrames[i]);
                        SetVertexFrames(i);
                        yield return new WaitForSeconds(frameDuration);
                    }
                }
            }
            else
            {
                // Single-shot: cycle all frames once.
                for (int i = 0; i < frames.Length; i++)
                {
                    SetBodyFrame(i);
                    SetVertexFrames(i);
                    yield return new WaitForSeconds(frameDuration);
                }
            }

            // Fade out.
            for (int i = 0; i < effectiveFadeFrames; i++)
            {
                float alpha = 1f - ((i + 1f) / effectiveFadeFrames);
                Color c = WithAlpha(tint, alpha);

                for (int j = 0; j < total; j++)
                    ApplyColor(pieces[j], c);

                if (originVertex != null) ApplyColor(originVertex, c);
                if (targetVertex != null) ApplyColor(targetVertex, c);

                yield return new WaitForSeconds(frameDuration);
            }

            AutoDestroyEffect autoDestroy = GetComponent<AutoDestroyEffect>();
            if (autoDestroy != null)
                autoDestroy.DestroyNow();
            else
                Destroy(gameObject);
        }

        private void SetBodyFrame(int frameIndex)
        {
            Sprite current = frames[frameIndex % frames.Length];
            for (int i = 0; i < pieces.Count; i++)
                pieces[i].sprite = current;
        }

        private void SetVertexFrames(int frameIndex)
        {
            if (originVertex != null && vertexSpriteFrames != null && vertexSpriteFrames.Length > 0)
                originVertex.sprite = vertexSpriteFrames[frameIndex % vertexSpriteFrames.Length];

            if (targetVertex != null && targetVertexSpriteFrames != null && targetVertexSpriteFrames.Length > 0)
                targetVertex.sprite = targetVertexSpriteFrames[frameIndex % targetVertexSpriteFrames.Length];
        }

        public void SetLooping(bool loop)
        {
            loopBeam = loop;
        }

        public void TriggerEnd()
        {
            endTriggered = true;
        }

        /// <summary>
        /// Called every frame by BeamAbility to track the live start position.
        /// This object's transform.position is the beam end; worldStart is the origin.
        /// </summary>
        public void UpdateGeometry(Vector3 worldStart)
        {
            if (beamRoot == null)
                return;

            Vector3 local = transform.InverseTransformPoint(worldStart);
            startOffset = new Vector2(local.x, local.y);

            beamRoot.localPosition = new Vector3(startOffset.x, startOffset.y, 0f);

            float angle = Mathf.Atan2(-startOffset.x, startOffset.y) * Mathf.Rad2Deg;
            beamRoot.localRotation = Quaternion.Euler(0f, 0f, angle);

            float strikeLength = startOffset.magnitude;
            float actualLength = cachedSegmentCount * cachedSegmentHeight;
            float scaleY = actualLength > 0f ? strikeLength / actualLength : 1f;
            beamRoot.localScale = new Vector3(1f, scaleY, 1f);

            if (originVertex != null)
                originVertex.transform.localPosition = new Vector3(startOffset.x, startOffset.y, 0f);
        }

        private void ShowAll()
        {
            for (int i = 0; i < pieces.Count; i++)
                ApplyColor(pieces[i], WithAlpha(tint, 1f));

            if (originVertex != null) ApplyColor(originVertex, WithAlpha(tint, 1f));
            if (targetVertex != null) ApplyColor(targetVertex, WithAlpha(tint, 1f));

            if (frames != null && frames.Length > 0)
                SetBodyFrame(0);
            SetVertexFrames(0);
        }

        private void SetBodySprite(Sprite sprite)
        {
            for (int i = 0; i < pieces.Count; i++)
                pieces[i].sprite = sprite;
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        private void ApplyColor(SpriteRenderer sr, Color c)
        {
            if (alphaMultiplier < 1f)
                c.a *= alphaMultiplier;

            sr.color = c;
            mpb.SetColor(tintPropertyName, c);
            sr.SetPropertyBlock(mpb);
        }

        private SpriteRenderer SpawnVertex(string goName, Transform parent, Vector3 localPos, Sprite sprite, float angle)
        {
            GameObject go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = sortingLayerName;
            sr.sortingOrder = sortingOrder + 1;

            Material mat = vertexMaterial != null ? vertexMaterial : spriteMaterial;
            if (mat != null)
                sr.material = mat;

            ApplyColor(sr, WithAlpha(tint, 0f));
            return sr;
        }

        private void ResolvePoints()
        {
            Transform p1 = FindDeep(transform, point1Name);
            Transform p2 = FindDeep(transform, point2Name);

            if (p1 == null)
            {
                Debug.LogError($"[BeamRenderer] usePoints=true but '{point1Name}' was not found under '{name}'.", this);
                return;
            }

            if (p2 == null)
            {
                Debug.LogError($"[BeamRenderer] usePoints=true but '{point2Name}' was not found under '{name}'.", this);
                return;
            }

            Vector3 worldP1 = p1.position;
            Vector3 worldP2 = p2.position;

            transform.position = worldP2;

            Vector3 local = transform.InverseTransformPoint(worldP1);
            startOffset = new Vector2(local.x, local.y);
        }

        private static Transform FindDeep(Transform root, string objectName)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == objectName) return t;

            return null;
        }

        /// <summary>
        /// Set the beam start world position after Instantiate, with this object at beam end.
        /// </summary>
        public void SetStartPoint(Vector3 worldStart)
        {
            Vector3 local = transform.InverseTransformPoint(worldStart);
            startOffset = new Vector2(local.x, local.y);
        }
    }
}
