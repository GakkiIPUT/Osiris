using UnityEngine;

/// <summary>
/// 入力スキーム（Keyboard/Mouse, Gamepad, Touch など）に応じて対象の可視状態を切り替えるコンポーネント。
/// </summary>
[ExecuteAlways]
public class VisibilityByInputScheme : MonoBehaviour
{
    [Header("Visibility Rules")]
    public bool visibleOnKeyboardMouse = true;

    public bool visibleOnGamepad = false;
    public bool visibleOnTouch = true;
    public bool visibleOnOther = true;

    [Header("Target")]
    [Tooltip("null なら this.gameObject を制御します")]
    public GameObject target;

    [Header("Apply Method")]
    public bool deactivateGameObject = true;     // SetActiveで切替（推奨）

    public bool useCanvasGroupWhenAvailable = true; // CanvasGroupがあればAlpha/操作も切替

    [Header("Debug")]
    public bool previewInEditor = true; // エディタ上でも見た目を反映

    /// <summary>有効化時、監視を有効化し現在スキームを反映する。</summary>
    private void OnEnable()
    {
        InputSchemeWatcher.EnsureExists();
        ApplyByCurrentScheme();
        InputSchemeWatcher.OnSchemeChanged += OnSchemeChanged;
    }

    /// <summary>無効化時、監視を解除する。</summary>
    private void OnDisable()
    {
        InputSchemeWatcher.OnSchemeChanged -= OnSchemeChanged;
    }

    private void OnSchemeChanged(InputSchemeType s) => ApplyByCurrentScheme();

#if UNITY_EDITOR

    /// <summary>エディタ上（非再生）でもプレビューを反映する。</summary>
    private void Update()
    {
        if (!Application.isPlaying && previewInEditor)
            ApplyByCurrentScheme();
    }

#endif

    /// <summary>現在のスキームに基づいて可視状態を反映する。</summary>
    private void ApplyByCurrentScheme()
    {
        var go = target ? target : gameObject;
        bool visible = IsVisibleFor(InputSchemeWatcher.CurrentScheme);

        if (deactivateGameObject)
        {
            if (go.activeSelf != visible)
                go.SetActive(visible);
            return;
        }

        // 非アクティブ化はせず、CanvasGroupで視覚/操作を切り替える
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null && useCanvasGroupWhenAvailable)
            cg = go.AddComponent<CanvasGroup>();

        if (cg != null)
        {
            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
        }
        else
        {
            // 最低限
            go.SetActive(visible);
        }
    }

    /// <summary>与えられたスキームで可視とするか判定する。</summary>
    private bool IsVisibleFor(InputSchemeType s)
    {
        switch (s)
        {
            case InputSchemeType.Gamepad: return visibleOnGamepad;
            case InputSchemeType.KeyboardMouse: return visibleOnKeyboardMouse;
            case InputSchemeType.Touch: return visibleOnTouch;
            case InputSchemeType.Other: return visibleOnOther;
            default: return visibleOnKeyboardMouse;
        }
    }
}
