using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class StageSelectUI : MonoBehaviour
{
    [Header("UI Refs")]
    public RectTransform grid;

    public Button stageButtonPrefab;
    public Button backToMainButton;

    [Header("Layout")]
    public int columns = 2;

    public Vector2 spacing = new Vector2(24, 24);
    public int padLeft = 16, padRight = 16, padTop = 16, padBottom = 16;
    public float minCellW = 160f;
    public float minCellH = 72f;

    private GameState gs;
    private GridLayoutGroup glg;

    /// <summary>参照の確立とGridLayoutGroupの初期設定、戻るボタンの配線。</summary>
    private void Awake()
    {
        if (grid == null) { enabled = false; return; }
        if (stageButtonPrefab == null) { enabled = false; return; }

        glg = grid.GetComponent<GridLayoutGroup>();
        if (!glg) glg = grid.gameObject.AddComponent<GridLayoutGroup>();

        glg.startAxis = GridLayoutGroup.Axis.Horizontal;
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = columns;
        glg.childAlignment = TextAnchor.UpperCenter;
        glg.spacing = spacing;
        glg.padding = new RectOffset(padLeft, padRight, padTop, padBottom);

        gs = UnityCompat.FindFirst<GameState>();
        if (backToMainButton) backToMainButton.onClick.AddListener(() => SceneNavigator.GoMain());
    }

    /// <summary>
    /// 画面生成（ボタン生成→セルサイズ調整→レイアウト再構築）と初期フォーカス設定。
    /// </summary>
    private void Start()
    {
        if (!enabled) return;
        BuildButtons();
        FitCellSize();
        RebuildNow();
        StartCoroutine(CoSelectFirst());
    }

    /// <summary>再表示時にも初期フォーカスを戻す。</summary>
    private void OnEnable()
    {
        if (!enabled) return;
        StartCoroutine(CoSelectFirst());
    }

    /// <summary>親Rectのサイズ変更に応じてセルサイズを再計算する。</summary>
    private void OnRectTransformDimensionsChange()
    {
        if (!enabled) return;
        if (!grid || !glg) return;
        FitCellSize();
        RebuildNow();
    }

    /// <summary>即時レイアウトリビルドを行う。</summary>
    private void RebuildNow()
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(grid);
    }

    /// <summary>
    /// カタログからステージボタンを生成し、蛇行配置順で並べる。
    /// クリックで Game シーンへ遷移する。
    /// </summary>
    private void BuildButtons()
    {
        for (int i = grid.childCount - 1; i >= 0; --i)
            Destroy(grid.GetChild(i).gameObject);

        if (gs == null || gs.catalog == null || gs.catalog.worlds == null || gs.worldIndex < 0 || gs.worldIndex >= gs.catalog.worlds.Count)
        {
            Debug.LogError("[StageSelectUI] GameState / Catalog / World の参照に失敗。");
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
            if (label) label.text = s.id;

            int captured = idx;
            btn.onClick.AddListener(() =>
            {
                if (gs != null)
                {
                    gs.stageIndex = captured;
                    PlayerPrefs.SetInt("lastWorldIndex", gs.worldIndex);
                    PlayerPrefs.SetInt("lastStageIndex", gs.stageIndex);
                    PlayerPrefs.SetInt("enteredViaStageSelect", 1);
                    PlayerPrefs.Save();
                }
                SceneNavigator.GoGame();
            });
        }
    }

    /// <summary>
    /// グリッド領域のサイズからセルサイズを算出し、最小値を下回らないよう調整する。
    /// </summary>
    private void FitCellSize()
    {
        if (!grid || !glg) return;

        Rect rect = grid.rect;
        int nStages = Mathf.Max(1, grid.childCount);
        int rows = Mathf.CeilToInt(nStages / (float)columns);

        float availW = rect.width - glg.padding.left - glg.padding.right - (columns - 1) * glg.spacing.x;
        float availH = rect.height - glg.padding.top - glg.padding.bottom - (rows - 1) * glg.spacing.y;

        float cellW = Mathf.Floor(availW / columns);
        float cellH = Mathf.Floor(availH / rows);

        glg.cellSize = new Vector2(Mathf.Max(cellW, minCellW), Mathf.Max(cellH, minCellH));
    }

    /// <summary>
    /// 次フレームで最初のステージボタンにフォーカスする（UI再構築後）。
    /// </summary>
    private System.Collections.IEnumerator CoSelectFirst()
    {
        yield return null;
        if (EventSystem.current == null) yield break;
        for (int i = 0; i < grid.childCount; i++)
        {
            var go = grid.GetChild(i).gameObject;
            var btn = go.GetComponent<Button>();
            if (btn && btn.interactable && go.activeInHierarchy)
            {
                EventSystem.current.SetSelectedGameObject(go);
                break;
            }
        }
    }
}
