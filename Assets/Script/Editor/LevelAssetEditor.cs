#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

[CustomEditor(typeof(LevelAsset), true)] // ← 継承（AsciiLevelAsset）も拾う
public class LevelAssetEditor : Editor
{
    private BoardManager board;

    public override void OnInspectorGUI()
    {
        var asset = (LevelAsset)target;

        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("id"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("rows"), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Board Link", EditorStyles.boldLabel);

        board = (BoardManager)EditorGUILayout.ObjectField("BoardManager", board, typeof(BoardManager), true);
        if (!board && Selection.activeGameObject)
            board = Selection.activeGameObject.GetComponentInParent<BoardManager>();

        using (new EditorGUI.DisabledScope(board == null))
        {
            if (GUILayout.Button("⬅ Pull from Board (copy Board → rows)"))
            {
                Undo.RecordObject(asset, "Pull from Board");
                asset.rows = (string[])board.level.Clone();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }

            if (GUILayout.Button("➡ Push to Board (apply rows → Board)"))
            {
                Undo.RecordObject(board, "Push to Board");
                board.SetLevel(asset.rows);
                EditorUtility.SetDirty(board);
            }
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Board から rows を吸い上げ/流し込みできます。", MessageType.Info);
    }
}
#endif
