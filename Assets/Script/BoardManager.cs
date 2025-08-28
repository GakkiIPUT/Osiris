using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CellType { Floor, Wall, Exit, Anchor }

[ExecuteAlways] // エディタでもプレビュー用に動かす
public class BoardManager : MonoBehaviour
{
    [Header("Prefabs (Tiles & Player)")]
    public GameObject pfFloor;
    public GameObject pfWall;
    public GameObject pfExit;
    public GameObject pfAnchor;   // 回転不可マス（@）
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
        // i = 鍵（必須アイテム）
        new ItemType{ symbol="i", prefab=null, label="Key", previewColor = new Color(0.25f,1f,0.9f,1f) },
        // t = 宝箱（コレクション用・クリア条件に含めない）
        new ItemType{ symbol="t", prefab=null, label="Treasure", previewColor = new Color(1.0f,0.7f,0.2f,1f) },
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
    private GameObject[,] tileGOs;  // 1マス=1オブジェクト（Floor/Wall/Exit/Anchor）

    // ルート
    public Transform tilesRoot;
    public Transform actorsRoot;

    [HideInInspector] public PlayerController player;
    [HideInInspector] public List<GuardController> guards = new();

    // ★ マップ上のアイテム：位置 → (記号, 実体)
    public Dictionary<Vector2Int, (char sym, GameObject go)> itemAt = new();

    public bool IsAnimating { get; private set; }


    [Header("DEV / Rotation")]
    public bool rotatePlayerWithArea = true; // 開発者モードで切り替え

    // ▼ 追加: 自由回転（ドラッグ）DEV設定
    [Header("DEV / Free Rotate")]
    [Tooltip("ドラッグ式の自由回転を有効にする（ON時でも T はキャンセル専用）")]
    public bool devEnableFreeRotate = true;
    [Tooltip("180°回転を許可（許可時は2AP想定）")]
    public bool devAllow180Rotation = false;
    [Tooltip("スナップ角度（最近傍吸着の目安、将来用）")]
    [Range(1f, 45f)] public float devSnapAngleDeg = 15f;
    [Tooltip("コミット許容角（将来用）。この角以内なら確定吸着する想定")]
    [Range(1f, 45f)] public float devCommitAngleDeg = 15f;
    [Tooltip("スティッキネス（将来用）")]
    [Range(0f, 1f)] public float devStickiness = 0.5f;
    [Tooltip("クリック直後の回転不可フラッシュ（赤Ghost）秒数")]
    [Range(0.1f, 2.0f)] public float devNgGhostSeconds = 0.5f;
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

        var c = cells[p.y, p.x];
        // 床 or 出口は歩行可。壁/アンカーは不可。
        bool tileOK = (c == CellType.Floor || c == CellType.Exit);
        if (!tileOK) return false;

        if (blockGuardsForPlayer && IsOccupiedByGuard(p)) return false;

