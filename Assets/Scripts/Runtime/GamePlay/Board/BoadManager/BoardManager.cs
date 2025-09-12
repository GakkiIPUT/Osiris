using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 盤面の構築・座標系・アイテム/ガード配置・通行判定・各種可視化設定を司る中核マネージャ（partial）。
/// Editorプレビュー/ランタイム双方に対応し、他コンポーネント（Player/Guard/Turn）と連携する。
/// </summary>
///
public enum CellType
{
    Empty = 0,  // 空（未使用・外周生成ON時のCore外など）
    Floor = 1,  // 床（.）
    Wall = 2,   // 壁（#）
    Exit = 3,   // 出口（E）
    Anchor = 4, // 回転不可マス（@）
    Pit = 5,    // 落とし穴（x）
}
public enum WallOrigin
{
    None = 0,   // 壁以外
    Core = 1,   // Core起源の壁
    Outer = 2,  // 外周起源の壁
}
public partial class BoardManager : MonoBehaviour
{

    [Header("Prefabs (Tiles & Player)")]
    public GameObject pfFloor;

    public GameObject pfWall;
    public GameObject pfExit;
    public GameObject pfAnchor;   // 回転不可マス（@）
    public GameObject pfPit;      // 落とし穴（x）
    public GameObject pfPlayer;   // PlayerはAddComponentでPlayerController付与（Prefab側にあってもOK）

    // TurnManager のイベント購読用（重複防止）
    private TurnManager _turn;

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

    /// <summary>指定座標がアンカー（回転不可）か。</summary>
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

    /// <summary>
    /// セル中心のワールド座標を返す。
    /// </summary>
    public Vector3 CellCenter(Vector2Int p, float y)
    {
        // ※ 当初の仕様に合わせて+0.0f（中心補正が不要な表現）
        return new Vector3(p.x + 0.0f, y, p.y + 0.0f);
    }

    /// <summary>盤面の幅（外周生成ON時は拡張後サイズ）。</summary>
    public int Width => autoGenerateOuterRings ? expandedWidth : (level.Length > 0 ? level[0].Length : 0);

    /// <summary>盤面の高さ（外周生成ON時は拡張後サイズ）。</summary>
    public int Height => autoGenerateOuterRings ? expandedHeight : level.Length;

    public CellType[,] cells;
    private GameObject[,] tileGOs;  // 1マス=1オブジェクト（Floor/Wall/Exit/Anchor）

    private int coreOffsetX, coreOffsetY;          // Core の左上オフセット（拡張後座標系内）
    private int coreWidth, coreHeight;             // 元 level の幅高さ
    private int expandedWidth, expandedHeight;     // 拡張後の全体サイズ
    private string[] levelOriginalCore;            // 元 level のコピー（外周 ON 時のみ使用）
    private WallOrigin[,] wallOrigin;              // 壁セルの起源（Wall 以外は未使用）

    private bool IsInsideCore(Vector2Int p)
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

    private bool _isAnimating;

    /// <summary>回転/移動などのボードアニメ中フラグ（ON/OFFログ付き）。</summary>
    public bool IsAnimating
    {
        get => _isAnimating;
        set
        {
            if (_isAnimating == value) return;
            _isAnimating = value;
            //Debug.Log($"[Board] IsAnimating {(value ? "ON" : "OFF")} t={Time.time:F3}");
        }
    }

    [Header("DEV / Rotation")]
    public bool rotatePlayerWithArea = true; // 開発者モードで切り替え

    [Header("Rotation Animation")]
    [Tooltip("回転時にピボット回転のアニメを使う（OFFで即時確定）")]
    public bool animateBoardRotation = true;

    [Tooltip("回転アニメの所要時間（秒）")]
    [Min(0.02f)] public float rotateAnimSeconds = 0.12f;

    [Tooltip("回転アニメの補間カーブ")]
    public AnimationCurve rotateAnimCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

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

    [Header("DEV / Input")]
    [Tooltip("Pad 左スティックで移動する（ONでPadによる自由回転は無効。マウス回転は可）")]
    public bool devPadLeftStickMoves = false;

    [Header("Outer Ring Visuals")]
    [Tooltip("外周Floor(CORE外に存在する全 Floor) 用マテリアル（未設定なら通常と同じ）")]
    public Material outerRingFloorMat;

    [Tooltip("Gizmo プレビュー用 外周Floor 色")]
    public Color previewOuterFloor = new Color(0.70f, 0.75f, 0.85f, 1f);

    // 外周Floor判定配列（autoGenerateOuterRings=false のとき null）
    private bool[,] outerRingFloor;

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

    [Tooltip("Core 外側に存在する Outer起源壁に適用すFるマテリアル")]
    public Material wallOuterMat;

    /// <summary>
    /// 指定座標がガードに占有されているか（移動中はNextPosも占有とみなす）。
    /// </summary>
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

    /// <summary>
    /// 通行可能かを判定する（プレイヤー向け）。ガード/泥棒/外周/タイル種別を考慮。
    /// </summary>
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

    /// <summary>
    /// 通行可能かを判定する（一般）。泥棒/外周/タイル種別を考慮。
    /// </summary>
    public bool IsWalkable(Vector2Int p)
    {
        if (!InBounds(p)) return false;
        // 泥棒がいるマスは侵入不可
        if (IsThiefAt(p)) return false;
        if (autoGenerateOuterRings && !IsInsideCore(p))
            return false;
        var c = cells[p.y, p.x];
        return c == CellType.Floor || c == CellType.Exit;
    }

    private void ConfigureGuardFromSymbol(GuardController g, char sym)
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

    // ========= 回転（任意マス・部分回転対応） ===========

