using UnityEngine;
using UnityEngine.UI;

public class GameFlow : MonoBehaviour
{
    [Header("Esc Menu")]
    public GameObject escMenuPanel;     // ← Canvas の子にあること
    public Button escCloseButton;
    public Button escToStageButton;
    public bool pauseOnEsc = true;

    [Header("Game Over")]
    public GameObject gameOverPanel;    // ← Canvas の子にあること
    public Button retryButton;
    public Button toMenuButton;

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

        // ★最小ロジック：初期は消す（SetActiveのみ）
        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);

        if (escCloseButton) escCloseButton.onClick.AddListener(CloseEscMenu);
        if (escToStageButton) escToStageButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoStage(); });

        if (retryButton) retryButton.onClick.AddListener(HardReset);
        if (toMenuButton) toMenuButton.onClick.AddListener(() => { ResumeIfPaused(); SceneNavigator.GoMain(); });
    }

    void Update()
    {
        if (!turn) turn = UnityCompat.FindFirst<TurnManager>();

        bool isOver = (turn && turn.gameOver);

        // ESC（GameOver中は無効）
        if (!isOver && Input.GetKeyDown(KeyCode.Escape))
        {
            if (escMenuPanel && escMenuPanel.activeSelf) CloseEscMenu();
            else OpenEscMenu();
        }

        // GameOver：出す/消すを明示（SetActiveのみ）
        if (gameOverPanel)
        {
            if (isOver && !gameOverPanel.activeSelf) ShowOnTop(gameOverPanel);
            if (!isOver && gameOverPanel.activeSelf) gameOverPanel.SetActive(false);
        }
    }

    // ---- 表示/非表示（SetActive + 最前面化） ----
    void ShowOnTop(GameObject panel)
    {
        panel.SetActive(true);
        // 同じ Canvas 内で最前面へ
        panel.transform.SetAsLastSibling();
        // もし別Canvasを使っているなら、そのCanvasの Sorting Order を上げる
        var ownCanvas = panel.GetComponentInParent<Canvas>();
        if (ownCanvas) ownCanvas.sortingOrder = 100; // HUDが0なら十分上
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
        if (pauseOnEsc && Time.timeScale == 0f) Time.timeScale = 1f;
    }

    public void HardReset()
    {
        ResumeIfPaused();
        if (escMenuPanel) escMenuPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        SceneNavigator.GoGame();
    }
}
