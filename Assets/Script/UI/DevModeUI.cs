using UnityEngine;

public class DevModeUI : MonoBehaviour
{
    BoardManager board;
    PlayerController player;
    TurnManager turn;

    void Update()
    {
        // 最新インスタンス取得
        board = Object.FindFirstObjectByType<BoardManager>();
        player = Object.FindFirstObjectByType<PlayerController>();
        turn = Object.FindFirstObjectByType<TurnManager>();

        // F1: 回転に自分を含めるトグル
        if (Input.GetKeyDown(KeyCode.F1) && board != null)
        {
            board.rotatePlayerWithArea = !board.rotatePlayerWithArea;
            Debug.Log($"[F1] rotatePlayerWithArea: {board.rotatePlayerWithArea}");
            board.SaveDevModeSettings();
        }

        // Shift+F1: 無敵トグル
        if (Input.GetKeyDown(KeyCode.F1) && player != null && Input.GetKey(KeyCode.LeftShift))
        {
            player.invincible = !player.invincible;
            Debug.Log($"[Shift+F1] invincible: {player.invincible}");
            board.SaveDevModeSettings();
        }

        // Ctrl+F1: 回転範囲サイズ変更（3→5→7→9→3…）
        if (Input.GetKeyDown(KeyCode.F1) && player != null && Input.GetKey(KeyCode.LeftControl))
        {
            int[] sizes = { 3, 5, 7, 9 };
            int idx = System.Array.IndexOf(sizes, player.areaSize);
            player.areaSize = sizes[(idx + 1) % sizes.Length];
            Debug.Log($"[Ctrl+F1] areaSize: {player.areaSize}");
            board.SaveDevModeSettings();
        }

        // Alt+F1: アイテム回収/ゴールフラグトグル
        if (Input.GetKeyDown(KeyCode.F1) && turn != null && Input.GetKey(KeyCode.LeftAlt))
        {
            turn.itemCollected = !turn.itemCollected;
            turn.goalReached = !turn.goalReached;
            Debug.Log($"[Alt+F1] itemCollected: {turn.itemCollected}, goalReached: {turn.goalReached}");
            board.SaveDevModeSettings();
        }
    }

    public TurnManager turnManager;

    public void ToggleScoreMode()
    {
        if (turnManager.scoreMode == TurnManager.ScoreMode.Legacy)
            turnManager.scoreMode = TurnManager.ScoreMode.ActionPoint;
        else
            turnManager.scoreMode = TurnManager.ScoreMode.Legacy;
    }
}