#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// LevelAsset の Inspector 拡張。Board と rows の相互同期を提供する。
/// </summary>
[CustomEditor(typeof(LevelAsset), true)]
public class LevelAssetEditor : Editor
{
    private BoardManager board;

    /// <summary>
    /// Inspector を描画する。id と rows を編集でき、BoardManager と同期する Pull/Push 操作を提供する。
    /// </summary>
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
