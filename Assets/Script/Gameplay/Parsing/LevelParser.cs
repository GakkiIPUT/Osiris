using System.Collections.Generic;

public static class LevelParser
{
    public sealed class ParseOptions
    {
        public bool AutoGenerateOuterRings;
        public int AnchorThickness = 1;
        public int OuterWallThickness = 1;
        public HashSet<char> GuardSymbols = new(); // 例: { 'G','H','I',... }
        public HashSet<char> ItemSymbols = new();  // 例: { 'i','t','d','e',... }
    }

    public static GridModel Parse(string[] level, ParseOptions opt)
    {
        if (level == null) level = new string[0];

        // Core 基本
        int coreHeight = level.Length;
        int coreWidth = coreHeight > 0 ? level[0].Length : 0;

        // Board（拡張後）サイズと Core オフセットの算出
        int coreOffsetX, coreOffsetY, width, height;
        string[] expandedRows;

        string[] levelOriginalCore = (string[])level.Clone();

        if (!opt.AutoGenerateOuterRings)
        {
            coreOffsetX = coreOffsetY = 0;
            width = coreWidth;
            height = coreHeight;
            expandedRows = (string[])level.Clone();
        }
        else
        {
            int totalPad = opt.AnchorThickness + opt.OuterWallThickness;
            coreOffsetX = totalPad;
            coreOffsetY = totalPad;
            width = coreWidth + totalPad * 2;
            height = coreHeight + totalPad * 2;

            var rows = new string[height];
            for (int y = 0; y < height; y++)
            {
                var line = new char[width];
                for (int x = 0; x < width; x++)
                {
                    bool isAnchorBand =
                        x < opt.AnchorThickness || y < opt.AnchorThickness ||
                        x >= width - opt.AnchorThickness ||
                        y >= height - opt.AnchorThickness;
                    if (isAnchorBand) { line[x] = '@'; continue; }

                    bool inOuterWall =
                        x < opt.AnchorThickness + opt.OuterWallThickness ||
                        y < opt.AnchorThickness + opt.OuterWallThickness ||
                        x >= width - (opt.AnchorThickness + opt.OuterWallThickness) ||
                        y >= height - (opt.AnchorThickness + opt.OuterWallThickness);
                    if (inOuterWall) { line[x] = '#'; continue; }

                    int cx = x - coreOffsetX;
                    int cy = y - coreOffsetY;
                    line[x] = (cx >= 0 && cx < coreWidth && cy >= 0 && cy < coreHeight)
                            ? level[cy][cx]
                            : '.';
                }
                rows[y] = new string(line);
            }
            expandedRows = rows;
        }

        // 出力配列確保
        var cells = new CellType[height, width];
        var wallOrigin = new WallOrigin[height, width];

        // セル埋め
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                char ch;
                if (opt.AutoGenerateOuterRings)
                {
                    // Core 内→元文字、Core 外→展開文字
                    if (x >= coreOffsetX && x < coreOffsetX + coreWidth &&
                        y >= coreOffsetY && y < coreOffsetY + coreHeight)
                    {
                        int cx = x - coreOffsetX;
                        int cy = y - coreOffsetY;
                        ch = levelOriginalCore[cy][cx];
                    }
                    else
                    {
                        ch = expandedRows[y][x];
                    }
                }
                else
                {
                    ch = coreHeight > 0 ? level[y][x] : '.';
                }

                // Guard / Item の記号はタイル的には Floor 扱い
                if ((opt.GuardSymbols != null && opt.GuardSymbols.Contains(ch)) ||
                    (opt.ItemSymbols != null && opt.ItemSymbols.Contains(ch)))
                {
                    cells[y, x] = CellType.Floor;
                    continue;
                }

                switch (ch)
                {
                    case '#':
                        if (opt.AutoGenerateOuterRings &&
                            (x < coreOffsetX || x >= coreOffsetX + coreWidth ||
                             y < coreOffsetY || y >= coreOffsetY + coreHeight))
                        {
                            // 外周 Wall 帯は Floor 性質（見た目や衝突は別層で表現）
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

        // 外周 Floor マスク（Core 外かつ Floor）
        bool[,] outerRingFloor = null;
        if (opt.AutoGenerateOuterRings)
        {
            outerRingFloor = new bool[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool insideCore =
                        x >= coreOffsetX && x < coreOffsetX + coreWidth &&
                        y >= coreOffsetY && y < coreOffsetY + coreHeight;
                    outerRingFloor[y, x] = (!insideCore && cells[y, x] == CellType.Floor);
                }
            }
        }

        return new GridModel(
            width, height,
            coreOffsetX, coreOffsetY, coreWidth, coreHeight,
            opt.AutoGenerateOuterRings,
            cells, wallOrigin, outerRingFloor,
            levelOriginalCore
        );
    }
}
