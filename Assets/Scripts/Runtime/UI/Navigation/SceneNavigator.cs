using UnityEngine.SceneManagement;

/// <summary>
/// シーン遷移のユーティリティ。Main/Stage/Game のロードAPIを提供する。
/// </summary>
public static class SceneNavigator
{
    public const string SCENE_MAIN = "MainMenu";
    public const string SCENE_STAGE = "StageSelect";
    public const string SCENE_GAME = "Game";

    /// <summary>MainMenu シーンへ遷移する。</summary>
    public static void GoMain() => SceneManager.LoadScene(SCENE_MAIN);

    /// <summary>StageSelect シーンへ遷移する。</summary>
    public static void GoStage() => SceneManager.LoadScene(SCENE_STAGE);

    /// <summary>Game シーンへ遷移する。</summary>
    public static void GoGame() => SceneManager.LoadScene(SCENE_GAME);
}
