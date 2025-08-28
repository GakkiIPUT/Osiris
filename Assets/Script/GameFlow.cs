using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock; // PS対応など
#endif 

public class GameFlow : MonoBehaviour
{
    [Header("Esc Menu")]
    public GameObject escMenuPanel;
    public Button escCloseButton;
    public Button escToStageButton;
    public Button escQuitButton; // 終了（強制終了）
    public bool pauseOnEsc = true;

    [Header("Game Over")]
    public GameObject gameOverPanel;
    public Button retryButton;
    public Button toMenuButton;

    [Header("Clear (Goal)")]
    public GameObject clearPanel;
    public Button clearToStageButton;
    public bool pauseOnClear = true;

    [Header("Clear Result (optional)")]
    [Tooltip("目標（パー）：ステージ内に表記")]
    public int parRot = 6;
    public int parAP = 6; // ※ 追加: ActionPoint用パー値
    public TMP_Text rankText;          // 例: "S"
    public TMP_Text scoreText;         // 例: "92"
    public TMP_Text detailRotText;     // 例: "回転 8 / 6（+2）"
    public TMP_Text detailRetryText;   // 例: "リトライ 1"
    public TMP_Text resultRankText;    // 結果表示用ランク
    public TMP_Text resultScoreText;   // 結果表示用スコア
    public TMP_Text detailStepText;    // 例: "歩数 8"

    [Header("Treasure (Collection) UI")]
    public GameObject treasurePanel;   // クリアパネル表示中に重ねて最上に出す
    public TMP_Text treasureText;      // 表示: 「◯◯を入手！」
    public Image treasureImage;        // コレクションSprite
    public Button treasureCloseButton; // とじる

    [Header("Key Bindings (in ESC)")]
    public Button bindResetKeyButton;
    public TMP_Text bindResetKeyLabel;

    StageManager stage;
    TurnManager turn;

    // クリア表示後に使う: コレクションパネルを最上表示するためのフラグ
    bool treasureOverlayPending = false;
    bool waitingResetRebind = false;

    // 最前面パネルの追跡（Padフォーカス更新のため）
    GameObject _lastTopPanel = null;

    void Start()
    {
        var gs = UnityCompat.FindFirst<GameState>();
        stage = UnityCompat.FindFirst<StageManager>();

        // ステージ選択経由フラグを確認し、次回のためにクリア
        bool viaStageSelect = PlayerPrefs.GetInt("enteredViaStageSelect", 0) == 1;
        PlayerPrefs.SetInt("enteredViaStageSelect", 0);
        PlayerPrefs.Save();

        // レベルペインタのオーバーライド読み込み
        bool hasDevOverride = PlayerPrefs.GetInt("dev_level_override_present", 0) == 1;
        string devText = hasDevOverride ? PlayerPrefs.GetString("dev_level_override_text", "") : "";

        if (!viaStageSelect && hasDevOverride && !string.IsNullOrEmpty(devText))
        {
            var board = UnityCompat.FindFirst<BoardManager>();
            if (board != null)
            {
                board.SetLevelFromText(devText, rebuild: true);
#if UNITY_EDITOR
                Debug.Log("[GameFlow] Loaded map from LevelPainter override.");
#endif
            }
        }
        else
        {
            if (stage != null)
            {
                int widx = gs ? gs.worldIndex : 0;
                int sidx = gs ? gs.stageIndex : 0;

                widx = PlayerPrefs.GetInt("lastWorldIndex", widx);
                sidx = PlayerPrefs.GetInt("lastStageIndex", sidx);

                var catalog = gs ? gs.catalog : null;
                if (catalog != null && catalog.worlds != null && catalog.worlds.Count > 0)
                {
                    widx = Mathf.Clamp(widx, 0, catalog.worlds.Count - 1);
                    var world = catalog.worlds[widx];

                    if (world != null && world.stageSet != null && world.stageSet.stages != null && world.stageSet.stages.Count > 0)
                    {
                        sidx = Mathf.Clamp(sidx, 0, world.stageSet.stages.Count - 1);

                        stage.stageSet = world.stageSet;
                        stage.Load(sidx);

#if UNITY_EDITOR
                        Debug.Log($"[GameFlow] Load stage by selection: worldIndex={widx}, stageIndex={sidx}, id={world.stageSet.stages[sidx].id}");
#endif
                    }
                    else
                    {
                        Debug.LogError("[GameFlow] StageSet が未設定です。");
                    }
                }
                else
                {
                    Debug.LogError("[GameFlow] GameCatalog を取得できません。");
                }
            }
        }

        // 初期UI非表示
        turn = UnityCompat.FindFirst<TurnManager>();

        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);
        if (treasurePanel) treasurePanel.SetActive(false);

