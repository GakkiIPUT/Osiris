using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TurnManager : MonoBehaviour
{
    public BoardManager board;
    bool playerTurn = true;
    public bool gameOver { get; private set; }
    public bool cleared { get; private set; }

    public bool IsPlayerTurn() => playerTurn && !board.IsAnimating && !gameOver && !cleared;

    public void EndPlayerTurn()
    {
        if (!playerTurn || gameOver || cleared) return;
        playerTurn = false;
        StartCoroutine(GuardsPhase());
    }

    IEnumerator GuardsPhase()
    {
        var guards = board.guards;
        foreach (var g in guards)
        {
            g.DoTurn(); // 1歩
            yield return new WaitForSeconds(0.05f);
            if (gameOver) yield break;
        }
        yield return new WaitForSeconds(0.05f);
        playerTurn = true;
    }

    public void TriggerGameOver()
    {
        if (!gameOver && !cleared) gameOver = true;
    }

    public void TriggerClear()
    {
        if (!cleared && !gameOver) cleared = true;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }

    public void ResetForRetry()
    {
        // private set のプロパティでも、クラス内部なら代入できます
        gameOver = false;
        cleared = false;

        // 必要なら、ターンや一時停止系もここで初期化
        // Time.timeScale = 1f;           // もし TurnManager が時間停止を管理しているなら
        // currentState = State.Player;    // あなたの状態名に合わせて
    }

    // === 収集ゴール用 ===
    public struct RequiredItem
    {
        public char sym;       // 記号（例：i/j/k/l）
        public bool collected; // 左から順に点灯
    }

    readonly List<RequiredItem> required = new();   // 表示順そのまま

    public IReadOnlyList<RequiredItem> CurrentRequired => required;

    public System.Action<IReadOnlyList<RequiredItem>> onRequiredChanged; // UIへ通知

    public void ResetGoalState()
    {
        required.Clear();
        cleared = false;
        gameOver = false;
        NotifyRequired();
    }

    // マップ解析結果（左→右→次の行…の順）で呼ぶ
    public void InitRequiredItems(IList<char> symbolsInOrder)
    {
        required.Clear();
        if (symbolsInOrder != null)
        {
            for (int i = 0; i < symbolsInOrder.Count; i++)
                required.Add(new RequiredItem { sym = symbolsInOrder[i], collected = false });
        }
        NotifyRequired();
    }

    // アイテム取得時：同じ記号の「未収集の一番左」を点灯
    public void OnItemPicked(char sym)
    {
        for (int i = 0; i < required.Count; i++)
        {
            if (required[i].sym == sym && !required[i].collected)
            {
                var e = required[i]; e.collected = true; required[i] = e;
                break;
            }
        }
        NotifyRequired();
    }

    public bool AllItemsCollected()
    {
        for (int i = 0; i < required.Count; i++)
            if (!required[i].collected) return false;
        return true;
    }

    public void TryClearAtExit()
    {
        if (AllItemsCollected())
        {
            cleared = true;
            NotifyRequired(); // 最終状態で再通知（任意）
        }
        else
        {
            // 未収集あり：ここでUI点滅などのフィードバックを出すならイベントをぶら下げてもOK
        }
    }

    void NotifyRequired() => onRequiredChanged?.Invoke(required);

}