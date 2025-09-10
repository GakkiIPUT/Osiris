using UnityEngine;
using UnityEngine.UI;

public class GameUI : MonoBehaviour
{
    public Button btnRotateL, btnRotateR, btnRangeToggle, btnReset;

    [Header("Tutorial Reset (Optional Buttons)")]
    public Button btnResetTutorialCurrent;
    public Button btnResetTutorialAll;

    private StageManager stage; private PlayerController player; private TurnManager turn;
    public bool devMode = false;

    [Header("Editor 用 初期設定")]
    [SerializeField] private bool editorDevMode = false;
    [SerializeField] private bool editorInvincible = false;
    [SerializeField, Range(3, 9)] private int editorAreaSize = 3;

    [Header("Editor 用 Board初期設定")]
    [SerializeField] private bool editorRotatePlayerWithArea = true;
    [SerializeField, Range(1, 10)] private int editorRotationCenterMaxDistance = 3;
    [SerializeField] private bool editorFreeRotate = true;
    [SerializeField] private bool editorAllow180Rotation = false;

    [Header("Development Build 用 初期設定")]
    [SerializeField] private bool devBuildDevMode = false;
    [SerializeField] private bool devBuildInvincible = false;
    [SerializeField, Range(3, 9)] private int devBuildAreaSize = 3;

    [Header("Development Build 用 Board初期設定")]
    [SerializeField] private bool devBuildRotatePlayerWithArea = true;
    [SerializeField, Range(1, 10)] private int devBuildRotationCenterMaxDistance = 3;
    [SerializeField] private bool devBuildFreeRotate = true;
    [SerializeField] private bool devBuildAllow180Rotation = false;

    [Header("適用タイミング")]
    [SerializeField] private bool applyInitialOnStart = true;

    private bool _appliedInitial = false;

    private void Awake()
    {
        // ボタンフック
        if (btnRotateL) btnRotateL.onClick.AddListener(ActionRotateL);
        if (btnRotateR) btnRotateR.onClick.AddListener(ActionRotateR);
        if (btnRangeToggle) btnRangeToggle.onClick.AddListener(ActionToggleRange);
        if (btnReset) btnReset.onClick.AddListener(ActionReset);
        if (btnResetTutorialCurrent) btnResetTutorialCurrent.onClick.AddListener(ResetTutorialForCurrentStage);
        if (btnResetTutorialAll) btnResetTutorialAll.onClick.AddListener(ResetTutorialForAllStages);
    }

    private void Start()
    {
        if (applyInitialOnStart) ApplyInitialSettingsOnce();
    }

    private void OnEnable()
    { ResolveRefs(); }  // 表示時に再解決

    private void ResolveRefs()
    {
        if (!stage) stage = UnityCompat.FindFirst<StageManager>();
        if (!player) player = UnityCompat.FindFirst<PlayerController>();
        if (!turn) turn = UnityCompat.FindFirst<TurnManager>();
    }

