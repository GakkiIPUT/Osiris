using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// タイトル/ワールド選択のメインメニューUI。GameState/SceneNavigator と連携して遷移する。
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    public Button[] worldButtons;
    public Button startButton;       // スタート
    public Button settingsButton;    // 設定
    public Button collectionButton;  // コレクション
    public GameObject titlePanel;
    public GameObject worldPanel;
    private GameState gs;

    /// <summary>GameState の解決およびボタン配線。</summary>
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

    /// <summary>タイトル表示と初期フォーカス設定、ワールドボタンの初期非表示。</summary>
    private void OnEnable()
    {
        if (!enabled) return;

        titlePanel?.SetActive(true);

        foreach (var b in worldButtons)
        {
            if (b) b.gameObject.SetActive(false);
        }

        if (EventSystem.current && startButton)
        {
            EventSystem.current.SetSelectedGameObject(startButton.gameObject);
        }
    }

    /// <summary>スタート押下時：タイトル非表示→ワールド選択表示へ切り替え。</summary>
    private void OnStartButton()
    {
        titlePanel?.SetActive(false);
        FitButtonsState();
        StartCoroutine(CoSelectFirst());
    }

    /// <summary>設定ボタン押下時の処理（プレースホルダ）。</summary>
    private void OnSettingsButton()
    {
        Debug.Log("[MainMenuUI] 終了ボタン押下");
    }

    /// <summary>コレクションボタン押下時の処理（プレースホルダ）。</summary>
    private void OnCollectionButton()
    {
        Debug.Log("[MainMenuUI] コレクションボタン押下");
    }

    /// <summary>ワールドボタンを再構築した次フレームで最初の有効ボタンを選択する。</summary>
    private System.Collections.IEnumerator CoSelectFirst()
    {
        yield return null;
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

    /// <summary>ワールドボタンにワールド選択とラベル設定を配線する。</summary>
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

    /// <summary>利用可能なワールド数に合わせてボタンの活性/表示を切り替える。</summary>
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
