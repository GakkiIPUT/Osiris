using System.Collections.Generic;
using UnityEngine;

public class StageManager : MonoBehaviour
{
    [Header("Refs")]
    public BoardManager board;

    [Header("Stages (drag LevelAsset here)")]
    public List<LevelAsset> levels = new List<LevelAsset>();

    [Header("Runtime")]
    public int currentIndex = 0;

    void Awake()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
    }

    void Start()
    {
        if (levels.Count > 0) LoadIndex(currentIndex);
    }

    public void LoadIndex(int idx)
    {
        if (levels == null || levels.Count == 0) return;
        idx = Mathf.Clamp(idx, 0, levels.Count - 1);
        currentIndex = idx;

        var lv = levels[idx];
        if (lv == null || lv.rows == null || lv.rows.Length == 0) return;

        board.SetLevel(lv.rows);     // ★ BoardManagerに追加するAPI（下で説明）
        Debug.Log($"Loaded stage: {(string.IsNullOrEmpty(lv.id) ? $"#{idx}" : lv.id)}");
    }

    public void LoadById(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] != null && levels[i].id == id)
            {
                LoadIndex(i);
                return;
            }
        }
        Debug.LogWarning($"Stage id '{id}' not found.");
    }

    public void Next()
    {
        if (levels == null || levels.Count == 0) return;
        if (currentIndex + 1 < levels.Count) LoadIndex(currentIndex + 1);
    }

    public void Prev()
    {
        if (levels == null || levels.Count == 0) return;
        if (currentIndex - 1 >= 0) LoadIndex(currentIndex - 1);
    }

    // デバッグ操作（任意）
    void Update()
    {
        // , で前 / . で次
        if (Input.GetKeyDown(KeyCode.Comma)) Prev();
        if (Input.GetKeyDown(KeyCode.Period)) Next();
    }
}
