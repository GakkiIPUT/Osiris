using System.Linq;
using UnityEngine;

/// <summary>
/// ステージセットから指定インデックスのステージをロードし、BoardManagerへ適用する管理クラス。
/// パラメータ（parRot/parAP）の反映や、現在ステージ情報の取得も提供する。
/// </summary>
public class StageManager : MonoBehaviour
{
    public StageSet stageSet;
    public int currentIndex = 0;
    public BoardManager board;

    /// <summary>
    /// BoardManager の参照を解決する。
    /// </summary>
    private void Awake()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
    }

    /// <summary>
    /// 指定インデックスのステージをロードして盤面を構築する。
    /// parRot は GameFlow、parAP は TurnManager に反映する。
    /// </summary>
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

    /// <summary>
    /// parRot を GameFlow に伝搬する（UI 表示用）。
    /// </summary>
    private void PushParToGameFlow(int par)
    {
        var gf = UnityCompat.FindFirst<GameFlow>();
        if (gf != null) gf.parRot = par;
    }

    /// <summary>現在のステージを再読み込みする。</summary>
    public void ReloadCurrent() => Load(currentIndex);

    /// <summary>次のステージを読み込む。</summary>
    public void Next() => Load(currentIndex + 1);

    /// <summary>前のステージを読み込む。</summary>
    public void Prev() => Load(currentIndex - 1);

    /// <summary>現在のステージIDを返す（なければ "-"）。</summary>
    public string Getid()
    {
        if (stageSet == null || stageSet.stages == null || stageSet.stages.Count == 0) return "-";
        return stageSet.stages[Mathf.Clamp(currentIndex, 0, stageSet.stages.Count - 1)].id;
    }

    /// <summary>ステージセット内の全ステージIDを配列で返す。</summary>
    public string[] GetAllNames()
    {
        if (stageSet == null || stageSet.stages == null) return new string[0];
        return stageSet.stages.Select(s => s.id).ToArray();
    }

    /// <summary>
    /// ステージ選択状態に応じてロード or 現在のBoard設定でビルドを実行する。
    /// </summary>
    public void CreateStage()
    {
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();

        bool hasStageSelected = stageSet != null && stageSet.stages != null && stageSet.stages.Count > 0
            && currentIndex >= 0 && currentIndex < stageSet.stages.Count;

        if (hasStageSelected)
        {
            Load(currentIndex);
        }
        else
        {
            if (board != null)
            {
                board.Build();
            }
        }
    }

    /// <summary>
    /// 現在のステージ Entry を返す（GameFlow のコレクション表示で使用）。
    /// </summary>
    public StageSet.Entry GetCurrentEntry()
    {
        if (stageSet == null || stageSet.stages == null || stageSet.stages.Count == 0) return null;
        int i = Mathf.Clamp(currentIndex, 0, stageSet.stages.Count - 1);
        return stageSet.stages[i];
    }
}
