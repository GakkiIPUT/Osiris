using System.Collections.Generic;
using UnityEngine;

public partial class BoardManager
{
    private void Awake()
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

    private void OnEnable()
    {
        if (!Application.isPlaying) Build();
    }

    // 破棄ヘルパー：エディタ停止中は DestroyImmediate
    private void SafeDestroy(Object o)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) { DestroyImmediate(o); return; }
#endif
        Destroy(o);
    }

    private void ClearAll()
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
    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            RestoreFreePreview();
        }
        UnsubscribeTurnEvents();
    }

    private void OnDestroy()
    {
        RestoreFreePreview();
        UnsubscribeTurnEvents();
    }

    // ===== Y合わせユーティリティ =====
    private Bounds GetWorldBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    private void AlignTopToY(GameObject go, float y)
    {
        var b = GetWorldBounds(go);
        float delta = y - b.max.y;
        go.transform.position += new Vector3(0, delta, 0);
    }

    private void AlignBottomToY(GameObject go, float y)
    {
        var b = GetWorldBounds(go);
        float delta = y - b.min.y;
        go.transform.position += new Vector3(0, delta, 0);
    }

    // ===== 2D自動整列ユーティリティ =====
    private bool IsQuadMesh(GameObject go)
    {
        var mf = go.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null && mf.sharedMesh.name.ToLower().Contains("quad");
    }

    private bool HasSpriteRenderer(GameObject go) => go.GetComponent<SpriteRenderer>() != null;

    private void AutoAlign2DObject(GameObject go, bool isActor, Vector2? xyScale = null)
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

    private string[] BuildExpandedLevelAndAllocateArrays()
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
    private void ParseCellsFromLevel()
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
                        // 外周 Wall 帯 → Floor（Core 内は Wall）
                        if (autoGenerateOuterRings &&
                            (x < coreOffsetX || x >= coreOffsetX + coreWidth ||
                             y < coreOffsetY || y >= coreOffsetY + coreHeight))
                        {
                            cells[y, x] = CellType.Floor;
                        }
                        else
                        {
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

    private void RecomputeOuterRingFloor()
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
                // Core 外 かつ Floor のセルをマーキング
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

    private bool IsRotateLockedCell(Vector2Int p)
    {
        if (!InBounds(p)) return false;
        var c = cells[p.y, p.x];
        return (c == CellType.Exit || c == CellType.Anchor);
    }

    private void PlaceTile(CellType t, Vector2Int p)
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

    private void UpdateWallAppearanceAt(Vector2Int p)
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

    private void UpdateAllWallAppearances()
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

        // ★ 必須アイテム（=鍵 'i'，e も鍵にカウント）
        var requiredSymbols = new List<char>();

        for (int y = 0; y < h; y++)
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
                        ch = '.'; // 外周領域：記号は存在しない扱い
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

        // Guards
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

            // 位置 → (記号, 実体)
            itemAt[it.pos] = (it.type.symbol[0], go);
        }

        // TurnManager
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

        // Exit 初期状態を受け取るため InitRequiredItems 前に購読 → 初期化は1回だけ
        _turn = tm;
        _turn.onRequiredChanged += OnRequiredChanged_UpdateExitOpenState;
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

        UpdateAllWallAppearances();
        var overlay = GetComponent<SelectionFramesOverlay>();
        if (overlay == null) overlay = gameObject.AddComponent<SelectionFramesOverlay>();
        overlay.board = this;
        overlay.innerSize = 3;
        overlay.outerRadius = 3;
    }

    // 必須アイテムの進捗 → Exit を一括で開閉
    private void OnRequiredChanged_UpdateExitOpenState(IReadOnlyList<TurnManager.RequiredItem> reqs)
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

    private void UnsubscribeTurnEvents()
    {
        if (_turn != null)
        {
            _turn.onRequiredChanged -= OnRequiredChanged_UpdateExitOpenState;
            _turn = null;
        }
    }
}
