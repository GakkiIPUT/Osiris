using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StageSelectUI : MonoBehaviour
{
    [Header("UI Refs")]
    public RectTransform grid;           // ← Inspector で必ず割り当て（Gridノード）
    public Button stageButtonPrefab;     // ← Inspector で割り当て
    public Button backToMainButton;      // 任意

    [Header("Layout")]
    public int columns = 2;
    public Vector2 spacing = new Vector2(24, 24);

    // ★ RectOffset を直接フィールドで new しない（禁止）
    //   代わりに int を公開して Awake で RectOffset を組み立てる
    public int padLeft = 16, padRight = 16, padTop = 16, padBottom = 16;

    public float minCellW = 220f;
    public float minCellH = 72f;

    GameState gs;
    GridLayoutGroup glg;

    void Awake()
    {
        // 参照チェック
        if (grid == null)
        {
            //Debug.LogError("[StageSelectUI] 'grid' が未割り当てです。Grid の RectTransform を Inspector で設定してください。");
            enabled = false;
            return;
        }
        if (stageButtonPrefab == null)
        {
            //Debug.LogError("[StageSelectUI] 'stageButtonPrefab' が未割り当てです。ボタンPrefabを設定してください。");
            enabled = false;
            return;
        }

        glg = grid.GetComponent<GridLayoutGroup>();
        if (!glg) glg = grid.gameObject.AddComponent<GridLayoutGroup>();

        glg.startAxis = GridLayoutGroup.Axis.Horizontal;
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = columns;
        glg.childAlignment = TextAnchor.UpperCenter;
        glg.spacing = spacing;

        // ★ ここで RectOffset を new して設定
        glg.padding = new RectOffset(padLeft, padRight, padTop, padBottom);

        gs = UnityCompat.FindFirst<GameState>();
        if (backToMainButton) backToMainButton.onClick.AddListener(() => SceneNavigator.GoMain());
    }

    void Start()
    {
        if (!enabled) return;
        BuildButtons();
        FitCellSize();
        RebuildNow();
    }

    void OnRectTransformDimensionsChange()
    {
        if (!enabled) return;
        if (!grid || !glg) return;   // ★ null ガード
        FitCellSize();
        RebuildNow();
    }

    void RebuildNow()
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(grid);
    }

    void BuildButtons()
    {
        // クリア
        for (int i = grid.childCount - 1; i >= 0; --i)
            Destroy(grid.GetChild(i).gameObject);

        if (gs == null || gs.catalog == null || gs.catalog.worlds == null || gs.worldIndex < 0 || gs.worldIndex >= gs.catalog.worlds.Count)
        {
            Debug.LogError("[StageSelectUI] GameState / Catalog / World の参照が不正です。");
            return;
        }

        var world = gs.catalog.worlds[gs.worldIndex];
        var set = world.stageSet;
        if (set == null || set.stages == null || set.stages.Count == 0)
        {
            Debug.LogWarning("[StageSelectUI] StageSet が空です。");
            return;
        }

        int n = set.stages.Count;

        // 表示順：1,1+half,2,2+half,…（左列=前半、右列=後半）
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
            btn.name = $"Stage_{s.id}";
            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label) label.text = s.id; // "1-1" 等

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
        if (!grid || !glg) return;

        Rect rect = grid.rect;
        int nStages = Mathf.Max(1, grid.childCount);
        int rows = Mathf.CeilToInt(nStages / (float)columns);

        // GridLayoutGroup.padding は RectOffset。ここでは glg.padding を信用し直す
        float availW = rect.width - glg.padding.left - glg.padding.right - (columns - 1) * glg.spacing.x;
        float availH = rect.height - glg.padding.top - glg.padding.bottom - (rows - 1) * glg.spacing.y;

        float cellW = Mathf.Floor(availW / columns);
        float cellH = Mathf.Floor(availH / rows);

        glg.cellSize = new Vector2(Mathf.Max(cellW, minCellW), Mathf.Max(cellH, minCellH));
    }
}
