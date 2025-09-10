using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock; // DualShockGamepad, DualSenseGamepadHID
#endif

public class GlobalEscMenu : MonoBehaviour
{
    [Header("ESC Menu (Common)")]
    public GameObject escMenuPanel;
    public Button closeButton;
    public Button toMainButton;
    public Button toStageButton;
    public Button quitButton;
    public bool pauseOnEsc = true;

    [Header("Key Bindings (optional)")]
    public Button bindResetKeyButton;
    public TMP_Text bindResetKeyLabel;

    [Header("Selection")]
    public Selectable firstSelected;

    private bool waitingResetRebind = false;
    public static bool IsMenuOpen { get; private set; } = false;

    private CanvasGroup _panelCg;
    private UIFocusScope _focusScope;

    private void Start()
    {
        if (escMenuPanel) escMenuPanel.SetActive(false);
        IsMenuOpen = false;

        // Game シーンでは GameFlow が ESC を握るので無効化
        if (UnityCompat.FindFirst<GameFlow>() != null)
        {
            enabled = false;
            return;
        }

        if (closeButton) closeButton.onClick.AddListener(Close);
        if (toMainButton) toMainButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoMain(); });
        if (toStageButton) toStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });
        if (quitButton) quitButton.onClick.AddListener(QuitGame);

        if (bindResetKeyButton) bindResetKeyButton.onClick.AddListener(BeginRebindResetKey);
        UpdateResetKeyLabel();
    }

    private void Update()
    {
        if (!enabled) return;

        // キーバインド待機
        if (waitingResetRebind)
        {
            if (InputBindings.TryGetAnyKeyboardKeyDown(out var kc))
            {
                if (kc == KeyCode.Escape)
                {
                    waitingResetRebind = false;
                    InputBindings.EndCapture();
                    UpdateResetKeyLabel();
                }
                else
                {
                    InputBindings.SetResetKey(kc);
                    waitingResetRebind = false;
                    InputBindings.EndCapture();
                    UpdateResetKeyLabel();
                }
            }
        }

        // KB: ESC で開閉
        if (Input.GetKeyDown(KeyCode.Escape)) ToggleMenu();

#if ENABLE_INPUT_SYSTEM
        var gp = Gamepad.current;
        if (gp != null && gp.startButton.wasPressedThisFrame) ToggleMenu();

        // PS4/PS5 対応: Options / Touchpadからでもトグル
        var ds4 = DualShockGamepad.current;
        if (ds4 != null && (ds4.optionsButton.wasPressedThisFrame || ds4.touchpadButton.wasPressedThisFrame))
            ToggleMenu();

        var ds5 = DualSenseGamepadHID.current;
        if (ds5 != null && (ds5.optionsButton.wasPressedThisFrame || ds5.touchpadButton.wasPressedThisFrame))
            ToggleMenu();
#endif
    }

    private void OnDisable()
    {
        if (IsMenuOpen) ResumeIfPaused();
        IsMenuOpen = false;
        waitingResetRebind = false;
        InputBindings.EndCapture();
        if (escMenuPanel) escMenuPanel.SetActive(false);
    }

    private void ToggleMenu()
    {
        if (escMenuPanel && escMenuPanel.activeSelf) Close();
        else Open();
    }

    private void Open()
    {
        if (!escMenuPanel) return;

        // モーダル化（パッド/マウス両方を前面で受ける）
        EnsureCanvasGroupAndFocusScope();

        escMenuPanel.SetActive(true);
        escMenuPanel.transform.SetAsLastSibling();

        // レイキャスト/操作を前面で受け止める
        _panelCg.blocksRaycasts = true;
        _panelCg.interactable = true;
        _panelCg.alpha = 1f;

        if (pauseOnEsc) Time.timeScale = 0f;
        IsMenuOpen = true;
        UpdateResetKeyLabel();

        // 最初の選択を確実にフォーカス
        if (_focusScope != null)
        {
            _focusScope.firstSelected = firstSelected ? firstSelected : (Selectable)closeButton;
            _focusScope.focusOnEnable = true;
            _focusScope.trapFocus = true;
            // 既にActiveにした後なので明示的にフォーカス実行
            _focusScope.FocusFirst();
        }
        else
        {
            StartCoroutine(CoFocusFirst());
        }
    }

    private System.Collections.IEnumerator CoFocusFirst()
    {
        yield return null;
        var select = firstSelected ? firstSelected : (Selectable)closeButton;
        if (select && EventSystem.current)
        {
            EventSystem.current.SetSelectedGameObject(select.gameObject);
        }
    }

    private void Close()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(false);
        waitingResetRebind = false;
        InputBindings.EndCapture();
        ResumeIfPaused();
        IsMenuOpen = false;

        if (EventSystem.current && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void ResumeIfPaused()
    {
        if (Time.timeScale == 0f) Time.timeScale = 1f;
    }

    private void QuitGame()
    {
        ResumeIfPaused();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
        // WebGL では終了不可
#else
#if UNITY_ANDROID
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                activity.Call("finishAndRemoveTask");
            }
        }
        catch { }
#endif
        Application.Quit(0);
#endif
    }

    private void BeginRebindResetKey()
    {
        waitingResetRebind = true;
        InputBindings.BeginCapture();
        if (bindResetKeyLabel) bindResetKeyLabel.text = "リセット: （次の入力を待機）";
    }

    private void UpdateResetKeyLabel()
    {
        if (bindResetKeyLabel) bindResetKeyLabel.text = $"リセット: {InputBindings.GetKeyDisplay(InputBindings.ResetKey)}";
    }

    private void EnsureCanvasGroupAndFocusScope()
    {
        _panelCg = escMenuPanel.GetComponent<CanvasGroup>();
        if (_panelCg == null) _panelCg = escMenuPanel.AddComponent<CanvasGroup>();

        _focusScope = escMenuPanel.GetComponent<UIFocusScope>();
        if (_focusScope == null) _focusScope = escMenuPanel.AddComponent<UIFocusScope>();
    }
}