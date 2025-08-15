using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CellType { Floor, Wall, Exit }

[ExecuteAlways] // エディタでもプレビュー用に動かす
public class BoardManager : MonoBehaviour
{
    [Header("Prefabs (Tiles & Player)")]
    public GameObject pfFloor;
    public GameObject pfWall;
    public GameObject pfExit;
    public GameObject pfPlayer;   // PlayerはAddComponentでPlayerController付与（Prefab側にあってもOK）

    // ===== Guard 種類のマッピング（記号 → Prefab） =====
    [System.Serializable]
    public struct GuardType
    {
        [Tooltip("ASCIIマップ上の記号（例: G, H, I など）")]
        public string symbol;                       // ← 1文字を入れる想定
        [Tooltip("この記号で配置するガードのPrefab（GuardControllerを付けておく）")]
        public GameObject prefab;
        [Tooltip("Level Painter のボタン名や説明に使うラベル（任意）")]
        public string label;
        [Tooltip("Sceneビューのプレビュー色")]
        public Color previewColor;
    }

    [System.Serializable]
    public struct ItemType
    {
        [Tooltip("ASCIIマップ上の記号（例: i, j, k など小文字推奨）")]
        public string symbol;                       // ← 1文字を入れる想定
        public GameObject prefab;                   // アイテムのPrefab（今は効果なしでOK）
        public string label;                        // UI表示用
        public Color previewColor;                  // Sceneプレビュー色
    }

    [Header("Guard Types (symbol → prefab)")]
    public List<GuardType> guardTypes = new List<GuardType>()
    {
        new GuardType{ symbol="G", prefab=null, label="Guard A", previewColor = new Color(1f,0.35f,0.35f,1f) },
    };

    [Header("Item Types (symbol → prefab)")]
    public List<ItemType> itemTypes = new List<ItemType>()
    {
        new ItemType{ symbol="i", prefab=null, label="Item", previewColor = new Color(0.25f,1f,0.9f,1f) },
    };

    public Transform itemsRoot; // アイテムの親

    [Header("Ghost Materials (optional)")]
    public Material ghostOkMat;
    public Material ghostNgMat;

    [Header("Vision Viz (optional)")]
    public Material guardVisionMat;   // 未割当なら ghostOkMat をフォールバック

    [Header("Guard Defaults")]
    public int defaultGuardViewRange = 5; // （GuardController側で useBoardDefaultViewRange=true のとき適用）

    [Header("Y Alignment")]
    public float floorY = 0f;           // 床の天面を合わせるY
    public float exitTopOffset = 0.01f; // Exitは床より少し上
    public float ghostY = 0.01f;        // ゴースト表示Y（床より少し上）

    [Header("2D Assets Auto-Align")]
    [Tooltip("QuadやSpriteを自動でX=90°回転し、上から見えるように整列します")]
    public bool autoAlign2D = true;
    [Tooltip("プレイヤー/敵（Sprite・Quad）の高さオフセット（床より少しだけ上）")]
    public float actorYOffset = 0.01f;

    [Header("Actor Visual Scale (in cells)")]
    [Tooltip("1.0=1セル相当。X=幅, Y=高さ（Sprite/Quad のローカルX/Y）")]
    public Vector2 playerVisualScale = new Vector2(1.0f, 1.0f);
    public Vector2 guardVisualScale = new Vector2(1.0f, 1.0f);

    [Header("Editor Preview (no GameObjects)")]
    public bool editorPreview = true;                   // エディタでは描画のみ（Hierarchyを汚さない）
    public Color previewFloor = new Color(0.85f, 0.85f, 0.85f, 1f);
    public Color previewWall = new Color(0.20f, 0.20f, 0.20f, 1f);
    public Color previewExit = new Color(1.00f, 0.85f, 0.20f, 1f);
    public Color previewP = new Color(0.20f, 0.60f, 1.00f, 1f);
    [HideInInspector] public Dictionary<Vector2Int, GameObject> itemAt = new Dictionary<Vector2Int, GameObject>();
    [Header("Level (ASCII)")]
    [TextArea(6, 20)]
    public string[] level = new string[]
    {
        "############",
        "#..G....#..#",
        "#.####..#..#",
        "#..#....#..#",
        "#..#..#####E",
        "#..#.......#",
        "#..####..#.#",
        "#......#.#.#",
        "#.####.#.#.#",
        "#P.....#...#",
        "#......#...#",
        "############",
    };

