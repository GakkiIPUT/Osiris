using System.Collections;
using UnityEngine;

[ExecuteAlways]
public class SelectionFramesOverlay : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("自動取得。未設定の場合はシーンから検索")]
    public BoardManager board;

    [Header("Sizes")]
    [Tooltip("内枠サイズ（常に3固定想定）")]
    public int innerSize = 3;          // 3x3
    [Tooltip("外枠半径（チェビシェフ）。k=1のとき Rout=3 → 7x7")]
    public int outerRadius = 3;        // 7x7 => size = 2*R+1 = 7

    [Header("Visual")]
    [Range(0.01f, 0.25f)]
    public float frameThickness = 0.06f;
    [Tooltip("内枠（OK時）の色（アルファ含む）")]
    public Color innerOkColor = new Color(0.2f, 0.6f, 1.0f, 0.35f);
    [Tooltip("内枠（NG時）の色（アルファ含む）")]
    public Color innerNgColor = new Color(1f, 0.25f, 0.25f, 0.5f);
    [Tooltip("外枠の色（アルファ含む）")]
    public Color outerColor = new Color(1f, 1f, 1f, 0.25f);
    [Tooltip("ボードのghostYに対する追加オフセット（z-fighting対策）")]
    public float innerYOffset = 0.00030f;
    public float outerYOffset = 0.00025f;

    [Header("Debug / Testing")]
    [Tooltip("Inspectorの色を毎フレーム適用（テスト用）")]
    public bool applyInspectorEveryFrame = false;
    [Tooltip("外枠の表示切替（デバッグ可視化用）")]
    public bool showOuter = true;

    private Transform _root, _innerRoot, _outerRoot;
    private Transform[] _innerEdges = new Transform[4];
    private Transform[] _outerEdges = new Transform[4];
    private Material _innerMat, _outerMat;

    private bool _innerOk = true;
    private Coroutine _flashCo;

    // シェーダ選好順（アルファ最優先で Sprites/Default を先頭に）
    private const string ShaderSprites = "Sprites/Default";                     // 最優先（確実に SrcAlpha/OneMinusSrcAlpha）
    private const string ShaderURPUnlit = "Universal Render Pipeline/Unlit";    // 使う場合はSurface=Transparentに強制
    private const string ShaderBuiltInUnlitTransparent = "Unlit/Transparent";

    private void Awake()
    {
        if (board == null) board = GetComponent<BoardManager>();
        if (board == null) board = FindObjectOfType<BoardManager>();
        EnsureSetup();
    }

    private void OnEnable()
    { EnsureSetup(); }
    private void OnDisable()
    { Cleanup(); }
    private void OnDestroy()
    { Cleanup(); }

    private void LateUpdate()
    {
        if (board == null || board.player == null) return;
        EnsureSetup();

        var pc = board.player;
        Vector2Int c = pc.AimCenter != default ? pc.AimCenter : pc.pos;
        var p = pc.pos;

        float yInner = board.ghostY + innerYOffset;
        float yOuter = board.ghostY + outerYOffset;

        PlaceSquareFrame(_innerEdges, new Vector2(c.x, c.y), innerSize, yInner, frameThickness);
        int outerSize = outerRadius * 2 + 1;
        PlaceSquareFrame(_outerEdges, new Vector2(p.x, p.y), outerSize, yOuter, frameThickness);

        if (_outerRoot != null) _outerRoot.gameObject.SetActive(showOuter);

        if (applyInspectorEveryFrame) ApplyInspectorColors();
    }

    private void EnsureSetup()
    {
        // 既存を再利用（増殖防止）
        if (_root == null)
        {
            var existing = transform.Find("SelectionFrames");
            if (existing != null)
            {
                _root = existing;
                _innerRoot = _root.Find("InnerFrame");
                _outerRoot = _root.Find("OuterFrame");

                if (_innerRoot && _innerRoot.childCount >= 1)
                {
                    var mr = _innerRoot.GetChild(0).GetComponent<MeshRenderer>();
                    if (mr) _innerMat = mr.sharedMaterial;
                }
                if (_outerRoot && _outerRoot.childCount >= 1)
                {
                    var mr = _outerRoot.GetChild(0).GetComponent<MeshRenderer>();
                    if (mr) _outerMat = mr.sharedMaterial;
                }
                RebindEdges(_innerRoot, _innerEdges, "Inner");
                RebindEdges(_outerRoot, _outerEdges, "Outer");
            }
        }
        if (_root && _innerRoot && _outerRoot && _innerEdges[0] && _outerEdges[0])
        {
            // 既存セットを透過強制＋色適用（URP Unlit の場合はSurfaceも補正）
            ForceTransparentState(_innerMat);
            ForceTransparentState(_outerMat);
            ApplyInspectorColors();
            return;
        }

        // 新規作成
        if (_root == null)
        {
            _root = new GameObject("SelectionFrames").transform;
            _root.SetParent(transform, false);
            _root.gameObject.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor | HideFlags.NotEditable;
        }
        if (_innerRoot == null)
        {
            _innerRoot = new GameObject("InnerFrame").transform;
            _innerRoot.SetParent(_root, false);
            _innerRoot.gameObject.hideFlags = _root.gameObject.hideFlags;
        }
        if (_outerRoot == null)
        {
            _outerRoot = new GameObject("OuterFrame").transform;
            _outerRoot.SetParent(_root, false);
            _outerRoot.gameObject.hideFlags = _root.gameObject.hideFlags;
        }

        if (_innerMat == null) _innerMat = CreateTransparentMaterial(innerOkColor, "Inner");
        if (_outerMat == null) _outerMat = CreateTransparentMaterial(outerColor, "Outer");

        CreateEdges(_innerRoot, _innerEdges, _innerMat, "Inner");
        CreateEdges(_outerRoot, _outerEdges, _outerMat, "Outer");

        ApplyInspectorColors();
    }

    private void RebindEdges(Transform parent, Transform[] edges, string prefix)
    {
        if (parent == null) return;
        edges[0] = parent.Find($"{prefix}_Top");
        edges[1] = parent.Find($"{prefix}_Bottom");
        edges[2] = parent.Find($"{prefix}_Left");
        edges[3] = parent.Find($"{prefix}_Right");
    }

    private Material CreateTransparentMaterial(Color color, string tag)
    {
        Shader sh = Shader.Find(ShaderSprites);
        if (sh == null) sh = Shader.Find(ShaderURPUnlit);
        if (sh == null) sh = Shader.Find(ShaderBuiltInUnlitTransparent);
        if (sh == null) sh = Shader.Find("Standard");

        var m = new Material(sh);
        SetMaterialColor(m, color);
        ForceTransparentState(m);
        return m;
    }

    // どのシェーダでも半透明になるように最低限の状態を強制
    private static void ForceTransparentState(Material m)
    {
        if (m == null) return;

        // URP Unlit のときは Surface を Transparent に
        if (m.shader != null && m.shader.name.Contains("Universal Render Pipeline/Unlit"))
        {
            // Surface設定: 0=Opaque, 1=Transparent
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            // Premultiplyは使わない
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        // 共通のブレンド / 深度（持っている場合のみ）
        if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);

        // RenderType/Queue を透過へ
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    // Shader毎の色プロパティ名を吸収
    private static void SetMaterialColor(Material m, Color c)
    {
        if (m == null) return;
        // 最も一般的な順に入れる
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); // URP/HDRP
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);     // Built-in / Sprites
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c); // 一部のUnlit
        // Material.color も併用（内部で _Color を持つ場合は連動）
        m.color = c;
    }

    private void Cleanup()
    {
        if (_flashCo != null) { StopCoroutine(_flashCo); _flashCo = null; }

        if (_root != null && _root.gameObject != null)
        {
#if UNITY_EDITOR
            if (Application.isPlaying) Destroy(_root.gameObject);
            else DestroyImmediate(_root.gameObject);
#else
            Destroy(_root.gameObject);
#endif
        }
        _root = _innerRoot = _outerRoot = null;
        for (int i = 0; i < 4; i++) { _innerEdges[i] = null; _outerEdges[i] = null; }

        if (_innerMat != null) { SafeDestroyMat(_innerMat); _innerMat = null; }
        if (_outerMat != null) { SafeDestroyMat(_outerMat); _outerMat = null; }
    }

    private static void SafeDestroyMat(Material m)
    {
#if UNITY_EDITOR
        if (Application.isPlaying) Object.Destroy(m);
        else Object.DestroyImmediate(m);
#else
        Object.Destroy(m);
#endif
    }

    private void CreateEdges(Transform parent, Transform[] edges, Material mat, string prefix)
    {
        edges[0] = CreateEdge(parent, $"{prefix}_Top", mat);
        edges[1] = CreateEdge(parent, $"{prefix}_Bottom", mat);
        edges[2] = CreateEdge(parent, $"{prefix}_Left", mat);
        edges[3] = CreateEdge(parent, $"{prefix}_Right", mat);
    }

    private Transform CreateEdge(Transform parent, string name, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = Vector3.one;
        go.gameObject.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor | HideFlags.NotEditable;

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = mat;
        }
