using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

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

    // 現在スキーム（最後に操作があったデバイス）
    public static InputSchemeType CurrentScheme { get; private set; } = InputSchemeType.KeyboardMouse;

    public static event Action<InputSchemeType> OnSchemeChanged;

    [Header("Debug")]
    public bool logChanges = false;

    void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        // 最初の推定
        if (Gamepad.current != null) SetScheme(InputSchemeType.Gamepad, true);
        else SetScheme(InputSchemeType.KeyboardMouse, true);

        // 入力イベントでも検出（より確実）
        InputSystem.onEvent += OnInputEvent;
#endif
    }

    void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        InputSystem.onEvent -= OnInputEvent;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    void OnInputEvent(InputEventPtr evt, InputDevice device)
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

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        // 念のためのフレーム監視（onEventが拾えないケースの保険）
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

    public static void EnsureExists()
    {
        if (I != null) return;
        var go = new GameObject(nameof(InputSchemeWatcher));
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<InputSchemeWatcher>();
    }

    static void SetScheme(InputSchemeType next, bool force = false)
    {
        if (!force && next == CurrentScheme) return;
        CurrentScheme = next;
        I?.LogChange(next);
        OnSchemeChanged?.Invoke(next);
    }

    void LogChange(InputSchemeType s)
    {
        if (!logChanges) return;
        Debug.Log($"[InputScheme] => {s}");
    }

#if ENABLE_INPUT_SYSTEM
    static bool AnyGamepadActuated(float thr = 0.15f)
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

    static bool AnyKeyboardMouseActuated()
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

    static bool AnyTouchActuated()
    {
        var ts = Touchscreen.current;
        if (ts == null) return false;
        return ts.primaryTouch.press.wasPressedThisFrame;
    }
#endif
}