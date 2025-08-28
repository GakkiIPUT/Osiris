using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock; // PS系も拾えるように
#endif

public class GameFlow : MonoBehaviour
{
    [Header("Esc Menu")]
    public GameObject escMenuPanel;
    public Button escCloseButton;
    public Button escToStageButton;
    public Button escQuitButton; // 終了（即終了）
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
    [Tooltip("目安（パー）: ステージ側に依存")]
    public int parRot = 6;
    public int parAP = 6; // ※ 追加: ActionPoint用パー値
    public TMP_Text rankText;          // 例: "S"
    public TMP_Text scoreText;         // 例: "92"
    public TMP_Text detailRotText;     // 例: "回転 8 / 6（+2）"
    public TMP_Text detailRetryText;   // 例: "リトライ 1"
    public TMP_Text resultRankText;    // 結果表示用ランク
    public TMP_Text resultScoreText;   // 結果表示用スコア
    public TMP_Text detailStepText;    // 例: "歩 8"

    [Header("Treasure (Collection) UI")]
    public GameObject treasurePanel;   // クリアパネル表示時に宝物を最前面に出す
    public TMP_Text treasureText;      // 表示: 「◯◯を入手！」
    public Image treasureImage;        // コレクションSprite
    public Button treasureCloseButton; // とじる

    [Header("Key Bindings (in ESC)")]
    public Button bindResetKeyButton;
    public TMP_Text bindResetKeyLabel;

    StageManager stage;
    TurnManager turn;

    // クリア表示制御用: クリアパネル表示時に宝物を最前面で出す
    bool treasureOverlayPending = false;
    bool waitingResetRebind = false;

    void Start()
    {
        var gs = UnityCompat.FindFirst<GameState>();
        stage = UnityCompat.FindFirst<StageManager>();

        // ステージ選択経由のフラグを読み取り、使い終わったらリセット
        bool viaStageSelect = PlayerPrefs.GetInt("enteredViaStageSelect", 0) == 1;
        PlayerPrefs.SetInt("enteredViaStageSelect", 0);
        PlayerPrefs.Save();

        // レベルペインターのオーバーライド適用確認
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

        // 基本UI初期化
        turn = UnityCompat.FindFirst<TurnManager>();

        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);
        if (treasurePanel) treasurePanel.SetActive(false);

