using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    bool waitingResetRebind = false;

    void Start()
    {
        if (escMenuPanel) escMenuPanel.SetActive(false);

        // Game シーンでは GameFlow 側の ESC を使う
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

    void Update()
    {
        if (!enabled) return;

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

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (escMenuPanel && escMenuPanel.activeSelf) Close();
            else Open();
        }
    }

    void Open()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(true);
        escMenuPanel.transform.SetAsLastSibling();
        if (pauseOnEsc) Time.timeScale = 0f;
        UpdateResetKeyLabel();
    }

    void Close()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(false);
        waitingResetRebind = false;
        InputBindings.EndCapture(); // 念のため
        ResumeIfPaused();
    }

    void ResumeIfPaused()
    {
        if (Time.timeScale == 0f) Time.timeScale = 1f;
    }

    void QuitGame()
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

    void BeginRebindResetKey()
    {
        waitingResetRebind = true;
        InputBindings.BeginCapture();
        if (bindResetKeyLabel) bindResetKeyLabel.text = "リセット: （押して設定中…）";
    }

    void UpdateResetKeyLabel()
    {
        if (bindResetKeyLabel) bindResetKeyLabel.text = $"リセット: {InputBindings.GetKeyDisplay(InputBindings.ResetKey)}";
    }
}