    private void ApplyInitialSettingsOnce()
    {
        if (_appliedInitial) return;
        _appliedInitial = true;

        ResolveRefs();
        var board = Object.FindFirstObjectByType<BoardManager>();

#if UNITY_EDITOR
        // Editor
        devMode = editorDevMode;

        if (player != null)
        {
            player.invincible = editorInvincible;
            player.areaSize = Mathf.RoundToInt(Mathf.Clamp(editorAreaSize, 3, 9));
            player.SaveDevModeSettings();
        }
        if (board != null)
        {
            board.rotatePlayerWithArea = editorRotatePlayerWithArea;
            board.rotationCenterMaxDistance = Mathf.RoundToInt(Mathf.Clamp(editorRotationCenterMaxDistance, 1, 10));
            board.devEnableFreeRotate = editorFreeRotate;     // 自由回転ON
            board.devAllow180Rotation = editorAllow180Rotation;
            board.SaveDevModeSettings();
        }
#elif DEVELOPMENT_BUILD
        // Development Build
        devMode = devBuildDevMode;                            // 既定 Dev モード

        if (player != null)
        {
            player.invincible = devBuildInvincible;
            player.areaSize = Mathf.RoundToInt(Mathf.Clamp(devBuildAreaSize, 3, 9));
            player.SaveDevModeSettings();
        }
        if (board != null)
        {
            board.rotatePlayerWithArea = devBuildRotatePlayerWithArea;
            board.rotationCenterMaxDistance = Mathf.RoundToInt(Mathf.Clamp(devBuildRotationCenterMaxDistance, 1, 10));
            board.devEnableFreeRotate = devBuildFreeRotate;   // 自由回転ON
            board.devAllow180Rotation = devBuildAllow180Rotation;
            board.SaveDevModeSettings();
        }
#else
        // 本番ビルド（非Development)
        devMode = false;

        if (player != null)
        {
            player.invincible = false;                        // 無敵は必ずOFF
            player.areaSize = Mathf.Clamp(player.areaSize, 3, 9);
            player.SaveDevModeSettings();
        }
        if (board != null)
        {
            board.devEnableFreeRotate = true;                // 自由回転
            board.devAllow180Rotation = false;
            board.SaveDevModeSettings();
        }
#endif
    }

    private void ActionRotateL()
    { ResolveRefs(); player?.UI_RotateCCW(); }
    private void ActionRotateR()
    { ResolveRefs(); player?.UI_RotateCW(); }
    private void ActionToggleRange()
    { ResolveRefs(); player?.UI_ToggleAreaSize(); }

    private void ActionReset()
    {
        ResolveRefs();
        var gf = UnityCompat.FindFirst<GameFlow>();
        if (gf != null) gf.RequestRetry();
        else stage?.ReloadCurrent(); // フォールバック
    }

    // 追加: チュートリアル既読を現在ステージのみリセット
    public void ResetTutorialForCurrentStage()
    {
        var st = UnityCompat.FindFirst<StageManager>();
        var entry = st != null ? st.GetCurrentEntry() : null;
        if (entry == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[GameUI] ResetTutorialForCurrentStage: Stage entry not found.");
#endif
            return;
        }

        string key = $"tut_seen_{entry.id}";
        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
#if UNITY_EDITOR
        Debug.Log($"[GameUI] Tutorial seen-flag cleared for stage: {entry.id} (key={key})");
#endif
    }

    // 追加: チュートリアル既読を全ステージ分リセット
    public void ResetTutorialForAllStages()
    {
        var st = UnityCompat.FindFirst<StageManager>();
        var set = st != null ? st.stageSet : null;
        if (set == null || set.stages == null || set.stages.Count == 0)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[GameUI] ResetTutorialForAllStages: StageSet not found or empty.");
#endif
            return;
        }

