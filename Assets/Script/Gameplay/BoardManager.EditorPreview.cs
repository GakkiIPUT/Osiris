using UnityEngine;

public partial class BoardManager
{
    // ========= エディタプレビュー描画（GameObject生成なし） =========
    private void OnDrawGizmos()
    {
        if (!enabled || !editorPreview || level == null) return;

        // ロジックだけ最新化
        ParseCellsFromLevel();

        if (cells == null)
            return;
        if (Height != cells.GetLength(0) || Width != cells.GetLength(1))
            return;
        // 描画順：床 → 壁 → 出口 → 役者マーカー
        float y0 = floorY;
        float y1 = floorY + 0.001f;
        float y2 = floorY + 0.002f;

        // 床（Exitの足元も床で塗る）
        Gizmos.color = previewFloor;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (cells[y, x] == CellType.Floor)
                {
                    bool isOuterF = outerRingFloor != null && outerRingFloor[y, x];
                    Gizmos.color = isOuterF ? previewOuterFloor : previewFloor;
                    DrawCellGizmo(new Vector2Int(x, y), y0);
                }
                else if (cells[y, x] == CellType.Exit)
                {
                    // Exit の足元も床色で塗る場合は以下コメントアウト解除
                    // bool isOuterF = outerRingFloor != null && outerRingFloor[y, x];
                    // Gizmos.color = isOuterF ? previewOuterFloor : previewFloor;
                    // DrawCellGizmo(new Vector2Int(x, y), y0);
                }
            }
        }
        // 壁
        Gizmos.color = previewWall;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (cells[y, x] == CellType.Wall)
                    DrawCellGizmo(new Vector2Int(x, y), y1);

        // アンカー（@ / CellType.Anchor）
        Gizmos.color = previewAnchor;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (cells[y, x] == CellType.Anchor)
                    DrawCellGizmo(new Vector2Int(x, y), y1 /* or y1 + 0.0001f */);

        // 落とし穴（x / CellType.Pit）
        Gizmos.color = previewPit;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (cells[y, x] == CellType.Pit)
                    DrawCellGizmo(new Vector2Int(x, y), y1);

        // 出口（Core 内のみ記号評価。Core 外は cells で Exit 表示済み）
        Gizmos.color = previewExit;
        if (autoGenerateOuterRings)
        {
            for (int y = 0; y < coreHeight; y++)
            {
                for (int x = 0; x < coreWidth; x++)
                {
                    if (levelOriginalCore[y][x] == 'E')
                    {
                        var wp = new Vector2Int(x + coreOffsetX, y + coreOffsetY);
                        DrawCellGizmo(wp, y2);
                    }
                }
            }
        }
        else
        {
            for (int y = 0; y < level.Length; y++)
                for (int x = 0; x < level[y].Length; x++)
                    if (level[y][x] == 'E')
                        DrawCellGizmo(new Vector2Int(x, y), y2);
        }

        // プレイヤー
        Gizmos.color = previewP;
        if (autoGenerateOuterRings)
        {
            for (int y = 0; y < coreHeight; y++)
            {
                for (int x = 0; x < coreWidth; x++)
                {
                    if (levelOriginalCore[y][x] == 'P')
                    {
                        var wp = new Vector2Int(x + coreOffsetX, y + coreOffsetY);
                        DrawActorDot(wp, y2 + 0.001f);
                    }
                }
            }
        }
        else
        {
            for (int y = 0; y < level.Length; y++)
                for (int x = 0; x < level[y].Length; x++)
                    if (level[y][x] == 'P')
                        DrawActorDot(new Vector2Int(x, y), y2 + 0.001f);
        }

        // Guard 記号マーカー
        if (autoGenerateOuterRings)
        {
            for (int y = 0; y < coreHeight; y++)
            {
                for (int x = 0; x < coreWidth; x++)
                {
                    char ch = levelOriginalCore[y][x];
                    for (int gi = 0; gi < guardTypes.Count; gi++)
                    {
                        if (!string.IsNullOrEmpty(guardTypes[gi].symbol) &&
                            guardTypes[gi].symbol[0] == ch)
                        {
                            Gizmos.color = guardTypes[gi].previewColor;
                            var wp = new Vector2Int(x + coreOffsetX, y + coreOffsetY);
                            DrawActorDot(wp, y2 + 0.001f);
                            break;
                        }
                    }
                }
            }
        }
        else
        {
            for (int y = 0; y < level.Length; y++)
            {
                for (int x = 0; x < level[y].Length; x++)
                {
                    char ch = level[y][x];
                    for (int gi = 0; gi < guardTypes.Count; gi++)
                    {
                        if (!string.IsNullOrEmpty(guardTypes[gi].symbol) &&
                            guardTypes[gi].symbol[0] == ch)
                        {
                            Gizmos.color = guardTypes[gi].previewColor;
                            DrawActorDot(new Vector2Int(x, y), y2 + 0.001f);
                            break;
                        }
                    }
                }
            }
        }

        // Item 記号マーカー
        if (autoGenerateOuterRings)
        {
            for (int y = 0; y < coreHeight; y++)
            {
                for (int x = 0; x < coreWidth; x++)
                {
                    char ch = levelOriginalCore[y][x];
                    for (int ii = 0; ii < itemTypes.Count; ii++)
                    {
                        if (!string.IsNullOrEmpty(itemTypes[ii].symbol) &&
                            itemTypes[ii].symbol[0] == ch)
                        {
                            Gizmos.color = itemTypes[ii].previewColor;
                            var wp = new Vector2Int(x + coreOffsetX, y + coreOffsetY);
                            DrawActorDot(wp, y2 + 0.001f);
                            break;
                        }
                    }
                }
            }
        }
        else
        {
            for (int y = 0; y < level.Length; y++)
            {
                for (int x = 0; x < level[y].Length; x++)
                {
                    char ch = level[y][x];
                    for (int ii = 0; ii < itemTypes.Count; ii++)
                    {
                        if (!string.IsNullOrEmpty(itemTypes[ii].symbol) &&
                            itemTypes[ii].symbol[0] == ch)
                        {
                            Gizmos.color = itemTypes[ii].previewColor;
                            DrawActorDot(new Vector2Int(x, y), y2 + 0.001f);
                            break;
                        }
                    }
                }
            }
        }
    }

    private void DrawCellGizmo(Vector2Int p, float y)
    {
        Vector3 c = GridToWorld(p) + new Vector3(0.5f, y, 0.5f);
        Gizmos.DrawCube(c, new Vector3(1f, 0.001f, 1f));
    }

    private void DrawActorDot(Vector2Int p, float y)
    {
        Vector3 c = GridToWorld(p) + new Vector3(0.5f, y, 0.5f);
        Gizmos.DrawCube(c, new Vector3(0.35f, 0.002f, 0.35f));
    }
}
