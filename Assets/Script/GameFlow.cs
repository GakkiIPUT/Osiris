using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameFlow : MonoBehaviour
{
    [Header("Esc Menu")]
    public GameObject escMenuPanel;
    public Button escCloseButton;
    public Button escToStageButton;
    public Button escQuitButton; // 既存（終了）
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
    [Tooltip("想定回転（パー）: ステージ毎に調整")]
    public int parRot = 6;
    public int parAP = 6; // ← 追加: ActionPoint用パー値
    public TMP_Text rankText;          // 例: "S"
    public TMP_Text scoreText;         // 例: "92"
    public TMP_Text detailRotText;     // 例: "回転 8 / 6（+2）"
    public TMP_Text detailRetryText;   // 例: "リトライ 1"
    public TMP_Text resultRankText;    // 結果表示用ランク
    public TMP_Text resultScoreText;   // 結果表示用スコア
    public TMP_Text detailStepText;    // 例: "歩数 8"

    [Header("Treasure (Collection) UI")]
    public GameObject treasurePanel;   // クリアパネルより上に出す
    public TMP_Text treasureText;      // 表示: 「「◯◯」を獲得！
    public Image treasureImage;        // コレクションSprite
    public Button treasureCloseButton; // 閉じる

    [Header("Key Bindings (in ESC)")]
    public Button bindResetKeyButton;
    public TMP_Text bindResetKeyLabel;

    StageManager stage;
    TurnManager turn;

    // クリア表示順制御用: クリアパネル表示後に宝箱を最前面に出す
    bool treasureOverlayPending = false;
    bool waitingResetRebind = false;

    void Start()
    {
        var gs = UnityCompat.FindFirst<GameState>();
        stage = UnityCompat.FindFirst<StageManager>();

        // ステージロード（StageManager 側が Board/Turn を生成）
        if (stage != null)
        {
            // GameState がなくても PlayerPrefs から復元
            int widx = gs ? gs.worldIndex : 0;
            int sidx = gs ? gs.stageIndex : 0;

            // フォールバック: 直前に選んだ値を優先
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
                    Debug.LogError("[GameFlow] StageSet が空か未設定です。");
                }
            }
            else
            {
                Debug.LogError("[GameFlow] GameCatalog が取得できません。");
            }
        }

        // 参照を取る（Load の直後なら同期的に見つかる想定）
        turn = UnityCompat.FindFirst<TurnManager>();

        // パネル初期化
        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);
        if (treasurePanel) treasurePanel.SetActive(false);

        // ESCボタン配線
        if (escCloseButton) escCloseButton.onClick.AddListener(CloseEscMenu);
        if (escToStageButton) escToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });
        if (escQuitButton) escQuitButton.onClick.AddListener(QuitGame);

        // バインドUI
        if (bindResetKeyButton) bindResetKeyButton.onClick.AddListener(BeginRebindResetKey);
        UpdateResetKeyLabel();

        // TurnManager イベント購読 & カウンタ初期化
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

            // StageManagerで設定済みの parAP を尊重。
            if (parAP != 0) turn.parAP = Mathf.Max(0, parAP);
        }
    }

    void Update()
    {
        if (!turn) turn = UnityCompat.FindFirst<TurnManager>();

        // Rebind待機中は最優先でキーを捕捉
        if (waitingResetRebind)
        {
            if (InputBindings.TryGetAnyKeyboardKeyDown(out var kc))
            {
                if (kc == KeyCode.Escape)
                {
                    // Esc でキャンセル
                    waitingResetRebind = false;
                    InputBindings.EndCapture(); // ← 追加（キャンセル）
                    UpdateResetKeyLabel();
                }
                else
                {
                    InputBindings.SetResetKey(kc);
                    waitingResetRebind = false;
                    InputBindings.EndCapture(); // ← 追加（確定）
                    UpdateResetKeyLabel();
                }
            }
            // Rebind中は他の更新は続行（必要ならreturnで抜けてもOK）
        }

        bool isOver = (turn && turn.gameOver);
        bool isClear = (turn && turn.cleared);

        // クリア中・ゲームオーバー中は ESC を無効（誤操作防止）
        if (!isOver && !isClear && Input.GetKeyDown(KeyCode.Escape))
        {
            if (escMenuPanel && escMenuPanel.activeSelf) CloseEscMenu();
            else OpenEscMenu();
        }

        // Game Over オーバーレイ
        if (gameOverPanel)
        {
            if (isOver && !gameOverPanel.activeSelf) ShowOnTop(gameOverPanel);
            if (!isOver && gameOverPanel.activeSelf) gameOverPanel.SetActive(false);
        }

        // Clear オーバーレイ（自動遷移はしない）
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

        // 宝箱はここでは表示せず「保留」。Clearパネル表示直後に最前面で出す
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
            treasureImage.enabled = (spr != null); // スプライト未設定なら画像を非表示
        }

        treasurePanel.SetActive(true);
        ShowOnTop(treasurePanel); // 最前面に
    }

    // 結果を表示（必要なら従来ロジックを利用）
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
        // 別Canvasを使う場合は Canvas.overrideSorting + sortingOrder をHUDより上に
    }

    void OpenEscMenu()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(true);
        escMenuPanel.transform.SetAsLastSibling();
        if (pauseOnEsc) Time.timeScale = 0f;
        UpdateResetKeyLabel();
    }
    void CloseEscMenu()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(false);
        waitingResetRebind = false;
        InputBindings.EndCapture(); // ← 追加（開きっぱなしをクリーンアップ）
        ResumeIfPaused();
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

        // パネル類を閉じて（必要なら一時停止解除）
        ResumeIfPaused();
        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);
        if (treasurePanel) treasurePanel.SetActive(false);

        // 盤面ソフトリロード
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
