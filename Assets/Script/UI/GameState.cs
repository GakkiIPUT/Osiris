using UnityEngine;

public class GameState : MonoBehaviour
{
    public static GameState I { get; private set; }

    [Header("Catalog")]
    public GameCatalog catalog;    // ‰º‚Å’è‹`‚·‚é ScriptableObject

    [Header("Selection")]
    public int worldIndex = 0;     // ‰½–Ê
    public int stageIndex = 0;     // 1-1,1-2...

    void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this; DontDestroyOnLoad(gameObject);
    }
}
