using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    public Button[] worldButtons; // インスペクタで 1面,2面…のボタンを並べる
    GameState gs;

    void Awake()
    {
        gs = UnityCompat.FindFirst<GameState>(); // ← FindObjectOfTypeは使わない
        if (gs == null)
        {
            Debug.LogError("[MainMenuUI] GameState が見つかりません。Mainシーンに常駐させてください。");
            enabled = false;
            return;
        }

        WireButtons();
    }

    void OnEnable()
    {
        if (!enabled) return;
        FitButtonsState();
    }

    void WireButtons()
    {
        if (worldButtons == null || worldButtons.Length == 0) return;

        for (int i = 0; i < worldButtons.Length; i++)
        {
            var btn = worldButtons[i];
            if (!btn) continue;

            // 二重登録防止
            btn.onClick.RemoveAllListeners();

            int idx = i; // ループ変数のキャプチャ対策
            btn.onClick.AddListener(() =>
            {
                if (gs?.catalog?.worlds == null || idx < 0 || idx >= gs.catalog.worlds.Count)
                {
                    Debug.LogWarning($"[MainMenuUI] World index {idx} は無効です。");
                    return;
                }
                gs.worldIndex = idx;
                gs.stageIndex = 0; // 先頭ステージから
                SceneNavigator.GoStage();
            });

            var label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label && gs.catalog != null && gs.catalog.worlds != null && idx < gs.catalog.worlds.Count)
            {
                // World の表示名が別にある場合はそちらに差し替え
                label.text = gs.catalog.worlds[idx].id;
            }
        }
    }

    // 利用可能なWorld数に合わせて、有効/無効や大きさを整える
    void FitButtonsState()
    {
        int available = (gs?.catalog?.worlds != null) ? gs.catalog.worlds.Count : 0;

        for (int i = 0; i < worldButtons.Length; i++)
        {
            var btn = worldButtons[i];
            if (!btn) continue;
            btn.interactable = (i < available);
            btn.gameObject.SetActive(i < available); // 余剰ボタンは非表示にするなら有効
        }
    }
}
