using System.Linq;
using UnityEngine;

public class StageManager : MonoBehaviour
{
    public StageSet stageSet;
    public int currentIndex = 0;
    public BoardManager board;

    private void Awake()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
    }

    public void Load(int index)
    {
        if (stageSet == null || stageSet.stages == null || stageSet.stages.Count == 0)
        {
            Debug.LogError("StageManager.Load: stageSet is not assigned or empty.");
            return;
        }

        currentIndex = Mathf.Clamp(index, 0, stageSet.stages.Count - 1);
        var entry = stageSet.stages[currentIndex];

        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (board == null) { Debug.LogError("BoardManager not found."); return; }

        if (entry.mapTxt != null)
            board.SetLevelFromText(entry.mapTxt.text, rebuild: true);
        else
            board.Build();

        // 旧仕様（回転パー）は従来どおり GameFlow へ
        PushParToGameFlow(entry.parRot);

        // 新仕様（APパー）は TurnManager へ
        var tm = UnityCompat.FindFirst<TurnManager>();
        if (tm != null) tm.parAP = Mathf.Max(0, entry.parAP);

#if UNITY_EDITOR
        Debug.Log($"[StageManager] Loaded {currentIndex}: {entry.id} (parRot={entry.parRot}, parAP={entry.parAP})");
#endif
    }

    private void PushParToGameFlow(int par)
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

    public void CreateStage()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();

        // ステージ選択済みか判定
        bool hasStageSelected = stageSet != null && stageSet.stages != null && stageSet.stages.Count > 0
            && currentIndex >= 0 && currentIndex < stageSet.stages.Count;

        if (hasStageSelected)
        {
            Load(currentIndex); // 選択したステージをロード
        }
        else
        {
            if (board != null)
            {
                board.Build(); // BoardManager.level（デフォルト）で生成
            }
        }
    }

    // ★ 現在のステージ Entry を取得（GameFlow がコレクション表示用に参照）
    public StageSet.Entry GetCurrentEntry()
    {
        if (stageSet == null || stageSet.stages == null || stageSet.stages.Count == 0) return null;
        int i = Mathf.Clamp(currentIndex, 0, stageSet.stages.Count - 1);
        return stageSet.stages[i];
    }
}
