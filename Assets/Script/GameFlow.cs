using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameFlow : MonoBehaviour
{
    [Header("Esc Menu")]
    public GameObject escMenuPanel;
    public Button escCloseButton;
    public Button escToStageButton;
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
    public TMP_Text rankText;          // 例: "S"
    public TMP_Text scoreText;         // 例: "92"
    public TMP_Text detailRotText;     // 例: "回転 8 / 6（+2）"
    public TMP_Text detailRetryText;   // 例: "リトライ 1"

    StageManager stage;
    TurnManager turn;

    void Start()
    {
        var gs = UnityCompat.FindFirst<GameState>();
        stage = UnityCompat.FindFirst<StageManager>();

        // ステージロード（StageManager 側が Board/Turn を生成）
        if (gs != null && stage != null)
        {
            var world = gs.catalog.worlds[gs.worldIndex];
            stage.stageSet = world.stageSet;
            stage.Load(gs.stageIndex);
        }

        // 参照を取る（Load の直後なら同期的に見つかる想定）
        turn = UnityCompat.FindFirst<TurnManager>();

        // まず全パネル非表示
        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);

        // ボタン配線
        if (escCloseButton) escCloseButton.onClick.AddListener(CloseEscMenu);
        if (escToStageButton) escToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });

        if (retryButton) retryButton.onClick.AddListener(RequestRetry);
        if (toMenuButton) toMenuButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoMain(); });

        if (clearToStageButton) clearToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });

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
            // ステージ開始時にスコアカウンタをクリア
            turn.ResetScoreCounters();
        }
    }

    void Update()
    {
        if (!turn) turn = UnityCompat.FindFirst<TurnManager>();

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
            }
            if (!isClear && clearPanel.activeSelf)
            {
                clearPanel.SetActive(false);
            }
        }
    }

    // ==== クリア結果の受取 ====
    void OnStageCleared(TurnManager.ScoreResult res)
    {
        // 表示（Textは未割当なら無視）
        if (rankText) rankText.text = $" {res.rank.ToString()}ランク";
        //if (scoreText) scoreText.text = $"スコア： {res.score.ToString()}点"; 
        if (detailRotText) detailRotText.text = $"回転数： {res.rot}回";
        if (detailRetryText) detailRetryText.text = $"リトライ数： {res.retries}回";
    }

    // ==== UIユーティリティ ====
    void ShowOnTop(GameObject panel)
    {
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        // ※ 別Canvasなら sortingOrder を HUD より上にしてください
    }

    void OpenEscMenu()
    {
        if (!escMenuPanel) return;
        ShowOnTop(escMenuPanel);
        if (pauseOnEsc) Time.timeScale = 0f;
    }
    void CloseEscMenu()
    {
        if (!escMenuPanel) return;
        escMenuPanel.SetActive(false);
        ResumeIfPaused();
    }

    void ResumeIfPaused()
    {
        if (Time.timeScale == 0f) Time.timeScale = 1f;
    }

    // ==== リトライ：ここ経由に統一 ====
    // GameFlow.cs の RequestRetry を差し替え
    public void RequestRetry()
    {
        var tm = UnityCompat.FindFirst<TurnManager>();
        tm?.RegisterRetry();        // ★1回だけ加算
        tm?.ResetForRestart();      // ★ゲームオーバー/クリア状態を解除し、回転数もリセット

        // パネル類を閉じて（必要なら一時停止解除）
        ResumeIfPaused();
        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);

        // 盤面ソフトリロード（同じシーンのまま現在ステージを再構築）
        UnityCompat.FindFirst<StageManager>()?.ReloadCurrent();
    }
}
