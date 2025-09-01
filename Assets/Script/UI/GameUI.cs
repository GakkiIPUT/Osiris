using UnityEngine;
using UnityEngine.UI;

public class GameUI : MonoBehaviour
{
    public Button btnRotateL, btnRotateR, btnRangeToggle, btnReset;
    StageManager stage; PlayerController player; TurnManager turn;

    // devMode追加
    public bool devMode = false;

    // ===== 初期設定（インスペクター） =====
    [Header("Editor 用 初期設定")]
    [SerializeField] bool editorDevMode = false;
    [Tooltip("Editor再生時の無敵初期値")]
    [SerializeField] bool editorInvincible = false;
    [SerializeField, Range(3, 9)] int editorAreaSize = 3;

    [Header("Editor 用 Board初期設定")]
    [SerializeField] bool editorRotatePlayerWithArea = true;
    [SerializeField, Range(1, 10)] int editorRotationCenterMaxDistance = 3;
    [SerializeField] bool editorFreeRotate = true;           // 自由回転は有効
    [SerializeField] bool editorAllow180Rotation = false;

    [Header("Development Build 用 初期設定")]
    [SerializeField] bool devBuildDevMode = false;            // 基本 Dev モード
    [SerializeField] bool devBuildInvincible = false;
    [SerializeField, Range(3, 9)] int devBuildAreaSize = 3;

    [Header("Development Build 用 Board初期設定")]
    [SerializeField] bool devBuildRotatePlayerWithArea = true;
    [SerializeField, Range(1, 10)] int devBuildRotationCenterMaxDistance = 3;
    [SerializeField] bool devBuildFreeRotate = true;         // 自由回転は有効
    [SerializeField] bool devBuildAllow180Rotation = false;

    [Header("適用タイミング")]
    [SerializeField] bool applyInitialOnStart = true;

    bool _appliedInitial = false;

    void Awake()
    {
        // ボタン紐付
        if (btnRotateL) btnRotateL.onClick.AddListener(ActionRotateL);
        if (btnRotateR) btnRotateR.onClick.AddListener(ActionRotateR);
        if (btnRangeToggle) btnRangeToggle.onClick.AddListener(ActionToggleRange);
        if (btnReset) btnReset.onClick.AddListener(ActionReset);
    }

    void Start()
    {
        if (applyInitialOnStart) ApplyInitialSettingsOnce();
    }

    void OnEnable() { ResolveRefs(); }  // 表示時に再解決

    void ResolveRefs()
    {
        if (!stage) stage = UnityCompat.FindFirst<StageManager>();
        if (!player) player = UnityCompat.FindFirst<PlayerController>();
        if (!turn) turn = UnityCompat.FindFirst<TurnManager>();
    }

    void ApplyInitialSettingsOnce()
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
        devMode = devBuildDevMode;                            // 基本 Dev モード

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
            player.invincible = false;                        // 無敵は必ずオフ
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

    void ActionRotateL() { ResolveRefs(); player?.UI_RotateCCW(); }
    void ActionRotateR() { ResolveRefs(); player?.UI_RotateCW(); }
    void ActionToggleRange() { ResolveRefs(); player?.UI_ToggleAreaSize(); }

    void ActionReset()
    {
        ResolveRefs();
        var gf = UnityCompat.FindFirst<GameFlow>();
        if (gf != null) gf.RequestRetry();
        else stage?.ReloadCurrent(); // フォールバック
    }

    void Update()
    {
        // 追加: メニュー表示中はゲームUIの入力処理を停止
        if (GlobalEscMenu.IsMenuOpen) return;

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

    void OnGUI()
    {
        if (!devMode) return; // devModeはboolで管理

        GUILayout.BeginArea(new Rect(10, 10, 330, 520), "開発者モード", GUI.skin.window);

        // 回転に関する設定
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (board != null)
        {
            bool changed = false;

            // プレイヤーを回転に含める
            bool rpwa = GUILayout.Toggle(board.rotatePlayerWithArea, "プレイヤーを回転に含める");
            if (rpwa != board.rotatePlayerWithArea) { board.rotatePlayerWithArea = rpwa; changed = true; }

            // 回転中心最大距離
            int maxDist = Mathf.RoundToInt(GUILayout.HorizontalSlider(board.rotationCenterMaxDistance, 1, 10));
            if (maxDist != board.rotationCenterMaxDistance) { board.rotationCenterMaxDistance = maxDist; changed = true; }
            GUILayout.Label($"回転中心の最大距離: {board.rotationCenterMaxDistance}");

            GUILayout.Space(6);
            GUILayout.Label("回転モード", EditorLabel());

            // 自由回転ON/OFF
            bool free = GUILayout.Toggle(board.devEnableFreeRotate, "自由回転（ドラッグ）を有効にする");
            if (free != board.devEnableFreeRotate) { board.devEnableFreeRotate = free; changed = true; }
            GUILayout.Label("※ 自由回転ONの間はQ/E回転は無効。Tはキャンセル専用");

            // 180度回転を許可
            bool allow180 = GUILayout.Toggle(board.devAllow180Rotation, "180°回転を許可（2AP想定）");
            if (allow180 != board.devAllow180Rotation) { board.devAllow180Rotation = allow180; changed = true; }

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
            if (newAreaSize != player.areaSize) {
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

        GUILayout.EndArea();
    }

    GUIStyle EditorLabel()
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontStyle = FontStyle.Bold;
        return s;
    }
}
