using UnityEngine;
using UnityEngine.UI;

public class GameUI : MonoBehaviour
{
    public Button btnRotateL, btnRotateR, btnRangeToggle, btnReset;
    StageManager stage; PlayerController player; TurnManager turn;

    // devModeを追加
    public bool devMode = false;

    void Awake()
    {
        // ボタン配線
        if (btnRotateL) btnRotateL.onClick.AddListener(ActionRotateL);
        if (btnRotateR) btnRotateR.onClick.AddListener(ActionRotateR);
        if (btnRangeToggle) btnRangeToggle.onClick.AddListener(ActionToggleRange);
        if (btnReset) btnReset.onClick.AddListener(ActionReset);
    }

    void OnEnable() { ResolveRefs(); }  // 画面復帰時も参照掴み直し

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
        else stage?.ReloadCurrent(); // フォールバック（カウントは増えない）
    }

    void Update()
    {
        // F1キーで開発者モードON/OFF
        if (Input.GetKeyDown(KeyCode.F1))
        {
            devMode = !devMode;
        }
        // Rキーでリセット：カウントを正しく増やすため RequestRetry に統一
        if (Input.GetKeyDown(KeyCode.R))
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
            int newAreaSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(player.areaSize, 3, 9));
            if (newAreaSize != player.areaSize) player.areaSize = newAreaSize;
            GUILayout.Label($"回転範囲サイズ: {player.areaSize}");

            player.invincible = GUILayout.Toggle(player.invincible, "無敵モード");
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
