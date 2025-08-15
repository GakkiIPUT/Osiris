using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameUI : MonoBehaviour
{
    [Header("Refs")]
    public StageManager stage;
    public BoardManager board;
    public PlayerController player;
    public TurnManager turn;

    [Header("Buttons")]
    public Button btnRotateL;
    public Button btnRotateR;
    public Button btnRangeToggle;
    public Button btnVision;
    public Button btnReset;
    public Button btnPrev;
    public Button btnNext;

    [Header("Stage Select (optional)")]
    public TMP_Dropdown stageDropdown; // TextMeshPro 推奨（なければ UnityEngine.UI.Dropdown でもOK）
    public TMP_Text stageLabel;

    void Awake()
    {
        if (stage == null) stage = UnityCompat.FindFirst<StageManager>();
        if (board == null) board = UnityCompat.FindFirst<BoardManager>();
        if (player == null) player = UnityCompat.FindFirst<PlayerController>();
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();

        // ボタンイベント
        if (btnRotateL) btnRotateL.onClick.AddListener(() => player?.UI_RotateCCW());
        if (btnRotateR) btnRotateR.onClick.AddListener(() => player?.UI_RotateCW());
        if (btnRangeToggle) btnRangeToggle.onClick.AddListener(() => player?.UI_ToggleAreaSize());
        if (btnVision) btnVision.onClick.AddListener(() => player?.UI_ToggleVision());
        if (btnReset) btnReset.onClick.AddListener(() => stage?.ReloadCurrent());
        if (btnPrev) btnPrev.onClick.AddListener(() => { stage?.Prev(); RefreshStageUI(); });
        if (btnNext) btnNext.onClick.AddListener(() => { stage?.Next(); RefreshStageUI(); });

        if (stageDropdown)
        {
            stageDropdown.onValueChanged.AddListener((i) =>
            {
                if (stage == null) return;
                stage.Load(i);
                RefreshStageUI();
            });
        }

        RefreshStageUI();
    }

    void RefreshStageUI()
    {
        if (stage == null) return;

        var names = stage.GetAllNames();
        if (stageDropdown)
        {
            stageDropdown.ClearOptions();
            stageDropdown.AddOptions(new System.Collections.Generic.List<string>(names));
            stageDropdown.value = Mathf.Clamp(stage.currentIndex, 0, Mathf.Max(0, names.Length - 1));
            stageDropdown.RefreshShownValue();
        }
        if (stageLabel) stageLabel.text = stage.GetDisplayName();
    }
}
