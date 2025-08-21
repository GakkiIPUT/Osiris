#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class LevelPainter : EditorWindow
{
    // ---- 参照 ----
    [SerializeField] BoardManager board;
    [SerializeField] TextAsset mapTxt;      // ← これを読み書きの「唯一のソース」にする

    // ---- 編集バッファ（mapTxtの内容を常にここに展開し、ペイントはこの配列へ）----
    string[] rows = new string[0];

    // ---- ブラシ ----
    char currentSymbol = '#';
    bool paintWhileDrag = true;
    Color hoverColor = new Color(1f, 1f, 0f, 0.75f);
    
    //----サイズ変更----
    int resizeW = -1;
    int resizeH = -1;

    // ---- UI スクロール ----
    Vector2 scroll;

    [MenuItem("Tools/Level Painter")]
    public static void Open() => GetWindow<LevelPainter>("Level Painter");

    void OnEnable() { SceneView.duringSceneGui += OnSceneGUI; }
    void OnDisable() { SceneView.duringSceneGui -= OnSceneGUI; }

    void OnGUI()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            board = (BoardManager)EditorGUILayout.ObjectField("Board", board, typeof(BoardManager), true);
            mapTxt = (TextAsset)EditorGUILayout.ObjectField("Map .txt", mapTxt, typeof(TextAsset), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selected Board"))
                {
                    var sel = Selection.activeGameObject;
            EditorGUILayout.LabelField($"Map Size  W:{Width}  H:{Height}");
        if (sel) board = sel.GetComponentInParent<BoardManager>();
                }
                EditorGUI.BeginDisabledGroup(mapTxt == null);
                if (GUILayout.Button("Load From Text")) { LoadFromText(); }
                if (GUILayout.Button("Save To Text")) { SaveToText(); }
                EditorGUI.EndDisabledGroup();
            }

            if (rows == null || rows.Length == 0)
            {
                EditorGUILayout.HelpBox("Load From Text を押すか、Board から現在のレベルを取り込んでください。", MessageType.Info);
                if (GUILayout.Button("Import From Board (current)")) ImportFromBoard();
            }
            else
            {
                EditorGUILayout.LabelField($"Map Size  W:{Width}  H:{Height}");
                // 初期値セット
                if (resizeW < 0 || resizeH < 0) { resizeW = Width; resizeH = Height; }

                // リサイズ入力と適用ボタン
                using (new EditorGUILayout.HorizontalScope())
                {
                    resizeW = Mathf.Max(1, EditorGUILayout.IntField("Width", resizeW));
                    resizeH = Mathf.Max(1, EditorGUILayout.IntField("Height", resizeH));
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Apply Resize (pad '.')")) { ApplyResize(resizeW, resizeH); }
                    if (GUILayout.Button("Fit to Current")) { resizeW = Width; resizeH = Height; }
                }
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            // タイル系（固定）
            using (new EditorGUILayout.HorizontalScope())
            {
                ToggleBrush('.', "Floor (.)");
                ToggleBrush('#', "Wall (#)");
                ToggleBrush('@', "Anchor (@)");
                ToggleBrush('E', "Exit (E)");
                ToggleBrush('P', "Player (P)");
            }

            // Guard を BoardManager.guardTypes から動的追加
            if (board && board.guardTypes != null && board.guardTypes.Count > 0)
            {
                EditorGUILayout.LabelField("Guards");
                int col = 0; EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < board.guardTypes.Count; i++)
                {
                    var gt = board.guardTypes[i];
                    char sym = SafeSym(gt.symbol, 'G');
                    string label = string.IsNullOrEmpty(gt.label) ? $"Guard ({sym})" : $"{gt.label} ({sym})";
                    ToggleBrush(sym, label);
                    if (++col % 3 == 0) { EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); }
                }
                EditorGUILayout.EndHorizontal();
            }

            // Item を BoardManager.itemTypes から動的追加
            if (board && board.itemTypes != null && board.itemTypes.Count > 0)
            {
                EditorGUILayout.LabelField("Items");
                int col = 0; EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < board.itemTypes.Count; i++)
                {
                    var it = board.itemTypes[i];
                    char sym = SafeSym(it.symbol, 'i');
                    string label = string.IsNullOrEmpty(it.label) ? $"Item ({sym})" : $"{it.label} ({sym})";
                    ToggleBrush(sym, label);
                    if (++col % 3 == 0) { EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            paintWhileDrag = EditorGUILayout.ToggleLeft("Paint while dragging", paintWhileDrag);
            hoverColor = EditorGUILayout.ColorField("Hover Color", hoverColor);

            EditorGUILayout.HelpBox(
                "Sceneビュー：左クリック/ドラッグで配置、右クリックで床('.')。キー: 1 '.' / 2 '#' / 3 '@' / 4 'E' / 5 'P'。文字キーで任意記号に切替。",
                MessageType.None
            );
        }
    }
    void ApplyResize(int newW, int newH)
    {
        if (rows == null) return;
        newW = Mathf.Max(1, newW);
        newH = Mathf.Max(1, newH);

        var newRows = new string[newH];
        for (int y = 0; y < newH; y++)
        {
            if (y < rows.Length)
            {
                var src = rows[y] ?? "";
                // 幅調整：短ければ '.' でパディング、長ければ切り落とし
                if (src.Length < newW) src = src + new string('.', newW - src.Length);
                else if (src.Length > newW) src = src.Substring(0, newW);
                newRows[y] = src;
            }
            else
            {
                newRows[y] = new string('.', newW); // 新規行は全面床
            }
        }

        rows = newRows;
        resizeW = newW; resizeH = newH;
        ApplyToBoard();        // 画面に即反映
        Repaint();
    }

    // -------- Scene GUI（ペイント） --------
    void OnSceneGUI(SceneView sv)
    {
        if (rows == null || rows.Length == 0) return;

        // ショートカット
        var e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Alpha1) currentSymbol = '.';
            else if (e.keyCode == KeyCode.Alpha2) currentSymbol = '#';
            else if (e.keyCode == KeyCode.Alpha3) currentSymbol = '@';
            else if (e.keyCode == KeyCode.Alpha4) currentSymbol = 'E';
            else if (e.keyCode == KeyCode.Alpha5) currentSymbol = 'P';
            else if (!char.IsControl(e.character) && e.character != '\0') currentSymbol = e.character;
            Repaint();
        }

        // マウス座標 → グリッド
        if (!TryGetMouseGrid(out var g)) return;

        // ホバー
        Handles.color = hoverColor;
        DrawCellWire(CellOrigin(g), 1f);

        // ペイント
        bool left = e.type == EventType.MouseDown && e.button == 0;
        bool leftDrag = paintWhileDrag && e.type == EventType.MouseDrag && e.button == 0;
        bool right = e.type == EventType.MouseDown && e.button == 1;

        if (!e.alt && (left || leftDrag || right))
        {
            e.Use();
            char sym = right ? '.' : currentSymbol;
            Paint(g, sym);
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        }
    }

    // --------- 主要処理 ---------
    void LoadFromText()
    {
        if (mapTxt == null) return;
        rows = Parse(mapTxt.text);
        NormalizeRows();
        Repaint();
        // 表示にも反映（即ボード確認したい時）
        ApplyToBoard();
    }

    void SaveToText()
    {
        if (mapTxt == null || rows == null) return;
        NormalizeRows();
        string path = AssetDatabase.GetAssetPath(mapTxt);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("TextAsset のパスが見つかりません。Project内の .txt を指定してください。");
            return;
        }
        File.WriteAllText(path, string.Join("\n", rows));
        AssetDatabase.ImportAsset(path);
        EditorUtility.SetDirty(mapTxt);
        Debug.Log($"Saved map to: {path}");
    }

    void ImportFromBoard()
    {
        if (!board || board.level == null || board.level.Length == 0)
        {
            Debug.LogWarning("Board の level が空です。");
            return;
        }
        rows = board.level.ToArray();
        NormalizeRows();
        Repaint();
    }

    void ApplyToBoard()
    {
        if (!board || rows == null || rows.Length == 0) return;
        NormalizeRows();
        board.SetLevel(rows);        // BoardManager 側で Build まで面倒を見ます
        // シーンに反映
        SceneView.RepaintAll();
    }

    // --------- ユーティリティ ---------
    int Width => (rows == null || rows.Length == 0) ? 0 : rows.Max(r => r?.Length ?? 0);
    int Height => rows?.Length ?? 0;

    void NormalizeRows()
    {
        if (rows == null) return;
        int w = Width;
        for (int y = 0; y < rows.Length; y++)
        {
            string r = rows[y] ?? "";
            if (r.Length < w) r = r + new string('.', w - r.Length);
            rows[y] = r;
        }
    }

    static string[] Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) return new string[0];
        return text.Replace("\r", "")
                   .Split('\n')
                   .Where(l => !string.IsNullOrWhiteSpace(l))
                   .ToArray();
    }

    void Paint(Vector2Int p, char sym)
    {
        if (p.x < 0 || p.y < 0 || p.y >= Height || p.x >= Width) return;

        // 'P' は常に1個に保つ（新規Pを置いたら他のPを床に）
        if (sym == 'P')
        {
            for (int y = 0; y < Height; y++)
            {
                int ix = rows[y].IndexOf('P');
                if (ix >= 0)
                {
                    var arr = rows[y].ToCharArray();
                    arr[ix] = '.';
                    rows[y] = new string(arr);
                }
            }
        }

        var line = rows[p.y].ToCharArray();
        line[p.x] = sym;
        rows[p.y] = new string(line);

        // 置いたら即 Board に適用して見た目を確認できるように
        ApplyToBoard();
    }

    // 2Dグリッド計算（BoardManager の座標系に合わせてXZ平面に1タイル=1m）
    Vector3 CellOrigin(Vector2Int g) => new Vector3(g.x, 0f, g.y);
    bool TryGetMouseGrid(out Vector2Int grid)
    {
        grid = default;
        var e = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (!plane.Raycast(ray, out float enter)) return false;
        Vector3 hit = ray.GetPoint(enter);

        // ボード境界でクランプ（rows基準）
        int gx = Mathf.FloorToInt(hit.x + 0.5f);
        int gy = Mathf.FloorToInt(hit.z + 0.5f);
        if (gx < 0 || gy < 0 || gy >= Height || gx >= Width) return false;
        grid = new Vector2Int(gx, gy);
        return true;
    }

    // UI
    void ToggleBrush(char symbol, string label)
    {
        bool on = currentSymbol == symbol;
        bool next = GUILayout.Toggle(on, label, "Button");
        if (next && !on) currentSymbol = symbol;
    }
    char SafeSym(string s, char fallback) => !string.IsNullOrEmpty(s) ? s[0] : fallback;

    // 目安のワイヤ
    void DrawCellWire(Vector3 world, float size)
    {
        var c = world + new Vector3(0.5f, 0, 0.5f);
        Vector3 a = c + new Vector3(-size / 2, 0, -size / 2);
        Vector3 b = c + new Vector3(size / 2, 0, -size / 2);
        Vector3 d = c + new Vector3(size / 2, 0, size / 2);
        Vector3 e = c + new Vector3(-size / 2, 0, size / 2);
        Handles.DrawLine(a, b); Handles.DrawLine(b, d); Handles.DrawLine(d, e); Handles.DrawLine(e, a);
    }
}
#endif