        int cnt = 0;
        for (int i = 0; i < set.stages.Count; i++)
        {
            var e = set.stages[i];
            if (e == null || string.IsNullOrEmpty(e.id)) continue;
            string key = $"tut_seen_{e.id}";
            PlayerPrefs.DeleteKey(key);
            cnt++;
        }
        PlayerPrefs.Save();
#if UNITY_EDITOR
        Debug.Log($"[GameUI] Tutorial seen-flags cleared for all stages. count={cnt}");
#endif
    }

    private void Update()
    {
        // 追加: メニュー/チュートリアルオーバーレイ表示中はゲームUIの入力処理を停止
        if (GlobalEscMenu.IsMenuOpen) return;
        if (GameFlow.TutorialOverlayOpen) return;

        if (Input.GetKeyDown(KeyCode.F1)) { devMode = !devMode; }

        if (!InputBindings.IsCapturing && InputBindings.IsResetPressed())
        {
            var gf = UnityCompat.FindFirst<GameFlow>();
            if (gf != null) gf.RequestRetry();
            else stage?.ReloadCurrent();
        }

        ResolveRefs();
        var board = Object.FindFirstObjectByType<BoardManager>();

        bool canRotate =
            player && turn != null && !turn.gameOver && !turn.cleared &&
            player.IsAiming &&
            board != null && !board.devEnableFreeRotate; // 自由回転ONなら回転ボタン無効

        if (btnRotateL) btnRotateL.interactable = canRotate;
        if (btnRotateR) btnRotateR.interactable = canRotate;
    }

    private void OnGUI()
    {
        if (!devMode) return; // devModeでUI表示制御

        GUILayout.BeginArea(new Rect(10, 10, 360, 600), "開発モード", GUI.skin.window);

        // 回転に関する設定
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (board != null)
        {
            bool changed = false;

            // プレイヤー回転に追従
            bool rpwa = GUILayout.Toggle(board.rotatePlayerWithArea, "プレイヤー回転に追従");
            if (rpwa != board.rotatePlayerWithArea) { board.rotatePlayerWithArea = rpwa; changed = true; }

            // 回転中心最大距離
            int maxDist = Mathf.RoundToInt(GUILayout.HorizontalSlider(board.rotationCenterMaxDistance, 1, 10));
            if (maxDist != board.rotationCenterMaxDistance) { board.rotationCenterMaxDistance = maxDist; changed = true; }
            GUILayout.Label($"回転中心の最大距離: {board.rotationCenterMaxDistance}");

            GUILayout.Space(6);
            GUILayout.Label("回転モード", EditorLabel());

            // 自由回転ON/OFF
            bool free = GUILayout.Toggle(board.devEnableFreeRotate, "自由回転（デバッグ）を有効にする");
            if (free != board.devEnableFreeRotate) { board.devEnableFreeRotate = free; changed = true; }
            GUILayout.Label("※ 自由回転ON中はQ/E回転無効");

            // 180度回転許可
            bool allow180 = GUILayout.Toggle(board.devAllow180Rotation, "180度回転許可（2AP）");
            if (allow180 != board.devAllow180Rotation) { board.devAllow180Rotation = allow180; changed = true; }

            // ★ 追加: スティック離し確定モード
            var frc = Object.FindFirstObjectByType<FreeRotateController>();
            if (frc != null)
            {
                bool rel = GUILayout.Toggle(frc.padUseReleaseToCommit, "スティック離しで確定 (Pad)");
                if (rel != frc.padUseReleaseToCommit) frc.padUseReleaseToCommit = rel;
                GUILayout.Label(rel ? "Aボタン=即確定(任意) / 離しでも確定" : "Aボタン=確定 / 離しでは確定しない");
            }

            if (changed) board.SaveDevModeSettings();
        }

        GUILayout.Space(10);

        // プレイヤー設定（無敵/範囲）
        PlayerController player = Object.FindFirstObjectByType<PlayerController>();
        if (player != null)
        {
            bool inv = GUILayout.Toggle(player.invincible, "無敵モード");
            if (inv != player.invincible) { player.invincible = inv; player.SaveDevModeSettings(); }

            int newAreaSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(player.areaSize, 3, 9));
            if (newAreaSize != player.areaSize)
            {
                player.areaSize = newAreaSize;
                player.SaveDevModeSettings();
            }
            GUILayout.Label($"回転範囲サイズ: {player.areaSize}");
        }

        // アイテム/ゴールフラグ
        TurnManager turn = Object.FindFirstObjectByType<TurnManager>();
        if (turn != null)
        {
            turn.itemCollected = GUILayout.Toggle(turn.itemCollected, "アイテム取得フラグ");
            turn.goalReached = GUILayout.Toggle(turn.goalReached, "ゴール到達フラグ");
        }

        GUILayout.Space(10);
        GUILayout.Label("チュートリアル", EditorLabel());
        if (GUILayout.Button("既読リセット（現在ステージ）")) { ResetTutorialForCurrentStage(); }
        if (GUILayout.Button("既読リセット（全ステージ）")) { ResetTutorialForAllStages(); }
        GUILayout.Label("次回ロードでチュートリアル再表示");

        GUILayout.EndArea();
    }

    private GUIStyle EditorLabel()
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontStyle = FontStyle.Bold;
        return s;
    }
}
