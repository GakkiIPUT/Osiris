using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CellType { Floor, Wall, Exit, Anchor, Pit } // ← 追加: 落とし穴
public enum WallOrigin { Core, Outer }

[ExecuteAlways] // エディタでもプレビュー用に動かす
public class BoardManager : MonoBehaviour
{
    [Header("Prefabs (Tiles & Player)")]
    public GameObject pfFloor;
    public GameObject pfWall;
    public GameObject pfExit;
    public GameObject pfAnchor;   // 回転不可マス（@）
    public GameObject pfPit;      // 落とし穴（x）
    public GameObject pfPlayer;   // PlayerはAddComponentでPlayerController付与（Prefab側にあってもOK）

    // TurnManager のイベント購読用（重複防止）
    TurnManager _turn;

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
        // i = 鍵（必須アイテム）
        new ItemType{ symbol="i", prefab=null, label="Key", previewColor = new Color(0.25f,1f,0.9f,1f) },
        // t = 宝箱（コレクション用・クリア条件に含めない）
        new ItemType{ symbol="t", prefab=null, label="Treasure", previewColor = new Color(1.0f,0.7f,0.2f,1f) },
        // d = 泥棒（視認で宝箱に変化／侵入不可）
        new ItemType{ symbol="d", prefab=null, label="Thief", previewColor = new (0.8f,0.4f,1.0f,1f) },
        // e = 鍵泥棒（視認で鍵に変化／侵入不可）← 追加
        new ItemType{ symbol="e", prefab=null, label="Thief (Key)", previewColor = new (0.25f,0.8f,1.0f,1f) },
    };

    public Transform itemsRoot; // アイテムの親（未設定ならAwakeで作る）
    [Header("Realtime / Rotation Limits")]
    public bool realtime = true;                   // ← リアルタイムモードON
    [Tooltip("回転中心として選べる最大距離（プレイヤーからのマンハッタン距離 or チェビシェフ距離）")]
    public int rotationCenterMaxDistance = 2;      // 1～2 推奨
    [Tooltip("回転範囲にアンカー(@)が含まれる場合は回転を禁止")]
    public bool forbidAnchorInArea = true;

    // アンカーの記録（@）
    public HashSet<Vector2Int> anchors = new HashSet<Vector2Int>();
    public bool IsAnchor(Vector2Int p) => anchors.Contains(p);

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
    public float ghostY = 0.000001f;        // ゴースト表示Y（床より少し上）

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
    public Color previewAnchor = Color.black;
    public Color previewPit = new Color(0.15f, 0.15f, 0.6f, 1f);



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

    public int Width => autoGenerateOuterRings ? expandedWidth : (level.Length > 0 ? level[0].Length : 0);
    public int Height => autoGenerateOuterRings ? expandedHeight : level.Length;

    public CellType[,] cells;
    private GameObject[,] tileGOs;  // 1マス=1オブジェクト（Floor/Wall/Exit/Anchor）

    int coreOffsetX, coreOffsetY;          // Core の左上オフセット（拡張後座標系内）
    int coreWidth, coreHeight;             // 元 level の幅高さ
    int expandedWidth, expandedHeight;     // 拡張後の全体サイズ
    string[] levelOriginalCore;            // 元 level のコピー（外周 ON 時のみ使用）
    WallOrigin[,] wallOrigin;              // 壁セルの起源（Wall 以外は未使用）

    bool IsInsideCore(Vector2Int p)
    {
        if (!autoGenerateOuterRings) return InBounds(p);
        return p.x >= coreOffsetX && p.x < coreOffsetX + coreWidth &&
               p.y >= coreOffsetY && p.y < coreOffsetY + coreHeight;
    }
    // ルート
    public Transform tilesRoot;
    public Transform actorsRoot;

    [HideInInspector] public PlayerController player;
    [HideInInspector] public List<GuardController> guards = new();

    // ★ マップ上のアイテム：位置 → (記号, 実体)
    public Dictionary<Vector2Int, (char sym, GameObject go)> itemAt = new();

    bool _isAnimating;
    public bool IsAnimating
    {
        get => _isAnimating;
        set
        {
            if (_isAnimating == value) return;
            _isAnimating = value;
            Debug.Log($"[Board] IsAnimating {(value ? "ON" : "OFF")} t={Time.time:F3}")
;
        }
    }


    [Header("DEV / Rotation")]
    public bool rotatePlayerWithArea = true; // 開発者モードで切り替え

    // ▼ 追加: 自由回転（ドラッグ）DEV設定
    [Header("DEV / Free Rotate")]
    [Tooltip("ドラッグ式の自由回転を有効にする（ON時でも T はキャンセル専用）")]
    public bool devEnableFreeRotate = true;
    [Tooltip("180°回転を許可（許可時は2AP想定）")]
    public bool devAllow180Rotation = true;
    [Tooltip("スナップ角度（最近傍吸着の目安、将来用）")]
    [Range(1f, 45f)] public float devSnapAngleDeg = 15f;
    [Tooltip("コミット許容角（将来用）。この角以内なら確定吸着する想定")]
    [Range(1f, 45f)] public float devCommitAngleDeg = 15f;
    [Tooltip("スティッキネス（将来用）")]
    [Range(0f, 1f)] public float devStickiness = 0.5f;
    [Tooltip("クリック直後の回転不可フラッシュ（赤Ghost）秒数")]
    [Range(0.1f, 2.0f)] public float devNgGhostSeconds = 0.5f;
    [Header("Outer Ring Visuals")]
    [Tooltip("外周Floor(CORE外に存在する全 Floor) 用マテリアル（未設定なら通常と同じ）")]
    public Material outerRingFloorMat;
    [Tooltip("Gizmo プレビュー用 外周Floor 色")]
    public Color previewOuterFloor = new Color(0.70f, 0.75f, 0.85f, 1f);

    // 外周Floor判定配列（autoGenerateOuterRings=false のとき null）
    bool[,] outerRingFloor;
    [Header("Outer Ring Generation")]
    [Tooltip("外周(Anchor帯 + OuterWall帯)を自動生成する")]
    public bool autoGenerateOuterRings = false;

    [Tooltip("最外周 Anchor(@) の厚み（通常1）")]
    [Min(1)] public int anchorThickness = 1;

    [Tooltip("Anchor 内側の外周 Wall(#) 厚み（0でAnchorのみ）")]
    [Min(0)] public int outerWallThickness = 1;

    [Header("Wall Materials (Outer/Core)")]
    [Tooltip("Core(元マップ) or 外周内に入った外周壁用（未設定なら従来見た目維持）")]
    public Material wallNormalMat;
    [Tooltip("Core 外側に存在する Outer起源壁に適用するマテリアル")]
    public Material wallOuterMat;
    void Awake()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) editorPreview = false;
