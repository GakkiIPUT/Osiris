using UnityEngine;

public class InputHelpUI : MonoBehaviour
{
    TurnManager turn;

    void Start()
    {
        turn = UnityCompat.FindFirst<TurnManager>();
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        GUILayout.BeginArea(new Rect(10, 10, 520, 200));
        GUILayout.Label("Move: WASD / Arrow  |  Aim: Left Click  |  Rotate: Q/E or Mouse Wheel  |  Area: 1=3x3 / 2=5x5  |  Reset: R", style);
        if (turn != null && turn.gameOver)
        {
            var r = new Rect(Screen.width / 2 - 100, 40, 350, 80);
            GUI.Label(r, "<color=red><b>DETECTED! (R to Restart)</b></color>", GetRichStyle());
        }
        if (turn != null && turn.cleared)
        {
            var r = new Rect(Screen.width / 2 - 100, 40, 350, 80);
            GUI.Label(r, "<color=cyan><b>CLEARED! (R to Restart)</b></color>", GetRichStyle());
        }
        GUILayout.EndArea();
    }

    GUIStyle GetRichStyle()
    {
        return new GUIStyle(GUI.skin.label) { richText = true, fontSize = 22, alignment = TextAnchor.UpperLeft };
    }
}
