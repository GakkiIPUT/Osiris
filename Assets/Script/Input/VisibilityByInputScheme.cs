using UnityEngine;

// 任意の親に付けて、子ごと丸ごと見せたり隠したりする。
// デフォルト: 「Pad操作中は非表示、キーボード＆マウス中は表示」
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

    void OnEnable()
    {
        InputSchemeWatcher.EnsureExists();
        ApplyByCurrentScheme();
        InputSchemeWatcher.OnSchemeChanged += OnSchemeChanged;
    }

    void OnDisable()
    {
        InputSchemeWatcher.OnSchemeChanged -= OnSchemeChanged;
    }

    void OnSchemeChanged(InputSchemeType s) => ApplyByCurrentScheme();

#if UNITY_EDITOR
    void Update()
    {
        if (!Application.isPlaying && previewInEditor)
            ApplyByCurrentScheme();
    }
#endif

    void ApplyByCurrentScheme()
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

    bool IsVisibleFor(InputSchemeType s)
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