#endif
        if (tilesRoot == null) tilesRoot = new GameObject("TilesRoot").transform;
        if (actorsRoot == null) actorsRoot = new GameObject("ActorsRoot").transform;
        if (itemsRoot == null) itemsRoot = new GameObject("ItemsRoot").transform;
        LoadDevModeSettings();
        Build(); // 必ず自身の level で生成
    }

    void OnEnable()
    {
        if (!Application.isPlaying) Build();
    }

    public Vector3 GridToWorld(Vector2Int p) => new Vector3(p.x, 0f, p.y);
    public Vector2Int WorldToGrid(Vector3 w) => new Vector2Int(Mathf.RoundToInt(w.x), Mathf.RoundToInt(w.z));
    public Vector3 GridToWorldActor(Vector2Int p) => GridToWorld(p) + new Vector3(0, actorYOffset, 0f);
    public bool InBounds(Vector2Int p) => p.x >= 0 && p.x < Width && p.y >= 0 && p.y < Height;

    // ★ガードがこのマスにいるか（移動先も占有とみなす）
    public bool IsOccupiedByGuard(Vector2Int p)
    {
        for (int i = 0; i < guards.Count; i++)
        {
            var g = guards[i];
            if (g == null) continue;

            // 現在位置 or 次の到達マス（移動中）をブロック
            if (g.pos == p) return true;
            if (g.IsMoving && g.NextPos == p) return true;
        }
        return false;
    }

    // ★プレイヤー用：敵をブロッカーとして扱うWalkable判定
    public bool IsWalkable(Vector2Int p, bool blockGuardsForPlayer)
    {
        if (!InBounds(p)) return false;
        if (autoGenerateOuterRings && !IsInsideCore(p))
            return false;
        var c = cells[p.y, p.x];
        // 床 or 出口は歩行可。壁/アンカーは不可。
        bool tileOK = (c == CellType.Floor || c == CellType.Exit);
        if (!tileOK) return false;
        // 泥棒がいるマスは侵入不可
        if (IsThiefAt(p)) return false;

        if (blockGuardsForPlayer && IsOccupiedByGuard(p)) return false;

        return true;
    }
    public bool IsWalkable(Vector2Int p)
    {
        if (!InBounds(p)) return false;
        // 泥棒がいるマスは侵入不可
        if (IsThiefAt(p)) return false;
        if (autoGenerateOuterRings && !IsInsideCore(p))
            return false; var c = cells[p.y, p.x];
        return c == CellType.Floor || c == CellType.Exit;
    }

    public bool BlocksVision(Vector2Int p)
    {
        if (!InBounds(p)) return true;
        var c = cells[p.y, p.x];
        return (c == CellType.Wall || c == CellType.Anchor);
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
        // 自由回転プレビュー中だった場合は必ず元に戻してから破棄
        RestoreFreePreview();

        if (player != null)
        {
            player.ClearGhost();
        }
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

    // 追加: 破棄時にもプレビューを確実に戻す
    void OnDisable()
    {
        if (Application.isPlaying)
        {
            RestoreFreePreview();
        }
        UnsubscribeTurnEvents();
    }

    void OnDestroy()
    {
        RestoreFreePreview();
        UnsubscribeTurnEvents();
    }
    //void ClearAll()
    //{

    //    if (player != null)
    //    {
    //        player.ClearGhost();
    //    }
    //    if (tilesRoot != null)
    //        for (int i = tilesRoot.childCount - 1; i >= 0; --i)
    //            SafeDestroy(tilesRoot.GetChild(i).gameObject);

    //    if (actorsRoot != null)
    //        for (int i = actorsRoot.childCount - 1; i >= 0; --i)
    //            SafeDestroy(actorsRoot.GetChild(i).gameObject);

    //    if (itemsRoot != null)
    //        for (int i = itemsRoot.childCount - 1; i >= 0; --i)
    //            SafeDestroy(itemsRoot.GetChild(i).gameObject);

    //    guards.Clear();
    //    player = null;
    //    itemAt.Clear();
    //}

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
    string[] BuildExpandedLevelAndAllocateArrays()
    {
        levelOriginalCore = (string[])level.Clone();
        coreHeight = level.Length;
        coreWidth = coreHeight > 0 ? level[0].Length : 0;

        if (!autoGenerateOuterRings)
        {
            coreOffsetX = coreOffsetY = 0;
            expandedWidth = coreWidth;
            expandedHeight = coreHeight;
            cells = new CellType[expandedHeight, expandedWidth];
            wallOrigin = new WallOrigin[expandedHeight, expandedWidth];
            outerRingFloor = autoGenerateOuterRings ? new bool[expandedHeight, expandedWidth] : null;
            return (string[])level.Clone();
        }

        int totalPad = anchorThickness + outerWallThickness;
        coreOffsetX = totalPad;
        coreOffsetY = totalPad;
        expandedWidth = coreWidth + totalPad * 2;
        expandedHeight = coreHeight + totalPad * 2;

        var rows = new string[expandedHeight];
        for (int y = 0; y < expandedHeight; y++)
        {
            var line = new char[expandedWidth];
            for (int x = 0; x < expandedWidth; x++)
            {
                // Anchor 帯
                bool isAnchorBand =
                    x < anchorThickness || y < anchorThickness ||
                    x >= expandedWidth - anchorThickness ||
                    y >= expandedHeight - anchorThickness;
                if (isAnchorBand) { line[x] = '@'; continue; }

                // Outer Wall 帯
                bool inOuterWall =
                    x < anchorThickness + outerWallThickness ||
                    y < anchorThickness + outerWallThickness ||
                    x >= expandedWidth - (anchorThickness + outerWallThickness) ||
                    y >= expandedHeight - (anchorThickness + outerWallThickness);
                if (inOuterWall) { line[x] = '#'; continue; }

                // Core 埋め込み
                int cx = x - coreOffsetX;
                int cy = y - coreOffsetY;
                line[x] = (cx >= 0 && cx < coreWidth && cy >= 0 && cy < coreHeight)
                        ? level[cy][cx]
                        : '.';
            }
            rows[y] = new string(line);
        }

        cells = new CellType[expandedHeight, expandedWidth];
        wallOrigin = new WallOrigin[expandedHeight, expandedWidth];
        return rows;
    }
    // ========= ロジックだけ更新（GameObject生成なし） =========
    // （修正前はここで PlaceTile / playerStart 等を参照していたためエラー）
    void ParseCellsFromLevel()
    {
        // 外周展開と配列確保
        string[] rows = BuildExpandedLevelAndAllocateArrays();
        int h = rows.Length;
        int w = h > 0 ? rows[0].Length : 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                char ch;

                if (autoGenerateOuterRings)
                {
                    // Core 内なら元 levelOriginalCore を参照
                    if (IsInsideCore(new Vector2Int(x, y)))
                    {
                        int cx = x - coreOffsetX;
                        int cy = y - coreOffsetY;
                        ch = levelOriginalCore[cy][cx];
                    }
                    else
                    {
                        // 外周で自動生成済み（@ / # / .）→ rows の文字をそのまま使用
                        ch = rows[y][x];
                    }
                }
                else
                {
                    ch = level[y][x];
                }

                // Guard / Item の記号はタイル的には床扱い
                bool isGuard = false;
                for (int gi = 0; gi < guardTypes.Count; gi++)
                {
                    if (!string.IsNullOrEmpty(guardTypes[gi].symbol) &&
                        guardTypes[gi].symbol[0] == ch) { isGuard = true; break; }
                }
                bool isItem = false;
                for (int ii = 0; ii < itemTypes.Count; ii++)
                {
                    if (!string.IsNullOrEmpty(itemTypes[ii].symbol) &&
                        itemTypes[ii].symbol[0] == ch) { isItem = true; break; }
                }
                if (isGuard || isItem)
                {
                    cells[y, x] = CellType.Floor;
                    continue;
                }

                switch (ch)
                {
                    case '#':
                        // ==== STEP1 MOD: 外周(#)を Floor 性質に（侵入不可は別判定で実装） ====
                        if (autoGenerateOuterRings &&
                            (x < coreOffsetX || x >= coreOffsetX + coreWidth ||
                             y < coreOffsetY || y >= coreOffsetY + coreHeight))
                        {
                            // 外周 Wall 帯 → Floor として扱う（視界は通し、回転ブロックしない）
                            cells[y, x] = CellType.Floor;
                        }
                        else
                        {
                            // Core 内の # は従来通り Wall
                            cells[y, x] = CellType.Wall;
                            wallOrigin[y, x] = WallOrigin.Core;
                        }
                        break;
                    case 'E': cells[y, x] = CellType.Exit; break;
                    case 'P': cells[y, x] = CellType.Floor; break;
                    case '@': cells[y, x] = CellType.Anchor; break;
                    case 'x': cells[y, x] = CellType.Pit; break;
                    default: cells[y, x] = CellType.Floor; break;
                }
            }
        }
        RecomputeOuterRingFloor();
    }
    void RecomputeOuterRingFloor()
    {
        if (!autoGenerateOuterRings)
        {
            outerRingFloor = null;
            return;
        }
        if (outerRingFloor == null ||
            outerRingFloor.GetLength(0) != Height ||
            outerRingFloor.GetLength(1) != Width)
        {
            outerRingFloor = new bool[Height, Width];
        }

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var p = new Vector2Int(x, y);
                // Core 外 かつ Floor のセルをマーキング（Exit は別扱い: 見た目は Exit 色で塗るので含めない）
                outerRingFloor[y, x] = (!IsInsideCore(p) && cells[y, x] == CellType.Floor);
            }
        }
    }
    public bool IsOuterFloor(Vector2Int p)
    {
        return outerRingFloor != null &&
               InBounds(p) &&
               outerRingFloor[p.y, p.x];
    }
    bool IsRotateLockedCell(Vector2Int p)
    {
        if (!InBounds(p)) return false;
        var c = cells[p.y, p.x];
        return (c == CellType.Exit || c == CellType.Anchor);
    }

    void PlaceTile(CellType t, Vector2Int p)
    {
        bool isOuterF = IsOuterFloor(p);

        GameObject prefab =
            (t == CellType.Wall) ? pfWall :
            (t == CellType.Exit) ? pfExit :
            (t == CellType.Anchor) ? pfAnchor :
            (t == CellType.Pit) ? pfPit : pfFloor;

        var go = Instantiate(prefab, GridToWorld(p), Quaternion.identity, tilesRoot);
        go.name = isOuterF ? $"OuterFloor_{p.x}_{p.y}" : $"{t}_{p.x}_{p.y}";
        tileGOs[p.y, p.x] = go;

        // 外周 Floor 用マテリアル
        if (isOuterF && outerRingFloorMat != null)
        {
            var rend = go.GetComponentInChildren<Renderer>();
            if (rend) rend.sharedMaterial = outerRingFloorMat;
        }

        if (autoAlign2D && (IsQuadMesh(go) || HasSpriteRenderer(go)))
        {
            AutoAlign2DObject(go, false);
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
            else if (t == CellType.Floor || isOuterF) AlignTopToY(go, floorY);
            else if (t == CellType.Exit) AlignTopToY(go, floorY + exitTopOffset);
        }

        if (t == CellType.Wall)
            UpdateWallAppearanceAt(p);
    }
    void UpdateWallAppearanceAt(Vector2Int p)
    {
        if (!InBounds(p)) return;
        if (cells[p.y, p.x] != CellType.Wall) return;
        if (!autoGenerateOuterRings) return;
        if (wallNormalMat == null || wallOuterMat == null) return;

        var go = tileGOs[p.y, p.x];
        if (!go) return;
        var rend = go.GetComponentInChildren<Renderer>();
        if (!rend) return;

        bool outsideCore = !IsInsideCore(p);
        var origin = wallOrigin[p.y, p.x];
        rend.sharedMaterial = (origin == WallOrigin.Outer && outsideCore) ? wallOuterMat : wallNormalMat;
    }
    void UpdateAllWallAppearances()
    {
        if (!autoGenerateOuterRings) return;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (cells[y, x] == CellType.Wall)
                    UpdateWallAppearanceAt(new Vector2Int(x, y));
    }
    public void Build()
    {
        // まずロジックだけ最新化
        ParseCellsFromLevel();
        // 再購読前に解除
        UnsubscribeTurnEvents();

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

        // ★ 上部UI用：このマップに存在する「必須アイテム（=鍵 'i'）」を左→右→次行…で列挙
        var requiredSymbols = new List<char>();

        for (int y = 0; y < h; y++)
        {
            {
                for (int x = 0; x < w; x++)
                {
                    Vector2Int p = new Vector2Int(x, y);

                    // Core/Outer 判定に基づき表示用記号を取得
                    char ch;
                    if (autoGenerateOuterRings)
                    {
                        if (IsInsideCore(p))
                        {
                            int cx = x - coreOffsetX;
                            int cy = y - coreOffsetY;
                            ch = levelOriginalCore[cy][cx];
                        }
                        else
                        {
                            ch = '.'; // 外周領域：プレイヤー / ガード / アイテム記号は存在しない扱い
                        }
                    }
                    else
                    {
                        ch = level[y][x];
                    }

                    // タイル生成（cells は ParseCellsFromLevel 済み）
                    PlaceTile(cells[y, x], p);

                    // プレイヤー初期位置
                    if (ch == 'P') playerStart = p;

                    // ガード収集
                    for (int gi = 0; gi < guardTypes.Count; gi++)
                    {
                        if (!string.IsNullOrEmpty(guardTypes[gi].symbol) &&
                            guardTypes[gi].symbol[0] == ch)
                        {
                            guardSpawns.Add((p, guardTypes[gi]));
                            break;
                        }
                    }

                    // アイテム収集
                    for (int ii = 0; ii < itemTypes.Count; ii++)
                    {
                        if (!string.IsNullOrEmpty(itemTypes[ii].symbol) &&
                            itemTypes[ii].symbol[0] == ch)
                        {
                            itemSpawns.Add((p, itemTypes[ii]));
                            if (ch == 'i' || ch == 'e')
                                requiredSymbols.Add('i'); // ‘e’ も鍵1つとしてカウント
                            break;
                        }
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
            player.LoadDevModeSettings();
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
            char sym = !string.IsNullOrEmpty(gs.type.symbol) ? gs.type.symbol[0] : 'G';
            ConfigureGuardFromSymbol(g, sym);

            g.Init(this, gs.pos);
            guards.Add(g);
        }

        // Items
        foreach (var it in itemSpawns)
        {
            if (!it.type.prefab) { Debug.LogError($"Item prefab null for '{it.type.symbol}'"); continue; }
            var go = Instantiate(it.type.prefab, GridToWorld(it.pos), Quaternion.identity, itemsRoot);
            go.name = $"Item_{it.pos.x}_{it.pos.y}_{it.type.symbol}";
            AutoAlign2DObject(go, true, GetItemVisualScaleBySymbol(it.type.symbol[0]));

            // ★ 位置 → (記号, 実体) を保存（拾得とUI更新に使う）
            itemAt[it.pos] = (it.type.symbol[0], go);
        }

        // TurnManager（未存在なら生成）＋収集UIへ「このマップの全アイテム並び」を渡す（鍵のみ）
        var tm = UnityCompat.FindFirst<TurnManager>();
        if (tm == null)
        {
            tm = new GameObject("TurnManager").AddComponent<TurnManager>();
            tm.ResetScoreCounters();
            tm.board = this;
        }
        else
        {
            tm.board = this; // 念のため再割当て
        }
        tm.ResetGoalState();
        tm.InitRequiredItems(requiredSymbols);

        // Exit の初期状態を受け取るため、InitRequiredItems より前に購読
        _turn = tm;
        _turn.onRequiredChanged += OnRequiredChanged_UpdateExitOpenState;
        tm.InitRequiredItems(requiredSymbols); // ここで初期コールバックが飛ぶ（未達成→閉）

        // カメラ追従
        var camFollow = UnityCompat.FindFirst<CameraFollow>();
        if (camFollow != null && player != null)
        {
            camFollow.board = this;
            camFollow.target = player.transform;
            camFollow.Snap();
        }

        // 生成後に視界可視化を更新
        RefreshAllGuardVision();

        UpdateAllWallAppearances();
        var overlay = GetComponent<SelectionFramesOverlay>();
        if (overlay == null) overlay = gameObject.AddComponent<SelectionFramesOverlay>();
        overlay.board = this;
        overlay.innerSize = 3;
        overlay.outerRadius = 3;
    }

    // 必須アイテムの進捗 → Exit を一括で開閉
    void OnRequiredChanged_UpdateExitOpenState(IReadOnlyList<TurnManager.RequiredItem> reqs)
    {
        bool all = true;
        if (reqs != null)
        {
            for (int i = 0; i < reqs.Count; i++)
                if (!reqs[i].collected) { all = false; break; }
        }
        SetAllExitsOpen(all);
    }

    // 盤面にある全 Exit へ開閉状態を反映（ExitController 経由）
    public void SetAllExitsOpen(bool open)
    {
        if (tileGOs == null) return;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (cells[y, x] != CellType.Exit) continue;
                var go = tileGOs[y, x];
                if (go == null) continue;
                var ec = go.GetComponentInChildren<ExitController>(true);
                if (ec != null) ec.SetOpen(open);
            }
        }
    }

    void UnsubscribeTurnEvents()
    {
        if (_turn != null)
        {
            _turn.onRequiredChanged -= OnRequiredChanged_UpdateExitOpenState;
            _turn = null;
        }
    }

    void ConfigureGuardFromSymbol(GuardController g, char sym)
    {
        if (g == null) return;

        // 既定：未定義記号はプレファブ設定をそのまま使う
        switch (sym)
        {
            case 'G': // R5
                g.patrolMode = GuardController.PatrolMode.PingPong;
                g.patternIsRelative = true;
                g.pattern = "R5";
                break;

            case 'H': // L5
                g.pattern = "L5";
                break;

            case 'I': // U5
                g.pattern = "U5";
                break;

            case 'J': // D5
                g.pattern = "D5";
                break;

            case 'K': // 四角巡回 R4,U4,L4,D4
                g.patrolMode = GuardController.PatrolMode.Loop;
                g.pattern = "R4,U4,L4,D4";
                break;

            case 'L': // 寄り道巡回（2マス）
                g.patrolMode = GuardController.PatrolMode.Loop;
                g.pattern = "R3,U3"; // ←三角にしたいなら ",D2" を外す
                break;

            case 'T': // 上を監視（その場）
                g.patrolMode = GuardController.PatrolMode.Static;
                g.watchMode = GuardController.WatchMode.OneDir;
                g.startFacing = GuardController.Facing.Up;
                g.pattern = "";
                break;

            case 'O': // 下
                g.patrolMode = GuardController.PatrolMode.Static;
                g.watchMode = GuardController.WatchMode.OneDir;
                g.startFacing = GuardController.Facing.Down;
                g.pattern = "";
                break;

            case 'M': // 左
                g.patrolMode = GuardController.PatrolMode.Static;
                g.watchMode = GuardController.WatchMode.OneDir;
                g.startFacing = GuardController.Facing.Left;
                g.pattern = "";
                break;

            case 'N': // 右
                g.patrolMode = GuardController.PatrolMode.Static;
                g.watchMode = GuardController.WatchMode.OneDir;
                g.startFacing = GuardController.Facing.Right;
                g.pattern = "";
                break;

            case 'R': // 上下2方向
                g.patrolMode = GuardController.PatrolMode.Static;
                g.watchMode = GuardController.WatchMode.TwoDirUD;
                g.pattern = "";
                break;

            case 'Q': // 左右2方向
                g.patrolMode = GuardController.PatrolMode.Static;
                g.watchMode = GuardController.WatchMode.TwoDirLR;
                g.pattern = "";
                break;

            case 'S': // 4方向ローテ（その場回転）
                g.patrolMode = GuardController.PatrolMode.Static;
                g.watchMode = GuardController.WatchMode.Rotate4Dir;
                g.rotatePeriod = 1.0f;        // お好みで
                g.rotateClockwise = true;     // お好みで
                g.pattern = "";
                break;
            default:
                // 記号未定義 → プレハブの設定をそのまま使う
                break;
        }
    }

    // プレイヤーが p を踏んだときに呼ぶ（アイテム取得してUI更新）
    public bool TryPickupItemAt(Vector2Int p)
    {
        if (itemAt.TryGetValue(p, out var t) && t.go != null)
        {
            // 泥棒は取得不可（そもそも侵入できない想定）
            if (t.sym == 'd' || t.sym == 'e') return false;
            itemAt.Remove(p);
            SafeDestroy(t.go); // エディタ/実行の両対応破棄

            // ★ TurnManagerへ通知
            var turn = UnityCompat.FindFirst<TurnManager>();
            if (turn != null)
            {
                if (t.sym == 'i')
                {
                    // 鍵だけを必須アイテムとして扱う
                    turn.OnItemPicked('i');
                }
                else if (t.sym == 't')
                {
                    // 宝箱（コレクション）
                    turn.OnTreasurePicked();
                }
                // 他の記号が増えたら必要に応じて分岐
            }

            return true;
        }
        return false;

    }
    // ===== 泥棒ユーティリティ =====
    public bool IsThiefAt(Vector2Int p)
    {
        return itemAt.TryGetValue(p, out var t) && (t.sym == 'd' || t.sym == 'e');
    }

    public bool TransformThiefToTreasureAt(Vector2Int p)
    {
        if (!itemAt.TryGetValue(p, out var t) || t.sym != 'd') return false;

        // 泥棒見た目を消す
        if (t.go) SafeDestroy(t.go);
        itemAt.Remove(p);

        // 宝箱プレハブを検索
        GameObject chestPf = null;
        for (int i = 0; i < itemTypes.Count; i++)
        {
            if (!string.IsNullOrEmpty(itemTypes[i].symbol) && itemTypes[i].symbol[0] == 't')
            {
                chestPf = itemTypes[i].prefab;
                break;
            }
        }

        GameObject chestGo = null;
        if (chestPf != null)
        {
            chestGo = Instantiate(chestPf, GridToWorld(p), Quaternion.identity, itemsRoot);
            chestGo.name = $"Item_{p.x}_{p.y}_t";
            AutoAlign2DObject(chestGo, true, GetItemVisualScaleBySymbol('t'));
        }
        else
        {
            Debug.LogWarning("[Thief] Treasure prefab for symbol 't' is not assigned in BoardManager.itemTypes.");
        }

        // 位置 → 宝箱を登録
        itemAt[p] = ('t', chestGo);
        return true;
    }

    // 鍵泥棒（e）→ 鍵（i）に変換
    public bool TransformThiefToKeyAt(Vector2Int p)
    {
        if (!itemAt.TryGetValue(p, out var t) || t.sym != 'e') return false;

        // 泥棒見た目を消す
        if (t.go) SafeDestroy(t.go);
        itemAt.Remove(p);

        // 鍵プレハブを検索
        GameObject keyPf = null;
        for (int i = 0; i < itemTypes.Count; i++)
        {
            if (!string.IsNullOrEmpty(itemTypes[i].symbol) && itemTypes[i].symbol[0] == 'i')
            {
                keyPf = itemTypes[i].prefab;
                break;
            }
        }

        GameObject keyGo = null;
        if (keyPf != null)
        {
            keyGo = Instantiate(keyPf, GridToWorld(p), Quaternion.identity, itemsRoot);
            keyGo.name = $"Item_{p.x}_{p.y}_i";
            AutoAlign2DObject(keyGo, true, GetItemVisualScaleBySymbol('i'));
        }
        else
        {
            Debug.LogWarning("[Thief] Key prefab for symbol 'i' is not assigned in BoardManager.itemTypes.");
        }

        // 位置 → 鍵を登録（UIの必須進捗は拾得時に更新）
        itemAt[p] = ('i', keyGo);
        return true;
    }

    // ========= エディタプレビュー描画（GameObject生成なし） =========
    void OnDrawGizmos()
    {
        if (!enabled || !editorPreview || level == null) return;

        // ロジックだけ最新化
        ParseCellsFromLevel();

        if (cells == null)
            return;
        if (Height != cells.GetLength(0) || Width != cells.GetLength(1))
            return;
        // 描画順：床 → 壁 → 出口 → 役者マーカー
        float y0 = floorY;
        float y1 = floorY + 0.001f;
        float y2 = floorY + 0.002f;

        // 床（Exitの足元も床で塗る）
        Gizmos.color = previewFloor;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (cells[y, x] == CellType.Floor)
                {
                    bool isOuterF = outerRingFloor != null && outerRingFloor[y, x];
                    Gizmos.color = isOuterF ? previewOuterFloor : previewFloor;
                    DrawCellGizmo(new Vector2Int(x, y), y0);
                }
                else if (cells[y, x] == CellType.Exit)
                {
                    // Exit の足元も床色で塗る場合は以下コメントアウト解除
                    // bool isOuterF = outerRingFloor != null && outerRingFloor[y, x];
                    // Gizmos.color = isOuterF ? previewOuterFloor : previewFloor;
                    // DrawCellGizmo(new Vector2Int(x, y), y0);
                }
            }
        }
        // 壁
        Gizmos.color = previewWall;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (cells[y, x] == CellType.Wall)
                    DrawCellGizmo(new Vector2Int(x, y), y1);

        // アンカー（@ / CellType.Anchor）…黒で塗る
        Gizmos.color = previewAnchor; // ← ここで黒に
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (cells[y, x] == CellType.Anchor)
                    DrawCellGizmo(new Vector2Int(x, y), y1 /* or y1 + 0.0001f */);

        // 落とし穴（x / CellType.Pit）
        Gizmos.color = previewPit;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (cells[y, x] == CellType.Pit)
                    DrawCellGizmo(new Vector2Int(x, y), y1);


        // 出口（Core 内のみ記号評価。Core 外は cells で Exit 表示済みのため、重複を避ける）
        Gizmos.color = previewExit;
        if (autoGenerateOuterRings)
        {
            for (int y = 0; y < coreHeight; y++)
            {
                for (int x = 0; x < coreWidth; x++)
                {
                    if (levelOriginalCore[y][x] == 'E')
                    {
                        var wp = new Vector2Int(x + coreOffsetX, y + coreOffsetY);
                        DrawCellGizmo(wp, y2);
                    }
                }
            }
        }
        else
        {
            for (int y = 0; y < level.Length; y++)
                for (int x = 0; x < level[y].Length; x++)
                    if (level[y][x] == 'E')
                        DrawCellGizmo(new Vector2Int(x, y), y2);
        }

        // プレイヤー
        Gizmos.color = previewP;
        if (autoGenerateOuterRings)
        {
            for (int y = 0; y < coreHeight; y++)
            {
                for (int x = 0; x < coreWidth; x++)
                {
                    if (levelOriginalCore[y][x] == 'P')
                    {
                        var wp = new Vector2Int(x + coreOffsetX, y + coreOffsetY);
                        DrawActorDot(wp, y2 + 0.001f);
                    }
                }
            }
        }
        else
        {
            for (int y = 0; y < level.Length; y++)
                for (int x = 0; x < level[y].Length; x++)
                    if (level[y][x] == 'P')
                        DrawActorDot(new Vector2Int(x, y), y2 + 0.001f);
        }

        // Guard 記号マーカー
        if (autoGenerateOuterRings)
        {
            for (int y = 0; y < coreHeight; y++)
            {
                for (int x = 0; x < coreWidth; x++)
                {
                    char ch = levelOriginalCore[y][x];
                    for (int gi = 0; gi < guardTypes.Count; gi++)
                    {
                        if (!string.IsNullOrEmpty(guardTypes[gi].symbol) &&
                            guardTypes[gi].symbol[0] == ch)
                        {
                            Gizmos.color = guardTypes[gi].previewColor;
                            var wp = new Vector2Int(x + coreOffsetX, y + coreOffsetY);
                            DrawActorDot(wp, y2 + 0.001f);
                            break;
                        }
                    }
                }
            }
        }
        else
        {
            for (int y = 0; y < level.Length; y++)
            {
                for (int x = 0; x < level[y].Length; x++)
                {
                    char ch = level[y][x];
                    for (int gi = 0; gi < guardTypes.Count; gi++)
                    {
                        if (!string.IsNullOrEmpty(guardTypes[gi].symbol) &&
                            guardTypes[gi].symbol[0] == ch)
                        {
                            Gizmos.color = guardTypes[gi].previewColor;
                            DrawActorDot(new Vector2Int(x, y), y2 + 0.001f);
                            break;
                        }
                    }
                }
            }
        }

        // Item 記号マーカー
        if (autoGenerateOuterRings)
        {
            for (int y = 0; y < coreHeight; y++)
            {
                for (int x = 0; x < coreWidth; x++)
                {
                    char ch = levelOriginalCore[y][x];
                    for (int ii = 0; ii < itemTypes.Count; ii++)
                    {
                        if (!string.IsNullOrEmpty(itemTypes[ii].symbol) &&
                            itemTypes[ii].symbol[0] == ch)
                        {
                            Gizmos.color = itemTypes[ii].previewColor;
                            var wp = new Vector2Int(x + coreOffsetX, y + coreOffsetY);
                            DrawActorDot(wp, y2 + 0.001f);
                            break;
                        }
                    }
                }
            }
        }
        else
        {
            for (int y = 0; y < level.Length; y++)
            {
                for (int x = 0; x < level[y].Length; x++)
                {
                    char ch = level[y][x];
                    for (int ii = 0; ii < itemTypes.Count; ii++)
                    {
                        if (!string.IsNullOrEmpty(itemTypes[ii].symbol) &&
                            itemTypes[ii].symbol[0] == ch)
                        {
                            Gizmos.color = itemTypes[ii].previewColor;
                            DrawActorDot(new Vector2Int(x, y), y2 + 0.001f);
                            break;
                        }
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

    public RotatePreview GetPreview(Vector2Int center, int size, int dir)
    {
        var res = new RotatePreview { valid = false, area = new List<Vector2Int>() };
        int k = (size - 1) / 2; bool hasAnyIn = false;
        for (int dy = -k; dy <= k; dy++)
            for (int dx = -k; dx <= k; dx++)
            {
                var p = new Vector2Int(center.x + dx, center.y + dy);
                res.area.Add(p);
                if (InBounds(p))
                {
                    hasAnyIn = true;
                    if (IsRotateLockedCell(p)) return res; // Exit/@含むならNG
                }
            }
        if (!hasAnyIn) return res;

        bool safeCW = WouldBeSafePartial(center, size, +1) && !WouldPlayerOverlapGuard(center, size, +1);
        bool safeCCW = WouldBeSafePartial(center, size, -1) && !WouldPlayerOverlapGuard(center, size, -1);

        // どちらか片方でもOKなら緑
        res.valid = safeCW || safeCCW;
        return res;
    }

    // 回転後にプレイヤー/衛兵位置へWallが来ないか（部分回転対応）
    // BoardManager.cs 内のメソッド置換用
    // WouldBeSafePartial: 90度回転の安全判定（敵マス＋移動中の前1マスはWallのみNG）
    bool WouldBeSafePartial(Vector2Int center, int size, int dir)
    {
        int k = (size - 1) / 2;

        // 占有セルの収集
        var occ = new List<Vector2Int>();
        if (player != null) occ.Add(player.pos);
        if (guards != null)
        {
            for (int i = 0; i < guards.Count; i++)
            {
                var g = guards[i];
                if (g == null) continue;
                occ.Add(g.pos);
                if (g.IsMoving && g.NextPos != g.pos)
                    occ.Add(g.NextPos); // 移動中の前1マスも保護
            }
        }

        bool playerIn = IsPlayerInsideArea(center, size);

        foreach (var o in occ)
        {
            // 回転範囲外は無視
            if (o.x < center.x - k || o.x > center.x + k ||
                o.y < center.y - k || o.y > center.y + k) continue;

            // o の上に回転後に来るタイル
            int lx = o.x - (center.x - k);
            int ly = o.y - (center.y - k);

            int gdir = -dir; // 配列は逆回転で計算
            int sx, sy;
            if (gdir > 0) { sx = ly; sy = size - 1 - lx; } // 右回り
            else { sx = size - 1 - ly; sy = lx; }          // 左回り

            int gx = center.x - k + sx;
            int gy = center.y - k + sy;

            CellType after = InBounds(new Vector2Int(gx, gy)) ? cells[gy, gx] : cells[o.y, o.x];

            bool oIn = (o.x >= center.x - k && o.x <= center.x + k &&
                                    o.y >= center.y - k && o.y <= center.y + k);
            Vector2Int finalPos = o;
            if (oIn)
                finalPos = Rot90(o, center, dir);

            // 外周 Floor への移動は不可（プレイヤーもガードも）
            if (autoGenerateOuterRings && oIn && !IsInsideCore(finalPos))
                return false;

            if (player != null && o == player.pos)
            {
                if (!playerIn && (after == CellType.Wall || after == CellType.Pit))
                    return false;
            }
            else
            {
                if (after == CellType.Wall)
                    return false;
            }
        }
        return true;
    }
    //bool WouldBeSafePartial(Vector2Int center, int size, int dir)
    //{
    //    int k = (size - 1) / 2;
    //    var occ = new List<Vector2Int>();
    //    if (player != null) occ.Add(player.pos);
    //    foreach (var g in guards) occ.Add(g.pos);

    //    foreach (var o in occ)
    //    {
    //        if (o.x < center.x - k || o.x > center.x + k ||
    //            o.y < center.y - k || o.y > center.y + k) continue;

    //        int lx = o.x - (center.x - k);
    //        int ly = o.y - (center.y - k);

    //        int gdir = -dir; // 配列側は符号反転（見た目と逆）

    //        int sx, sy;
    //        if (gdir > 0) { sx = ly; sy = size - 1 - lx; } // 時計回り（配列）
    //        else { sx = size - 1 - ly; sy = lx; } // 反時計（配列）

    //        int gx = center.x - k + sx;
    //        int gy = center.y - k + sy;

    //        CellType after = InBounds(new Vector2Int(gx, gy)) ? cells[gy, gx] : cells[o.y, o.x];
    //        if (after == CellType.Wall) return false;
    //    }
    //    return true;
    //}

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
        // プレイヤーが範囲内/外に関係なく、回転後に敵と重なるなら禁止
        if (WouldPlayerOverlapGuard(center, size, dir)) { onDone?.Invoke(); return; }

        StartCoroutine(RotateCoro(center, size, dir, onDone));
    }

    // 成功時だけ回転を開始して onSuccess を呼ぶ。失敗時は false（コールバックは呼ばない）
    public bool TryRotateArea(Vector2Int center, int size, int dir, System.Action onSuccess)
    {
        if (IsAnimating) return false;
        if (AreaHasExit(center, size)) return false;
        if (!WouldBeSafePartial(center, size, dir)) return false;
        // プレイヤーが範囲内/外に関係なく、回転後に敵と重なるなら禁止
        if (WouldPlayerOverlapGuard(center, size, dir)) return false;

        StartCoroutine(RotateCoro(center, size, dir, onSuccess));
        return true;
    }

    // 成功可否を返す即時回転（成功時のみ実行して true）
    public bool RotateAreaInstantIfPossible(Vector2Int center, int size, int dir)
    {
        if (IsAnimating) return false;
        if (AreaHasExit(center, size)) return false;
        if (!WouldBeSafePartial(center, size, dir)) return false;
        if (WouldPlayerOverlapGuard(center, size, dir)) return false;
        // 以降は RotateAreaInstant と同等（onDone なし）
        IsAnimating = true;

        int k = (size - 1) / 2;

        // 1) セル内容の回転（配列）
        var newCells = new Dictionary<Vector2Int, CellType>();
        var newOrigins = new Dictionary<Vector2Int, WallOrigin>();
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                int gx = center.x + i - k;
                int gy = center.y + j - k;
                var dest = new Vector2Int(gx, gy);
                if (!InBounds(dest)) continue;

                int gdir = -dir; // 配列は逆回転で計算
                int sx, sy;
                if (gdir > 0) { sx = j; sy = size - 1 - i; }
                else { sx = size - 1 - j; sy = i; }

                int sgx = center.x - k + sx;
                int sgy = center.y - k + sy;

                CellType after = InBounds(new Vector2Int(sgx, sgy)) ? cells[sgy, sgx] : cells[gy, gx];
                newCells[dest] = after;
                if (after == CellType.Wall)
                {
                    if (InBounds(new Vector2Int(sgx, sgy)))
                        newOrigins[dest] = wallOrigin[sgy, sgx];
                    else
                        newOrigins[dest] = wallOrigin[gy, gx];
                }
            }
        }

        // 2) 既存タイル破棄
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                int gx = center.x + i - k;
                int gy = center.y + j - k;
                var gp = new Vector2Int(gx, gy);
                if (!InBounds(gp)) continue;
                var oldGo = tileGOs[gy, gx];
                if (oldGo) SafeDestroy(oldGo);
            }

        // 3) 新タイル生成
        foreach (var kv in newCells)
        {
            var p = kv.Key;
            cells[p.y, p.x] = kv.Value;
            if (kv.Value == CellType.Wall && newOrigins.TryGetValue(p, out var wo))
                wallOrigin[p.y, p.x] = wo;
            var go2 = Instantiate(
                (cells[p.y, p.x] == CellType.Wall) ? pfWall :
                (cells[p.y, p.x] == CellType.Exit) ? pfExit :
                (cells[p.y, p.x] == CellType.Anchor) ? pfAnchor :
                (cells[p.y, p.x] == CellType.Pit) ? pfPit : pfFloor,
                GridToWorld(p), Quaternion.identity, tilesRoot);
            go2.name = $"{cells[p.y, p.x]}_{p.x}_{p.y}";
            AutoAlign2DObject(go2, false);
            if (cells[p.y, p.x] == CellType.Exit)
            {
                var pos = go2.transform.position;
                pos.y = floorY + exitTopOffset;
                go2.transform.position = pos;
            }
            tileGOs[p.y, p.x] = go2;
        }
        // 外周 Floor 再計算 & 見た目更新
        RecomputeOuterRingFloor();
        // 見た目更新
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (IsOuterFloor(new Vector2Int(x, y)))
                {
                    var go = tileGOs[y, x];
                    if (go != null && outerRingFloorMat != null)
                    {
                        var rend = go.GetComponentInChildren<Renderer>();
                        if (rend) rend.sharedMaterial = outerRingFloorMat;
                        if (!go.name.StartsWith("OuterFloor_"))
                            go.name = $"OuterFloor_{x}_{y}";
                    }
                }
        // 4) アイテム位置更新
        if (itemAt != null && itemAt.Count > 0)
        {
            var moved = new List<(Vector2Int from, Vector2Int to, char sym, GameObject go)>();
            foreach (var kv in itemAt)
            {
                var p = kv.Key;
                if (p.x >= center.x - k && p.x <= center.x + k &&
                    p.y >= center.y - k && p.y <= center.y + k)
                {
                    var dest = Rot90(p, center, dir);
                    moved.Add((p, dest, kv.Value.sym, kv.Value.go));
                }
            }
            foreach (var m in moved) itemAt.Remove(m.from);
            foreach (var m in moved)
            {
                if (m.go)
                {
                    m.go.transform.position = GridToWorld(m.to);
                    AutoAlign2DObject(m.go, true, GetItemVisualScaleBySymbol(m.sym));
                }
                itemAt[m.to] = (m.sym, m.go);
            }
        }

        // 5) プレイヤー位置更新（範囲内のみ）
        if (player != null && IsPlayerInsideArea(center, size))
        {
            var newP = Rot90(player.pos, center, dir);
            player.pos = newP;
            player.transform.position = GridToWorldActor(newP);
        }

        // 6) 後処理
        ResolvePitfallsAfterRotation();
        UpdateAllWallAppearances();
        IsAnimating = false;
        RefreshAllGuardVision();
        return true;
    }

    Vector2Int Rot90(Vector2Int p, Vector2Int c, int dir)
    {
        // dir>0 = 時計回り, dir<0 = 反時計回り
        var d = p - c;
        return (dir > 0)
            ? new Vector2Int(c.x + d.y, c.y - d.x)
            : new Vector2Int(c.x - d.y, c.y + d.x);
    }
    IEnumerator RotateCoro(Vector2Int center, int size, int dir, System.Action onDone)
    {
        IsAnimating = true;
        var pivotGO = new GameObject($"RotatePivot_{center.x}_{center.y}");
        pivotGO.transform.position = GridToWorld(center) + new Vector3(0, 0.05f, 0);

        List<GameObject> targets = new();
        int k = (size - 1) / 2;

        var movedItems = new List<(Vector2Int from, Vector2Int to, char sym, GameObject go)>();
        // ★ プレイヤーが回転範囲内かチェック＆回転前の回転を保存
        bool playerIn = false;
        Quaternion savedPlayerRot = Quaternion.identity;
        Transform playerTf = null;
        Vector2Int newPlayerPos = default;

        if (player != null)
        {
            var p = player.pos;
            playerIn = IsPlayerInsideArea(center, size);
            if (playerIn)
            {
                playerTf = player.transform;
                savedPlayerRot = playerTf.rotation; // ←見た目の向きを保存
                                                    // 一緒に動かすため、ピボットにぶら下げる（位置はそのまま）
                playerTf.SetParent(pivotGO.transform, true);
                // 最終的な新座標は配列回転と同じ式で先に計算しておく
                newPlayerPos = Rot90(player.pos, center, dir);
            }
        }

        // 既存：タイルGOをピボット配下に
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
        if (itemAt != null && itemAt.Count > 0)
        {
            foreach (var kv in itemAt)
            {
                var p = kv.Key;
                if (p.x >= center.x - k && p.x <= center.x + k &&
                    p.y >= center.y - k && p.y <= center.y + k)
                {
                    var (sym, go) = kv.Value;
                    if (go) go.transform.SetParent(pivotGO.transform, true);
                    var dest = Rot90(p, center, dir);
                    movedItems.Add((p, dest, sym, go));
                }
            }
        }

        // 既存：回転アニメ（この回転が子＝プレイヤーにも掛かるが、後で向きを戻す）
        float t = 0f, dur = 0.15f;
        Quaternion from = pivotGO.transform.rotation;
        Quaternion to = Quaternion.AngleAxis(90f * dir, Vector3.up) * from;
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            pivotGO.transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0, 1, t));
            yield return null;
        }

        // 既存：セル内容の回転（dest←src の逆写映像で newCells を作る）
        var newCells = new Dictionary<Vector2Int, CellType>();
        var newOrigins = new Dictionary<Vector2Int, WallOrigin>();
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                int gx = center.x + i - k;
                int gy = center.y + j - k;
                var dest = new Vector2Int(gx, gy);
                if (!InBounds(dest)) continue;

                int gdir = -dir; // 配列側は符号反転（見た目と逆）
                int sx, sy;
                if (gdir > 0) { sx = j; sy = size - 1 - i; }
                else { sx = size - 1 - j; sy = i; }

                int sgx = center.x - k + sx;
                int sgy = center.y - k + sy;

                CellType after = InBounds(new Vector2Int(sgx, sgy)) ? cells[sgy, sgx] : cells[gy, gx];
                newCells[dest] = after;
                if (after == CellType.Wall)
                {
                    if (InBounds(new Vector2Int(sgx, sgy)))
                        newOrigins[dest] = wallOrigin[sgy, sgx];
                    else
                        newOrigins[dest] = wallOrigin[gy, gx];
                }
            }

        // 既存：古いタイル片付け
        foreach (var go in targets) SafeDestroy(go);
        // ★ プレイヤーとアイテムを親から戻す
        if (playerIn && playerTf) playerTf.SetParent(actorsRoot, true);
        foreach (var mi in movedItems) if (mi.go) mi.go.transform.SetParent(itemsRoot, true);
        SafeDestroy(pivotGO);

        // 既存：新タイル生成
        foreach (var kv in newCells)
        {
            var p = kv.Key;
            cells[p.y, p.x] = kv.Value;
            if (kv.Value == CellType.Wall && newOrigins.TryGetValue(p, out var wo))
                wallOrigin[p.y, p.x] = wo;
            var go2 = Instantiate(
                (cells[p.y, p.x] == CellType.Wall) ? pfWall :
                (cells[p.y, p.x] == CellType.Exit) ? pfExit :
                (cells[p.y, p.x] == CellType.Anchor) ? pfAnchor :
                (cells[p.y, p.x] == CellType.Pit) ? pfPit : pfFloor,
                GridToWorld(p), Quaternion.identity, tilesRoot);
            go2.name = $"{cells[p.y, p.x]}_{p.x}_{p.y}";
            AutoAlign2DObject(go2, false);
            if (cells[p.y, p.x] == CellType.Exit)
            {
                var pos = go2.transform.position;
                pos.y = floorY + exitTopOffset;
                go2.transform.position = pos;
            }
            tileGOs[p.y, p.x] = go2;
        }
        // 外周 Floor 再計算 & 見た目更新
        RecomputeOuterRingFloor();
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (IsOuterFloor(new Vector2Int(x, y)))
                {
                    var go = tileGOs[y, x];
                    if (go != null && outerRingFloorMat != null)
                    {
                        var rend = go.GetComponentInChildren<Renderer>();
                        if (rend) rend.sharedMaterial = outerRingFloorMat;
                        if (!go.name.StartsWith("OuterFloor_"))
                            go.name = $"OuterFloor_{x}_{y}";
                    }
                }
        // ★ アイテムの辞書＆位置を更新
        if (movedItems.Count > 0)
        {
            foreach (var mi in movedItems) itemAt.Remove(mi.from);
            foreach (var mi in movedItems)
            {
                if (mi.go)
                {
                    mi.go.transform.position = GridToWorld(mi.to);
                    AutoAlign2DObject(mi.go, true, GetItemVisualScaleBySymbol(mi.sym));
                }
                itemAt[mi.to] = (mi.sym, mi.go); // 値が GameObject のみなら: itemAt[mi.to] = mi.go;
            }
        }
        // ★ プレイヤーのグリッド座標とワールド位置を更新（向きはそのまま）
        if (playerIn)
        {
            playerTf.rotation = savedPlayerRot;
            player.pos = newPlayerPos;
            player.transform.position = GridToWorldActor(newPlayerPos);
        }

        // ★ 回転後に落とし穴にいるガードを排除（アニメ版にも適用）
        ResolvePitfallsAfterRotation();

        UpdateAllWallAppearances();
        IsAnimating = false;
        RefreshAllGuardVision();
        onDone?.Invoke();
    }

    public void SetLevelFromText(string text, bool rebuild = true)
    {
        level = ParseRows(text);
        level = NormalizeRows(level, pad: '.');
        if (rebuild) Build();
    }
    static string[] NormalizeRows(string[] rows, char pad = '.')
    {
        if (rows == null || rows.Length == 0) return new string[0];
        int w = 0;
        for (int i = 0; i < rows.Length; i++)
            w = Mathf.Max(w, rows[i].Length);

        var outRows = new string[rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            var s = rows[y];
            if (s.Length == w) { outRows[y] = s; continue; }
            // 足りないぶんを床('.')
            if (s.Length < w) outRows[y] = s + new string(pad, w - s.Length);
            else outRows[y] = s.Substring(0, w); // 長すぎる場合は右端をカット
        }
        return outRows;
    }
    // 改行コードや空行トリムに強い行分割
    public static string[] ParseRows(string text)
    {
        if (string.IsNullOrEmpty(text)) return new string[0];
        text = text.Replace("\r", "");
        var lines = text.Split('\n');
        var rows = new List<string>(lines.Length);

        foreach (var raw in lines)
        {
            var s = raw.TrimEnd();            // 末尾スペース除去（行長の不揃い対策）
            if (s.Length == 0) continue;      // 空行はスキップ（お好みで残してもOK）
            rows.Add(s);
        }
        return rows.ToArray();
    }

    // =========== LoS（角抜け防止のsupercover版） ===========
    public bool HasLineOfSight(Vector2Int from, Vector2Int to)
    {
        // 同一セルは常に見える扱い
        if (from == to) return true;

        // 簡易ゲート: from→to の主軸方向に1マス進んだセルが遮蔽なら視界を即カット
        int dx0 = to.x - from.x;
        int dy0 = to.y - from.y;
        Vector2Int primaryDir;
        if (Mathf.Abs(dx0) >= Mathf.Abs(dy0))
            primaryDir = new Vector2Int(System.Math.Sign(dx0), 0); // ← ここを修正
        else
            primaryDir = new Vector2Int(0, System.Math.Sign(dy0)); // ← ここを修正

        if (primaryDir != Vector2Int.zero)
        {
            var gate = from + primaryDir;
            if (BlocksVision(gate)) return false;
        }

        // 以降は従来の supercover Bresenham（角抜け防止）
        int x0 = from.x, y0 = from.y, x1 = to.x, y1 = to.y;
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        int prevX = x0, prevY = y0;

        while (true)
        {
            if (!(x0 == from.x && y0 == from.y))
            {
                if (BlocksVision(new Vector2Int(x0, y0))) return false;

                if (x0 != prevX && y0 != prevY)
                {
                    var sideA = new Vector2Int(prevX + sx, prevY);
                    var sideB = new Vector2Int(prevX, prevY + sy);
                    if (BlocksVision(sideA) && BlocksVision(sideB)) return false;
                }
            }

            if (x0 == x1 && y0 == y1) break;

            int e2 = 2 * err;
            prevX = x0; prevY = y0;

            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }

        return true;
    }
    // そのセルにガードがいる？
    bool IsGuardAt(Vector2Int p)
    {
        if (guards == null) return false;
        for (int i = 0; i < guards.Count; i++)
            if (guards[i] != null && guards[i].pos == p) return true;
        return false;
    }

    // エリア内にプレイヤーが含まれる？
    bool IsPlayerInsideArea(Vector2Int center, int size)
    {
        if (player == null) return false;
        if (!rotatePlayerWithArea) return false; // ←フラグがfalseなら常に含めない
        int k = (size - 1) / 2;
        return (player.pos.x >= center.x - k && player.pos.x <= center.x + k &&
                player.pos.y >= center.y - k && player.pos.y <= center.y + k);
    }

    // 指定方向に回したとき、プレイヤーの新座標がガードに重なる？（現在/次位置の両方を禁止）
    public bool WouldPlayerOverlapGuard(Vector2Int center, int size, int dir)
    {
        if (player == null) return false;

        int k = (size - 1) / 2;
        bool playerIn = IsPlayerInsideArea(center, size);

        // プレイヤーの最終位置（範囲内なら回転に追従、範囲外なら据え置き）
        Vector2Int nextP = playerIn ? Rot90(player.pos, center, dir) : player.pos;

        for (int i = 0; i < guards.Count; i++)
        {
            var g = guards[i];
            if (g == null) continue;

            // 敵の現在位置 or 移動先と衝突するならNG
            if (g.pos == nextP) return true;
            if (g.IsMoving && g.NextPos == nextP) return true;
        }
        return false;
    }

    // =========== 視界可視化の一括制御 ===========
    public void RefreshAllGuardVision()
    {
#if UNITY_EDITOR
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
#if UNITY_EDITOR
    [Header("DEV / Editor")]
    public bool devUseSceneLevelInEditor = true;     // ← 追加：再生時にScene内の盤面をそのまま使う
    public TextAsset devTargetTextAsset;             // ← （任意）保存先

    [ContextMenu("DEV: Save current level into devTargetTextAsset")]
    public void DevSaveLevelToTextAsset()
    {
        if (devTargetTextAsset == null) { Debug.LogWarning("devTargetTextAsset not assigned."); return; }
        var path = UnityEditor.AssetDatabase.GetAssetPath(devTargetTextAsset);
        System.IO.File.WriteAllText(path, string.Join("\n", level));
        UnityEditor.AssetDatabase.ImportAsset(path);
        Debug.Log($"Saved level to {path}");
    }
#endif
    [Header("Movement (Smooth Toggle & Speeds)")]
    [Tooltip("プレイヤー移動を補間（スムーズ）にする")]
    public bool smoothPlayerMove = true;
    [Tooltip("ガード移動を補間（スムーズ）にする")]
    public bool smoothGuardMove = true;
    [Tooltip("プレイヤーの移動速度（セル/秒）")]
    [Min(0.1f)] public float playerMoveCellsPerSec = 6f;
    [Tooltip("ガードの移動速度（セル/秒）")]
    [Min(0.1f)] public float guardMoveCellsPerSec = 4f;
    [Tooltip("ガードの回転速度（度/秒） … 視線/向きの補間速度")]
    [Min(30f)] public float guardRotateDegPerSec = 1080f; // ← 360 → 1080 に引き上げ
    [Tooltip("向き変更（移動/監視）時に即スナップする（補間をスキップ）")]
    public bool snapGuardFacingOnMove = true;             // ← 追加
    [Tooltip("ガード視界の見た目更新間隔（秒）。小さいほど滑らかだが負荷が上がる")]
    [Range(0.01f, 0.2f)] public float guardVisionUpdateInterval = 0.05f;

    public void SaveDevModeSettings()
    {
        PlayerPrefs.SetInt("rotatePlayerWithArea", rotatePlayerWithArea ? 1 : 0);

        // ▼ 追加: 自由回転関連の永続化
        PlayerPrefs.SetInt("devEnableFreeRotate", devEnableFreeRotate ? 1 : 0);
        PlayerPrefs.SetInt("devAllow180Rotation", devAllow180Rotation ? 1 : 0);
        PlayerPrefs.SetFloat("devSnapAngleDeg", devSnapAngleDeg);
        PlayerPrefs.SetFloat("devCommitAngleDeg", devCommitAngleDeg);
        PlayerPrefs.SetFloat("devStickiness", devStickiness);
        PlayerPrefs.SetFloat("devNgGhostSeconds", devNgGhostSeconds);

        PlayerPrefs.Save();
    }
    public void LoadDevModeSettings()
    {
        rotatePlayerWithArea = PlayerPrefs.GetInt("rotatePlayerWithArea", 1) == 1;

        // ▼ 追加: 自由回転関連の永続化
        devEnableFreeRotate = PlayerPrefs.GetInt("devEnableFreeRotate", 1) == 1;
        devAllow180Rotation = PlayerPrefs.GetInt("devAllow180Rotation", 0) == 1;
        devSnapAngleDeg = PlayerPrefs.GetFloat("devSnapAngleDeg", 15f);
        devCommitAngleDeg = PlayerPrefs.GetFloat("devCommitAngleDeg", 15f);
        devStickiness = PlayerPrefs.GetFloat("devStickiness", 0.5f);
        devNgGhostSeconds = PlayerPrefs.GetFloat("devNgGhostSeconds", 0.5f);
    }

    // ========= 可否ヘルパ =========
    public bool IsCenterWithinLimit(Vector2Int center)
    {
        if (player == null) return false;
        int dx = Mathf.Abs(center.x - player.pos.x);
        int dy = Mathf.Abs(center.y - player.pos.y);
        int chebyshev = Mathf.Max(dx, dy); // 正方形エリア向け
        return chebyshev <= rotationCenterMaxDistance;
    }
    public bool AreaContainsLocked(Vector2Int center, int size)
    {
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!InBounds(p)) continue;
                if (IsRotateLockedCell(p)) return true;
            }
        return false;
    }
    // ==== ADD STEP1: 回転エリアが Core 外（外周帯）を含むか（Anchor 帯含む外周全部） ====
    bool AreaCrossesOuterRing(Vector2Int center, int size)
    {
        if (!autoGenerateOuterRings) return false;
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
        {
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!InBounds(p)) continue;
                if (!IsInsideCore(p)) return true; // Core から外れたセルを含む
            }
        }
        return false;
    }
    public struct StepValidity
    {
        public bool cw90, ccw90, cw180, ccw180;
        public bool Any(bool allow180) => cw90 || ccw90 || (allow180 && (cw180 || ccw180));
    }

    public StepValidity GetStepValidity(Vector2Int center, int size)
    {
        var v = new StepValidity();

        // ±90°
        v.cw90 = WouldBeSafePartial(center, size, +1) && !WouldPlayerOverlapGuard(center, size, +1);
        v.ccw90 = WouldBeSafePartial(center, size, -1) && !WouldPlayerOverlapGuard(center, size, -1);

        // ±180°（許可時のみ評価）
        if (devAllow180Rotation)
        {
            v.cw180 = WouldBeSafePartial180(center, size) && !WouldPlayerOverlapGuard180(center, size);
            v.ccw180 = v.cw180; // 180°は向きに依らず同一
        }
        else
        {
            v.cw180 = v.ccw180 = false;
        }
        return v;
    }

    // 180°の安全判定（壁衝突）
    // BoardManager.cs 内のメソッド置換用
    // WouldBeSafePartial180: 180度回転の安全判定（敵マス＋移動中の前1マスも保護、Wall/PitでNG）
    bool WouldBeSafePartial180(Vector2Int center, int size)
    {
        int k = (size - 1) / 2;

        // 占有セル（プレイヤー、全ガード、移動中ガードのNextPos）
        var occ = new List<Vector2Int>();
        if (player != null) occ.Add(player.pos);
        if (guards != null)
        {
            for (int i = 0; i < guards.Count; i++)
            {
                var g = guards[i];
                if (g == null) continue;
                occ.Add(g.pos);
                if (g.IsMoving && g.NextPos != g.pos)
                    occ.Add(g.NextPos);
            }
        }

        bool playerIn = IsPlayerInsideArea(center, size);

        foreach (var o in occ)
        {
            if (o.x < center.x - k || o.x > center.x + k ||
                o.y < center.y - k || o.y > center.y + k) continue;

            int lx = o.x - (center.x - k);
            int ly = o.y - (center.y - k);

            // 180度の逆回転
            int sx = size - 1 - lx;
            int sy = size - 1 - ly;

            int gx = center.x - k + sx;
            int gy = center.y - k + sy;

            CellType after = InBounds(new Vector2Int(gx, gy)) ? cells[gy, gx] : cells[o.y, o.x];

            bool oIn = (o.x >= center.x - k && o.x <= center.x + k &&
                                    o.y >= center.y - k && o.y <= center.y + k);
            Vector2Int finalPos = o;
            if (oIn)
                finalPos = Rot180(o, center);

            if (autoGenerateOuterRings && oIn && !IsInsideCore(finalPos))
                return false;

            if (player != null && o == player.pos)
            {
                if (!playerIn && (after == CellType.Wall || after == CellType.Pit))
                    return false;
            }
            else
            {
                if (after == CellType.Wall)
                    return false;
            }
        }
        return true;
    }
    // 180°でプレイヤーがガードと重なるか
    public bool WouldPlayerOverlapGuard180(Vector2Int center, int size)
    {
        if (player == null) return false;

        bool playerIn = IsPlayerInsideArea(center, size);
        Vector2Int nextP = playerIn ? Rot180(player.pos, center) : player.pos;

        for (int i = 0; i < guards.Count; i++)
        {
            var g = guards[i];
            if (g == null) continue;
            if (g.pos == nextP) return true;
            if (g.IsMoving && g.NextPos == nextP) return true;
        }
        return false;
    }

    Vector2Int Rot180(Vector2Int p, Vector2Int c)
    {
        // p' = 2c - p
        return new Vector2Int(2 * c.x - p.x, 2 * c.y - p.y);
    }

    // ========= 実プレビュー（親子付け）API =========
    GameObject freePreviewPivot;
    Transform freePreviewGroup;   // Pivot直下のグループ
    List<Transform> freeTiles = new();
    List<Transform> freeItems = new();
    Transform freePlayerTf;
    Quaternion freeSavedPlayerRot = Quaternion.identity;
    bool freePlayerIn = false;
    Vector2Int freeCenter;
    int freeSize;
    struct PreviewWallEntry
    {
        public Transform tf;
        public WallOrigin origin;
        public Vector2Int originalGrid;
    }
    List<PreviewWallEntry> previewWalls = new();
    public bool AreaContainsLockedExceptCenter(Vector2Int center, int size)
    {
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
        {
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!InBounds(p)) continue;
                if (p == center) continue; // 中心は例外
                if (IsRotateLockedCell(p)) return true;
            }
        }
        return false;
    }
    public bool BeginFreePreview(Vector2Int center, int size)
    {
        if (freePreviewPivot != null) RestoreFreePreview(); // 保険
        freeCenter = center;
        freeSize = size;

        // Pivot はセル中心に置く
        freePreviewPivot = new GameObject($"FreePreviewPivot_{center.x}_{center.y}");
        freePreviewPivot.transform.position = CellCenter(center, floorY + 0.05f);

        // Group は Pivot の子（対象は全て Group にぶら下げる）
        freePreviewGroup = new GameObject("FreePreviewGroup").transform;
        freePreviewGroup.SetParent(freePreviewPivot.transform, false);
        freePreviewGroup.localPosition = Vector3.zero;
        freePreviewGroup.localRotation = Quaternion.identity;
        freePreviewGroup.localScale = Vector3.one;

        int k = (size - 1) / 2;

        // タイル
        freeTiles.Clear();
        previewWalls.Clear();
        for (int j = -k; j <= k; j++)
        {
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!InBounds(p)) continue;
                var go = tileGOs[p.y, p.x];
                if (!go) continue;
                go.transform.SetParent(freePreviewGroup, true);
                freeTiles.Add(go.transform);
                if (cells[p.y, p.x] == CellType.Wall)
                    previewWalls.Add(new PreviewWallEntry
                    {
                        tf = go.transform,
                        origin = autoGenerateOuterRings ? wallOrigin[p.y, p.x] : WallOrigin.Core,
                        originalGrid = p
                    });
            }
        }

        // アイテム
        freeItems.Clear();
        if (itemAt != null && itemAt.Count > 0)
        {
            foreach (var kv in itemAt)
            {
                var p = kv.Key;
                if (p.x >= center.x - k && p.x <= center.x + k &&
                    p.y >= center.y - k && p.y <= center.y + k)
                {
                    var go = kv.Value.go;
                    if (go)
                    {
                        go.transform.SetParent(freePreviewGroup, true);
                        freeItems.Add(go.transform);
                    }
                }
            }
        }

        // プレイヤー（設定＆範囲内）
        freePlayerIn = IsPlayerInsideArea(center, size);
        if (player != null && rotatePlayerWithArea && freePlayerIn)
        {
            freePlayerTf = player.transform;
            freeSavedPlayerRot = freePlayerTf.rotation;
            freePlayerTf.SetParent(freePreviewGroup, true);
        }
        else
        {
            freePlayerTf = null;
        }

        return true;
    }
    // ==== STEP11 REPLACE: プレビュー中の壁マテリアル一時反映 ====
    public void UpdateFreePreviewAngle(float angleDeg)
    {
        if (freePreviewPivot == null) return;
        freePreviewPivot.transform.rotation = Quaternion.Euler(0f, angleDeg, 0f);

        if (!autoGenerateOuterRings) return;
        if (previewWalls.Count == 0) return;
        if (wallNormalMat == null || wallOuterMat == null) return;

        float rad = angleDeg * Mathf.Deg2Rad;
        float sin = Mathf.Sin(rad);
        float cos = Mathf.Cos(rad);

        foreach (var w in previewWalls)
        {
            // 元位置差分
            var d = w.originalGrid - freeCenter;
            float rx = d.x * cos - d.y * sin;
            float ry = d.x * sin + d.y * cos;
            var proj = new Vector2Int(Mathf.RoundToInt(rx) + freeCenter.x,
                                      Mathf.RoundToInt(ry) + freeCenter.y);
            bool outside = !IsInsideCore(proj);

            var rend = w.tf.GetComponentInChildren<Renderer>();
            if (!rend) continue;

            if (w.origin == WallOrigin.Outer && outside)
                rend.sharedMaterial = wallOuterMat;
            else
                rend.sharedMaterial = wallNormalMat;
        }
    }
    public void RestoreFreePreview()
    {
        if (freePreviewPivot == null) return;

        // 回転を戻してから親戻し
        freePreviewPivot.transform.rotation = Quaternion.identity;
        if (freePreviewGroup != null) freePreviewGroup.localRotation = Quaternion.identity;

        // タイル戻し
        for (int i = 0; i < freeTiles.Count; i++)
        {
            var t = freeTiles[i];
            if (t) t.SetParent(tilesRoot, true);
        }
        freeTiles.Clear();

        // アイテム戻し
        for (int i = 0; i < freeItems.Count; i++)
        {
            var t = freeItems[i];
            if (t) t.SetParent(itemsRoot, true);
        }
        freeItems.Clear();

        // プレイヤー戻し
        if (freePlayerTf)
        {
            freePlayerTf.SetParent(actorsRoot, true);
            freePlayerTf.rotation = freeSavedPlayerRot;
        }
        freePlayerTf = null;
        freePlayerIn = false;

        // 生成物破棄
        if (freePreviewGroup != null)
        {
            SafeDestroy(freePreviewGroup.gameObject);
            freePreviewGroup = null;
        }
        SafeDestroy(freePreviewPivot);
        freePreviewPivot = null;
        previewWalls.Clear();
        UpdateAllWallAppearances();
    }

    void ResolvePitfallsAfterRotation()
    {
        if (guards == null || guards.Count == 0) return;
        for (int i = guards.Count - 1; i >= 0; i--)
        {
            var g = guards[i];
            if (g == null) { guards.RemoveAt(i); continue; }
            var p = g.pos;
            if (InBounds(p) && cells[p.y, p.x] == CellType.Pit)
            {
                SafeDestroy(g.gameObject);
                guards.RemoveAt(i);
            }
        }
    }

    // Ghost を親付けするための Transform を返す（Group を優先）
    public Transform GetFreePreviewPivot()
    {
        return freePreviewGroup != null ? freePreviewGroup : (freePreviewPivot != null ? freePreviewPivot.transform : null);
    }
    public bool IsFreePreviewActive => freePreviewPivot != null;

    Vector2 GetItemVisualScaleBySymbol(char sym)
    {
        // 泥棒だけ1セルサイズ、その他は従来の小さめ表示
        if (sym == 'd') return new Vector2(0.2f, 0.2f);
        if (sym == 'e') return new Vector2(0.25f, 0.25f);
        return new Vector2(0.07f, 0.07f);
    }

}
