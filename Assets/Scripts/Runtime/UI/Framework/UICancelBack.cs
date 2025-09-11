using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM

using UnityEngine.InputSystem;

#endif

/// <summary>
/// 表示中に「Cancel」（Esc/Pad East）が押されたら target.onClick を呼ぶ。
/// パネルのルートに付けて戻るボタンを割り当てる用途。
/// </summary>
public class UICancelBack : MonoBehaviour
{
    [Tooltip("戻り先のボタン（未設定なら親階層で最初のButtonを自動検出）")]
    public Button target;

    [Tooltip("このGameObjectがactiveな時だけ受け付ける")]
    public bool onlyWhenActive = true;

    [Tooltip("UIにフォーカスが無くても受け付ける")]
    public bool acceptWithoutFocus = true;

    /// <summary>有効化時、未設定なら target を自動検出する。</summary>
    private void OnEnable()
    {
        if (!target) target = GetComponentInChildren<Button>(true);
    }

    /// <summary>Cancel 入力を監視し、条件を満たせば target を発火する。</summary>
    private void Update()
    {
        if (onlyWhenActive && !gameObject.activeInHierarchy) return;

        bool cancel = false;

        if (Input.GetKeyDown(KeyCode.Escape)) cancel = true;

#if ENABLE_INPUT_SYSTEM
        var gp = Gamepad.current;
        if (gp != null && gp.buttonEast.wasPressedThisFrame) cancel = true;
#endif

        if (!cancel) return;

        if (!acceptWithoutFocus)
        {
            if (EventSystem.current == null) return;
            if (EventSystem.current.currentSelectedGameObject == null) return;
        }

        if (target && target.interactable && target.gameObject.activeInHierarchy)
        {
            target.onClick?.Invoke();
        }
    }
}
