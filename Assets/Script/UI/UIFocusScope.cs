using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UIFocusScope : MonoBehaviour
{
    [Header("Focus")]
    [Tooltip("このパネルを開いたときに最初に選択するUI")]
    public Selectable firstSelected;

    [Tooltip("有効時、選択がこのパネル外へ出たら直ちに戻す")]
    public bool trapFocus = true;

    [Tooltip("OnEnable時に firstSelected を自動選択する")]
    public bool focusOnEnable = true;

    CanvasGroup _cg;

    void Awake()
    {
        _cg = GetComponent<CanvasGroup>();
    }

    void OnEnable()
    {
        if (focusOnEnable)
            StartCoroutine(CoFocusFirstNextFrame());
    }

    IEnumerator CoFocusFirstNextFrame()
    {
        // Canvasのレイアウト完了後に選択
        yield return null;
        FocusFirst();
    }

    void Update()
    {
        if (!trapFocus) return;
        if (!isActiveAndEnabled) return;
        if (!gameObject.activeInHierarchy) return;
        if (!IsInteractableThisScope()) return;
        if (EventSystem.current == null) return;

        var cur = EventSystem.current.currentSelectedGameObject;
        // 何も選択されていない or このパネルの外を選んでいるなら、内部の最初の要素に戻す
        if (cur == null || !IsMyDescendant(cur.transform))
        {
            var target = GetFirstSelectableInChildren();
            if (target != null)
                EventSystem.current.SetSelectedGameObject(target.gameObject);
        }
    }

    public void FocusFirst()
    {
        if (EventSystem.current == null) return;

        var target = firstSelected ? firstSelected : GetFirstSelectableInChildren();
        if (target != null)
            EventSystem.current.SetSelectedGameObject(target.gameObject);
    }

    bool IsMyDescendant(Transform t)
    {
        if (t == null) return false;
        var root = transform;
        var cur = t;
        while (cur != null)
        {
            if (cur == root) return true;
            cur = cur.parent;
        }
        return false;
    }

    Selectable GetFirstSelectableInChildren()
    {
        // アクティブかつ interactable な最初の Selectable を探す
        var selectables = GetComponentsInChildren<Selectable>(true);
        foreach (var s in selectables)
        {
            if (s != null && s.gameObject.activeInHierarchy && s.interactable)
                return s;
        }
        return null;
    }

    bool IsInteractableThisScope()
    {
        // CanvasGroup がある場合は、その interactable を尊重
        if (_cg != null)
            return _cg.interactable;
        return true;
    }
}