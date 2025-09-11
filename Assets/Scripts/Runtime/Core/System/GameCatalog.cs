using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Catalog", fileName = "GameCatalog")]
public class GameCatalog : ScriptableObject
{
    [System.Serializable]
    public class World
    {
        /// <summary>ワールドID（表示用）</summary>
        public string id = "1面";

        /// <summary>このワールドに属するステージ集合</summary>
        public StageSet stageSet; // 以前作った StageSet（1-1,1-2…の束）
    }

    /// <summary>カタログ内のワールド一覧</summary>
    public List<World> worlds = new();
}
