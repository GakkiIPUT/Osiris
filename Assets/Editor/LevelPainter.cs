#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public class LevelPainter : EditorWindow
{
    BoardManager board;
    char Sym(string s, char fallback) => !string.IsNullOrEmpty(s) ? s[0] : fallback;

    // 現在の“記号ブラシ”
    char currentSymbol = '#';

    bool paintWhileDrag = true;
    Color hoverColor = new Color(1f, 1f, 0f, 0.75f);

    int resizeW = -1;
    int resizeH = -1;

    Vector2 scroll;
    bool foldTiles = true;
    bool foldGuards = true;
    bool foldItems = true;

    [MenuItem("Tools/Level Painter")]
    public static void Open() => GetWindow<LevelPainter>("Level Painter");

    void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
    void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Target Board", EditorStyles.boldLabel);
        board = (BoardManager)EditorGUILayout.ObjectField(board, typeof(BoardManager), true);

        if (GUILayout.Button("Use Selected (BoardManager)"))
        {
            var sel = Selection.activeGameObject;
            if (sel) board = sel.GetComponentInParent<BoardManager>();
        }

        if (board && (resizeW < 0 || resizeH < 0))
        {
            resizeW = board.Width;
            resizeH = board.Height;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);

        // ---- Tiles ----
        foldTiles = EditorGUILayout.Foldout(foldTiles, "Tiles", true);
        if (foldTiles)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (ToggleBrush('.', "1 Floor (.)")) { }
                if (ToggleBrush('#', "2 Wall (#)")) { }
                if (ToggleBrush('@', "3 Anchor (@)")) { }  // ★ 追加：回転不可マス
                if (ToggleBrush('E', "4 Exit (E)")) { }
                if (ToggleBrush('P', "5 Player (P)")) { }
            }
        }

        // ---- Guards ----
        if (board && board.guardTypes != null && board.guardTypes.Count > 0)
        {
            EditorGUILayout.Space();
            foldGuards = EditorGUILayout.Foldout(foldGuards, "Guards", true);
            if (foldGuards)
            {
                int col = 0;
                EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < board.guardTypes.Count; i++)
                {
                    var gt = board.guardTypes[i];
                    string label = string.IsNullOrEmpty(gt.label)
                        ? $"Guard ({gt.symbol})"
                        : $"{gt.label} ({gt.symbol})";
                    if (ToggleBrush(Sym(gt.symbol, 'G'), $"{label}")) { }

                    if (++col % 2 == 0)
                    {
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("数字キー: 6以降で順にガードに割り当ててもOK。", MessageType.None);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("BoardManager > Guard Types にガードPrefabを登録すると、ここにブラシが増えます。", MessageType.Info);
        }

        // ---- Items ----
        if (board && board.itemTypes != null && board.itemTypes.Count > 0)
        {
            EditorGUILayout.Space();
            foldItems = EditorGUILayout.Foldout(foldItems, "Items", true);
            if (foldItems)
            {
                int col = 0;
                EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < board.itemTypes.Count; i++)
                {
                    var it = board.itemTypes[i];
                    string label = string.IsNullOrEmpty(it.label)
                        ? $"Item ({it.symbol})"
                        : $"{it.label} ({it.symbol})";
                    if (ToggleBrush(Sym(it.symbol, 'i'), label)) { }

                    if (++col % 3 == 0)
                    {
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("文字キーで直接記号に切替できます（例: i, j, k…）。", MessageType.None);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("BoardManager > Item Types にアイテムPrefabを登録すると、ここにブラシが増えます。", MessageType.Info);
        }

        EditorGUILayout.EndScrollView();

        paintWhileDrag = EditorGUILayout.ToggleLeft("Paint while dragging", paintWhileDrag, GUILayout.Width(220));
        hoverColor = EditorGUILayout.ColorField("Hover Color", hoverColor);

        EditorGUILayout.Space();
        if (board)
        {
            EditorGUILayout.LabelField($"Map Size: {board.Width} x {board.Height}");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild Level Now")) board.SendMessage("EditorRebuildNow", SendMessageOptions.DontRequireReceiver);
                if (GUILayout.Button("Commit Tiles Now (Editor)")) board.SendMessage("EditorCommitNow", SendMessageOptions.DontRequireReceiver);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resize Map", EditorStyles.boldLabel);
            resizeW = Mathf.Max(1, EditorGUILayout.IntField("Width", resizeW));
            resizeH = Mathf.Max(1, EditorGUILayout.IntField("Height", resizeH));
            if (GUILayout.Button("Apply Resize (pad with '.')"))
            {
                ResizeLevel(resizeW, resizeH, '.');
                Rebuild();
            }
        }

        EditorGUILayout.HelpBox(
            "Sceneビュー：左クリック/ドラッグで配置。右クリックで床('.')に戻す。数字キー: 1=Floor, 2=Wall, 3=Anchor, 4=Exit, 5=Player。",
            MessageType.Info
        );
    }

    bool ToggleBrush(char symbol, string label)
    {
        bool on = currentSymbol == symbol;
        bool next = GUILayout.Toggle(on, label, "Button");
        if (next && !on) currentSymbol = symbol;
        return next;
    }

    void OnSceneGUI(SceneView sv)
    {
        if (!board) return;

        var e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Alpha1) currentSymbol = '.';
            else if (e.keyCode == KeyCode.Alpha2) currentSymbol = '#';
            else if (e.keyCode == KeyCode.Alpha3) currentSymbol = '@'; // ★ Anchor
            else if (e.keyCode == KeyCode.Alpha4) currentSymbol = 'E';
            else if (e.keyCode == KeyCode.Alpha5) currentSymbol = 'P';
            else if (!char.IsControl(e.character) && e.character != '\0')
            {
                currentSymbol = e.character; // i,j,k…などダイレクト入力
            }
            Repaint();
        }

        if (!TryGetMouseGrid(out var grid)) return;

        Handles.color = hoverColor;
        DrawCellWire(board.GridToWorld(grid), 1f);

        bool left = e.type == EventType.MouseDown && e.button == 0;
        bool leftDrag = paintWhileDrag && e.type == EventType.MouseDrag && e.button == 0;
        bool right = e.type == EventType.MouseDown && e.button == 1;

        if (!e.alt && (left || leftDrag || right))
        {
            e.Use();
            char sym = right ? '.' : currentSymbol;
            Paint(grid, sym);
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        }
    }

    bool TryGetMouseGrid(out Vector2Int grid)
    {
        grid = default;
        var ev = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(ev.mousePosition);
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (!plane.Raycast(ray, out float enter)) return false;
        Vector3 hit = ray.GetPoint(enter);
        if (!board) return false;
        grid = board.WorldToGrid(hit);
        return board.InBounds(grid);
    }

    void Paint(Vector2Int p, char symbol)
    {
        if (!board.InBounds(p)) return;

        Undo.RecordObject(board, "Paint Level");
        var rows = board.level.ToArray();
        char[] row = rows[p.y].ToCharArray();

        if (symbol == 'P')
        {
            for (int y = 0; y < rows.Length; y++)
            {
                int idx = rows[y].IndexOf('P');
                if (idx >= 0)
                {
                    var r = rows[y].ToCharArray();
                    r[idx] = '.';
                    rows[y] = new string(r);
                }
            }
        }

        row[p.x] = symbol;
        rows[p.y] = new string(row);
        board.level = rows;

        Rebuild();
    }

    void Rebuild()
    {
        board.Build();
        EditorUtility.SetDirty(board);
        var scene = SceneManager.GetActiveScene();
        if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        SceneView.RepaintAll();
        Repaint();
    }

    void ResizeLevel(int newW, int newH, char pad)
    {
        int w = board.Width;
        int h = board.Height;
        var newRows = new string[newH];
        for (int y = 0; y < newH; y++)
        {
            if (y < h)
            {
                var src = board.level[y];
                if (src.Length < newW) src = src + new string(pad, newW - src.Length);
                else if (src.Length > newW) src = src.Substring(0, newW);
                newRows[y] = src;
            }
            else newRows[y] = new string(pad, newW);
        }
        Undo.RecordObject(board, "Resize Level");
        board.level = newRows;
    }

    void DrawCellWire(Vector3 world, float size)
    {
        var c = world + new Vector3(0.5f, 0f, 0.5f);
        Vector3 a = c + new Vector3(-size / 2, 0, -size / 2);
        Vector3 b = c + new Vector3(size / 2, 0, -size / 2);
        Vector3 d = c + new Vector3(size / 2, 0, size / 2);
        Vector3 e = c + new Vector3(-size / 2, 0, size / 2);
        Handles.DrawLine(a, b); Handles.DrawLine(b, d); Handles.DrawLine(d, e); Handles.DrawLine(e, a);
    }
}
#endif
