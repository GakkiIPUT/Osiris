using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// このパネル内にUIフォーカスを保持するための補助コンポーネント。
/// 有効時の初期フォーカス、および外部へのフォーカス流出を抑止する。
/// </summary>
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

    private CanvasGroup _cg;

    /// <summary>CanvasGroup をキャッシュする。</summary>
    private void Awake()
    {
        _cg = GetComponent<CanvasGroup>();
    }

    /// <summary>有効化時、必要なら初期フォーカスを設定する。</summary>
    private void OnEnable()
    {
        if (focusOnEnable)
            StartCoroutine(CoFocusFirstNextFrame());
    }

    /// <summary>次フレームで初期フォーカスを設定する。</summary>
    private IEnumerator CoFocusFirstNextFrame()
    {
        yield return null;
        FocusFirst();
    }

    /// <summary>毎フレーム、フォーカスの流出を監視し必要なら引き戻す。</summary>
    private void Update()
    {
        if (!trapFocus) return;
        if (!isActiveAndEnabled) return;
        if (!gameObject.activeInHierarchy) return;
        if (!IsInteractableThisScope()) return;
        if (EventSystem.current == null) return;

        var cur = EventSystem.current.currentSelectedGameObject;
        if (cur == null || !IsMyDescendant(cur.transform))
        {
            var target = GetFirstSelectableInChildren();
            if (target != null)
                EventSystem.current.SetSelectedGameObject(target.gameObject);
        }
    }

    /// <summary>firstSelected があればそれを、なければ最初の Selectable にフォーカスする。</summary>
    public void FocusFirst()
    {
        if (EventSystem.current == null) return;

        var target = firstSelected ? firstSelected : GetFirstSelectableInChildren();
        if (target != null)
            EventSystem.current.SetSelectedGameObject(target.gameObject);
    }

    private bool IsMyDescendant(Transform t)
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

    private Selectable GetFirstSelectableInChildren()
    {
        var selectables = GetComponentsInChildren<Selectable>(true);
        foreach (var s in selectables)
        {
            if (s != null && s.gameObject.activeInHierarchy && s.interactable)
                return s;
        }
        return null;
    }

    private bool IsInteractableThisScope()
    {
        if (_cg != null)
            return _cg.interactable;
        return true;
    }
}
