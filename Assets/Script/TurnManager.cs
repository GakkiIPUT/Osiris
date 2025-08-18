using System.Collections;
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
}
