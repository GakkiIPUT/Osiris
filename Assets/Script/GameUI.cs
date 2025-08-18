using UnityEngine;
using UnityEngine.UI;

public class GameUI : MonoBehaviour
{
    public Button btnRotateL, btnRotateR, btnRangeToggle, btnReset;
    StageManager stage; PlayerController player; TurnManager turn;

    void Awake()
    {
        // ここでは結線だけ（参照はまだnullでもOK）
        if (btnRotateL) btnRotateL.onClick.AddListener(ActionRotateL);
        if (btnRotateR) btnRotateR.onClick.AddListener(ActionRotateR);
        if (btnRangeToggle) btnRangeToggle.onClick.AddListener(ActionToggleRange);
        if (btnReset) btnReset.onClick.AddListener(ActionReset);
    }

    void OnEnable() { ResolveRefs(); }  // 画面に戻った時も掴み直す

    void ResolveRefs()
    {
        if (!stage) stage = UnityCompat.FindFirst<StageManager>();
        if (!player) player = UnityCompat.FindFirst<PlayerController>();
        if (!turn) turn = UnityCompat.FindFirst<TurnManager>();
    }

    void ActionRotateL() { ResolveRefs(); player?.UI_RotateCCW(); }
    void ActionRotateR() { ResolveRefs(); player?.UI_RotateCW(); }
    void ActionToggleRange() { ResolveRefs(); player?.UI_ToggleAreaSize(); }
    void ActionReset() { UnityCompat.FindFirst<GameFlow>()?.HardReset(); }

    void Update()
    {
        // Rキーでリセット
        if (Input.GetKeyDown(KeyCode.R)) { ResolveRefs(); stage?.ReloadCurrent(); }

        // 回転ボタンは「プレイヤーターン かつ エイム中」だけ有効
        ResolveRefs();
        bool canRotate = player && turn != null && turn.IsPlayerTurn() && player.IsAiming;
        if (btnRotateL) btnRotateL.interactable = canRotate;
        if (btnRotateR) btnRotateR.interactable = canRotate;
    }
}
