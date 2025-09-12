using System.Collections.Generic;
using UnityEngine;

public partial class BoardManager
{
    // 追加: 通常Floor用のキャッシュ
    private Material floorNormalMat;

    private void EnsureFloorNormalMat()
    {
        if (floorNormalMat != null) return;
        if (pfFloor == null) return;
        var r = pfFloor.GetComponentInChildren<Renderer>();
        if (r != null) floorNormalMat = r.sharedMaterial;
    }

    /// <summary>
    /// 参照の初期化とレベル生成を行う。
    /// </summary>
    private void Awake()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) editorPreview = false;
#endif
        if (tilesRoot == null) tilesRoot = new GameObject("TilesRoot").transform;
        if (actorsRoot == null) actorsRoot = new GameObject("ActorsRoot").transform;
        if (itemsRoot == null) itemsRoot = new GameObject("ItemsRoot").transform;
        LoadDevModeSettings();
        Build();
    }

    /// <summary>
    /// 有効化時にエディタプレビューへ反映する。
    /// </summary>
    private void OnEnable()
    {
        if (!Application.isPlaying) Build();
    }

    /// <summary>
    /// GameObject を実行/エディタ文脈に応じて安全に破棄する。
    /// </summary>
    private void SafeDestroy(Object o)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) { DestroyImmediate(o); return; }
