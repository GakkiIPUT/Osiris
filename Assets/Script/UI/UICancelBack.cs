using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 表示中に "Cancel" 相当（ESC / Gamepad East）を押すと target.onClick を呼ぶ。
// Canvasやパネルのルートに付けて、戻るボタンを割り当てる。
public class UICancelBack : MonoBehaviour
{
    [Tooltip("戻り先のボタン（未設定なら親階層で最初のButtonを自動検出）")]
    public Button target;

    [Tooltip("このGameObjectがactiveな時だけ受け付ける")]
    public bool onlyWhenActive = true;

    [Tooltip("UIにフォーカスが無くても受け付ける")]
    public bool acceptWithoutFocus = true;

    private void OnEnable()
    {
        if (!target) target = GetComponentInChildren<Button>(true);
    }

    private void Update()
    {
        if (onlyWhenActive && !gameObject.activeInHierarchy) return;

        bool cancel = false;

        // Keyboard
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