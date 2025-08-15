using UnityEngine;

public static class UnityCompat
{
    // 最初に見つかった1件を取得（推奨）
    public static T FindFirst<T>() where T : Object
    {
#if UNITY_2022_2_OR_NEWER
        return Object.FindFirstObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }

    // どれでも良い最初の1件（高速）
    public static T FindAny<T>() where T : Object
    {
#if UNITY_2022_2_OR_NEWER
        return Object.FindAnyObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