#if UNITY_EDITOR
        var col = go.GetComponent<Collider>();
        if (col) DestroyImmediate(col);
#endif
        return go.transform;
    }

    private void PlaceSquareFrame(Transform[] edges, Vector2 center, int size, float y, float thickness)
    {
        float half = size * 0.5f;

        PlaceRect(edges[0], new Vector3(center.x, y, center.y + half), size, thickness);
        PlaceRect(edges[1], new Vector3(center.x, y, center.y - half), size, thickness);
        PlaceRect(edges[2], new Vector3(center.x - half, y, center.y), thickness, size);
        PlaceRect(edges[3], new Vector3(center.x + half, y, center.y), thickness, size);
    }

    private void PlaceRect(Transform tr, Vector3 pos, float widthX, float depthZ)
    {
        if (tr == null) return;
        tr.position = pos;
        tr.localScale = new Vector3(widthX, depthZ, 1f);
    }

    public void SetInnerOk(bool ok)
    {
        _innerOk = ok;
        if (_innerMat == null) return;
        var target = ok ? innerOkColor : innerNgColor;
        SetMaterialColor(_innerMat, target);
        ForceTransparentState(_innerMat);
    }

    public void FlashInnerNg(float seconds)
    {
        if (!isActiveAndEnabled) return;
        if (_flashCo != null) StopCoroutine(_flashCo);
        _flashCo = StartCoroutine(CoFlashInnerNg(seconds));
    }

    private IEnumerator CoFlashInnerNg(float seconds)
    {
        var prevOk = _innerOk;
        SetInnerOk(false);
        yield return new WaitForSeconds(seconds);
        SetInnerOk(prevOk);
        _flashCo = null;
    }

    private void ApplyInspectorColors()
    {
        if (_innerMat != null)
        {
            var c = (_innerOk ? innerOkColor : innerNgColor);
            SetMaterialColor(_innerMat, c);
            ForceTransparentState(_innerMat);
        }
        if (_outerMat != null)
        {
            SetMaterialColor(_outerMat, outerColor);
            ForceTransparentState(_outerMat);
        }
    }

    private void OnValidate()
    {
        ApplyInspectorColors();
        if (_outerRoot != null) _outerRoot.gameObject.SetActive(showOuter);
    }

#if UNITY_EDITOR
    [ContextMenu("Overlay: Dump Materials")]
    private void CtxDumpMaterials()
    {
        Debug.Log($"[Overlay] InnerMat={_innerMat?.shader?.name}, q={_innerMat?.renderQueue}, col={_innerMat?.color}");
        Debug.Log($"[Overlay] OuterMat={_outerMat?.shader?.name}, q={_outerMat?.renderQueue}, col={_outerMat?.color}");
    }
#endif
}