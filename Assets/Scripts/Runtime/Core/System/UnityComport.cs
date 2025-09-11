using UnityEngine;

/// <summary>
/// Unityバージョン差異を吸収するための軽量ユーティリティ。
/// FindFirst/FindAny でシーン内の最初のインスタンスを取得する。
/// </summary>
public static class UnityCompat
{
    /// <summary>
    /// シーン内で最初に見つかった1件を返す（推奨）。
    /// </summary>
    public static T FindFirst<T>() where T : Object
    {
#if UNITY_2022_2_OR_NEWER
        return Object.FindFirstObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }

    /// <summary>
    /// どれでも良い最初の1件（高速）を返す。
    /// </summary>
    public static T FindAny<T>() where T : Object
    {
#if UNITY_2022_2_OR_NEWER
        return Object.FindAnyObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
