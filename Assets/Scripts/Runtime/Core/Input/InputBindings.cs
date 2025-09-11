using System;
using UnityEngine;

/// <summary>
/// 入力バインドの管理（現状はリセットキーのみ）。
/// リバインド/キャプチャ状態/表示文字列の取得を提供する。
/// </summary>
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

    /// <summary>現在のリセットキー。</summary>
    public static KeyCode ResetKey => _resetKey;

    /// <summary>リバインドキャプチャ中か。</summary>
    public static bool IsCapturing => _capturing;

    /// <summary>
    /// リセットキーを設定し、PlayerPrefs へ保存する。
    /// </summary>
    public static void SetResetKey(KeyCode key)
    {
        _resetKey = key;
        PlayerPrefs.SetInt(Pref_ResetKey, (int)key);
        PlayerPrefs.Save();
        Debug.Log($"[KeyBind] Reset = {key}");
    }

    /// <summary>
    /// リバインドのキャプチャを開始する（この間は入力を無視）。
    /// </summary>
    public static void BeginCapture()
    {
        _capturing = true;
    }

    /// <summary>
    /// リバインドのキャプチャを終了する。指定フレーム数だけ入力を抑止する。
    /// </summary>
    public static void EndCapture(int suppressFrames = 2)
    {
        _capturing = false;
        _suppressFrames = Mathf.Max(_suppressFrames, suppressFrames);
    }

    /// <summary>
    /// 今フレーム、リセット入力が押下されたか（キャプチャ中/抑止中は false）。
    /// </summary>
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

    /// <summary>キー表示用の文字列を返す。</summary>
    public static string GetKeyDisplay(KeyCode key) => key.ToString();

    /// <summary>
    /// 任意のキーボードキーの押下を検出する（Mouse/Joystick を除外）。
    /// </summary>
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
