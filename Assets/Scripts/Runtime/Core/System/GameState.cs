using UnityEngine;

/// <summary>
/// ゲーム全体の永続状態（カタログ/選択中のワールド・ステージ）を保持するシングルトン。
/// </summary>
public class GameState : MonoBehaviour
{
    public static GameState I { get; private set; }

    [Header("Catalog")]
    public GameCatalog catalog;    // 下で定義する ScriptableObject

    [Header("Selection")]
    public int worldIndex = 0;     // 何面

    public int stageIndex = 0;     // 1-1,1-2...

    /// <summary>シングルトン初期化（多重配置を抑止、DontDestroyOnLoad）。</summary>
    private void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this; DontDestroyOnLoad(gameObject);
    }
}
