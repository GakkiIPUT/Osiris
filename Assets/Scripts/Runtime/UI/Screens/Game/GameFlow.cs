using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM

using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

#endif

/// <summary>
/// ステージの読み込み・進行UI・ESCメニュー・結果表示・チュートリアル表示を統括する。
/// TurnManager と連携してクリア/ゲームオーバー時の画面制御も行う。
/// </summary>
public class GameFlow : MonoBehaviour
{
    [Header("Esc Menu")]
    public GameObject escMenuPanel;

    public Button escCloseButton;
    public Button escToStageButton;
    public Button escQuitButton;
    public bool pauseOnEsc = true;

    [Header("Game Over")]
    public GameObject gameOverPanel;

    public Button retryButton;
    public Button toMenuButton;

    [Header("Clear (Goal)")]
    public GameObject clearPanel;

    public Button clearToStageButton;
    public Button clearNextButton;
    public Button clearRetryButton;
    public bool pauseOnClear = true;

    [Header("Clear Result (optional)")]
    [Tooltip("回転（パー）: ステージUIに表示")]
    public int parRot = 6;

    public int parAP = 6;
    public TMP_Text rankText;
    public TMP_Text scoreText;
    public TMP_Text detailRotText;
    public TMP_Text detailRetryText;
    public TMP_Text resultRankText;
    public TMP_Text resultScoreText;
    public TMP_Text detailStepText;

    [Header("Treasure (Collection) UI")]
    public GameObject treasurePanel;

    public TMP_Text treasureText;
    public Image treasureImage;
    public Button treasureCloseButton;

    [Header("Key Bindings (in ESC)")]
    public Button bindResetKeyButton;

    public TMP_Text bindResetKeyLabel;

    [Header("Tutorial (Optional)")]
    [Tooltip("チュートリアル用のパネル（Canvas下の子オブジェクト）")]
    public GameObject tutorialPanel;

    public Image tutorialImage;
    public Button tutorialCloseButton;
    public bool pauseOnTutorial = true;

    /// <summary>チュートリアルオーバーレイが開いているか（入力抑止用）</summary>
    public static bool TutorialOverlayOpen { get; private set; } = false;

    private StageManager stage;
    private TurnManager turn;
    private bool treasureOverlayPending = false;
    private bool waitingResetRebind = false;
    private GameObject _lastTopPanel = null;
    private bool _suppressClearPanelOnce = false;

    /// <summary>
    /// ステージ読み込み・UIセットアップ・TurnManager購読・チュートリアル表示を行う。
    /// </summary>
    private void Start()
    {
        var gs = UnityCompat.FindFirst<GameState>();
        stage = UnityCompat.FindFirst<StageManager>();

        // ステージ入口判定
        bool viaStageSelect = PlayerPrefs.GetInt("enteredViaStageSelect", 0) == 1;
        PlayerPrefs.SetInt("enteredViaStageSelect", 0);
        PlayerPrefs.Save();

        // LevelPainterオーバーライド
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

        // UI初期化
        turn = UnityCompat.FindFirst<TurnManager>();

        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);
        if (treasurePanel) treasurePanel.SetActive(false);
        if (tutorialPanel) tutorialPanel.SetActive(false);
        TutorialOverlayOpen = false;

