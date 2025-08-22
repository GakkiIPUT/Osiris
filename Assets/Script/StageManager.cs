using System.Linq;
using UnityEngine;

public class StageManager : MonoBehaviour
{
    public StageSet stageSet;
    public int currentIndex = 0;
    public BoardManager board;

    void Awake()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (stageSet != null) Load(currentIndex);
    }

    public void Load(int index)
    {
        if (stageSet == null || stageSet.stages == null || stageSet.stages.Count == 0)
        {
            Debug.LogError("StageManager.Load: stageSet is not assigned or empty.");
            return;
        }

        // 安全にクランプして currentIndex を更新
        currentIndex = Mathf.Clamp(index, 0, stageSet.stages.Count - 1);

        var entry = stageSet.stages[currentIndex];

        // Board参照を確保（見つかったらキャッシュ）
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (board == null)
        {
            Debug.LogError("BoardManager not found.");
            return;
        }

#if UNITY_EDITOR
        if (board.devUseSceneLevelInEditor)
        {
            board.Build();                 // ← LevelPainterで塗った BoardManager.level をそのまま再生
            PushParToGameFlow(stageSet.stages[index].parRot);
            currentIndex = index;
            return;
        }
#endif

        // TextAsset から直接読み込み
        if (entry.mapTxt != null)
        {
            board.SetLevelFromText(entry.mapTxt.text, rebuild: true);
        }
        else
        {
            Debug.LogWarning($"Stage '{entry.id}' has no mapTxt assigned. Using existing BoardManager.level.");
            if (board.player != null)
            {
                board.player.ClearGhost();
            }
            board.Build();
        }
        PushParToGameFlow(stageSet.stages[index].parRot);
        currentIndex = index;
        // parRot を GameFlow に反映（任意）
        var gf = UnityCompat.FindFirst<GameFlow>();
        if (gf != null) gf.parRot = entry.parRot;
    }
    void PushParToGameFlow(int par)
    {
        var gf = UnityCompat.FindFirst<GameFlow>();
        if (gf != null) gf.parRot = par;
    }
    public void ReloadCurrent() => Load(currentIndex);
    public void Next() => Load(currentIndex + 1);
    public void Prev() => Load(currentIndex - 1);

    public string Getid()
    {
        if (stageSet == null || stageSet.stages == null || stageSet.stages.Count == 0) return "-";
        return stageSet.stages[Mathf.Clamp(currentIndex, 0, stageSet.stages.Count - 1)].id;
    }

    public string[] GetAllNames()
    {
        if (stageSet == null || stageSet.stages == null) return new string[0];
        return stageSet.stages.Select(s => s.id).ToArray();
    }

    // 旧Ascii読みは不要になったので使わない（残すなら staticユーティリティとしてどうぞ）
    // static string[] ParseAscii(TextAsset ta) { ... }
}
