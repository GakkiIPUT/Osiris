using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class ControlsHelpUI : MonoBehaviour
{
    [Header("Tab Buttons")] 
    public Button tabKeyboardButton;
    public Button tabGamepadButton;

    [Header("Tab Roots")]
    public GameObject keyboardRoot;
    public GameObject gamepadRoot;

    [Header("Texts")]
    public TMP_Text keyboardText;
    public TMP_Text gamepadText;

    [Header("Close")]
    public Button closeButton; // 追加: クローズボタン

    void Awake()
    {
        if (tabKeyboardButton) tabKeyboardButton.onClick.AddListener(SelectKeyboard);
        if (tabGamepadButton) tabGamepadButton.onClick.AddListener(SelectGamepad);
        if (closeButton) closeButton.onClick.AddListener(Close); // 追加: クリックで閉じる
    }

    void OnEnable()
    {
        // デフォルトはキーボードタブ
        RefreshTexts();
        SelectKeyboard();

        // ボタン選択式: どちらのボタンも常に選択可能に
        SetTabInteractable(keyboard: true, gamepad: true);
    }

    // 親(ESCメニュー)ごと閉じられた場合でも、次回は説明を自動で開かないように自身を必ずOFFにする
    void OnDisable()
    {
        if (gameObject.activeSelf)
        {
            // 既に自分でClose()している場合はactiveSelf=falseだが、
            // 親が閉じられて非表示になったケースではactiveSelf=trueのままなのでOFFに倒す
            gameObject.SetActive(false);
        }
    }

    void Update()
    {
        // Escキーでも説明を閉じてESCメニューへ戻す
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
        }
    }

    public void RefreshTexts()
    {
        string resetKey = InputBindings.GetKeyDisplay(InputBindings.ResetKey);

        if (keyboardText)
        {
            keyboardText.text =
                "キーボード + マウス\n" +
                "・移動: WASD / 方向キー（長押しでオートリピート）\n" +
                "・回転中心の開始: 左クリック（位置で開始）\n" +
                "・回転中心の移動: 左クリックで再指定\n" +
                "・回転中心の終了: 右クリック / Esc / T\n" +
                "・回転（90°）: エイム中 Q=反時計回り, E=時計回り\n" +
                "・自由回転（ドラッグ）: 左ドラッグで回転 → ボタンを離すと確定（±90/±180にスナップ）\n" +
                "・範囲サイズ切替: マウスホイール（1=3, 2=5でも可）\n" +
                "・敵視界の表示切替: V\n" +
                $"・リセット（リトライ）: {resetKey}（ESCメニュー表示中は無効）\n" +
                "・メニュー（ポーズ）: Esc";
        }

        if (gamepadText)
        {
            gamepadText.text =
                "ゲームパッド\n" +
                "・移動: 左スティック / 十字キー（長押しでオートリピート）\n" +
                "・回転中心の開始/終了: 南ボタン（A/×）でトグル\n" +
                "・回転中心の移動: 右スティック（ホールドでリピート）\n" +
                "・エイム解除: 東ボタン（B/○）\n" +
                "・回転（90°）: エイム中 LB/L1=反時計回り, RB/R1=時計回り\n" +
                "・自由回転（左スティック）: 倒した方向の角度に追従 → スティックをニュートラルに0.12秒以上で確定（±90/±180にスナップ）\n" +
                "・範囲サイズ切替: RT/R2\n" +
                "・敵視界の表示切替: L3（左スティック押し込み）\n" +
                "・リセット（リトライ）: 北ボタン（Y/△）※メニュー表示中は無効\n" +
                "・メニュー（ポーズ）: Start（Options/タッチパッド）";
        }
    }

    public void SelectKeyboard()
    {
        SetActiveSafe(keyboardRoot, true);
        SetActiveSafe(gamepadRoot, false);
        // ボタン選択式: どちらも選択可能に保つ
        SetTabInteractable(keyboard: true, gamepad: true);
    }

    public void SelectGamepad()
    {
        SetActiveSafe(keyboardRoot, false);
        SetActiveSafe(gamepadRoot, true);
        // ボタン選択式: どちらも選択可能に保つ
        SetTabInteractable(keyboard: true, gamepad: true);
    }

    public void Close()
    {
        // この説明パネルを閉じる（次回自動で出ないようactiveSelf=false）
        gameObject.SetActive(false);
        // ESCメニュー側へフォーカスを戻す（GameFlow/GlobalEscMenuの両対応）
        TryFocusEscMenu();
    }

    void TryFocusEscMenu()
    {
        if (EventSystem.current == null) return;

        // Gameシーン（GameFlow側のESC）
        var gf = UnityCompat.FindFirst<GameFlow>();
        if (gf != null && gf.escCloseButton != null && gf.escCloseButton.gameObject.activeInHierarchy)
        {
            EventSystem.current.SetSelectedGameObject(gf.escCloseButton.gameObject);
            return;
        }

        // その他シーン（GlobalEscMenu）
        var gm = UnityCompat.FindFirst<GlobalEscMenu>();
        if (gm != null && gm.closeButton != null && gm.closeButton.gameObject.activeInHierarchy)
        {
            EventSystem.current.SetSelectedGameObject(gm.closeButton.gameObject);
        }
    }

    void SetActiveSafe(GameObject go, bool on) { if (go) go.SetActive(on); }

    void SetTabInteractable(bool keyboard, bool gamepad)
    {
        // ボタン選択式: 常に両方trueにする（引数は無視）
        if (tabKeyboardButton) tabKeyboardButton.interactable = true;
        if (tabGamepadButton) tabGamepadButton.interactable = true;
    }
}