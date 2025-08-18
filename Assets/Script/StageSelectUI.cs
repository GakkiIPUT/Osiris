using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StageSelectUI : MonoBehaviour
{
    public TMP_Text worldLabel;
    public RectTransform listRoot;       // ステージボタンの親
    public Button stageButtonPrefab;     // シンプルなButtonプレハブ
    public Button backToMainButton;

    void Start()
    {
        var gs = UnityCompat.FindFirst<GameState>();
        var world = gs.catalog.worlds[gs.worldIndex];
        worldLabel.text = world.displayName;

        // 既存ボタン破棄
        for (int i = listRoot.childCount - 1; i >= 0; --i) Destroy(listRoot.GetChild(i).gameObject);

        // ステージボタン生成
        var set = world.stageSet;
        for (int i = 0; i < set.stages.Count; i++)
        {
            int idx = i;
            var b = Instantiate(stageButtonPrefab, listRoot);
            b.GetComponentInChildren<TMP_Text>().text = set.stages[i].displayName;
            b.onClick.AddListener(() =>
            {
                gs.stageIndex = idx;
                SceneNavigator.GoGame();
            });
        }

        backToMainButton.onClick.AddListener(() => SceneNavigator.GoMain());
    }
}
