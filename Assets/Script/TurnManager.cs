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
            g.DoTurn(); // 1•à
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
}