    /// <summary>
    /// テキストからレベルを設定し、必要ならBuildを実行する。
    /// </summary>
    public void SetLevelFromText(string text, bool rebuild = true)
    {
        level = ParseRows(text);
        level = NormalizeRows(level, pad: '.');
        if (rebuild) Build();
    }

#if UNITY_EDITOR

    [ContextMenu("Commit Tiles Now (Editor)")]
    private void EditorCommitNow()
    {
        bool prev = editorPreview;
        editorPreview = false;
        Build();
        editorPreview = prev;
    }

    [ContextMenu("Rebuild Level Now")]
    private void EditorRebuildNow()
    { Build(); }

#endif
#if UNITY_EDITOR

    [Header("DEV / Editor")]
    public bool devUseSceneLevelInEditor = true;     // ← 追加：再生時にScene内の盤面をそのまま使う

    public TextAsset devTargetTextAsset;             // ← （任意）保存先

    /// <summary>
    /// 現在の level を指定の TextAsset に保存する（エディタ専用）。
    /// </summary>
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

    /// <summary>開発用設定（自由回転など）を PlayerPrefs に保存する。</summary>
    public void SaveDevModeSettings()
    {
        PlayerPrefs.SetInt("rotatePlayerWithArea", rotatePlayerWithArea ? 1 : 0);

        // ▼ 追加/更新: 自由回転・入力関連の永続化
        PlayerPrefs.SetInt("devEnableFreeRotate", devEnableFreeRotate ? 1 : 0);
        PlayerPrefs.SetInt("devAllow180Rotation", devAllow180Rotation ? 1 : 0);
        PlayerPrefs.SetFloat("devSnapAngleDeg", devSnapAngleDeg);
        PlayerPrefs.SetFloat("devCommitAngleDeg", devCommitAngleDeg);
        PlayerPrefs.SetFloat("devStickiness", devStickiness);
        PlayerPrefs.SetFloat("devNgGhostSeconds", devNgGhostSeconds);
        PlayerPrefs.SetInt("devPadLeftStickMoves", devPadLeftStickMoves ? 1 : 0);

        PlayerPrefs.Save();
    }

    /// <summary>開発用設定（自由回転など）を PlayerPrefs から読み込む。</summary>
    public void LoadDevModeSettings()
    {
        rotatePlayerWithArea = PlayerPrefs.GetInt("rotatePlayerWithArea", 1) == 1;

        // ▼ 自由回転/入力 関連
        devEnableFreeRotate   = PlayerPrefs.GetInt("devEnableFreeRotate", 1) == 1;
        devAllow180Rotation   = PlayerPrefs.GetInt("devAllow180Rotation", 0) == 1;
        devSnapAngleDeg       = PlayerPrefs.GetFloat("devSnapAngleDeg", 15f);
        devCommitAngleDeg     = PlayerPrefs.GetFloat("devCommitAngleDeg", 15f);
        devStickiness         = PlayerPrefs.GetFloat("devStickiness", 0.5f);
        devNgGhostSeconds     = PlayerPrefs.GetFloat("devNgGhostSeconds", 0.5f);
        devPadLeftStickMoves  = PlayerPrefs.GetInt("devPadLeftStickMoves", 0) == 1;
    }

    // ===== Core / Board 座標ユーティリティ（LevelPainter 用） =====

    // Core の左上オフセット公開（必要なら LevelPainter で参照）
    public int CoreOffsetX => coreOffsetX;

    public int CoreOffsetY => coreOffsetY;
    public int CoreWidth => coreWidth;
    public int CoreHeight => coreHeight;

    /// <summary>
    /// Board座標 → Core座標に変換（外周生成ON時はコア範囲内のみ成功）。
    /// </summary>
    public bool TryBoardToCore(Vector2Int boardPos, out Vector2Int corePos)
    {
        if (!autoGenerateOuterRings)
        {
            // そのまま
            if (boardPos.x < 0 || boardPos.y < 0 ||
                boardPos.y >= level.Length || boardPos.x >= (level.Length > 0 ? level[0].Length : 0))
            {
                corePos = default;
                return false;
            }
            corePos = boardPos;
            return true;
        }

        if (!IsInsideCore(boardPos))
        {
            corePos = default;
            return false;
        }
        corePos = new Vector2Int(boardPos.x - coreOffsetX, boardPos.y - coreOffsetY);
        return corePos.x >= 0 && corePos.y >= 0 &&
               corePos.x < coreWidth && corePos.y < coreHeight;
    }

    /// <summary>
    /// Core座標 → Board座標に変換（外周生成ON時はオフセットを加算）。
    /// </summary>
    public Vector2Int CoreToBoard(Vector2Int corePos)
    {
        if (!autoGenerateOuterRings) return corePos;
        return new Vector2Int(corePos.x + coreOffsetX, corePos.y + coreOffsetY);
    }

    /// <summary>
    /// Core文字を置換して即反映する。rebuild=true なら Build、false ならパースのみ。
    /// </summary>
    public bool SetCoreCellChar(Vector2Int corePos, char ch, bool rebuild = true)
    {
        if (corePos.x < 0 || corePos.y < 0 ||
            corePos.y >= level.Length || corePos.x >= level[corePos.y].Length)
            return false;

        var row = level[corePos.y];
        if (corePos.x >= row.Length) return false;

        if (row[corePos.x] == ch) // 変化なし
        {
            if (rebuild) { ParseCellsFromLevel(); UpdateAllWallAppearances(); }
            return true;
        }

        // 文字列書換
        var chars = row.ToCharArray();
        chars[corePos.x] = ch;
        level[corePos.y] = new string(chars);

        if (rebuild) Build(); else ParseCellsFromLevel();
        return true;
    }
}
