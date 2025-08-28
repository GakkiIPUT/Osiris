using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class MainMenuUI : MonoBehaviour
{
    public Button[] worldButtons;
    GameState gs;

    void Awake()
    {
        gs = UnityCompat.FindFirst<GameState>();
        if (gs == null)
        {
            Debug.LogError("[MainMenuUI] GameState が見つかりません。Mainシーンに配置してください。");
            enabled = false;
            return;
        }
        WireButtons();
    }

    void OnEnable()
    {
        if (!enabled) return;
        FitButtonsState();
        StartCoroutine(CoSelectFirst());
    }

    System.Collections.IEnumerator CoSelectFirst()
    {
        yield return null; // 次フレームで選択（UI再構築後）
        if (EventSystem.current == null || worldButtons == null) yield break;
        foreach (var b in worldButtons)
        {
            if (b && b.gameObject.activeInHierarchy && b.interactable)
            {
                EventSystem.current.SetSelectedGameObject(b.gameObject);
                break;
            }
        }
    }

    void WireButtons()
    {
        if (worldButtons == null || worldButtons.Length == 0) return;

        for (int i = 0; i < worldButtons.Length; i++)
        {
            var btn = worldButtons[i];
            if (!btn) continue;

            btn.onClick.RemoveAllListeners();

            int idx = i;
            btn.onClick.AddListener(() =>
            {
                if (gs?.catalog?.worlds == null || idx < 0 || idx >= gs.catalog.worlds.Count)
                {
                    Debug.LogWarning($"[MainMenuUI] World index {idx} は無効。");
                    return;
                }
                gs.worldIndex = idx;
                gs.stageIndex = 0;
                SceneNavigator.GoStage();
            });

            var label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label && gs.catalog != null && gs.catalog.worlds != null && idx < gs.catalog.worlds.Count)
            {
                label.text = gs.catalog.worlds[idx].id;
            }
        }
    }

    void FitButtonsState()
    {
        int available = (gs?.catalog?.worlds != null) ? gs.catalog.worlds.Count : 0;

        for (int i = 0; i < worldButtons.Length; i++)
        {
            var btn = worldButtons[i];
            if (!btn) continue;
            bool on = i < available;
            btn.interactable = on;
            btn.gameObject.SetActive(on);
        }
    }
}
