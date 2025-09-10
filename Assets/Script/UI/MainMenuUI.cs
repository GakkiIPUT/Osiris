using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    public Button[] worldButtons;
    public Button startButton;       // スタート
    public Button settingsButton;    // 設定
    public Button collectionButton;  // コレクション
    public GameObject titlePanel;
    public GameObject worldPanel;
    private GameState gs;



    private void Awake()
    {

        gs = UnityCompat.FindFirst<GameState>();
        if (gs == null)
        {
            Debug.LogError("[MainMenuUI] GameState が見つかりません。Mainシーンに配置してください。");
            enabled = false;
            return;
        }

        if (startButton)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OnStartButton);
        }

        if (settingsButton)
        {
            settingsButton.onClick.RemoveAllListeners();
            settingsButton.onClick.AddListener(OnSettingsButton);
        }

        if (collectionButton)
        {
            collectionButton.onClick.RemoveAllListeners();
            collectionButton.onClick.AddListener(OnCollectionButton);
        }
        WireButtons();
    }

    private void OnEnable()
    {
        if (!enabled) return;

        // タイトル表示
        titlePanel?.SetActive(true);

        // ワールドボタンは非表示にしておく
        foreach (var b in worldButtons)
        {
            if (b) b.gameObject.SetActive(false);
        }

        // 最初のフォーカスをスタートボタンへ
        if (EventSystem.current && startButton)
        {
            EventSystem.current.SetSelectedGameObject(startButton.gameObject);
        }
    }

    private void OnStartButton()
    {
        // タイトル隠す
        titlePanel?.SetActive(false);

        // ワールド選択表示
        FitButtonsState();
        StartCoroutine(CoSelectFirst());
    }

    private void OnSettingsButton()
    {
        // ここで設定画面を開く処理
        Debug.Log("[MainMenuUI] 終了ボタン押下");
    }
    private void OnCollectionButton()
    {
        // ここでコレクション画面を開く処理
        Debug.Log("[MainMenuUI] コレクションボタン押下");
    }
    private System.Collections.IEnumerator CoSelectFirst()
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

    private void WireButtons()
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

    private void FitButtonsState()
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