        return true;
    }
    public bool IsWalkable(Vector2Int p)
    {
        if (!InBounds(p)) return false;
        var c = cells[p.y, p.x];
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

                // ItemType の記号も床扱い（実生成はBuildで）
                bool isItemSymbol = false;
                for (int ii = 0; ii < itemTypes.Count; ii++)
                {
                    if (!string.IsNullOrEmpty(itemTypes[ii].symbol) && itemTypes[ii].symbol[0] == ch) { isItemSymbol = true; break; }
                }

                if (isGuardSymbol || isItemSymbol)
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
                        case '@': cells[y, x] = CellType.Anchor; break; // 回転不可マス
                        default: cells[y, x] = CellType.Floor; break;
                    }
                }
            }
        }
    }

    bool IsRotateLockedCell(Vector2Int p)
    {
        if (!InBounds(p)) return false;
        var c = cells[p.y, p.x];
        return (c == CellType.Exit || c == CellType.Anchor);
    }

    void PlaceTile(CellType t, Vector2Int p)
    {
        GameObject prefab = (t == CellType.Wall) ? pfWall :
                            (t == CellType.Exit) ? pfExit :
                            (t == CellType.Anchor) ? pfAnchor : pfFloor;
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

        // ★ 上部UI用：このマップに存在する「必須アイテム（=鍵 'i'）」を左→右→次行…で列挙
        var requiredSymbols = new List<char>();

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
                // アイテム（記号→ItemType解決）＋ 必須（= 'i'）のみ並び順に記録
                for (int ii = 0; ii < itemTypes.Count; ii++)
                {
                    if (!string.IsNullOrEmpty(itemTypes[ii].symbol) && itemTypes[ii].symbol[0] == ch)
                    {
                        itemSpawns.Add((p, itemTypes[ii]));
                        if (itemTypes[ii].symbol[0] == 'i') // ★ 鍵のみ必須扱い
                            requiredSymbols.Add('i');
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
            AutoAlign2DObject(go, true /*見た目少し浮かす*/, new Vector2(0.07f, 0.07f)); // 見た目縮小は好みで

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
    }

    void ConfigureGuardFromSymbol(GuardController g, char sym)
    {
        if (g == null) return;

        // まずデフォルトを明示的に（フォールバック無効化のため）
        g.patrolMode = GuardController.PatrolMode.PingPong;
        g.patternIsRelative = true;
        g.pattern = ""; // 記号に応じて必ず上書き

        switch (sym)
        {
            case 'G': // R5
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

            case 'K': // 四角巡回 R3,U3,L3,D3
                g.patrolMode = GuardController.PatrolMode.Loop;
                g.pattern = "R3,U3,L3,D3";
                break;

            case 'L': // 小さめ巡回（2マス）※四角にしたい場合はD2も足します
                g.patrolMode = GuardController.PatrolMode.Loop;
                g.pattern = "R2,U2,L2,D2"; // ←三角にしたいなら ",D2" を外す
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

        // アンカー（@ / CellType.Anchor）…黒で塗る
        Gizmos.color = previewAnchor; // ← ここで黒に
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (cells[y, x] == CellType.Anchor)
                    DrawCellGizmo(new Vector2Int(x, y), y1 /* or y1 + 0.0001f */);
        
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

        // 各 Item 記号のマーカー（四角ドット）
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
    bool WouldBeSafePartial(Vector2Int center, int size, int dir)
    {
        int k = (size - 1) / 2;
        var occ = new List<Vector2Int>();
        if (player != null) occ.Add(player.pos);
        foreach (var g in guards) occ.Add(g.pos);

        bool playerIn = IsPlayerInsideArea(center, size);

        foreach (var o in occ)
        {
            if (o.x < center.x - k || o.x > center.x + k ||
                o.y < center.y - k || o.y > center.y + k) continue;

            int lx = o.x - (center.x - k);
            int ly = o.y - (center.y - k);

            int gdir = -dir; // 配列側は符号反転（見た目と逆）

            int sx, sy;
            if (gdir > 0) { sx = ly; sy = size - 1 - lx; } // 時計回り（配列）
            else { sx = size - 1 - ly; sy = lx; } // 反時計（配列）

            int gx = center.x - k + sx;
            int gy = center.y - k + sy;

            CellType after = InBounds(new Vector2Int(gx, gy)) ? cells[gy, gx] : cells[o.y, o.x];

            // プレイヤーが範囲外の場合のみ、壁と重なるのを禁止
            if (!playerIn && player != null && o == player.pos && after == CellType.Wall)
                return false;

            // ガードは常に壁と重なるのを禁止
            if (o != player.pos && after == CellType.Wall)
                return false;
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

    public void RotateAreaInstant(Vector2Int center, int size, int dir)
    {
        // 同一フレームで即時反映（ガードの歩行等をブロックするため短時間だけON）
        IsAnimating = true;

        // 事前NGチェックはRotateAreaと同等
        if (AreaHasExit(center, size)) { IsAnimating = false; return; }
        if (!WouldBeSafePartial(center, size, dir)) { IsAnimating = false; return; }
        if (WouldPlayerOverlapGuard(center, size, dir)) { IsAnimating = false; return; }

        int k = (size - 1) / 2;

        // 1) セル内容の回転（配列）
        var newCells = new Dictionary<Vector2Int, CellType>();
        for (int j = 0; j < size; j++)
        {
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
            }
        }

        // 2) 既存タイル破棄
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                int gx = center.x + i - k;
                int gy = center.y + j - k;
                var gp = new Vector2Int(gx, gy);
                if (!InBounds(gp)) continue;
                var oldGo = tileGOs[gy, gx];
                if (oldGo) SafeDestroy(oldGo);
            }
        }

        // 3) 新タイル生成
        foreach (var kv in newCells)
        {
            var p = kv.Key;
            cells[p.y, p.x] = kv.Value;
            var go2 = Instantiate(
                (cells[p.y, p.x] == CellType.Wall) ? pfWall :
                (cells[p.y, p.x] == CellType.Exit) ? pfExit :
                (cells[p.y, p.x] == CellType.Anchor) ? pfAnchor : pfFloor,
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

        // 4) アイテムの更新（位置と辞書）
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
                    AutoAlign2DObject(m.go, true, new Vector2(0.07f, 0.07f));
                }
                itemAt[m.to] = (m.sym, m.go);
            }
        }

        // 5) プレイヤー位置更新（設定と範囲に応じて）
        if (player != null)
        {
            bool playerIn = IsPlayerInsideArea(center, size);
            if (playerIn)
            {
                var newP = Rot90(player.pos, center, dir);
                player.pos = newP;
                player.transform.position = GridToWorldActor(newP);
            }
        }

        IsAnimating = false;
        RefreshAllGuardVision();
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
                    var (sym, go) = kv.Value; // 値が GameObject のみなら: var go = kv.Value;
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
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                int gx = center.x + i - k;
                int gy = center.y + j - k;
                var dest = new Vector2Int(gx, gy);
                if (!InBounds(dest)) continue;

                int gdir = -dir; // 配列側は符号反転
                int sx, sy;
                if (gdir > 0) { sx = j; sy = size - 1 - i; }
                else { sx = size - 1 - j; sy = i; }

                int sgx = center.x - k + sx;
                int sgy = center.y - k + sy;

                CellType after = InBounds(new Vector2Int(sgx, sgy)) ? cells[sgy, sgx] : cells[gy, gx];
                newCells[dest] = after;
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
            var go2 = Instantiate(
                (cells[p.y, p.x] == CellType.Wall) ? pfWall :
                (cells[p.y, p.x] == CellType.Exit) ? pfExit : pfFloor,
                GridToWorld(p), Quaternion.identity, tilesRoot);
            go2.name = $"{cells[p.y, p.x]}_{p.x}_{p.y}";
            AutoAlign2DObject(go2, false);
            tileGOs[p.y, p.x] = go2;
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
                    AutoAlign2DObject(mi.go, true, new Vector2(0.07f, 0.07f)); // 見た目調整はお好み
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
    bool WouldBeSafePartial180(Vector2Int center, int size)
    {
        int k = (size - 1) / 2;
        var occ = new List<Vector2Int>();
        if (player != null) occ.Add(player.pos);
        foreach (var g in guards) occ.Add(g.pos);

        bool playerIn = IsPlayerInsideArea(center, size);

        foreach (var o in occ)
        {
            if (o.x < center.x - k || o.x > center.x + k ||
                o.y < center.y - k || o.y > center.y + k) continue;

            int lx = o.x - (center.x - k);
            int ly = o.y - (center.y - k);

            // 180°の配列回転: (sx,sy) = (size-1-lx, size-1-ly)
            int sx = size - 1 - lx;
            int sy = size - 1 - ly;

            int gx = center.x - k + sx;
            int gy = center.y - k + sy;

            CellType after = InBounds(new Vector2Int(gx, gy)) ? cells[gy, gx] : cells[o.y, o.x];

            // プレイヤーが範囲外の場合のみ、壁と重なるのを禁止
            if (!playerIn && player != null && o == player.pos && after == CellType.Wall)
                return false;

            // ガードは常に壁と重なるのを禁止
            if (o != player.pos && after == CellType.Wall)
                return false;
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
    List<Transform> freeTiles = new();
    List<Transform> freeItems = new();
    Transform freePlayerTf;
    Quaternion freeSavedPlayerRot = Quaternion.identity;
    bool freePlayerIn = false;
    Vector2Int freeCenter;
    int freeSize;

    public bool BeginFreePreview(Vector2Int center, int size)
    {
        if (freePreviewPivot != null) RestoreFreePreview(); // 保険
        freeCenter = center;
        freeSize = size;

        freePreviewPivot = new GameObject($"FreePreviewPivot_{center.x}_{center.y}");
        freePreviewPivot.transform.position = GridToWorld(center) + new Vector3(0, 0.05f, 0);

        int k = (size - 1) / 2;

        // タイルをぶら下げ
        for (int j = -k; j <= k; j++)
        {
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!InBounds(p)) continue;
                var go = tileGOs[p.y, p.x];
                if (!go) continue;
                go.transform.SetParent(freePreviewPivot.transform, true);
                freeTiles.Add(go.transform);
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
                        go.transform.SetParent(freePreviewPivot.transform, true);
                        freeItems.Add(go.transform);
                    }
                }
            }
        }

        // プレイヤー（含める設定＆範囲内なら）
        freePlayerIn = IsPlayerInsideArea(center, size);
        if (player != null && rotatePlayerWithArea && freePlayerIn)
        {
            freePlayerTf = player.transform;
            freeSavedPlayerRot = freePlayerTf.rotation;
            freePlayerTf.SetParent(freePreviewPivot.transform, true);
        }
        else
        {
            freePlayerTf = null;
        }

        return true;
    }

    public void UpdateFreePreviewAngle(float angleDeg)
    {
        if (freePreviewPivot == null) return;
        freePreviewPivot.transform.rotation = Quaternion.Euler(0f, angleDeg, 0f);
    }

    public void RestoreFreePreview()
    {
        if (freePreviewPivot == null) return;

        // 元の見た目に戻すため、回転を0にしてから親を戻す
        freePreviewPivot.transform.rotation = Quaternion.identity;

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

        // プレイヤー戻し（向きも戻す）
        if (freePlayerTf)
        {
            freePlayerTf.SetParent(actorsRoot, true);
            freePlayerTf.rotation = freeSavedPlayerRot;
        }
        freePlayerTf = null;
        freePlayerIn = false;

        SafeDestroy(freePreviewPivot);
        freePreviewPivot = null;
    }
}
