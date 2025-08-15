using UnityEngine;

[CreateAssetMenu(menuName = "Game Level", fileName = "Level_1-1")]
public class LevelAsset : ScriptableObject
{
    [Tooltip("—á: 1-1, 1-2 ‚È‚Ç•\Ž¦—pID")]
    public string id = "1-1";

    [TextArea(6, 40)]
    public string[] rows;
}