using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 必須アイテムの取得状況を UI 上に表示する。TurnManager と連動し、種別ごとに整列表示する。
/// </summary>
public class RequiredItemsUI : MonoBehaviour
{
    [System.Serializable]
    public class IconDef
    {
        public char symbol;   // 例: 'i','j','k','l'
        public Sprite sprite; // 表示用
        public string label;  // 任意（未使用なら空でOK）
    }

    [Header("Refs")]
    public Transform container;   // HorizontalLayoutGroup を付けたノード

    public GameObject iconPrefab; // Image/TMP_Text を持つプレハブ

    [Header("Icon Library")]
    public List<IconDef> icons = new List<IconDef>();

    public Sprite fallbackSprite; // 未登録シンボル用（任意）

    [Header("Style")]
    [Tooltip("未取得アイコンの透明度")] public float uncollectedAlpha = 0.35f;

    [Tooltip("種別同士の間隔（px）※スペーサーで表現")] public float groupGap = 16f;
    [Tooltip("アイコンの推奨サイズ（px）")] public float iconPreferredSize = 56f;

    private TurnManager turn;

    /// <summary>必要ならレイアウト補助を付与し、TurnManager と接続する。</summary>
    private void Start()
    {
        EnsureContainerLayout();
        TryHookTurnManager();
    }

    /// <summary>再表示時にも TurnManager をフックする。</summary>
    private void OnEnable()
    {
        TryHookTurnManager();
    }

    /// <summary>イベント購読を解除する。</summary>
    private void OnDestroy()
    {
        if (turn != null) turn.onRequiredChanged -= Refresh;
    }

    // ==== TurnManager 取得＆初期描画 ====

    /// <summary>TurnManager を見つけて onRequiredChanged を購読し、初期描画する。</summary>
    private void TryHookTurnManager()
    {
        if (turn != null) return;

        turn = UnityCompat.FindFirst<TurnManager>();
        if (turn != null)
        {
            turn.onRequiredChanged += Refresh;
            Refresh(turn.CurrentRequired);
        }
        else
        {
            StartCoroutine(HookWhenReady());
        }
    }

    /// <summary>1フレーム待ってから再度 TurnManager を探索し、購読・描画する。</summary>
    private IEnumerator HookWhenReady()
    {
        yield return null;
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn != null)
        {
            turn.onRequiredChanged += Refresh;
            Refresh(turn.CurrentRequired);
        }
    }

    // ==== レイアウト補助 ====

    /// <summary>コンテナに HorizontalLayoutGroup を自動で付与・設定する。</summary>
    private void EnsureContainerLayout()
    {
        if (!container) return;
        var h = container.GetComponent<HorizontalLayoutGroup>();
        if (h == null) h = container.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleLeft;
        h.spacing = 12f;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
    }

    /// <summary>アイコンの推奨サイズ/レイアウトを適用する。</summary>
    private void ApplyIconLayout(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt != null) rt.sizeDelta = new Vector2(iconPreferredSize, iconPreferredSize);

        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.preferredWidth = iconPreferredSize;
        le.preferredHeight = iconPreferredSize;
    }

    /// <summary>固定幅のスペーサー要素を生成する。</summary>
    private GameObject CreateSpacer(float width)
    {
        var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(container, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width;
        return go;
    }

    // ==== 描画 ====

    /// <summary>
    /// TurnManager から渡された RequiredItem のフラット配列を、種別ごとに集計し並べて描画する。
    /// </summary>
    public void Refresh(IReadOnlyList<TurnManager.RequiredItem> list)
    {
        for (int i = container.childCount - 1; i >= 0; --i)
            Destroy(container.GetChild(i).gameObject);

        if (list == null || list.Count == 0) return;

        AddFlexibleSpacer("LeftFlex");

        var total = new Dictionary<char, int>();
        var collected = new Dictionary<char, int>();
        var firstIndex = new Dictionary<char, int>();

        for (int i = 0; i < list.Count; i++)
        {
            char s = list[i].sym;
            if (!total.ContainsKey(s))
            {
                total[s] = 0;
                collected[s] = 0;
                firstIndex[s] = i;
            }
            total[s]++;
            if (list[i].collected) collected[s]++;
        }

        var symbols = new List<char>(total.Keys);
        symbols.Sort((a, b) => firstIndex[a].CompareTo(firstIndex[b]));

        bool firstGroup = true;
        foreach (char s in symbols)
        {
            int need = total[s];
            int got = collected[s];

            if (!firstGroup && groupGap > 0f) CreateSpacer(groupGap);
            firstGroup = false;

            var def = icons.Find(x => x.symbol == s);
            Sprite spr = def != null ? def.sprite : fallbackSprite;
            string label = (def != null && !string.IsNullOrEmpty(def.label)) ? def.label : s.ToString();

            for (int i = 0; i < need; i++)
            {
                var go = Instantiate(iconPrefab, container);
                go.name = $"Item_{s}_{i + 1}/{need}";
                ApplyIconLayout(go);

                var img = go.GetComponentInChildren<Image>(true);
                var txt = go.GetComponentInChildren<TMP_Text>(true);
                if (img) img.sprite = spr;
                if (txt) txt.text = label;

                SetAlpha(go, (i < got) ? 1f : uncollectedAlpha);
            }
        }

        AddFlexibleSpacer("RightFlex");
    }

    /// <summary>左右の余白吸収用のフレキシブルスペーサーを追加する。</summary>
    private void AddFlexibleSpacer(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(container, false);
        var le = go.GetComponent<LayoutElement>();
        le.minWidth = 0f;
        le.preferredWidth = 0f;
        le.flexibleWidth = 1f;
    }

    /// <summary>子階層のGraphicへアルファ値を一括適用する。</summary>
    private void SetAlpha(GameObject go, float a)
    {
        foreach (var g in go.GetComponentsInChildren<Graphic>(true))
        {
            var c = g.color; c.a = a; g.color = c;
        }
    }
}
