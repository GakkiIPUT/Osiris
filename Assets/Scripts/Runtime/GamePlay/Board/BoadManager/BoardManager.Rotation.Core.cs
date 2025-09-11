using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class BoardManager
{
    private bool IsPlayerInsideArea(Vector2Int center, int size)
    {
        if (player == null) return false;
        return IsPointInArea(center, size, player.pos);
    }

    /// <summary>
    /// 指定中心とサイズのプレビュー情報を生成する。領域外やCore外が含まれる場合は valid=false。
    /// </summary>
    public RotatePreview GetPreview(Vector2Int center, int size, int dir)
    {
        var p = new RotatePreview { valid = true, area = new List<Vector2Int>() };
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
        {
            for (int i = -k; i <= k; i++)
            {
                var gp = new Vector2Int(center.x + i, center.y + j);
                p.area.Add(gp);
                if (!InBounds(gp)) p.valid = false;
                if (autoGenerateOuterRings && !IsInsideCore(gp)) p.valid = false;
            }
        }
        return p;
    }

    /// <summary>
    /// 回転後のプレイヤー位置（設定や範囲に応じて追従/据置）とガードの重なりを判定（±90°）。
    /// </summary>
    public bool WouldPlayerOverlapGuard(Vector2Int center, int size, int dir)
    {
        if (player == null) return false;
        Vector2Int checkPos = GetPlayerCheckPosForRotation(center, size, dir);

        for (int i = 0; i < guards.Count; i++)
        {
            var g = guards[i];
            if (g == null) continue;
            if (g.pos == checkPos) return true;
            if (g.IsMoving && g.NextPos == checkPos) return true;
        }
        return false;
    }

    /// <summary>
    /// 回転後のプレイヤー位置（設定や範囲に応じて追従/据置）とガードの重なりを判定（180°）。
    /// </summary>
    public bool WouldPlayerOverlapGuard180(Vector2Int center, int size)
    {
        if (player == null) return false;
        bool playerInArea = IsPlayerInsideArea(center, size);

        Vector2Int checkPos = player.pos;
        if (rotatePlayerWithArea && playerInArea)
            checkPos = EngineRot180(player.pos, center);

        for (int i = 0; i < guards.Count; i++)
        {
            var g = guards[i];
            if (g == null) continue;
            if (g.pos == checkPos) return true;
            if (g.IsMoving && g.NextPos == checkPos) return true;
        }
        return false;
    }

    private bool WouldPlayerBeBlockedByTileAfter(Vector2Int center, int size, int dir)
    {
        if (player == null) return false;
        var map = RotationEngine.BuildCellRotationMap(SnapshotGridModel(), center, size, dir);
        return WouldPlayerBeBlockedByTileAfter(center, size, dir, map);
    }

    private bool WouldPlayerBeBlockedByTileAfter(Vector2Int center, int size, int dir, IReadOnlyDictionary<Vector2Int, Vector2Int> map)
    {
        if (player == null) return false;

        // プレイヤーの最終座標（設定により回転追従 or 据置）
        Vector2Int dest = GetPlayerCheckPosForRotation(center, size, dir);

        // 対象エリアの回転マップ（dest -> src）
        CellType after = map.TryGetValue(dest, out var src)
            ? cells[src.y, src.x]
            : cells[dest.y, dest.x];

        return !IsWalkableTileType(after);
    }

    // ガード足元に回転後 Wall が来るか（±90°）…Wall なら回転キャンセル。Pit はキャンセルしない（後で死亡処理）
    private bool WouldGuardGetWallUnderfoot(Vector2Int center, int size, int dir)
    {
        var map = RotationEngine.BuildCellRotationMap(SnapshotGridModel(), center, size, dir);
        return WouldGuardGetWallUnderfoot(center, size, dir, map);
    }

    private bool WouldGuardGetWallUnderfoot(Vector2Int center, int size, int dir, IReadOnlyDictionary<Vector2Int, Vector2Int> map)
    {
        for (int i = 0; i < guards.Count; i++)
        {
            var g = guards[i];
            if (g == null) continue;

            if (Check(g.pos)) return true;
            if (g.IsMoving && g.NextPos != g.pos && Check(g.NextPos)) return true;
        }
        return false;

        bool Check(Vector2Int dest)
        {
            if (!InBounds(dest)) return false;
            if (!map.TryGetValue(dest, out var src)) return false;
            var after = cells[src.y, src.x];
            return after == CellType.Wall;
        }
    }

    private bool WouldBeSafePartial(Vector2Int center, int size, int dir)
    {
        var pv = GetPreview(center, size, dir);
        if (!pv.valid) return false;
        if (AreaCrossesOuterRing(center, size)) return false;
        if (forbidAnchorInArea && AreaContainsLocked(center, size)) return false;
        if (EngineAreaHasExit(center, size)) return false;

        if (WouldPlayerBeBlockedByTileAfter(center, size, dir)) return false;
        if (WouldGuardGetWallUnderfoot(center, size, dir)) return false;
        return true;
    }

    private bool WouldBeSafePartial(Vector2Int center, int size, int dir, IReadOnlyDictionary<Vector2Int, Vector2Int> map)
    {
        var pv = GetPreview(center, size, dir);
        if (!pv.valid) return false;
        if (AreaCrossesOuterRing(center, size)) return false;
        if (forbidAnchorInArea && AreaContainsLocked(center, size)) return false;
        if (EngineAreaHasExit(center, size)) return false;

        if (WouldPlayerBeBlockedByTileAfter(center, size, dir, map)) return false;
        if (WouldGuardGetWallUnderfoot(center, size, dir, map)) return false;
        return true;
    }

    private bool WouldBeSafePartial180(Vector2Int center, int size)
    {
        var pv = GetPreview(center, size, 0);
        if (!pv.valid) return false;
        if (AreaCrossesOuterRing(center, size)) return false;
        if (forbidAnchorInArea && AreaContainsLocked(center, size)) return false;
        if (EngineAreaHasExit(center, size)) return false;
        return true;
    }

    private bool CanRotateNow(Vector2Int center, int size, int dir)
    {
        if (dir == 0) return false;
        if (!WouldBeSafePartial(center, size, dir)) return false;
        if (WouldPlayerOverlapGuard(center, size, dir)) return false;

        if (!rotatePlayerWithArea && player != null && IsPlayerInsideArea(center, size))
        {
            var map = RotationEngine.BuildCellRotationMap(SnapshotGridModel(), center, size, dir);
            if (map.TryGetValue(player.pos, out var srcForFoot))
            {
                var post = cells[srcForFoot.y, srcForFoot.x];
                if (!IsWalkableTileType(post)) return false;
            }
        }
        return true;
    }

    private bool CanRotateNow(Vector2Int center, int size, int dir, IReadOnlyDictionary<Vector2Int, Vector2Int> map)
    {
        if (dir == 0) return false;
        if (!WouldBeSafePartial(center, size, dir, map)) return false;
        if (WouldPlayerOverlapGuard(center, size, dir)) return false;

        if (!rotatePlayerWithArea && player != null && IsPlayerInsideArea(center, size))
        {
            if (map.TryGetValue(player.pos, out var srcForFoot))
            {
                var post = cells[srcForFoot.y, srcForFoot.x];
                if (!IsWalkableTileType(post)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 指定エリアを回転（アニメ対応）。dir&gt;0=CW, dir&lt;0=CCW。成功時に onDone を呼ぶ。
    /// </summary>
    public bool TryRotateArea(Vector2Int center, int size, int dir, Action onDone)
    {
        if (IsFreePreviewActive) RestoreFreePreview();

        if (!animateBoardRotation)
        {
            if (RotateAreaInstantIfPossible(center, size, dir))
            {
                onDone?.Invoke();
                return true;
            }
            return false;
        }

        var rotationMap = RotationEngine.BuildCellRotationMap(SnapshotGridModel(), center, size, dir);
        if (!CanRotateNow(center, size, dir, rotationMap)) return false;

        BeginFreePreview(center, size);
        float endAngle = (dir > 0) ? 90f : -90f;
        StartCoroutine(RotateAnimCoro(center, size, dir, endAngle, rotationMap, onDone));
        return true;
    }

    /// <summary>
    /// 回転アニメーションを再生し、完了後に論理確定する。
    /// </summary>
    private IEnumerator RotateAnimCoro(Vector2Int center, int size, int dir, float endAngle, IReadOnlyDictionary<Vector2Int, Vector2Int> rotationMap, Action onDone)
    {
        IsAnimating = true;

        float dur = Mathf.Max(0.02f, rotateAnimSeconds);
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float w = Mathf.Clamp01(t);
            float eased = (rotateAnimCurve != null) ? rotateAnimCurve.Evaluate(w) : w;
            float cur = Mathf.Lerp(0f, endAngle, eased);
            UpdateFreePreviewAngle(cur);
            yield return null;
        }
        UpdateFreePreviewAngle(endAngle);

        RestoreFreePreview();
        RotateAreaInstantIfPossible(center, size, dir, rotationMap);

        IsAnimating = false;
        onDone?.Invoke();
    }

    /// <summary>
    /// 指定エリアを即時回転（アニメ無し）。dir&gt;0=CW, dir&lt;0=CCW。
    /// </summary>
    public bool RotateAreaInstantIfPossible(Vector2Int center, int size, int dir)
    {
        if (dir == 0) return false;
        var map = RotationEngine.BuildCellRotationMap(SnapshotGridModel(), center, size, dir);
        return RotateAreaInstantIfPossible(center, size, dir, map);
    }

    /// <summary>
    /// 指定エリアを即時回転（アニメ無し）。回転マップを使い回すオーバーロード。
    /// </summary>
    public bool RotateAreaInstantIfPossible(Vector2Int center, int size, int dir, IReadOnlyDictionary<Vector2Int, Vector2Int> map)
    {
        if (dir == 0) return false;
        if (!WouldBeSafePartial(center, size, dir, map)) return false;
        if (WouldPlayerOverlapGuard(center, size, dir)) return false;

        if (!rotatePlayerWithArea && player != null && IsPlayerInsideArea(center, size))
        {
            if (map.TryGetValue(player.pos, out var srcForFoot))
            {
                var post = cells[srcForFoot.y, srcForFoot.x];
                if (!IsWalkableTileType(post)) return false;
            }
        }

        var newCells = (CellType[,])cells.Clone();
        var newWO = (WallOrigin[,])wallOrigin.Clone();

        foreach (var kv in map)
        {
            var dest = kv.Key;
            var src = kv.Value;
            newCells[dest.y, dest.x] = cells[src.y, src.x];
            newWO[dest.y, dest.x] = wallOrigin[src.y, src.x];
        }

        cells = newCells;
        wallOrigin = newWO;
        RecomputeOuterRingFloor();

        var newTileGOs = new GameObject[Height, Width];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                newTileGOs[y, x] = tileGOs[y, x];

        foreach (var kv in map)
        {
            var dest = kv.Key;
            var src = kv.Value;
            var go = tileGOs[src.y, src.x];
            newTileGOs[dest.y, dest.x] = go;
            if (go != null)
            {
                var pos = go.transform.position;
                var target = GridToWorld(dest);
                go.transform.position = new Vector3(target.x, pos.y, target.z);
            }
        }
        tileGOs = newTileGOs;

        if (itemAt != null && itemAt.Count > 0)
        {
            var moved = new Dictionary<Vector2Int, (char sym, GameObject go)>();
            foreach (var kv in itemAt)
            {
                var p = kv.Key;
                bool inArea = IsPointInArea(center, size, p);
                if (!inArea)
                {
                    moved[p] = kv.Value;
                }
                else
                {
                    var np = EngineRot90(p, center, dir);
                    moved[np] = kv.Value;
                    if (kv.Value.go != null)
                        kv.Value.go.transform.position = GridToWorldActor(np);
                }
            }
            itemAt = moved;
        }

        ResolvePitfallsAfterRotation();

        if (player != null && rotatePlayerWithArea && IsPlayerInsideArea(center, size))
        {
            var np = EngineRot90(player.pos, center, dir);
            player.pos = np;
            player.transform.position = GridToWorldActor(np);
        }

        UpdateAllWallAppearances();
        RefreshAllGuardVision();

        return true;
    }

    /// <summary>
    /// 回転結果で落とし穴上にいるガードを除去する。
    /// </summary>
    private void ResolvePitfallsAfterRotation()
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
}
