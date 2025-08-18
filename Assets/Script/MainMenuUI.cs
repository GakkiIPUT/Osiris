using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    public Button[] worldButtons; // インスペクタで 1面,2面…のボタンを並べる

    void Start()
    {
        var gs = FindObjectOfType<GameState>();
        for (int i = 0; i < worldButtons.Length; i++)
        {
            int idx = i;
            worldButtons[i].onClick.AddListener(() =>
            {
                gs.worldIndex = idx;
                gs.stageIndex = 0; // 先頭から
                SceneNavigator.GoStage();
            });
        }
    }
}