        // ESC メニュー
        if (escCloseButton) escCloseButton.onClick.AddListener(CloseEscMenu);
        if (escToStageButton) escToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });
        if (escQuitButton) escQuitButton.onClick.AddListener(QuitGame);

        // GameOver/クリア パネルのボタン
        if (retryButton) retryButton.onClick.AddListener(RequestRetry);
        if (toMenuButton) toMenuButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });
        if (clearToStageButton) clearToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });

        //宝箱パネル
        if (treasureCloseButton) treasureCloseButton.onClick.AddListener(() => { if (treasurePanel) treasurePanel.SetActive(false); });

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

            // StageManagerで設定済みの parAP を尊重
            if (parAP != 0) turn.parAP = Mathf.Max(0, parAP);
        }
    }

    void Update()
    {
        if (!turn) turn = UnityCompat.FindFirst<TurnManager>();

        // Rebind待機中はここで処理（省略）…

        bool isOver = (turn && turn.gameOver);
        bool isClear = (turn && turn.cleared);

        // クリア/ゲームオーバー時以外は ESC トグル（Keyboard）
        if (!isOver && !isClear && Input.GetKeyDown(KeyCode.Escape))
        {
            if (escMenuPanel && escMenuPanel.activeSelf) CloseEscMenu();
            else OpenEscMenu();
        }

        // クリア/ゲームオーバー時以外は Start でもトグル（Gamepad）
#if ENABLE_INPUT_SYSTEM
        if (!isOver && !isClear)
        {
            var gp = Gamepad.current;
            if (gp != null && gp.startButton.wasPressedThisFrame)
            {
                if (escMenuPanel && escMenuPanel.activeSelf) CloseEscMenu();
                else OpenEscMenu();
            }

            // PS系（Windows HID など）での取りこぼし対策（Options/Touchpad）
            var ds4 = DualShockGamepad.current;
            if (ds4 != null && (ds4.optionsButton.wasPressedThisFrame || ds4.touchpadButton.wasPressedThisFrame))
            {
                if (escMenuPanel && escMenuPanel.activeSelf) CloseEscMenu();
                else OpenEscMenu();
            }
#endif
        }

        // Game Over 表示
        if (gameOverPanel)
        {
            if (isOver && !gameOverPanel.activeSelf) ShowOnTop(gameOverPanel);
            if (!isOver && gameOverPanel.activeSelf) gameOverPanel.SetActive(false);
        }

        // Clear 表示（一時停止あり）
        if (clearPanel)
        {
            if (isClear && !clearPanel.activeSelf)
            {
                ShowOnTop(clearPanel);
                if (pauseOnClear) Time.timeScale = 0f;

                if (treasureOverlayPending)
                {
                    ShowTreasureOverlayOnTop();
                    treasureOverlayPending = false;
                }
            }
            if (!isClear && clearPanel.activeSelf) clearPanel.SetActive(false);
        }
    }

    // リバインド開始
    void BeginRebindResetKey()
    {
        waitingResetRebind = true;
        InputBindings.BeginCapture(); // ← 追加
        if (bindResetKeyLabel) bindResetKeyLabel.text = "リセット: （押して設定中…）";
    }

    void UpdateResetKeyLabel()
    {
        if (bindResetKeyLabel) bindResetKeyLabel.text = $"リセット: {InputBindings.GetKeyDisplay(InputBindings.ResetKey)}";
    }

    // ==== クリア結果の受取 ====
    void OnStageCleared(TurnManager.ScoreResult res)
    {
        if (rankText) rankText.text = $" {res.rank.ToString()}ランク";
        if (detailRotText) detailRotText.text = $"回転数： {res.rot}回";
        if (detailRetryText) detailRetryText.text = $"リトライ数： {res.retries}回";
        if (detailStepText) detailStepText.text = $"歩数： {res.steps}歩";

        var tm = UnityCompat.FindFirst<TurnManager>();
        treasureOverlayPending = (tm != null && tm.treasurePicked);

        Debug.Log($"[Result/UI] rank:{res.rank} score:{res.score} rot:{res.rot} steps:{res.steps} retries:{res.retries}");
    }

    // クリアパネルの直後に呼ぶ：宝箱UIを最前面に表示
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

        string name = (entry != null && !string.IsNullOrEmpty(entry.collectName)) ? entry.collectName : "宝箱";
        if (treasureText) treasureText.text = $"「{name}」を獲得！";

        if (treasureImage)
        {
            var spr = (entry != null) ? entry.collectSprite : null;
            treasureImage.sprite = spr;
            treasureImage.enabled = (spr != null);
        }

        treasurePanel.SetActive(true);
        ShowOnTop(treasurePanel);
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
    }

    void OpenEscMenu()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(true);
        escMenuPanel.transform.SetAsLastSibling();
        if (pauseOnEsc) Time.timeScale = 0f;
        UpdateResetKeyLabel();

        // 表示直後に初期選択を設定（パッドで操作しやすく）
        if (EventSystem.current != null)
        {
            var first = escCloseButton ? escCloseButton.gameObject : null;
            EventSystem.current.SetSelectedGameObject(first);
        }
    }
    void CloseEscMenu()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(false);
        waitingResetRebind = false;
        InputBindings.EndCapture(); // 念のため
        ResumeIfPaused();

        if (EventSystem.current && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    void ResumeIfPaused()
    {
        if (Time.timeScale == 0f) Time.timeScale = 1f;
    }

    // ==== リトライ：ここ経由に統一 ====
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
    }

    // 追加: Quit 処理（Editor停止 / 各プラットフォーム終了）
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
}
