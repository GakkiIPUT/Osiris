using UnityEngine;
using UnityEngine.UI;

public class GameUI : MonoBehaviour
{
    public Button btnRotateL, btnRotateR, btnRangeToggle, btnReset;
    StageManager stage; PlayerController player; TurnManager turn;

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
}
