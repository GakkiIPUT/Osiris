using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StageSelectUI : MonoBehaviour
{
    [Header("UI Refs")]
    public RectTransform grid;           // Grid Layout Group のノード
    public Button stageButtonPrefab;     // ボタンPrefab（子に TMP_Text）
    public Button backToMainButton;      // メインに戻る（任意）

    [Header("Layout")]
    public int columns = 2;              // 2列固定
    public Vector2 spacing = new Vector2(24, 24);
    public RectOffset padding = new RectOffset(16, 16, 16, 16);
    public float minCellW = 220f;        // 小さすぎ防止
    public float minCellH = 72f;         // 小さすぎ防止（必要なら 64 に）

    GameState gs;
    GridLayoutGroup glg;

    void Awake()
    {
        glg = grid.GetComponent<GridLayoutGroup>();
        if (!glg) glg = grid.gameObject.AddComponent<GridLayoutGroup>();

        // ★行ベースで左→右に詰めてから次の行へ
        glg.startAxis = GridLayoutGroup.Axis.Horizontal;
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = columns;
        glg.childAlignment = TextAnchor.UpperCenter;
        glg.spacing = spacing;
        glg.padding = padding;

        gs = UnityCompat.FindFirst<GameState>();
        if (backToMainButton) backToMainButton.onClick.AddListener(() => SceneNavigator.GoMain());
    }

    void Start()
    {
        BuildButtons();
        FitCellSize();   // 初回フィット
        RebuildNow();    // レイアウト反映を確実に
    }

    void OnRectTransformDimensionsChange()
    {
        FitCellSize();
        RebuildNow();
    }

    void RebuildNow()
    {
        // レイアウト確定を強制（縦が合わない症状の対策）
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(grid);
    }

    void BuildButtons()
    {
        for (int i = grid.childCount - 1; i >= 0; --i)
            Destroy(grid.GetChild(i).gameObject);

        var world = gs.catalog.worlds[gs.worldIndex];
        var set = world.stageSet;
        int n = set.stages.Count;

        // 表示順：1,1+half,2,2+half,…（行ごとの左/右ペア）
        int half = (n + 1) / 2;
        var order = new List<int>(n);
        for (int i = 0; i < half; i++)
        {
            order.Add(i);
            if (i + half < n) order.Add(i + half);
        }

        foreach (int idx in order)
        {
            var s = set.stages[idx];
            var btn = Instantiate(stageButtonPrefab, grid);
            btn.name = $"Stage_{s.displayName}";
            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label) label.text = s.displayName; // "1-1" 等

            int captured = idx;
            btn.onClick.AddListener(() =>
            {
                gs.stageIndex = captured;
                SceneNavigator.GoGame();
            });
        }
    }

    void FitCellSize()
    {
        var rect = grid.rect;
        int nStages = Mathf.Max(1, grid.childCount);
        int rows = Mathf.CeilToInt(nStages / (float)columns);

        float availW = rect.width - glg.padding.left - glg.padding.right - (columns - 1) * glg.spacing.x;
        float availH = rect.height - glg.padding.top - glg.padding.bottom - (rows - 1) * glg.spacing.y;

        float cellW = Mathf.Floor(availW / columns);
        float cellH = Mathf.Floor(availH / rows);

        // 小さすぎ防止（必要なら下げてOK）
        cellW = Mathf.Max(cellW, minCellW);
        cellH = Mathf.Max(cellH, minCellH);

        glg.cellSize = new Vector2(cellW, cellH);
    }
}
