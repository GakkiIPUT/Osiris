using UnityEngine;

public partial class BoardManager
{
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

    public bool BlocksVision(Vector2Int p)
    {
        if (!InBounds(p)) return true;
        var c = cells[p.y, p.x];
        return (c == CellType.Wall || c == CellType.Anchor);
    }

    // そのセルにガードがいる？
    private bool IsGuardAt(Vector2Int p)
    {
        if (guards == null) return false;
        for (int i = 0; i < guards.Count; i++)
            if (guards[i] != null && guards[i].pos == p) return true;
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
}
