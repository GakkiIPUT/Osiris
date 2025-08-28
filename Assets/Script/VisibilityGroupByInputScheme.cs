using System.Collections.Generic;
using UnityEngine;

// 複数の対象をまとめて可視/不可視にする版
// デフォルト: 「Pad操作中は非表示、キーボード＆マウス中は表示」
[ExecuteAlways]
public class VisibilityGroupByInputScheme : MonoBehaviour
{
    [Header("Targets")]
    public List<GameObject> targets = new List<GameObject>();

    [Tooltip("targets が空なら、このオブジェクトの直下の子を全て制御します")]
    public bool useChildrenIfListEmpty = false;

    [Header("Visibility Rules")]
    public bool visibleOnKeyboardMouse = true;
    public bool visibleOnGamepad = false;
    public bool visibleOnTouch = true;
    public bool visibleOnOther = true;

    [Header("Apply Method")]
    public bool deactivateGameObject = true;          // SetActiveで切替（推奨）
    public bool useCanvasGroupWhenAvailable = true;   // CanvasGroupがあればAlpha/操作も切替

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
        bool visible = IsVisibleFor(InputSchemeWatcher.CurrentScheme);

        if (targets != null && targets.Count > 0)
        {
            for (int i = 0; i < targets.Count; i++)
                ApplyToTarget(targets[i], visible);
        }
        else if (useChildrenIfListEmpty)
        {
            int n = transform.childCount;
            for (int i = 0; i < n; i++)
                ApplyToTarget(transform.GetChild(i)?.gameObject, visible);
        }
    }

    void ApplyToTarget(GameObject go, bool visible)
    {
        if (go == null) return;

        if (deactivateGameObject)
        {
            if (go.activeSelf != visible)
                go.SetActive(visible);
            return;
        }

        // 非アクティブ化せずにCanvasGroupで見た目/操作を切替
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