    [Header("Overlay Heights")]
    public float previewY = 0.0001f; // 選択プレビュー用（床ほぼベタ）
    public float visionY = 0.0002f;  // 敵視界用（少しだけ上）
    public Vector3 CellCenter(Vector2Int p, float y)
    {
        // ※ 当初の仕様に合わせて+0.0f（中心補正が不要な表現）
        return new Vector3(p.x + 0.0f, y, p.y + 0.0f);
    }

    public int Width => level.Length > 0 ? level[0].Length : 0;
    public int Height => level.Length;

    public CellType[,] cells;
    private GameObject[,] tileGOs;  // 1マス=1オブジェクト（Floor/Wall/Exit）

    // ルート
    public Transform tilesRoot;
    public Transform actorsRoot;

    [HideInInspector] public PlayerController player;
    [HideInInspector] public List<GuardController> guards = new();

    public bool IsAnimating { get; private set; }

    void Awake()
    {
        if (tilesRoot == null) tilesRoot = new GameObject("TilesRoot").transform;
        if (actorsRoot == null) actorsRoot = new GameObject("ActorsRoot").transform;
        if (itemsRoot == null) itemsRoot = new GameObject("ItemsRoot").transform;

#if UNITY_EDITOR
        tilesRoot.hideFlags = HideFlags.HideInHierarchy;
        actorsRoot.hideFlags = HideFlags.HideInHierarchy;
        itemsRoot.hideFlags = HideFlags.HideInHierarchy;
#endif
        Build();
    }

    void OnEnable()
    {
        if (!Application.isPlaying) Build();
    }

    public Vector3 GridToWorld(Vector2Int p) => new Vector3(p.x, 0f, p.y);
    public Vector2Int WorldToGrid(Vector3 w) => new Vector2Int(Mathf.RoundToInt(w.x), Mathf.RoundToInt(w.z));
    public Vector3 GridToWorldActor(Vector2Int p) => GridToWorld(p) + new Vector3(0, actorYOffset, 0f);
    public bool InBounds(Vector2Int p) => p.x >= 0 && p.x < Width && p.y >= 0 && p.y < Height;

    public bool IsWalkable(Vector2Int p)
    {
        if (!InBounds(p)) return false;
        var c = cells[p.y, p.x];
        return c == CellType.Floor || c == CellType.Exit;
    }

    public bool BlocksVision(Vector2Int p)
    {
        if (!InBounds(p)) return true;
        return cells[p.y, p.x] == CellType.Wall;
    }

    // 破棄ヘルパー：エディタ停止中は DestroyImmediate
    void SafeDestroy(Object o)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) { DestroyImmediate(o); return; }
