using UnityEngine;

public class GridModel
{
    // 盤面サイズ（拡張後＝Board座標系）
    public int Width { get; }
    public int Height { get; }

    // Core（元レベル）情報
    public int CoreOffsetX { get; }
    public int CoreOffsetY { get; }
    public int CoreWidth { get; }
    public int CoreHeight { get; }

    // 外周生成の有無
    public bool AutoGenerateOuterRings { get; }

    // セル情報
    public CellType[,] Cells { get; }
    public WallOrigin[,] WallOrigin { get; }
    public bool[,] OuterRingFloor { get; }

    // Gizmosなどで使う元 Core の文字列（必要な場合のみ）
    public string[] LevelOriginalCore { get; }

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

    public bool InBounds(Vector2Int p) => p.x >= 0 && p.x < Width && p.y >= 0 && p.y < Height;

    public bool IsInsideCore(Vector2Int p)
    {
        if (!AutoGenerateOuterRings) return InBounds(p);
        return p.x >= CoreOffsetX && p.x < CoreOffsetX + CoreWidth &&
               p.y >= CoreOffsetY && p.y < CoreOffsetY + CoreHeight;
    }

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

    public Vector2Int CoreToBoard(Vector2Int corePos)
    {
        if (!AutoGenerateOuterRings) return corePos;
        return new Vector2Int(corePos.x + CoreOffsetX, corePos.y + CoreOffsetY);
    }
}
