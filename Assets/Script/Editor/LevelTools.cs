#if UNITY_EDITOR
using System.IO;

using UnityEditor;

using UnityEngine;

public static class LevelTools
{
    private static BoardManager GetBoard()
    {
        var sel = Selection.activeGameObject;
        var b = sel ? sel.GetComponentInParent<BoardManager>() : null;
        if (!b) b = Object.FindFirstObjectByType<BoardManager>();
        return b;
    }

    [MenuItem("Tools/Level IO/Save Board To New Level Asset...")]
    public static void SaveBoardToNewAsset()
    {
        var board = GetBoard();
        if (!board) { EditorUtility.DisplayDialog("Save Board", "BoardManager が見つかりません。", "OK"); return; }

        string path = EditorUtility.SaveFilePanelInProject("Save Level", "Level_1-1", "asset", "保存先を選んでください");
        if (string.IsNullOrEmpty(path)) return;

        var asset = ScriptableObject.CreateInstance<LevelAsset>();
        asset.id = Path.GetFileNameWithoutExtension(path);
        asset.rows = (string[])board.level.Clone();

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(asset);
        Debug.Log($"Saved Board → {path}");
    }

    [MenuItem("Tools/Level IO/Load Selected Level → Board")]
    public static void LoadSelectedAssetToBoard()
    {
        var asset = Selection.activeObject as LevelAsset; // AsciiLevelAsset も拾える
        var board = GetBoard();
        if (!asset || !board) { EditorUtility.DisplayDialog("Load Level", "LevelAsset と BoardManager を選択/配置してください。", "OK"); return; }

        Undo.RecordObject(board, "Load Level Asset");
        board.SetLevel(asset.rows);
        EditorUtility.SetDirty(board);
        Debug.Log($"Loaded '{asset.name}' to Board");
    }

    [MenuItem("Tools/Level IO/Update Selected Level ← Board")]
    public static void UpdateSelectedAssetFromBoard()
    {
        var asset = Selection.activeObject as LevelAsset;
        var board = GetBoard();
        if (!asset || !board) { EditorUtility.DisplayDialog("Update Level", "LevelAsset と BoardManager を選択/配置してください。", "OK"); return; }

        Undo.RecordObject(asset, "Update Level Asset");
        asset.rows = (string[])board.level.Clone();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Debug.Log($"Updated Level Asset '{asset.name}' from Board");
    }

    [MenuItem("Tools/Level IO/Export Board To .txt...")]
    public static void ExportBoardToTxt()
    {
        var board = GetBoard();
        if (!board) { EditorUtility.DisplayDialog("Export .txt", "BoardManager が見つかりません。", "OK"); return; }

        string path = EditorUtility.SaveFilePanel("Export Board to .txt", "", "level.txt", "txt");
        if (string.IsNullOrEmpty(path)) return;

        File.WriteAllLines(path, board.level);
        EditorUtility.RevealInFinder(path);
        Debug.Log($"Exported Board → {path}");
    }

    [MenuItem("Tools/Level IO/Import .txt To Board...")]
    public static void ImportTxtToBoard()
    {
        var board = GetBoard();
        if (!board) { EditorUtility.DisplayDialog("Import .txt", "BoardManager が見つかりません。", "OK"); return; }

        string path = EditorUtility.OpenFilePanel("Import .txt to Board", "", "txt");
        if (string.IsNullOrEmpty(path)) return;

        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) { Debug.LogWarning("空のファイルです"); return; }

        int w = 0; foreach (var s in lines) w = Mathf.Max(w, s.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length < w) lines[i] = lines[i] + new string('.', w - lines[i].Length);
            else if (lines[i].Length > w) lines[i] = lines[i].Substring(0, w);
        }

        Undo.RecordObject(board, "Import .txt");
        board.SetLevel(lines);
        EditorUtility.SetDirty(board);
        Debug.Log($"Imported .txt → Board ({w}x{lines.Length})");
    }
}
#endif