#endif
        Destroy(o);
    }

    void ClearAll()
    {
        if (tilesRoot != null)
            for (int i = tilesRoot.childCount - 1; i >= 0; --i)
                SafeDestroy(tilesRoot.GetChild(i).gameObject);

        if (actorsRoot != null)
            for (int i = actorsRoot.childCount - 1; i >= 0; --i)
                SafeDestroy(actorsRoot.GetChild(i).gameObject);

        if (itemsRoot != null)
            for (int i = itemsRoot.childCount - 1; i >= 0; --i)
                SafeDestroy(itemsRoot.GetChild(i).gameObject);

        guards.Clear();
        player = null;
        itemAt.Clear();
    }

    // ===== Y合わせユーティリティ =====
    Bounds GetWorldBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }
    void AlignTopToY(GameObject go, float y)
    {
        var b = GetWorldBounds(go);
        float delta = y - b.max.y;
        go.transform.position += new Vector3(0, delta, 0);
    }
    void AlignBottomToY(GameObject go, float y)
    {
        var b = GetWorldBounds(go);
        float delta = y - b.min.y;
        go.transform.position += new Vector3(0, delta, 0);
    }

    // ===== 2D自動整列ユーティリティ =====
    bool IsQuadMesh(GameObject go)
    {
        var mf = go.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null && mf.sharedMesh.name.ToLower().Contains("quad");
    }
    bool HasSpriteRenderer(GameObject go) => go.GetComponent<SpriteRenderer>() != null;

    void AutoAlign2DObject(GameObject go, bool isActor, Vector2? xyScale = null)
    {
        if (!autoAlign2D) return;

        bool isQuad = IsQuadMesh(go);
        bool isSprite = HasSpriteRenderer(go);

        if (isQuad || isSprite)
        {
            // 上から見えるように倒す（XY→XZ）
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // Y位置：タイル=床、アクター=少し上げる
            var p = go.transform.position;
            p.y = floorY + (isActor ? actorYOffset : 0f);
            go.transform.position = p;

            // 視覚スケール（セル基準）
            if (xyScale.HasValue)
            {
                var s = xyScale.Value;
                go.transform.localScale = new Vector3(s.x, s.y, 1f);
            }
        }
    }

    public void SetLevel(string[] newRows)
    {
        if (newRows == null || newRows.Length == 0) return;
        // ディープコピーして安全に保持
        level = new string[newRows.Length];
        for (int i = 0; i < newRows.Length; i++) level[i] = newRows[i];
        Build();
    }

    // ========= ロジックだけ更新（GameObject生成なし） =========
    void ParseCellsFromLevel()
    {
        int h = Height;
        int w = Width;
        if (cells == null || cells.GetLength(0) != h || cells.GetLength(1) != w)
            cells = new CellType[h, w];

        for (int y = 0; y < h; y++)
        {
            var row = level[y];
            for (int x = 0; x < w; x++)
            {
                char ch = row[x];

                // GuardType の記号は床扱い（実生成はBuildで）
                bool isGuardSymbol = false;
                for (int gi = 0; gi < guardTypes.Count; gi++)
                {
                    if (!string.IsNullOrEmpty(guardTypes[gi].symbol) && guardTypes[gi].symbol[0] == ch) { isGuardSymbol = true; break; }
                }

                if (isGuardSymbol)
                {
                    cells[y, x] = CellType.Floor;
                }
                else
                {
                    switch (ch)
                    {
                        case '#': cells[y, x] = CellType.Wall; break;
                        case 'E': cells[y, x] = CellType.Exit; break;
                        case 'P': cells[y, x] = CellType.Floor; break; // プレイヤーの足元は床
                        default: cells[y, x] = CellType.Floor; break;
                    }
                }
            }
        }
    }

    void PlaceTile(CellType t, Vector2Int p)
    {
        GameObject prefab = (t == CellType.Wall) ? pfWall :
                            (t == CellType.Exit) ? pfExit : pfFloor;
        var go = Instantiate(prefab, GridToWorld(p), Quaternion.identity, tilesRoot);
        go.name = $"{t}_{p.x}_{p.y}";
        tileGOs[p.y, p.x] = go;

        // 2D（Quad/Sprite）なら自動整列。3Dなら従来のY整列。
        if (autoAlign2D && (IsQuadMesh(go) || HasSpriteRenderer(go)))
        {
            AutoAlign2DObject(go, false); // タイルは役者扱いしない
            if (t == CellType.Exit)
            {
                var pos = go.transform.position;
                pos.y = floorY + exitTopOffset;
                go.transform.position = pos;
            }
        }
        else
        {
            if (t == CellType.Wall) AlignBottomToY(go, floorY);
            else if (t == CellType.Floor) AlignTopToY(go, floorY);
            else if (t == CellType.Exit) AlignTopToY(go, floorY + exitTopOffset);
        }
    }

    public void Build()
    {
        // まずロジックだけ最新化
        ParseCellsFromLevel();

        // エディタのプレビュー時は見た目を一旦全消しして終了（Hierarchy汚さない）
        if (!Application.isPlaying && editorPreview)
        {
            ClearAll();
            return;
        }

        // ここから実際の生成
        ClearAll();

        int h = Height;
        int w = Width;
        tileGOs = new GameObject[h, w];

        Vector2Int? playerStart = null;

        // Guard/Item スポーン情報
        var guardSpawns = new List<(Vector2Int pos, GuardType type)>();
        var itemSpawns = new List<(Vector2Int pos, ItemType type)>();

        for (int y = 0; y < h; y++)
        {
            var row = level[y];
            for (int x = 0; x < w; x++)
            {
                char ch = row[x];
                Vector2Int p = new Vector2Int(x, y);

                // タイル配置（cellsで判定）
                PlaceTile(cells[y, x], p);

                // プレイヤー
                if (ch == 'P') playerStart = p;

                // ガード（記号→GuardType解決）
                for (int gi = 0; gi < guardTypes.Count; gi++)
                {
                    if (!string.IsNullOrEmpty(guardTypes[gi].symbol) && guardTypes[gi].symbol[0] == ch)
                    {
                        guardSpawns.Add((p, guardTypes[gi]));
                        break;
                    }
                }
                // アイテム
                for (int ii = 0; ii < itemTypes.Count; ii++)
                {
                    if (!string.IsNullOrEmpty(itemTypes[ii].symbol) && itemTypes[ii].symbol[0] == ch)
                    {
                        itemSpawns.Add((p, itemTypes[ii]));
                        break;
                    }
                }
            }
        }

        // Player
        if (playerStart.HasValue && pfPlayer != null)
        {
            var go = Instantiate(pfPlayer, GridToWorld(playerStart.Value), Quaternion.identity, actorsRoot);
            go.name = "Player";
            AutoAlign2DObject(go, true, playerVisualScale);
            var pc = go.GetComponent<PlayerController>();
            if (pc == null) pc = go.AddComponent<PlayerController>();
            player = pc;
            player.Init(this, playerStart.Value);
        }

        // Guards（Prefabに GuardController が付いている前提）
        foreach (var gs in guardSpawns)
        {
            if (gs.type.prefab == null)
            {
                Debug.LogError($"Guard prefab is null for symbol '{gs.type.symbol}'. Set it in BoardManager.guardTypes.");
                continue;
            }

            var go = Instantiate(gs.type.prefab, GridToWorld(gs.pos), Quaternion.identity, actorsRoot);
            go.name = $"Guard_{gs.pos.x}_{gs.pos.y}_{gs.type.symbol}";
            AutoAlign2DObject(go, true, guardVisualScale);

            var g = go.GetComponent<GuardController>();
            if (g == null)
            {
                Debug.LogError($"GuardController is missing on prefab for symbol '{gs.type.symbol}'. Please attach it on the prefab.");
                SafeDestroy(go);
                continue;
            }

            g.Init(this, gs.pos);
            guards.Add(g);
        }

        // Items
        foreach (var it in itemSpawns)
        {
            if (!it.type.prefab) { Debug.LogError($"Item prefab null for '{it.type.symbol}'"); continue; }
            var go = Instantiate(it.type.prefab, GridToWorld(it.pos), Quaternion.identity, itemsRoot);
            go.name = $"Item_{it.pos.x}_{it.pos.y}_{it.type.symbol}";
            AutoAlign2DObject(go, true /*見た目少し浮かす*/, new Vector2(0.8f, 0.8f)); // 見た目縮小は好みで
            itemAt[it.pos] = go;
        }

        // TurnManager（未存在なら生成）
        if (UnityCompat.FindFirst<TurnManager>() == null)
        {
            var tm = new GameObject("TurnManager").AddComponent<TurnManager>();
            tm.board = this;
        }

        // カメラ追従
        var camFollow = UnityCompat.FindFirst<CameraFollow>();
        if (camFollow != null && player != null)
        {
            camFollow.board = this;
            camFollow.target = player.transform;
            camFollow.Snap();
        }

        RefreshAllGuardVision();
    }
    // プレイヤーが p を踏んだときに呼ぶ
    public bool TryPickup(Vector2Int p)
    {
        if (itemAt.TryGetValue(p, out var go) && go != null)
        {
            itemAt.Remove(p);
            SafeDestroy(go);      // エディタ/実行の両対応破棄
      // TODO: SEやエフェクト、インベントリ加算などはここで
            return true;
        }
        return false;
    }
    // ========= エディタプレビュー描画（GameObject生成なし） =========
    void OnDrawGizmos()
    {
        if (!enabled || !editorPreview || level == null) return;

        // ロジックだけ最新化
        ParseCellsFromLevel();

        // 描画順：床 → 壁 → 出口 → 役者マーカー
        float y0 = floorY;
        float y1 = floorY + 0.001f;
        float y2 = floorY + 0.002f;

        // 床（Exitの足元も床で塗る）
        Gizmos.color = previewFloor;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (cells[y, x] == CellType.Floor || cells[y, x] == CellType.Exit)
                    DrawCellGizmo(new Vector2Int(x, y), y0);

        // 壁
        Gizmos.color = previewWall;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (cells[y, x] == CellType.Wall)
                    DrawCellGizmo(new Vector2Int(x, y), y1);

        // 出口
        Gizmos.color = previewExit;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (level[y][x] == 'E')
                    DrawCellGizmo(new Vector2Int(x, y), y2);

        // プレイヤー
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (level[y][x] == 'P')
                {
                    Gizmos.color = previewP;
                    DrawActorDot(new Vector2Int(x, y), y2 + 0.001f);
                }

        // 各 GuardType 記号のマーカー（四角ドット）
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                char ch = level[y][x];
                for (int gi = 0; gi < guardTypes.Count; gi++)
                {
                    if (!string.IsNullOrEmpty(guardTypes[gi].symbol) && guardTypes[gi].symbol[0] == ch)
                    {
                        Gizmos.color = guardTypes[gi].previewColor;
                        DrawActorDot(new Vector2Int(x, y), y2 + 0.001f);
                        break;
                    }
                }
            }
        }

        // 各 Item 記号のマーカー（四角ドットに戻す）
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                char ch = level[y][x];
                for (int ii = 0; ii < itemTypes.Count; ii++)
                {
                    if (!string.IsNullOrEmpty(itemTypes[ii].symbol) && itemTypes[ii].symbol[0] == ch)
                    {
                        Gizmos.color = itemTypes[ii].previewColor;
                        DrawActorDot(new Vector2Int(x, y), y2 + 0.001f);
                        break;
                    }
                }
            }
        }
    }

    void DrawCellGizmo(Vector2Int p, float y)
    {
        Vector3 c = GridToWorld(p) + new Vector3(0.5f, y, 0.5f);
        Gizmos.DrawCube(c, new Vector3(1f, 0.001f, 1f));
    }
    void DrawActorDot(Vector2Int p, float y)
    {
        Vector3 c = GridToWorld(p) + new Vector3(0.5f, y, 0.5f);
        Gizmos.DrawCube(c, new Vector3(0.35f, 0.002f, 0.35f));
    }

    // ========= 回転（任意マス・部分回転対応） ===========
    public struct RotatePreview { public bool valid; public List<Vector2Int> area; }

    public RotatePreview GetPreview(Vector2Int center, int size)
    {
        var res = new RotatePreview { valid = false, area = new List<Vector2Int>() };
        int k = (size - 1) / 2;
        bool hasAnyInBounds = false;
        bool hasExit = false;
        for (int dy = -k; dy <= k; dy++)
            for (int dx = -k; dx <= k; dx++)
            {
                var p = new Vector2Int(center.x + dx, center.y + dy);
                res.area.Add(p);
                if (InBounds(p))
                {
                    hasAnyInBounds = true;
                    if (cells[p.y, p.x] == CellType.Exit) hasExit = true; // Exit含むと回転禁止
                }
            }
        if (!hasAnyInBounds) return res;
        if (hasExit) return res;

        bool safeCW = WouldBeSafePartial(center, size, +1);
        bool safeCCW = WouldBeSafePartial(center, size, -1);
        res.valid = safeCW && safeCCW;
        return res;
    }

    bool WouldBeSafePartial(Vector2Int center, int size, int dir)
    {
        int k = (size - 1) / 2;
        var occ = new List<Vector2Int>();
        if (player != null) occ.Add(player.pos);
        foreach (var g in guards) occ.Add(g.pos);

        foreach (var o in occ)
        {
            if (o.x < center.x - k || o.x > center.x + k ||
                o.y < center.y - k || o.y > center.y + k) continue;

            int lx = o.x - (center.x - k);
            int ly = o.y - (center.y - k);

            int gdir = -dir; // 配列側は符号反転

            int sx, sy;
            if (gdir > 0) { sx = ly; sy = size - 1 - lx; } // 時計回り（配列）
            else { sx = size - 1 - ly; sy = lx; } // 反時計（配列）

            int gx = center.x - k + sx;
            int gy = center.y - k + sy;

            CellType after = InBounds(new Vector2Int(gx, gy)) ? cells[gy, gx] : cells[o.y, o.x];
            if (after == CellType.Wall) return false;
        }
        return true;
    }

    bool AreaHasExit(Vector2Int center, int size)
    {
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!InBounds(p)) continue;
                if (cells[p.y, p.x] == CellType.Exit) return true;
            }
        return false;
    }

    public void RotateArea(Vector2Int center, int size, int dir, System.Action onDone)
    {
        if (IsAnimating) return;
        if (AreaHasExit(center, size)) { onDone?.Invoke(); return; }
        if (!WouldBeSafePartial(center, size, dir)) { onDone?.Invoke(); return; }
        StartCoroutine(RotateCoro(center, size, dir, onDone));
    }

    IEnumerator RotateCoro(Vector2Int center, int size, int dir, System.Action onDone)
    {
        IsAnimating = true;
        var pivotGO = new GameObject($"RotatePivot_{center.x}_{center.y}");
        pivotGO.transform.position = GridToWorld(center) + new Vector3(0, 0.05f, 0);

        List<GameObject> targets = new();
        int k = (size - 1) / 2;
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                int gx = center.x + i - k;
                int gy = center.y + j - k;
                var gp = new Vector2Int(gx, gy);
                if (!InBounds(gp)) continue;
                var tile = tileGOs[gy, gx];
                tile.transform.SetParent(pivotGO.transform, true);
                targets.Add(tile);
            }

        float t = 0f, dur = 0.15f;
        Quaternion from = pivotGO.transform.rotation;
        Quaternion to = Quaternion.AngleAxis(90f * dir, Vector3.up) * from; // ←アニメはこのままでOK
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            pivotGO.transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0, 1, t));
            yield return null;
        }

        var newCells = new Dictionary<Vector2Int, CellType>();
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                int gx = center.x + i - k;
                int gy = center.y + j - k;
                var dest = new Vector2Int(gx, gy);
                if (!InBounds(dest)) continue;

                int gdir = -dir; // 配列側は符号反転

                int sx, sy; // 逆写像（dest→src）
                if (gdir > 0) { sx = j; sy = size - 1 - i; } // 時計回り（配列）
                else { sx = size - 1 - j; sy = i; } // 反時計（配列）

                int sgx = center.x - k + sx;
                int sgy = center.y - k + sy;

                CellType after = InBounds(new Vector2Int(sgx, sgy)) ? cells[sgy, sgx] : cells[gy, gx];
                newCells[dest] = after;
            }

        foreach (var go in targets) SafeDestroy(go);
        SafeDestroy(pivotGO);

        foreach (var kv in newCells)
        {
            var p = kv.Key;
            cells[p.y, p.x] = kv.Value;
            var go2 = Instantiate(
                (cells[p.y, p.x] == CellType.Wall) ? pfWall :
                (cells[p.y, p.x] == CellType.Exit) ? pfExit : pfFloor,
                GridToWorld(p), Quaternion.identity, tilesRoot);
            go2.name = $"{cells[p.y, p.x]}_{p.x}_{p.y}";
            AutoAlign2DObject(go2, false);
            tileGOs[p.y, p.x] = go2;
        }

        IsAnimating = false;
        RefreshAllGuardVision();
        onDone?.Invoke();
    }

    // =========== LoS ===========
    public bool HasLineOfSight(Vector2Int from, Vector2Int to)
    {
        int x0 = from.x, y0 = from.y, x1 = to.x, y1 = to.y;
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        int prevX = x0, prevY = y0;

        while (true)
        {
            // 始点は無視、以降の通過セルで遮蔽判定
            if (!(x0 == from.x && y0 == from.y))
            {
                if (BlocksVision(new Vector2Int(x0, y0))) return false;

                // 直前から "斜めに動いた" フレームでは角抜けチェック
                if (x0 != prevX && y0 != prevY)
                {
                    // 中間に接している2セル（横・縦）が両方とも壁なら遮断
                    var sideA = new Vector2Int(prevX + sx, prevY); // 横
                    var sideB = new Vector2Int(prevX, prevY + sy); // 縦
                    if (BlocksVision(sideA) && BlocksVision(sideB)) return false;
                }
            }

            if (x0 == x1 && y0 == y1) break;

            int e2 = 2 * err;
            prevX = x0; prevY = y0;

            // supercover: 同一フレームでX/Yの両方が進む可能性を残す
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }

        return true;
    }

    // =========== 視界可視化の一括制御 ===========
    public void RefreshAllGuardVision()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) { } // 実害なしの置き忘れ防止
        if (!Application.isPlaying) return; // エディタでは作らない（Hierarchy汚さない）
#endif
        foreach (var g in guards)
            if (g != null) g.UpdateVisionOverlay();
    }
    public void SetAllGuardVision(bool on)
    {
        foreach (var g in guards)
        {
            if (g == null) continue;
            g.showVision = on;
            g.UpdateVisionOverlay();
        }
    }
    public void ToggleAllGuardVision()
    {
        bool next = true;
        if (guards.Count > 0 && guards[0] != null) next = !guards[0].showVision;
        SetAllGuardVision(next);
    }

#if UNITY_EDITOR
    [ContextMenu("Commit Tiles Now (Editor)")]
    void EditorCommitNow()
    {
        bool prev = editorPreview;
        editorPreview = false;
        Build();
        editorPreview = prev;
    }

    [ContextMenu("Rebuild Level Now")]
    void EditorRebuildNow() { Build(); }
#endif
}
