using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Stage Set")]
public class StageSet : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string id = "1-1";   // 表示用・任意
        public TextAsset mapTxt;    // ← ここに .txt を割り当てる
        public int parRot = 6;      // 想定回転数（UI/スコア用）
    }

    public List<Entry> stages = new List<Entry>();
}
