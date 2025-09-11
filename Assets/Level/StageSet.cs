using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Stage Set")]
public class StageSet : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        /// <summary>ステージID（表示用）例: "1-1"</summary>
        public string id = "1-1";   // 表示用・任意

        /// <summary>ASCIIマップのTextAsset</summary>
        public TextAsset mapTxt;    // ← ここに .txt を割り当てる

        /// <summary>想定回転数（レガシー方式）</summary>
        public int parRot = 6;      // 想定回転数（UI/スコア用）

        [Tooltip("想定AP（パー）。このAPまでは減点しない")]
        [Min(0)] public int parAP = 6;

        [Header("Collection (Treasure)")]
        [Tooltip("このステージで宝箱を取ってクリアしたら授与するコレクション名")]
        public string collectName = "不思議なコレクション";

        [Tooltip("授与コレクションの見た目（UI表示用Sprite）")]
        public Sprite collectSprite;

        [Header("Tutorial (Optional)")]
        [Tooltip("ステージ開始時にチュートリアルを表示する")]
        public bool showTutorialOnStart = false;

        [Tooltip("チュートリアルに表示する画像")]
        public Sprite tutorialSprite;

        [Tooltip("初回のみ表示する（idで既読管理）")]
        public bool tutorialOnlyOnce = true;
    }

    /// <summary>このセットに含まれるステージ配列</summary>
    public List<Entry> stages = new List<Entry>();
}
