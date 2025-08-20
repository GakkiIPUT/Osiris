using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Catalog", fileName = "GameCatalog")]
public class GameCatalog : ScriptableObject
{
    [System.Serializable]
    public class World
    {
        public string id = "1面";
        public StageSet stageSet; // 以前作った StageSet（1-1,1-2…の束）
    }
    public List<World> worlds = new();
}