        // ESCメニュー
        if (escCloseButton) escCloseButton.onClick.AddListener(CloseEscMenu);
        if (escToStageButton) escToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });
        if (escQuitButton) escQuitButton.onClick.AddListener(QuitGame);

        // GameOver/クリア
        if (retryButton) retryButton.onClick.AddListener(RequestRetry);
        if (toMenuButton) toMenuButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });
        if (clearToStageButton) clearToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });
        if (clearNextButton) clearNextButton.onClick.AddListener(ClearGoNext);
        if (clearRetryButton) clearRetryButton.onClick.AddListener(ClearRetry);

        // コレクション
        if (treasureCloseButton) treasureCloseButton.onClick.AddListener(() =>
        {
            if (treasurePanel) treasurePanel.SetActive(false);
        });

        // キーコンフィグ
        if (bindResetKeyButton) bindResetKeyButton.onClick.AddListener(BeginRebindResetKey);
        UpdateResetKeyLabel();

        // チュートリアル
        if (tutorialCloseButton) tutorialCloseButton.onClick.AddListener(CloseTutorial);

        HookTurnManager();

        // ステージ開始時に必要ならチュートリアルを表示
        MaybeShowTutorialAtStart();
    }

    /// <summary>
    /// 購読解除などの後片付け。
    /// </summary>
    private void OnDestroy()
    {
        if (turn != null)
        {
            turn.onStageCleared -= OnStageCleared;
        }
    }

    /// <summary>
    /// TurnManager へのイベント購読とスコア初期化、parAP反映を行う。
    /// </summary>
    private void HookTurnManager()
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

    /// <summary>
    /// ESCメニューの開閉、結果画面の表示、キーコンフィグのキャプチャ等を処理する。
    /// </summary>
    private void Update()
    {
        if (!turn) turn = UnityCompat.FindFirst<TurnManager>();

        // 追記: キーボインドのキャプチャ中処理
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

        bool isOver = (turn && turn.gameOver);
        bool isClear = (turn && turn.cleared);

        // ESC
        if (!isOver && !isClear && Input.GetKeyDown(KeyCode.Escape))
        {
            if (escMenuPanel && escMenuPanel.activeSelf) CloseEscMenu();
            else OpenEscMenu();
        }

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

        // Pad: 北ボタン＝リセット（ESCメニュー開いてないとき）
        {
            var gp = Gamepad.current;
            if (gp != null && gp.buttonNorth.wasPressedThisFrame && !waitingResetRebind)
            {
                if (escMenuPanel == null || !escMenuPanel.activeSelf)
                {
                    RequestRetry();
                    return;
                }
            }
        }
#endif

        // Game Over
        if (gameOverPanel)
        {
            if (isOver && !gameOverPanel.activeSelf)
            {
                ShowOnTop(gameOverPanel);
                SelectDefault(retryButton ? retryButton.gameObject : null, gameOverPanel);
            }
            if (!isOver && gameOverPanel.activeSelf) gameOverPanel.SetActive(false);
        }

        // Clear
        if (clearPanel)
        {
            if (isClear && !clearPanel.activeSelf && !_suppressClearPanelOnce)
            {
                ShowOnTop(clearPanel);
                if (pauseOnClear) Time.timeScale = 0f;

                // 次へ/もう一度ボタンの有効/無効とデフォルト選択を更新
                UpdateClearButtonsAndSelection();

                if (treasureOverlayPending)
                {
                    ShowTreasureOverlayOnTop();
                    treasureOverlayPending = false;
                }
            }
            if (!isClear && clearPanel.activeSelf) clearPanel.SetActive(false);

            // 抑止フラグは「クリア状態が解けた」ら解除
            if (!isClear && _suppressClearPanelOnce)
                _suppressClearPanelOnce = false;
        }

        // 最上位のみ操作可能に
        ApplyExclusiveFocus();
    }

    // ==== Tutorial ====
    /// <summary>
    /// ステージ開始時にチュートリアルを表示すべきなら表示する。
    /// </summary>
    private void MaybeShowTutorialAtStart()
    {
        if (!tutorialPanel) return;

        var st = UnityCompat.FindFirst<StageManager>();
        var entry = st != null ? st.GetCurrentEntry() : null;
        if (entry == null) return;
        if (!entry.showTutorialOnStart) return;
        if (entry.tutorialSprite == null) return;

        if (entry.tutorialOnlyOnce)
        {
            string key = $"tut_seen_{entry.id}";
            if (PlayerPrefs.GetInt(key, 0) == 1)
            {
#if UNITY_EDITOR
                Debug.Log($"[Tutorial] already seen ({entry.id})");
#endif
                return;
            }
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
        }

        if (tutorialImage)
        {
            tutorialImage.sprite = entry.tutorialSprite;
            tutorialImage.enabled = true;
        }

        ShowTutorial();
    }

    /// <summary>
    /// チュートリアルパネルを開き、一時停止・フォーカス設定を行う。
    /// </summary>
    private void ShowTutorial()
    {
        if (!tutorialPanel) return;
        TutorialOverlayOpen = true;

        ShowOnTop(tutorialPanel);
        if (pauseOnTutorial) Time.timeScale = 0f;

        SelectDefault(tutorialCloseButton ? tutorialCloseButton.gameObject : null, tutorialPanel);
        ApplyExclusiveFocus();
    }

    /// <summary>
    /// チュートリアルパネルを閉じ、必要ならタイムスケールを戻す。
    /// </summary>
    private void CloseTutorial()
    {
        if (!tutorialPanel) return;
        tutorialPanel.SetActive(false);
        TutorialOverlayOpen = false;

        if (!IsAnyBlockingPanelActive())
            ResumeIfPaused();

        if (EventSystem.current && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);

        ApplyExclusiveFocus();
    }

    /// <summary>
    /// 現在ステージに必要ならチュートリアルを表示する。
    /// </summary>
    public void ShowTutorialIfNeededForCurrentStage()
    {
        MaybeShowTutorialAtStart();
    }

    /// <summary>
    /// いずれかのブロッキングパネルが開いているか。
    /// </summary>
    private bool IsAnyBlockingPanelActive()
    {
        return (escMenuPanel && escMenuPanel.activeInHierarchy) ||
               (gameOverPanel && gameOverPanel.activeInHierarchy) ||
               (clearPanel && clearPanel.activeInHierarchy) ||
               (treasurePanel && treasurePanel.activeInHierarchy) ||
               (tutorialPanel && tutorialPanel.activeInHierarchy);
    }

    // ==== 結果（通知など） ====
    /// <summary>
    /// ステージクリア時の統計を受け取り、UI表示や宝物オーバーレイの保留を行う。
    /// </summary>
    private void OnStageCleared(TurnManager.ScoreResult res)
    {
        if (rankText) rankText.text = $" {res.rank.ToString()}ランク";
        if (detailRotText) detailRotText.text = $"回転数: {res.rot}回";
        if (detailRetryText) detailRetryText.text = $"リトライ数: {res.retries}回";
        if (detailStepText) detailStepText.text = $"歩数: {res.steps}歩";

        var tm = UnityCompat.FindFirst<TurnManager>();
        treasureOverlayPending = (tm != null && tm.treasurePicked);

        Debug.Log($"[Result/UI] rank:{res.rank} score:{res.score} rot:{res.rot} steps:{res.steps} retries:{res.retries}");
    }

    /// <summary>
    /// 宝物オーバーレイを最前面で表示する。
    /// </summary>
    private void ShowTreasureOverlayOnTop()
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

        string name = (entry != null && !string.IsNullOrEmpty(entry.collectName)) ? entry.collectName : "宝";
        if (treasureText) treasureText.text = $"『{name}』獲得！";

        if (treasureImage)
        {
            var spr = (entry != null) ? entry.collectSprite : null;
            treasureImage.sprite = spr;
            treasureImage.enabled = (spr != null);
        }

        ShowOnTop(treasurePanel);
        SelectDefault(treasureCloseButton ? treasureCloseButton.gameObject : null, treasurePanel);
    }

    /// <summary>
    /// 結果（ランク/スコア）を算出して即時表示する。
    /// </summary>
    private void ShowResult()
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
    /// <summary>
    /// 任意のパネルを最前面に出し、入力を独占させる。
    /// </summary>
    private void ShowOnTop(GameObject panel)
    {
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        ApplyExclusiveFocus();
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
        {
            SelectDefault(null, panel);
        }
    }

    /// <summary>
    /// ESCメニューを開き、必要なら一時停止・デフォルト選択を行う。
    /// </summary>
    private void OpenEscMenu()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(true);
        escMenuPanel.transform.SetAsLastSibling();
        if (pauseOnEsc) Time.timeScale = 0f;
        UpdateResetKeyLabel();

        SelectDefault(escCloseButton ? escCloseButton.gameObject : null, escMenuPanel);

        ApplyExclusiveFocus();
    }

    /// <summary>
    /// ESCメニューを閉じ、必要なら再開・フォーカス解除を行う。
    /// </summary>
    private void CloseEscMenu()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(false);
        waitingResetRebind = false;
        InputBindings.EndCapture();

        if (!IsAnyBlockingPanelActive())
            ResumeIfPaused();

        if (EventSystem.current && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);

        ApplyExclusiveFocus();
    }

    /// <summary>
    /// 一時停止中ならタイムスケールを1に戻す。
    /// </summary>
    private void ResumeIfPaused()
    {
        if (Time.timeScale == 0f) Time.timeScale = 1f;
    }

    /// <summary>
    /// ステージのリトライ（スコアポリシーに応じてカウンタ調整）を行う。
    /// </summary>
    public void RequestRetry()
    {
        // 回転中・自由回転プレビュー中はリセット拒否
        var board = UnityCompat.FindFirst<BoardManager>();
        if (board != null && (board.IsAnimating || board.IsFreePreviewActive))
        {
#if UNITY_EDITOR
            Debug.LogWarning("[GameFlow] Reset rejected: board is rotating or free-preview active.");
#endif
            // 視覚フィードバックは行わない（Ghost/aimingが崩れないため）
            return;
        }

        var tm = UnityCompat.FindFirst<TurnManager>();
        if (tm != null)
        {
            // ゲームオーバー中のリトライは加点対象外
            bool fromGameOver = tm.gameOver;
            if (!fromGameOver)
            {
                tm.RegisterRetry();
            }
            // スコア系は残して再開（retryCountのみ、ゲームオーバー回数は0維持）
            tm.ResetForRestart();
        }

        if (!IsAnyBlockingPanelActive())
            ResumeIfPaused();
        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);
        if (treasurePanel) treasurePanel.SetActive(false);
        if (tutorialPanel) tutorialPanel.SetActive(false);
        TutorialOverlayOpen = false;

        UnityCompat.FindFirst<StageManager>()?.ReloadCurrent();

        ApplyExclusiveFocus();
    }

    /// <summary>
    /// アプリケーションの終了（WebGL/Editor/Androidに配慮）。
    /// </summary>
    private void QuitGame()
    {
        ResumeIfPaused();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
        // WebGLでは終了なし
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

    /// <summary>
    /// 操作可能なボタンのうち、妥当なデフォルト選択を設定する。
    /// </summary>
    private void SelectDefault(GameObject preferred, GameObject panel)
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

    /// <summary>
    /// 最前面のパネルだけを操作可能にし、必要に応じてデフォルト選択を設定する。
    /// </summary>
    private void ApplyExclusiveFocus()
    {
        var top = GetTopActivePanel();
        if (_lastTopPanel == top)
        {
            SetPanelInteractable(escMenuPanel, escMenuPanel == top);
            SetPanelInteractable(gameOverPanel, gameOverPanel == top);
            SetPanelInteractable(clearPanel, clearPanel == top);
            SetPanelInteractable(treasurePanel, treasurePanel == top);
            SetPanelInteractable(tutorialPanel, tutorialPanel == top);
            return;
        }

        _lastTopPanel = top;

        SetPanelInteractable(escMenuPanel, escMenuPanel == top);
        SetPanelInteractable(gameOverPanel, gameOverPanel == top);
        SetPanelInteractable(clearPanel, clearPanel == top);
        SetPanelInteractable(treasurePanel, treasurePanel == top);
        SetPanelInteractable(tutorialPanel, tutorialPanel == top);

        if (top != null)
        {
            SelectDefault(null, top);
        }
        else
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }

    /// <summary>
    /// 最前面にあるアクティブなパネルを取得する。
    /// </summary>
    private GameObject GetTopActivePanel()
    {
        GameObject[] ps = new GameObject[] { escMenuPanel, gameOverPanel, clearPanel, treasurePanel, tutorialPanel };
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

    /// <summary>
    /// 指定パネルのCanvasGroupを切り替え、入力独占を制御する。
    /// </summary>
    private void SetPanelInteractable(GameObject panel, bool on)
    {
        if (panel == null) return;
        var cg = panel.GetComponent<CanvasGroup>();
        if (cg == null) cg = panel.AddComponent<CanvasGroup>();
        cg.interactable = on;
        cg.blocksRaycasts = on;
    }

    /// <summary>
    /// リセットキーのリバインドを開始する。
    /// </summary>
    private void BeginRebindResetKey()
    {
        waitingResetRebind = true;
        InputBindings.BeginCapture();
        if (bindResetKeyLabel) bindResetKeyLabel.text = "リセット: （次の入力で設定）";
    }

    /// <summary>
    /// リセットキーのラベル表示を更新する。
    /// </summary>
    private void UpdateResetKeyLabel()
    {
        if (bindResetKeyLabel) bindResetKeyLabel.text = $"リセット: {InputBindings.GetKeyDisplay(InputBindings.ResetKey)}";
    }

    /// <summary>
    /// クリア画面の「次へ/もう一度」ボタン状態とデフォルト選択を更新する。
    /// </summary>
    private void UpdateClearButtonsAndSelection()
    {
        bool hasNext = HasNextStage();

        if (clearNextButton)
        {
            clearNextButton.interactable = hasNext;
            clearNextButton.gameObject.SetActive(true);
        }
        if (clearRetryButton)
        {
            clearRetryButton.interactable = true; // クリア後は常に有効
            clearRetryButton.gameObject.SetActive(true);
        }

        // フォーカス優先は「次へ」→「もう一度」→「ステージ選択」
        GameObject preferred =
            (clearNextButton && clearNextButton.interactable) ? clearNextButton.gameObject :
            (clearRetryButton ? clearRetryButton.gameObject : (clearToStageButton ? clearToStageButton.gameObject : null));

        SelectDefault(preferred, clearPanel);
    }

    /// <summary>
    /// 次のステージが存在するか。
    /// </summary>
    private bool HasNextStage()
    {
        if (stage == null) stage = UnityCompat.FindFirst<StageManager>();
        if (stage == null || stage.stageSet == null || stage.stageSet.stages == null) return false;
        return (stage.currentIndex + 1) < stage.stageSet.stages.Count;
    }

    /// <summary>
    /// クリア後に次のステージへ遷移する（必要なリセット/抑止を実施）。
    /// </summary>
    private void ClearGoNext()
    {
        if (HasNextStage())
        {
            ResumeIfPaused();

            // パネルを閉じる
            if (clearPanel) clearPanel.SetActive(false);
            if (treasurePanel) treasurePanel.SetActive(false);
            if (tutorialPanel) tutorialPanel.SetActive(false);
            TutorialOverlayOpen = false;

            // クリア状態を解除（次フレームの自動オープン抑止）
            var tm = UnityCompat.FindFirst<TurnManager>();
            if (tm != null)
            {
                tm.ResetForRestart(keepRetryCount: false);
            }
            _suppressClearPanelOnce = true;

            // 次のステージへ
            stage.Next();

            // ここで次ステージのチュートリアル判定・表示を即実行
            ShowTutorialIfNeededForCurrentStage();

            ApplyExclusiveFocus();
        }
        else
        {
            // 次がなければステージ選択へ
            ResumeIfPaused();
            SceneNavigator.GoStage();
        }
    }

    /// <summary>
    /// クリア画面から即リトライ（スコアは維持し再開）。
    /// </summary>
    private void ClearRetry()
    {
        // 回転中・自由回転プレビュー中は拒否（念のため）
        var board = UnityCompat.FindFirst<BoardManager>();
        if (board != null && (board.IsAnimating || board.IsFreePreviewActive))
        {
#if UNITY_EDITOR
            Debug.LogWarning("[GameFlow] ClearRetry rejected: board is rotating or free-preview active.");
#endif
            return;
        }

        // リトライ数は増やさずリスタート
        var tm = UnityCompat.FindFirst<TurnManager>();
        if (tm != null)
        {
            tm.ResetForRestart();
        }

        ResumeIfPaused();
        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);
        if (treasurePanel) treasurePanel.SetActive(false);
        if (tutorialPanel) tutorialPanel.SetActive(false);
        TutorialOverlayOpen = false;

        UnityCompat.FindFirst<StageManager>()?.ReloadCurrent();

        ApplyExclusiveFocus();
    }
}
