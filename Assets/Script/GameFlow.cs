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
    public int parAP = 6; // ← 追加: ActionPoint用パー値
    public TMP_Text rankText;          // 例: "S"
    public TMP_Text scoreText;         // 例: "92"
    public TMP_Text detailRotText;     // 例: "回転 8 / 6（+2）"
    public TMP_Text detailRetryText;   // 例: "リトライ 1"
    public TMP_Text resultRankText;    // 結果表示用ランク
    public TMP_Text resultScoreText;   // 結果表示用スコア
    public TMP_Text detailStepText;  　// 例: "歩数 8"
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

            // カウンタ初期化
            turn.ResetScoreCounters();

            // StageManagerで設定済みの parAP を尊重。
            // GameFlow.parAP に手動指定があるときのみ上書きしたい場合は条件付きにする。
            if (parAP != 0)
            {
                turn.parAP = Mathf.Max(0, parAP);
            }
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
        if (rankText) rankText.text = $" {res.rank.ToString()}ランク";
        if (detailRotText) detailRotText.text = $"回転数： {res.rot}回";
        if (detailRetryText) detailRetryText.text = $"リトライ数： {res.retries}回";
        if (detailStepText) detailStepText.text = $"歩数： {res.steps}歩";   // ← 追加

        Debug.Log($"[Result/UI] rank:{res.rank} score:{res.score} rot:{res.rot} steps:{res.steps} retries:{res.retries}");
    }
    // 結果を表示
    void ShowResult()
    {
        var turnManager = UnityCompat.FindFirst<TurnManager>();
        int score;
        char rank;

        if (turnManager.scoreMode == TurnManager.ScoreMode.ActionPoint)
        {
            score = turnManager.CalcScore();
            // ランク判定（例）
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

        // UIに反映
        resultScoreText.text = score.ToString();
        resultRankText.text = rank.ToString();
        // 他の項目も必要に応じて
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
