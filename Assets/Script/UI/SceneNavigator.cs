using UnityEngine.SceneManagement;

public static class SceneNavigator
{
    public const string SCENE_MAIN = "MainMenu";
    public const string SCENE_STAGE = "StageSelect";
    public const string SCENE_GAME = "Game";

    public static void GoMain() => SceneManager.LoadScene(SCENE_MAIN);
    public static void GoStage() => SceneManager.LoadScene(SCENE_STAGE);
    public static void GoGame() => SceneManager.LoadScene(SCENE_GAME);
}
