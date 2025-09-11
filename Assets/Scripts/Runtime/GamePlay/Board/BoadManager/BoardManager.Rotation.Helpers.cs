using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BoardManager
{
    /// <summary>
    /// 点 p が中心 center・サイズ size の正方形エリア内か判定する。
    /// </summary>
    private bool IsPointInArea(Vector2Int center, int size, Vector2Int p)
    {
        int k = (size - 1) / 2;
        return Mathf.Abs(p.x - center.x) <= k && Mathf.Abs(p.y - center.y) <= k;
    }

    /// <summary>
    /// タイル種別が歩行可能（床/出口）か判定する。
    /// </summary>
    private static bool IsWalkableTileType(CellType c) =>
        c == CellType.Floor || c == CellType.Exit;

    /// <summary>
    /// 回転時のプレイヤー判定座標を取得する（設定に応じて追従/据置）。
    /// </summary>
    private Vector2Int GetPlayerCheckPosForRotation(Vector2Int center, int size, int dir)
    {
        if (player == null) return default;
        if (rotatePlayerWithArea && IsPlayerInsideArea(center, size))
            return EngineRot90(player.pos, center, dir);
        return player.pos;
    }

    /// <summary>
    /// エリア内を走査するヘルパ。
    /// </summary>
    private void ForEachAreaCell(Vector2Int center, int size, Action<Vector2Int> action)
    {
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
            for (int i = -k; i <= k; i++)
                action(new Vector2Int(center.x + i, center.y + j));
    }

    /// <summary>
    /// 回転プレビュー情報。
    /// </summary>
    public struct RotatePreview
    {
        public bool valid;
        public List<Vector2Int> area;
    }

    /// <summary>
    /// 90/180 度回転の可否。
    /// </summary>
    public struct StepValidity
    {
        public bool cw90, ccw90, cw180, ccw180;

        public bool Any(bool allow180) => cw90 || ccw90 || (allow180 && (cw180 || ccw180));
    }

    /// <summary>
    /// 回転中心の距離制限（チェビシェフ距離）判定。
    /// </summary>
    public bool IsCenterWithinLimit(Vector2Int center)
    {
        if (player == null) return false;
        int dx = Mathf.Abs(center.x - player.pos.x);
        int dy = Mathf.Abs(center.y - player.pos.y);
        int chebyshev = Mathf.Max(dx, dy);
        return chebyshev <= rotationCenterMaxDistance;
    }

    /// <summary>
    /// 回転禁止セル（例: アンカー等）を含むか。
    /// </summary>
    public bool AreaContainsLocked(Vector2Int center, int size)
    {
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
        {
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!InBounds(p)) continue;
                if (IsRotateLockedCell(p)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 回転エリアが Core 外（外周帯）を跨ぐか。
    /// </summary>
    private bool AreaCrossesOuterRing(Vector2Int center, int size)
    {
        if (!autoGenerateOuterRings) return false;
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
        {
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!InBounds(p)) continue;
                if (!IsInsideCore(p)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 各回転ステップ（±90/±180）の可否を評価する。
    /// </summary>
    public StepValidity GetStepValidity(Vector2Int center, int size)
    {
        var v = new StepValidity
        {
            cw90 = WouldBeSafePartial(center, size, +1) && !WouldPlayerOverlapGuard(center, size, +1),
            ccw90 = WouldBeSafePartial(center, size, -1) && !WouldPlayerOverlapGuard(center, size, -1)
        };

        if (devAllow180Rotation)
        {
            v.cw180 = WouldBeSafePartial180(center, size) && !WouldPlayerOverlapGuard180(center, size);
            v.ccw180 = v.cw180;
        }
        else
        {
            v.cw180 = v.ccw180 = false;
        }
        return v;
    }
}
