using System.Collections.Generic;
using UnityEngine;

public partial class BoardManager
{
    // ===== グリッド <-> ワールド座標 =====
    public Vector3 GridToWorld(Vector2Int p) => new Vector3(p.x, 0f, p.y);
    public Vector2Int WorldToGrid(Vector3 w) => new Vector2Int(Mathf.RoundToInt(w.x), Mathf.RoundToInt(w.z));
    public Vector3 GridToWorldActor(Vector2Int p) => GridToWorld(p) + new Vector3(0, actorYOffset, 0f);
    public bool InBounds(Vector2Int p) => p.x >= 0 && p.x < Width && p.y >= 0 && p.y < Height;

    // ===== レベルテキスト整形 =====
    private static string[] NormalizeRows(string[] rows, char pad = '.')
    {
        if (rows == null || rows.Length == 0) return new string[0];
        int w = 0;
        for (int i = 0; i < rows.Length; i++)
            w = Mathf.Max(w, rows[i].Length);

        var outRows = new string[rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            var s = rows[y];
            if (s.Length == w) { outRows[y] = s; continue; }
            // 足りないぶんを床('.')
            if (s.Length < w) outRows[y] = s + new string(pad, w - s.Length);
            else outRows[y] = s.Substring(0, w); // 長すぎる場合は右端をカット
        }
        return outRows;
    }

    // 改行コードや空行トリムに強い行分割
    public static string[] ParseRows(string text)
    {
        if (string.IsNullOrEmpty(text)) return new string[0];
        text = text.Replace("\r", "");
        var lines = text.Split('\n');
        var rows = new List<string>(lines.Length);

        foreach (var raw in lines)
        {
            var s = raw.TrimEnd();            // 末尾スペース除去（行長の不揃い対策）
            if (s.Length == 0) continue;      // 空行はスキップ
            rows.Add(s);
        }
        return rows.ToArray();
    }
}
