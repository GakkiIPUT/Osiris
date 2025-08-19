using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    public GameObject iconPrefab; // Image(アイコン) (+ 任意でTMP_Text) を持つプレハブ

    [Header("Icon Library")]
    public List<IconDef> icons = new List<IconDef>();
    public Sprite fallbackSprite;         // 未登録シンボル用（任意）

    [Header("Style")]
    [Tooltip("未取得アイコンの透明度")] public float uncollectedAlpha = 0.35f;
    [Tooltip("種別同士の間隔（px）※スペーサーで表現")] public float groupGap = 16f;
    [Tooltip("アイコンの推奨サイズ（px）")] public float iconPreferredSize = 56f;

    TurnManager turn;

    void Start()
    {
        EnsureContainerLayout();   // ★ レイアウトを自動補強
        TryHookTurnManager();      // ★ TurnManager を見つけて即描画
    }

    void OnEnable()
    {
        // シーン再表示等のケースでも拾えるように
        TryHookTurnManager();
    }

    void OnDestroy()
    {
        if (turn != null) turn.onRequiredChanged -= Refresh;
    }

    // ==== TurnManager 取得＆初期描画 ====
    void TryHookTurnManager()
    {
        if (turn != null) return;

        turn = UnityCompat.FindFirst<TurnManager>();
        if (turn != null)
        {
            turn.onRequiredChanged += Refresh;
            // ★ 初期状態を即描画（「取るまで出ない」を防止）
            Refresh(turn.CurrentRequired);
        }
        else
        {
            // BoardManager.Build() が後で TurnManager を作る場合に備えて1フレーム後に再試行
            StartCoroutine(HookWhenReady());
        }
    }

    IEnumerator HookWhenReady()
    {
        yield return null; // 1フレーム待つ
        if (turn == null) turn = UnityCompat.FindFirst<TurnManager>();
        if (turn != null)
        {
            turn.onRequiredChanged += Refresh;
            Refresh(turn.CurrentRequired);
        }
    }

    // ==== レイアウト補助 ====
    void EnsureContainerLayout()
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

    void ApplyIconLayout(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt != null) rt.sizeDelta = new Vector2(iconPreferredSize, iconPreferredSize);

        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.preferredWidth = iconPreferredSize;
        le.preferredHeight = iconPreferredSize;
    }

    GameObject CreateSpacer(float width)
    {
        var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(container, false);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width;
        return go;
    }

    // ==== 描画 ====
    // TurnManager から RequiredItem の「フラット配列（重複あり）」が来る
    // ここで “種別ごと” にまとめ直して並べる
    public void Refresh(IReadOnlyList<TurnManager.RequiredItem> list)
    {
        // 既存を全消し
        for (int i = container.childCount - 1; i >= 0; --i)
            Destroy(container.GetChild(i).gameObject);

        if (list == null || list.Count == 0) return;

        // 1) 集計：total / collected / firstIndex（初出順で並べる）
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
                firstIndex[s] = i; // 初めて見えた位置を保持（初出順ソート用）
            }
            total[s]++;
            if (list[i].collected) collected[s]++;
        }

        // 2) 種別を“初出順”にソート
        var symbols = new List<char>(total.Keys);
        symbols.Sort((a, b) => firstIndex[a].CompareTo(firstIndex[b]));

        // 3) 種別ごとにまとまりを作って並べる
        bool firstGroup = true;
        foreach (char s in symbols)
        {
            int need = total[s];
            int got = collected[s];

            // グループ間の余白（スペーサー）
            if (!firstGroup && groupGap > 0f) CreateSpacer(groupGap);
            firstGroup = false;

            // need 個のアイコンを横に並べる。左から got 個を不透明に
            var def = icons.Find(x => x.symbol == s);
            Sprite spr = def != null ? def.sprite : fallbackSprite;
            string label = (def != null && !string.IsNullOrEmpty(def.label)) ? def.label : s.ToString();

            for (int i = 0; i < need; i++)
            {
                var go = Instantiate(iconPrefab, container);
                go.name = $"Item_{s}_{i + 1}/{need}";
                ApplyIconLayout(go); // ★ レイアウト要素付与

                var img = go.GetComponentInChildren<Image>(true);
                var txt = go.GetComponentInChildren<TMP_Text>(true);
                if (img) img.sprite = spr;
                if (txt) txt.text = label; // ラベル不要ならPrefabからTMP_Textを外してOK

                // 取得済み（左から）を不透明、残りは半透明
                SetAlpha(go, (i < got) ? 1f : uncollectedAlpha);
            }
        }
    }

    void SetAlpha(GameObject go, float a)
    {
        foreach (var g in go.GetComponentsInChildren<Graphic>(true))
        {
            var c = g.color; c.a = a; g.color = c;
        }
    }
}
