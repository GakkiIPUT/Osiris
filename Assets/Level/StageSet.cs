using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Stage Set", fileName = "StageSet")]
public class StageSet : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string displayName = "1-1";
        [Tooltip("ASCII マップ（テキストアセット）")]
        public TextAsset asciiLevel;
    }

    public List<Entry> stages = new();
}
