using System.Collections.Generic;
using UnityEngine;

public static class RotationEngine
{
    // 点の90度回転（dir>0: CW, dir<0: CCW）
    public static Vector2Int Rot90(Vector2Int p, Vector2Int c, int dir)
    {
        var d = p - c;
        return (dir > 0)
            ? new Vector2Int(c.x + d.y, c.y - d.x)
            : new Vector2Int(c.x - d.y, c.y + d.x);
    }

    // 点の180度回転
    public static Vector2Int Rot180(Vector2Int p, Vector2Int c)
    {
        return new Vector2Int(2 * c.x - p.x, 2 * c.y - p.y);
    }

    // 回転エリア（size×size, 中心center）に対する「dest(回転後) → src(回転前)」マップを作る
    // 配列適用時に使う（Cells/WallOrigin の転写）
    public static Dictionary<Vector2Int, Vector2Int> BuildCellRotationMap(GridModel model, Vector2Int center, int size, int dir)
    {
        var map = new Dictionary<Vector2Int, Vector2Int>();
        int k = (size - 1) / 2;

        // 配列側は視覚と逆回転でサンプリング（BoardManager と同じ考え方）
        int gdir = -dir;

        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                int gx = center.x + i - k;
                int gy = center.y + j - k;
                var dest = new Vector2Int(gx, gy);
                if (!model.InBounds(dest)) continue;

                int sx, sy;
                if (gdir > 0) { sx = j; sy = size - 1 - i; } // CW（配列）
                else { sx = size - 1 - j; sy = i; }          // CCW（配列）

                int sgx = center.x - k + sx;
                int sgy = center.y - k + sy;

                var src = new Vector2Int(sgx, sgy);
                if (!model.InBounds(src)) src = dest; // 範囲外は自己を参照（安全策）
                map[dest] = src;
            }
        }
        return map;
    }

    // 回転エリア内に Exit が含まれるか（回転禁止用）
    public static bool AreaHasExit(GridModel model, Vector2Int center, int size)
    {
        int k = (size - 1) / 2;
        for (int j = -k; j <= k; j++)
        {
            for (int i = -k; i <= k; i++)
            {
                var p = new Vector2Int(center.x + i, center.y + j);
                if (!model.InBounds(p)) continue;
                if (model.Cells[p.y, p.x] == CellType.Exit) return true;
            }
        }
        return false;
    }
}
