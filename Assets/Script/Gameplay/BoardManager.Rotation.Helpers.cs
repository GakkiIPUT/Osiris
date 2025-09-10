using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BoardManager
{
    // 指定点が中心center・正方サイズsizeのエリア内か
    private bool IsPointInArea(Vector2Int center, int size, Vector2Int p)
    {
        int k = (size - 1) / 2;
        return Mathf.Abs(p.x - center.x) <= k && Mathf.Abs(p.y - center.y) <= k;
    }

    // タイルタイプが歩行可能（床/出口）か
    private static bool IsWalkableTileType(CellType c) =>
        c == CellType.Floor || c == CellType.Exit;

    // 回転時、プレイヤーとガード重なり判定に使う「プレイヤーのチェック座標」
    // 仕様: rotatePlayerWithArea が true かつプレイヤーがエリア内→回転後座標。そうでなければ据置座標。
    private Vector2Int GetPlayerCheckPosForRotation(Vector2Int center, int size, int dir)
    {
        if (player == null) return default;
        if (rotatePlayerWithArea && IsPlayerInsideArea(center, size))
            return EngineRot90(player.pos, center, dir);
        return player.pos;
    }

    // エリア内を走査（将来用のユーティリティ）
    private void ForEachAreaCell(Vector2Int center, int size, Action<Vector2Int> action)
    {
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
            for (int i = -k; i <= k; i++)
                action(new Vector2Int(center.x + i, center.y + j));
    }

    // ========= 回転（任意マス・部分回転対応） ===========
    public struct RotatePreview
    {
        public bool valid;
        public List<Vector2Int> area;
    }

    public struct StepValidity
    {
        public bool cw90, ccw90, cw180, ccw180;
        public bool Any(bool allow180) => cw90 || ccw90 || (allow180 && (cw180 || ccw180));
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

    // 回転エリアが Core 外（外周帯）を含むか
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
                if (!IsInsideCore(p)) return true; // Core から外れたセルを含む
            }
        }
        return false;
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
}
