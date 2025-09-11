using UnityEngine;

/// <summary>
/// 盤面の状態を表す不変モデル（Board 座標系）。外周生成の有無や Core 領域情報も含む。
/// </summary>
public class GridModel
{
    /// <summary>盤面の幅（拡張後）</summary>
    public int Width { get; }

    /// <summary>盤面の高さ（拡張後）</summary>
    public int Height { get; }

    /// <summary>Core 左上の X オフセット（Board 座標系）</summary>
    public int CoreOffsetX { get; }

    /// <summary>Core 左上の Y オフセット（Board 座標系）</summary>
    public int CoreOffsetY { get; }

    /// <summary>Core の幅</summary>
    public int CoreWidth { get; }

    /// <summary>Core の高さ</summary>
    public int CoreHeight { get; }

    /// <summary>外周（Anchor/OuterWall）を自動生成しているか</summary>
    public bool AutoGenerateOuterRings { get; }

    /// <summary>セル種別配列</summary>
    public CellType[,] Cells { get; }

    /// <summary>壁セルの起源（Core/Outer）</summary>
    public WallOrigin[,] WallOrigin { get; }

    /// <summary>Core 外にある Floor マスク</summary>
    public bool[,] OuterRingFloor { get; }

    /// <summary>Core 部分の元文字列（Gizmos 等で参照する）</summary>
    public string[] LevelOriginalCore { get; }

    /// <summary>
    /// GridModel を構築する。
    /// </summary>
    public GridModel(
        int width, int height,
        int coreOffsetX, int coreOffsetY, int coreWidth, int coreHeight,
        bool autoGenerateOuterRings,
        CellType[,] cells,
        WallOrigin[,] wallOrigin,
        bool[,] outerRingFloor,
        string[] levelOriginalCore)
    {
        Width = width;
        Height = height;
        CoreOffsetX = coreOffsetX;
        CoreOffsetY = coreOffsetY;
        CoreWidth = coreWidth;
        CoreHeight = coreHeight;
        AutoGenerateOuterRings = autoGenerateOuterRings;
        Cells = cells;
        WallOrigin = wallOrigin;
        OuterRingFloor = outerRingFloor;
        LevelOriginalCore = levelOriginalCore;
    }

    /// <summary>
    /// 盤面内の座標か判定する。
    /// </summary>
    public bool InBounds(Vector2Int p) => p.x >= 0 && p.x < Width && p.y >= 0 && p.y < Height;

    /// <summary>
    /// 指定座標が Core 領域内か判定する。
    /// </summary>
    public bool IsInsideCore(Vector2Int p)
    {
        if (!AutoGenerateOuterRings) return InBounds(p);
        return p.x >= CoreOffsetX && p.x < CoreOffsetX + CoreWidth &&
               p.y >= CoreOffsetY && p.y < CoreOffsetY + CoreHeight;
    }

    /// <summary>
    /// Board 座標を Core 座標へ変換する。
    /// </summary>
    public bool TryBoardToCore(Vector2Int boardPos, out Vector2Int corePos)
    {
        if (!AutoGenerateOuterRings)
        {
            if (!InBounds(boardPos))
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
        corePos = new Vector2Int(boardPos.x - CoreOffsetX, boardPos.y - CoreOffsetY);
        return corePos.x >= 0 && corePos.y >= 0 &&
               corePos.x < CoreWidth && corePos.y < CoreHeight;
    }

    /// <summary>
    /// Core 座標を Board 座標へ変換する。
    /// </summary>
    public Vector2Int CoreToBoard(Vector2Int corePos)
    {
        if (!AutoGenerateOuterRings) return corePos;
        return new Vector2Int(corePos.x + CoreOffsetX, corePos.y + CoreOffsetY);
    }
}