#endif
        Destroy(o);
    }

    /// <summary>
    /// 生成済みのタイル/アクター/アイテムを全て破棄し、状態を初期化する。
    /// </summary>
    private void ClearAll()
    {
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

    /// <summary>
    /// 無効化時の復帰と購読解除。
    /// </summary>
    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            RestoreFreePreview();
        }
        UnsubscribeTurnEvents();
    }

    /// <summary>
    /// 破棄時の復帰と購読解除。
    /// </summary>
    private void OnDestroy()
    {
        RestoreFreePreview();
        UnsubscribeTurnEvents();
    }

    /// <summary>
    /// 子レンダラーも含めたバウンズを取得する。
    /// </summary>
    private Bounds GetWorldBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    /// <summary>
    /// オブジェクトの天面を指定Yへ揃える。
    /// </summary>
    private void AlignTopToY(GameObject go, float y)
    {
        var b = GetWorldBounds(go);
        float delta = y - b.max.y;
        go.transform.position += new Vector3(0, delta, 0);
    }

    /// <summary>
    /// オブジェクトの底面を指定Yへ揃える。
    /// </summary>
    private void AlignBottomToY(GameObject go, float y)
    {
        var b = GetWorldBounds(go);
        float delta = y - b.min.y;
        go.transform.position += new Vector3(0, delta, 0);
    }

    /// <summary>
    /// Mesh が Quad かどうか。
    /// </summary>
    private bool IsQuadMesh(GameObject go)
    {
        var mf = go.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null && mf.sharedMesh.name.ToLower().Contains("quad");
    }

    /// <summary>
    /// SpriteRenderer を持つかどうか。
    /// </summary>
    private bool HasSpriteRenderer(GameObject go) => go.GetComponent<SpriteRenderer>() != null;

    /// <summary>
    /// 2Dアセット（Quad/Sprite）を上面投影向きへ回し、Yやスケールを整える。
    /// </summary>
    private void AutoAlign2DObject(GameObject go, bool isActor, Vector2? xyScale = null)
    {
        if (!autoAlign2D) return;

        bool isQuad = IsQuadMesh(go);
        bool isSprite = HasSpriteRenderer(go);

        if (isQuad || isSprite)
        {
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var p = go.transform.position;
            p.y = floorY + (isActor ? actorYOffset : 0f);
            go.transform.position = p;

            if (xyScale.HasValue)
            {
                var s = xyScale.Value;
                go.transform.localScale = new Vector3(s.x, s.y, 1f);
            }
        }
    }

    /// <summary>
    /// 与えられた行配列でレベルを差し替え、ビルドする。
    /// </summary>
    public void SetLevel(string[] newRows)
    {
        if (newRows == null || newRows.Length == 0) return;
        level = new string[newRows.Length];
        for (int i = 0; i < newRows.Length; i++) level[i] = newRows[i];
        Build();
    }

    /// <summary>
    /// 外周展開を含めた表示用行配列を構築し、内部配列を確保する。
    /// </summary>
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
                bool isAnchorBand =
                    x < anchorThickness || y < anchorThickness ||
                    x >= expandedWidth - anchorThickness ||
                    y >= expandedHeight - anchorThickness;
                if (isAnchorBand) { line[x] = '@'; continue; }

                bool inOuterWall =
                    x < anchorThickness + outerWallThickness ||
                    y < anchorThickness + outerWallThickness ||
                    x >= expandedWidth - (anchorThickness + outerWallThickness) ||
                    y >= expandedHeight - (anchorThickness + outerWallThickness);
                if (inOuterWall) { line[x] = '#'; continue; }

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

    /// <summary>
    /// 行配列からタイル種別配列を構築する（GameObjectは生成しない）。
    /// </summary>
    private void ParseCellsFromLevel()
    {
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
                    if (IsInsideCore(new Vector2Int(x, y)))
                    {
                        int cx = x - coreOffsetX;
                        int cy = y - coreOffsetY;
                        ch = levelOriginalCore[cy][cx];
                    }
                    else
                    {
                        ch = rows[y][x];
                    }
                }
                else
                {
                    ch = level[y][x];
                }

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

    /// <summary>
    /// 外周Floor判定配列を再計算する。
    /// </summary>
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
                outerRingFloor[y, x] = (!IsInsideCore(p) && cells[y, x] == CellType.Floor);
            }
        }
    }

    /// <summary>
    /// 指定セルが外周Floorか。
    /// </summary>
    public bool IsOuterFloor(Vector2Int p)
    {
        return outerRingFloor != null &&
               InBounds(p) &&
               outerRingFloor[p.y, p.x];
    }

    /// <summary>
    /// 回転禁止セル（Exit/Anchor）か。
    /// </summary>
    private bool IsRotateLockedCell(Vector2Int p)
    {
        if (!InBounds(p)) return false;
        var c = cells[p.y, p.x];
        return (c == CellType.Exit || c == CellType.Anchor);
    }

    /// <summary>
    /// 盤面を再構築する（ロジック→見た目→アクター/アイテム生成→視界/Exit更新）。
    /// </summary>
    public void Build()
    {
        ParseCellsFromLevel();
        UnsubscribeTurnEvents();

        if (!Application.isPlaying && editorPreview)
        {
            ClearAll();
            return;
        }

        ClearAll();

        // 追加: 通常Floorマテリアルの確保
        EnsureFloorNormalMat();

        int h = Height;
        int w = Width;
        tileGOs = new GameObject[h, w];

        Vector2Int? playerStart = null;

        var guardSpawns = new List<(Vector2Int pos, GuardType type)>();
        var itemSpawns = new List<(Vector2Int pos, ItemType type)>();

        var requiredSymbols = new List<char>();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector2Int p = new Vector2Int(x, y);

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
                        ch = '.';
                    }
                }
                else
                {
                    ch = level[y][x];
                }

                PlaceTile(cells[y, x], p);

                if (ch == 'P') playerStart = p;

                for (int gi = 0; gi < guardTypes.Count; gi++)
                {
                    if (!string.IsNullOrEmpty(guardTypes[gi].symbol) &&
                        guardTypes[gi].symbol[0] == ch)
                    {
                        guardSpawns.Add((p, guardTypes[gi]));
                        break;
                    }
                }

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

        foreach (var it in itemSpawns)
        {
            if (!it.type.prefab) { Debug.LogError($"Item prefab null for '{it.type.symbol}'"); continue; }
            var go = Instantiate(it.type.prefab, GridToWorld(it.pos), Quaternion.identity, itemsRoot);
            go.name = $"Item_{it.pos.x}_{it.pos.y}_{it.type.symbol}";
            AutoAlign2DObject(go, true, GetItemVisualScaleBySymbol(it.type.symbol[0]));

            itemAt[it.pos] = (it.type.symbol[0], go);
        }

        var tm = UnityCompat.FindFirst<TurnManager>();
        if (tm == null)
        {
            tm = new GameObject("TurnManager").AddComponent<TurnManager>();
            tm.ResetScoreCounters();
            tm.board = this;
        }
        else
        {
            tm.board = this;
        }
        tm.ResetGoalState();

        _turn = tm;
        _turn.onRequiredChanged += OnRequiredChanged_UpdateExitOpenState;
        tm.InitRequiredItems(requiredSymbols);

        var camFollow = UnityCompat.FindFirst<CameraFollow>();
        if (camFollow != null && player != null)
        {
            camFollow.board = this;
            camFollow.target = player.transform;
            camFollow.Snap();
        }

        RefreshAllGuardVision();

        // 壁見た目の一括更新は無効化（no-op化）
        UpdateAllWallAppearances();

        // 追加: 床見た目の一括更新
        UpdateAllFloorAppearances();

        var overlay = GetComponent<SelectionFramesOverlay>();
        if (overlay == null) overlay = gameObject.AddComponent<SelectionFramesOverlay>();
        overlay.board = this;
        overlay.innerSize = 3;
        overlay.outerRadius = 3;
    }

    /// <summary>
    /// 単一セルのタイルを配置し、見た目を整える。
    /// </summary>
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

        // 床の見た目（外周⇔内側）
        var rend = go.GetComponentInChildren<Renderer>();
        if (t == CellType.Floor && rend != null)
        {
            if (isOuterF && outerRingFloorMat != null)
            {
                rend.sharedMaterial = outerRingFloorMat;
            }
            else
            {
                EnsureFloorNormalMat();
                if (floorNormalMat != null) rend.sharedMaterial = floorNormalMat;
            }
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

        // 壁見た目の個別更新は無効化（no-op化）
        if (t == CellType.Wall)
            UpdateWallAppearanceAt(p);
    }

    /// <summary>
    /// 単一セルのFloor見た目をOuter/Coreに応じて更新する。
    /// </summary>
    private void UpdateFloorAppearanceAt(Vector2Int p)
    {
        if (!InBounds(p)) return;
        if (cells[p.y, p.x] != CellType.Floor) return;

        var go = tileGOs[p.y, p.x];
        if (!go) return;

        var rend = go.GetComponentInChildren<Renderer>();
        if (!rend) return;

        bool isOuterF = IsOuterFloor(p);
        if (isOuterF && outerRingFloorMat != null)
        {
            rend.sharedMaterial = outerRingFloorMat;
        }
        else
        {
            EnsureFloorNormalMat();
            if (floorNormalMat != null) rend.sharedMaterial = floorNormalMat;
        }
    }

    /// <summary>
    /// 盤面中の全Floor見た目を更新する。
    /// </summary>
    private void UpdateAllFloorAppearances()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (cells[y, x] == CellType.Floor)
                    UpdateFloorAppearanceAt(new Vector2Int(x, y));
    }

    /// <summary>
    /// 壁の見た目更新（無効化: 見た目だけの機能のため）
    /// </summary>
    private void UpdateWallAppearanceAt(Vector2Int p)
    {
        // no-op（実行ロジックに影響なし）
        return;
    }

    /// <summary>
    /// 全壁の見た目更新（無効化: 見た目だけの機能のため）
    /// </summary>
    private void UpdateAllWallAppearances()
    {
        // no-op（実行ロジックに影響なし）
        return;
    }

    /// <summary>
    /// 必須アイテム進捗に応じて Exit の開閉を一括更新する。
    /// </summary>
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

    /// <summary>
    /// 盤上の ExitController へ開閉状態を反映する。
    /// </summary>
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

    /// <summary>
    /// TurnManager のイベント購読を解除する。
    /// </summary>
    private void UnsubscribeTurnEvents()
    {
        if (_turn != null)
        {
            _turn.onRequiredChanged -= OnRequiredChanged_UpdateExitOpenState;
            _turn = null;
        }
    }
}
