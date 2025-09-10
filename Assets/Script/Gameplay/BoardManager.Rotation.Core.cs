using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class BoardManager
{
    // 3x3 を中心 center に置いたとき、プレイヤーがエリア内に居るか
    private bool IsPlayerInsideArea(Vector2Int center, int size)
    {
        if (player == null) return false;
        return IsPointInArea(center, size, player.pos);
    }

    // プレビュー（枠線用）。dir は未使用（将来用）。valid は「エリアが有効領域か」のみを表す
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

    // rotatePlayerWithArea を考慮したガード重なり（±90°）
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

    // 180°
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

    // プレイヤー足元が回転後に非Walkable（Wall/Pit）になるか（±90°）
    private bool WouldPlayerBeBlockedByTileAfter(Vector2Int center, int size, int dir)
    {
        if (player == null) return false;

        // プレイヤーの最終座標（設定により回転追従 or 据置）
        Vector2Int dest = GetPlayerCheckPosForRotation(center, size, dir);

        // 対象エリアの回転マップ（dest -> src）
        var map = RotationEngine.BuildCellRotationMap(SnapshotGridModel(), center, size, dir);

        // dest がエリア内なら src からタイルが流入、エリア外なら据置
        CellType after = map.TryGetValue(dest, out var src)
            ? cells[src.y, src.x]
            : cells[dest.y, dest.x];

        return !IsWalkableTileType(after);
    }

    // ガード足元に回転後 Wall が来るか（±90°）…Wall なら回転キャンセル。Pit はキャンセルしない（後で死亡処理）
    private bool WouldGuardGetWallUnderfoot(Vector2Int center, int size, int dir)
    {
        var map = RotationEngine.BuildCellRotationMap(SnapshotGridModel(), center, size, dir);

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
            if (!map.TryGetValue(dest, out var src)) return false; // エリア外は据置
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

        // 追加：プレイヤー足元が非Walkableになる → キャンセル
        if (WouldPlayerBeBlockedByTileAfter(center, size, dir)) return false;
        // 追加：ガード足元に Wall が来る → キャンセル（Pitは許容して後で死亡処理）
        if (WouldGuardGetWallUnderfoot(center, size, dir)) return false;

        return true;
    }

    private bool WouldBeSafePartial180(Vector2Int center, int size)
    {
        var pv = GetPreview(center, size, 0);
        if (!pv.valid) return false;
        if (AreaCrossesOuterRing(center, size)) return false;
        if (forbidAnchorInArea && AreaContainsLocked(center, size)) return false;
        if (EngineAreaHasExit(center, size)) return false;

        // 180°版の詳細チェックは必要に応じて追加
        return true;
    }

    // 追加: 即時確定と同等の事前チェック（プレイヤー非追従時の足元など）
    private bool CanRotateNow(Vector2Int center, int size, int dir)
    {
        if (dir == 0) return false;
        if (!WouldBeSafePartial(center, size, dir)) return false;
        if (WouldPlayerOverlapGuard(center, size, dir)) return false;

        if (!rotatePlayerWithArea && player != null && IsPlayerInsideArea(center, size))
        {
            // 非追従時でも足元に非Walkableが来るならキャンセル
            var map = RotationEngine.BuildCellRotationMap(SnapshotGridModel(), center, size, dir);
            if (map.TryGetValue(player.pos, out var srcForFoot))
            {
                var post = cells[srcForFoot.y, srcForFoot.x];
                if (!IsWalkableTileType(post)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 指定エリアを回転（アニメ対応）。dir>0=CW, dir<0=CCW。成功なら onDone を呼ぶ。
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

        if (!CanRotateNow(center, size, dir)) return false;

        BeginFreePreview(center, size);
        float endAngle = (dir > 0) ? 90f : -90f;
        StartCoroutine(RotateAnimCoro(center, size, dir, endAngle, onDone));
        return true;
    }

    private IEnumerator RotateAnimCoro(Vector2Int center, int size, int dir, float endAngle, Action onDone)
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

        RotateAreaInstantIfPossible(center, size, dir);

        IsAnimating = false;
        onDone?.Invoke();
    }

    /// <summary>
    /// 指定エリアを即時回転（アニメ無し）。dir>0=CW, dir<0=CCW。
    /// </summary>
    public bool RotateAreaInstantIfPossible(Vector2Int center, int size, int dir)
    {
        if (dir == 0) return false;
        if (!WouldBeSafePartial(center, size, dir)) return false;
        if (WouldPlayerOverlapGuard(center, size, dir)) return false;

        var map = RotationEngine.BuildCellRotationMap(SnapshotGridModel(), center, size, dir);

        // プレイヤー非追従時の足元チェック（エリア内のみ）
        if (!rotatePlayerWithArea && player != null && IsPlayerInsideArea(center, size))
        {
            if (map.TryGetValue(player.pos, out var srcForFoot))
            {
                var post = cells[srcForFoot.y, srcForFoot.x];
                if (!IsWalkableTileType(post)) return false;
            }
        }

        // 1) 配列の転写（Cells / WallOrigin）
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

        // 2) タイルGameObjectの位置補正
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

        // 3) アイテムの位置補正
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

        // 4) ガードは回転で動かさない（仕様）
        //    → このあと Pit の上にいるガードは死亡させる
        ResolvePitfallsAfterRotation();

        // 5) プレイヤーだけ設定に応じて移動
        if (player != null && rotatePlayerWithArea && IsPlayerInsideArea(center, size))
        {
            var np = EngineRot90(player.pos, center, dir);
            player.pos = np;
            player.transform.position = GridToWorldActor(np);
        }

        // 6) 見た目更新
        UpdateAllWallAppearances();
        RefreshAllGuardVision();

        return true;
    }

    // 回転結果で Pit 上にいるガードを排除
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