        // ESC メニュー
        if (escCloseButton) escCloseButton.onClick.AddListener(CloseEscMenu);
        if (escToStageButton) escToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });
        if (escQuitButton) escQuitButton.onClick.AddListener(QuitGame);

        // GameOver/クリア ボタン
        if (retryButton) retryButton.onClick.AddListener(RequestRetry);
        if (toMenuButton) toMenuButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });
        if (clearToStageButton) clearToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });

        // コレクションパネル
        if (treasureCloseButton) treasureCloseButton.onClick.AddListener(() =>
        {
            if (treasurePanel) treasurePanel.SetActive(false);
        });

        // キーコンフィグ（ESC内）
        if (bindResetKeyButton) bindResetKeyButton.onClick.AddListener(BeginRebindResetKey);
        UpdateResetKeyLabel();

        HookTurnManager();
    }

    void OnDestroy()
    {
        if (turn != null)
        {
            turn.onStageCleared -= OnStageCleared;
        }
    }

    void HookTurnManager()
    {
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn != null)
        {
            turn.onStageCleared -= OnStageCleared;
            turn.onStageCleared += OnStageCleared;

            // カウンタ初期化
            turn.ResetScoreCounters();

            // StageManagerで設定済みの parAP を反映
            if (parAP != 0) turn.parAP = Mathf.Max(0, parAP);
        }
    }

    void Update()
    {
        if (!turn) turn = UnityCompat.FindFirst<TurnManager>();

        bool isOver = (turn && turn.gameOver);
        bool isClear = (turn && turn.cleared);

        // ESC（キーボード）
        if (!isOver && !isClear && Input.GetKeyDown(KeyCode.Escape))
        {
            if (escMenuPanel && escMenuPanel.activeSelf) CloseEscMenu();
            else OpenEscMenu();
        }

        // Pad: Start/Options で ESC メニュー
#if ENABLE_INPUT_SYSTEM
        if (!isOver && !isClear)
        {
            var gp = Gamepad.current;
            if (gp != null && gp.startButton.wasPressedThisFrame)
            {
                if (escMenuPanel && escMenuPanel.activeSelf) CloseEscMenu();
                else OpenEscMenu();
            }

            var ds4 = DualShockGamepad.current;
            if (ds4 != null && (ds4.optionsButton.wasPressedThisFrame || ds4.touchpadButton.wasPressedThisFrame))
            {
                if (escMenuPanel && escMenuPanel.activeSelf) CloseEscMenu();
                else OpenEscMenu();
            }
        }

        // Pad: 北ボタン＝リセット（ESCメニュー表示中は無効）
        {
            var gp = Gamepad.current;
            if (gp != null && gp.buttonNorth.wasPressedThisFrame && !waitingResetRebind)
            {
                if (escMenuPanel == null || !escMenuPanel.activeSelf)
                {
                    RequestRetry();
                    // リセット要求後は他処理をスキップ
                    return;
                }
            }
        }
#endif

        // Game Over 表示
        if (gameOverPanel)
        {
            if (isOver && !gameOverPanel.activeSelf)
            {
                ShowOnTop(gameOverPanel);
                // 最初に選択するボタン（Pad対応）
                SelectDefault(retryButton ? retryButton.gameObject : null, gameOverPanel);
            }
            if (!isOver && gameOverPanel.activeSelf) gameOverPanel.SetActive(false);
        }

        // Clear 表示（一時停止）
        if (clearPanel)
        {
            if (isClear && !clearPanel.activeSelf)
            {
                ShowOnTop(clearPanel);
                if (pauseOnClear) Time.timeScale = 0f;

                // 最初に選択するボタン（Pad対応）
                SelectDefault(clearToStageButton ? clearToStageButton.gameObject : null, clearPanel);

                if (treasureOverlayPending)
                {
                    ShowTreasureOverlayOnTop();
                    treasureOverlayPending = false;
                }
            }
            if (!isClear && clearPanel.activeSelf) clearPanel.SetActive(false);
        }

        // 最前面のみ操作可能にする（Padナビゲーションの誤選択防止）
        ApplyExclusiveFocus();
    }

    // りバインド開始
    void BeginRebindResetKey()
    {
        waitingResetRebind = true;
        InputBindings.BeginCapture(); // ※ 追加済み
        if (bindResetKeyLabel) bindResetKeyLabel.text = "リセット: （次の入力で設定）";
    }

    void UpdateResetKeyLabel()
    {
        if (bindResetKeyLabel) bindResetKeyLabel.text = $"リセット: {InputBindings.GetKeyDisplay(InputBindings.ResetKey)}";
    }

    // ==== クリア時の結果反映 ====
    void OnStageCleared(TurnManager.ScoreResult res)
    {
        if (rankText) rankText.text = $" {res.rank.ToString()}ランク";
        if (detailRotText) detailRotText.text = $"回転数: {res.rot}回";
        if (detailRetryText) detailRetryText.text = $"リトライ数: {res.retries}回";
        if (detailStepText) detailStepText.text = $"歩数: {res.steps}歩";

        var tm = UnityCompat.FindFirst<TurnManager>();
        treasureOverlayPending = (tm != null && tm.treasurePicked);

        Debug.Log($"[Result/UI] rank:{res.rank} score:{res.score} rot:{res.rot} steps:{res.steps} retries:{res.retries}");
    }

    // クリアパネルの上にコレクションパネルを最前面表示
    void ShowTreasureOverlayOnTop()
    {
        if (!treasurePanel) return;

        var tm = UnityCompat.FindFirst<TurnManager>();
        if (tm == null || !tm.treasurePicked)
        {
            treasurePanel.SetActive(false);
            return;
        }

        var st = UnityCompat.FindFirst<StageManager>();
        StageSet.Entry entry = st != null ? st.GetCurrentEntry() : null;

        string name = (entry != null && !string.IsNullOrEmpty(entry.collectName)) ? entry.collectName : "？";
        if (treasureText) treasureText.text = $"「{name}」を入手！";

        if (treasureImage)
        {
            var spr = (entry != null) ? entry.collectSprite : null;
            treasureImage.sprite = spr;
            treasureImage.enabled = (spr != null);
        }

        ShowOnTop(treasurePanel);
        // とじるボタンを初期選択
        SelectDefault(treasureCloseButton ? treasureCloseButton.gameObject : null, treasurePanel);
    }

    void ShowResult()
    {
        var turnManager = UnityCompat.FindFirst<TurnManager>();
        int score;
        char rank;

        if (turnManager.scoreMode == TurnManager.ScoreMode.ActionPoint)
        {
            score = turnManager.CalcScore();
            rank = (score >= 95) ? 'S' :
                   (score >= 85) ? 'A' :
                   (score >= 70) ? 'B' :
                   (score >= 50) ? 'C' : 'D';
        }
        else
        {
            var result = turnManager.ComputeScore(parRot);
            score = result.score;
            rank = result.rank;
        }

        if (resultScoreText) resultScoreText.text = score.ToString();
        if (resultRankText) resultRankText.text = rank.ToString();
    }

    // ==== UIユーティリティ ====
    void ShowOnTop(GameObject panel)
    {
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        // 表示した時点で最前面のみ操作に限定
        ApplyExclusiveFocus();
        // 何か1つでも選択されていなければ、このパネル内の最初の操作可能なSelectableを選ぶ
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
        {
            SelectDefault(null, panel);
        }
    }

    void OpenEscMenu()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(true);
        escMenuPanel.transform.SetAsLastSibling();
        if (pauseOnEsc) Time.timeScale = 0f;
        UpdateResetKeyLabel();

        // 表示直後にフォーカス設定（Padで動くように）
        SelectDefault(escCloseButton ? escCloseButton.gameObject : null, escMenuPanel);

        // ESCメニュー最前面を強制（下のUIは非操作化）
        ApplyExclusiveFocus();
    }

    void CloseEscMenu()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(false);
        waitingResetRebind = false;
        InputBindings.EndCapture(); // 前回の続きがあれば解除
        ResumeIfPaused();

        if (EventSystem.current && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);

        // 排他制御更新
        ApplyExclusiveFocus();
    }

    void ResumeIfPaused()
    {
        if (Time.timeScale == 0f) Time.timeScale = 1f;
    }

    // ==== リトライ：スコア初期化 → 現在ステージ再読込 ====
    public void RequestRetry()
    {
        var tm = UnityCompat.FindFirst<TurnManager>();
        tm?.RegisterRetry();
        tm?.ResetForRestart();

        ResumeIfPaused();
        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);
        if (treasurePanel) treasurePanel.SetActive(false);

        UnityCompat.FindFirst<StageManager>()?.ReloadCurrent();

        // 排他制御リセット
        ApplyExclusiveFocus();
    }

    // Editor/Web/Android等の終了
    void QuitGame()
    {
        ResumeIfPaused();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
        // WebGLは終了不可
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

    // 指定パネル内から最初に操作できる Selectable を探して選択
    void SelectDefault(GameObject preferred, GameObject panel)
    {
        if (EventSystem.current == null || panel == null || !panel.activeInHierarchy) return;

        GameObject target = null;

        if (preferred && preferred.activeInHierarchy)
        {
            var sel = preferred.GetComponent<Selectable>();
            if (sel != null && sel.IsActive() && sel.IsInteractable())
                target = preferred;
        }

        if (target == null)
        {
            var sels = panel.GetComponentsInChildren<Selectable>(true);
            foreach (var s in sels)
            {
                if (s != null && s.IsActive() && s.IsInteractable())
                {
                    target = s.gameObject;
                    break;
                }
            }
        }

        if (target != null)
        {
            EventSystem.current.SetSelectedGameObject(target);
        }
    }

    // 最前面のアクティブなパネルだけ操作可能にする（他はPadナビゲーション無効化）
    void ApplyExclusiveFocus()
    {
        var top = GetTopActivePanel();
        if (_lastTopPanel == top)
        {
            // 変化なしでも、非表示化されたものを念のため抑止
            SetPanelInteractable(escMenuPanel, escMenuPanel == top);
            SetPanelInteractable(gameOverPanel, gameOverPanel == top);
            SetPanelInteractable(clearPanel, clearPanel == top);
            SetPanelInteractable(treasurePanel, treasurePanel == top);
            return;
        }

        _lastTopPanel = top;

        SetPanelInteractable(escMenuPanel, escMenuPanel == top);
        SetPanelInteractable(gameOverPanel, gameOverPanel == top);
        SetPanelInteractable(clearPanel, clearPanel == top);
        SetPanelInteractable(treasurePanel, treasurePanel == top);

        // 最前面が切り替わったら、そのパネルから初期フォーカスも設定
        if (top != null)
        {
            SelectDefault(null, top);
        }
        else
        {
            // どのパネルも無ければ選択解除
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }

    GameObject GetTopActivePanel()
    {
        GameObject[] ps = new GameObject[] { escMenuPanel, gameOverPanel, clearPanel, treasurePanel };
        GameObject top = null;
        int topIndex = -1;
        foreach (var p in ps)
        {
            if (p != null && p.activeInHierarchy)
            {
                int idx = p.transform.GetSiblingIndex();
                if (idx >= topIndex)
                {
                    top = p;
                    topIndex = idx;
                }
            }
        }
        return top;
    }

    void SetPanelInteractable(GameObject panel, bool on)
    {
        if (panel == null) return;
        var cg = panel.GetComponent<CanvasGroup>();
        if (cg == null) cg = panel.AddComponent<CanvasGroup>();
        cg.interactable = on;
        cg.blocksRaycasts = on;
        // alpha は見た目に関与するので触らない
    }
}
