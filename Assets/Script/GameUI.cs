using UnityEngine;
using UnityEngine.UI;

public class GameUI : MonoBehaviour
{
    public Button btnRotateL, btnRotateR, btnRangeToggle, btnReset;
    StageManager stage; PlayerController player; TurnManager turn;

    // devMode追加
    public bool devMode = false;

    void Awake()
    {
        // ボタン配線
        if (btnRotateL) btnRotateL.onClick.AddListener(ActionRotateL);
        if (btnRotateR) btnRotateR.onClick.AddListener(ActionRotateR);
        if (btnRangeToggle) btnRangeToggle.onClick.AddListener(ActionToggleRange);
        if (btnReset) btnReset.onClick.AddListener(ActionReset);
    }

    void OnEnable() { ResolveRefs(); }  // 表示直後に参照解決

    void ResolveRefs()
    {
        if (!stage) stage = UnityCompat.FindFirst<StageManager>();
        if (!player) player = UnityCompat.FindFirst<PlayerController>();
        if (!turn) turn = UnityCompat.FindFirst<TurnManager>();
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
        // F1キーでデバッグモードON/OFF
        if (Input.GetKeyDown(KeyCode.F1))
        {
            devMode = !devMode;
        }

        // リセット（リバインド中/抑止中は無効化）
        if (!InputBindings.IsCapturing && InputBindings.IsResetPressed())
        {
            var gf = UnityCompat.FindFirst<GameFlow>();
            if (gf != null) gf.RequestRetry();
            else stage?.ReloadCurrent(); // フォールバック
        }

        // 回転ボタンは「プレイヤーターン かつ エイム中」だけ有効
        ResolveRefs();
        //ターン制　bool canRotate = (player && turn != null && turn.IsPlayerTurn() && player.IsAiming);
        bool canRotate = player && turn != null && !turn.gameOver && !turn.cleared && player.IsAiming;

        if (btnRotateL) btnRotateL.interactable = canRotate;
        if (btnRotateR) btnRotateR.interactable = canRotate;
    }

    void OnGUI()
    {
        if (!devMode) return; // devModeはboolで管理

        GUILayout.BeginArea(new Rect(10, 10, 300, 400), "開発者モード", GUI.skin.window);

        // 回転に自分を含めるか
        BoardManager board = Object.FindFirstObjectByType<BoardManager>();
        if (board != null)
        {
            board.rotatePlayerWithArea = GUILayout.Toggle(board.rotatePlayerWithArea, "回転に自分を含める");
            board.rotationCenterMaxDistance = Mathf.RoundToInt(GUILayout.HorizontalSlider(board.rotationCenterMaxDistance, 1, 10));
            GUILayout.Label($"回転中心距離: {board.rotationCenterMaxDistance}");
        }

        // 回転範囲半径
        PlayerController player = Object.FindFirstObjectByType<PlayerController>();
        if (player != null)
        {
            player.invincible = GUILayout.Toggle(player.invincible, "無敵モード");
            player.SaveDevModeSettings();

            int newAreaSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(player.areaSize, 3, 9));
            if (newAreaSize != player.areaSize) {
                player.areaSize = newAreaSize;
                player.SaveDevModeSettings();
            }
            GUILayout.Label($"回転範囲サイズ: {player.areaSize}");
        }

        // アイテム回収/ゴールフラグ
        TurnManager turn = Object.FindFirstObjectByType<TurnManager>();
        if (turn != null)
        {
            turn.itemCollected = GUILayout.Toggle(turn.itemCollected, "アイテム回収フラグ");
            turn.goalReached = GUILayout.Toggle(turn.goalReached, "ゴールフラグ");
        }

        GUILayout.EndArea();
    }
}
