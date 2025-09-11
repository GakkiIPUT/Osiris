using System.Collections.Generic;
using UnityEngine;

public partial class BoardManager
{
    /// <summary>
    /// グリッド座標をワールド座標へ変換する（床Y=0）。
    /// </summary>
    public Vector3 GridToWorld(Vector2Int p) => new Vector3(p.x, 0f, p.y);

    /// <summary>
    /// ワールド座標を最寄りのグリッド座標へ変換する。
    /// </summary>
    public Vector2Int WorldToGrid(Vector3 w) => new Vector2Int(Mathf.RoundToInt(w.x), Mathf.RoundToInt(w.z));

    /// <summary>
    /// グリッド座標をアクター用のワールド座標へ変換する（高さオフセット適用）。
    /// </summary>
    public Vector3 GridToWorldActor(Vector2Int p) => GridToWorld(p) + new Vector3(0, actorYOffset, 0f);

    /// <summary>
    /// ボード内の座標かを判定する。
    /// </summary>
    public bool InBounds(Vector2Int p) => p.x >= 0 && p.x < Width && p.y >= 0 && p.y < Height;

    /// <summary>
    /// レベル行配列の右パディング/トリムを行い、行長を揃える。
    /// </summary>
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
            if (s.Length < w) outRows[y] = s + new string(pad, w - s.Length);
            else outRows[y] = s.Substring(0, w);
        }
        return outRows;
    }

    /// <summary>
    /// テキストから行配列へ変換する（改行/空行/末尾空白に強い）。
    /// </summary>
    public static string[] ParseRows(string text)
    {
        if (string.IsNullOrEmpty(text)) return new string[0];
        text = text.Replace("\r", "");
        var lines = text.Split('\n');
        var rows = new List<string>(lines.Length);

        foreach (var raw in lines)
        {
            var s = raw.TrimEnd();
            if (s.Length == 0) continue;
            rows.Add(s);
        }
        return rows.ToArray();
    }
}
