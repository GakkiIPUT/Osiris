using UnityEngine;
using UnityEngine.UI;

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
    public GameObject clearPanel;           // ← 追加
    public Button clearToStageButton;       // ← 追加
    public bool pauseOnClear = true;        // ← 追加

    StageManager stage;
    TurnManager turn;

    void Start()
    {
        var gs = UnityCompat.FindFirst<GameState>();
        stage = UnityCompat.FindFirst<StageManager>();
        turn = UnityCompat.FindFirst<TurnManager>();

        var world = gs.catalog.worlds[gs.worldIndex];
        stage.stageSet = world.stageSet;
        stage.Load(gs.stageIndex);

        // 初期は非表示（SetActiveのみ）
        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);

        if (escCloseButton) escCloseButton.onClick.AddListener(CloseEscMenu);
        if (escToStageButton) escToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });

        if (retryButton) retryButton.onClick.AddListener(HardReset);
        if (toMenuButton) toMenuButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoMain(); });

        if (clearToStageButton) clearToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });
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

        // Clear オーバーレイ（ここで自動遷移はしない）
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

    // ---- 表示/非表示（SetActive + 最前面化） ----
    void ShowOnTop(GameObject panel)
    {
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        // （別Canvasに置いた場合は、そのCanvas.sortingOrder をHUDより上に）
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

    public void HardReset()
    {
        ResumeIfPaused();
        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        if (clearPanel) clearPanel.SetActive(false);
        SceneNavigator.GoGame();
    }
}
