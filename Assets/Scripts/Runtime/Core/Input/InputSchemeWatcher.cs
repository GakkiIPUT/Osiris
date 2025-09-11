using System;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM

using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

#endif

/// <summary>
/// 入力デバイスの最終操作スキーム（Keyboard/Mouse, Gamepad, Touch, Other）を常駐監視し、変更イベントを発火する。
/// シーンを跨いで維持される。
/// </summary>
public enum InputSchemeType
{
    Unknown = 0,
    KeyboardMouse = 1,
    Gamepad = 2,
    Touch = 3,
    Other = 9,
}

// シーンを跨いで"最後に操作したデバイス種別"をトラッキングする常駐ウォッチャ
[DefaultExecutionOrder(-1000)]
public class InputSchemeWatcher : MonoBehaviour
{
    public static InputSchemeWatcher I { get; private set; }

    /// <summary>現在のスキーム（最後に操作のあったデバイス）</summary>
    public static InputSchemeType CurrentScheme { get; private set; } = InputSchemeType.KeyboardMouse;

    /// <summary>スキーム変更時に発火されるイベント。</summary>
    public static event Action<InputSchemeType> OnSchemeChanged;

    [Header("Debug")]
    public bool logChanges = false;

    /// <summary>シングルトン初期化（多重を抑止）。</summary>
    private void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>有効化時、推定スキーム設定とInputSystemイベント購読を行う。</summary>
    private void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        // 最初の推定
        if (Gamepad.current != null) SetScheme(InputSchemeType.Gamepad, true);
        else SetScheme(InputSchemeType.KeyboardMouse, true);

        // 入力イベントでも検出（より確実）
        InputSystem.onEvent += OnInputEvent;
#endif
    }

    /// <summary>無効化時、InputSystemイベント購読を解除する。</summary>
    private void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        InputSystem.onEvent -= OnInputEvent;
#endif
    }

#if ENABLE_INPUT_SYSTEM

    /// <summary>入力イベントからデバイス種別を判定し、必要に応じてスキームを更新する。</summary>
    private void OnInputEvent(InputEventPtr evt, InputDevice device)
    {
        if (!evt.IsA<StateEvent>() && !evt.IsA<DeltaStateEvent>()) return;
        if (device == null) return;

        if (device is Gamepad)
        {
            SetScheme(InputSchemeType.Gamepad);
        }
        else if (device is Keyboard || device is Mouse)
        {
            SetScheme(InputSchemeType.KeyboardMouse);
        }
        else if (device is Touchscreen)
        {
            SetScheme(InputSchemeType.Touch);
        }
        else
        {
            SetScheme(InputSchemeType.Other);
        }
    }

#endif

    /// <summary>フレーム監視による保険（onEventで拾えないケース向け）。</summary>
    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (AnyGamepadActuated())
        {
            SetScheme(InputSchemeType.Gamepad);
        }
        else if (AnyKeyboardMouseActuated())
        {
            SetScheme(InputSchemeType.KeyboardMouse);
        }
        else if (AnyTouchActuated())
        {
            SetScheme(InputSchemeType.Touch);
        }
#endif
    }

    /// <summary>シーン上に無ければ自動生成する。</summary>
    public static void EnsureExists()
    {
        if (I != null) return;
        var go = new GameObject(nameof(InputSchemeWatcher));
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<InputSchemeWatcher>();
    }

    private static void SetScheme(InputSchemeType next, bool force = false)
    {
        if (!force && next == CurrentScheme) return;
        CurrentScheme = next;
        I?.LogChange(next);
        OnSchemeChanged?.Invoke(next);
    }

    private void LogChange(InputSchemeType s)
    {
        if (!logChanges) return;
        Debug.Log($"[InputScheme] => {s}");
    }

#if ENABLE_INPUT_SYSTEM

    private static bool AnyGamepadActuated(float thr = 0.15f)
    {
        foreach (var gp in Gamepad.all)
        {
            if (gp == null) continue;
            if (gp.startButton.wasPressedThisFrame) return true;
            if (gp.buttonSouth.wasPressedThisFrame) return true;
            if (gp.buttonEast.wasPressedThisFrame) return true;
            if (gp.leftShoulder.wasPressedThisFrame || gp.rightShoulder.wasPressedThisFrame) return true;
            if (gp.leftTrigger.IsActuated(thr) || gp.rightTrigger.IsActuated(thr)) return true;
            if (gp.dpad.IsActuated(thr)) return true;
            if (gp.leftStick.IsActuated(thr) || gp.rightStick.IsActuated(thr)) return true;
        }
        return false;
    }

    private static bool AnyKeyboardMouseActuated()
    {
        var kb = Keyboard.current;
        var ms = Mouse.current;
        if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
        if (ms != null)
        {
            if (ms.leftButton.wasPressedThisFrame || ms.rightButton.wasPressedThisFrame || ms.middleButton.wasPressedThisFrame) return true;
            if (ms.scroll.ReadValue().sqrMagnitude > 0f) return true;
            if (ms.delta.ReadValue().sqrMagnitude > 0f) return true;
        }
        return false;
    }

    private static bool AnyTouchActuated()
    {
        var ts = Touchscreen.current;
        if (ts == null) return false;
        return ts.primaryTouch.press.wasPressedThisFrame;
    }

#endif
}
