using System;
using UnityEngine;

public static class InputBindings
{
    private const string Pref_ResetKey = "key_reset";

    private static KeyCode _resetKey = KeyCode.P; // 既定は P
    private static bool _capturing = false;       // リバインド中フラグ
    private static int _suppressFrames = 0;       // 終了直後の誤爆抑止

    static InputBindings()
    {
        if (PlayerPrefs.HasKey(Pref_ResetKey))
        {
            int raw = PlayerPrefs.GetInt(Pref_ResetKey, (int)KeyCode.P);
            _resetKey = (KeyCode)raw;
        }
    }

    public static KeyCode ResetKey => _resetKey;
    public static bool IsCapturing => _capturing;

    public static void SetResetKey(KeyCode key)
    {
        _resetKey = key;
        PlayerPrefs.SetInt(Pref_ResetKey, (int)key);
        PlayerPrefs.Save();
        Debug.Log($"[KeyBind] Reset = {key}");
    }

    // リバインド開始（この間はリセット入力を無視）
    public static void BeginCapture()
    {
        _capturing = true;
    }

    // リバインド終了（数フレームだけ抑止して誤爆防止）
    public static void EndCapture(int suppressFrames = 2)
    {
        _capturing = false;
        _suppressFrames = Mathf.Max(_suppressFrames, suppressFrames);
    }

    public static bool IsResetPressed()
    {
        if (_capturing) return false;
        if (_suppressFrames > 0)
        {
            _suppressFrames--;
            return false;
        }
        return Input.GetKeyDown(_resetKey);
    }

    public static string GetKeyDisplay(KeyCode key) => key.ToString();

    // 任意のキーボードキーを1つ取得（マウス/ジョイスティックは除外）
    public static bool TryGetAnyKeyboardKeyDown(out KeyCode key)
    {
        key = KeyCode.None;
        Array values = Enum.GetValues(typeof(KeyCode));
        foreach (KeyCode kc in values)
        {
            if (kc == KeyCode.None) continue;
            string n = kc.ToString();
            if (n.StartsWith("Mouse") || n.StartsWith("Joystick")) continue;

            if (Input.GetKeyDown(kc))
            {
                key = kc;
                return true;
            }
        }
        return false;
    }
}