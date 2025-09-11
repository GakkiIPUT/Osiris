using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 操作説明（キーボード/ゲームパッド）のタブ切替UI。ESCメニュー配下で開閉される。
/// </summary>
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

    /// <summary>タブ/クローズのボタンイベントを登録する。</summary>
    private void Awake()
    {
        if (tabKeyboardButton) tabKeyboardButton.onClick.AddListener(SelectKeyboard);
        if (tabGamepadButton) tabGamepadButton.onClick.AddListener(SelectGamepad);
        if (closeButton) closeButton.onClick.AddListener(Close); // 追加: クリックで閉じる
    }

    /// <summary>有効化時、文面更新とデフォルトタブを適用する。</summary>
    private void OnEnable()
    {
        RefreshTexts();
        SelectKeyboard();
        SetTabInteractable(keyboard: true, gamepad: true);
    }

    /// <summary>
    /// 親のESCパネル経由で閉じられた場合に備え、次回自動で開かないよう自身をOFFに倒す。
    /// </summary>
    private void OnDisable()
    {
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>Esc キーで説明を閉じる。</summary>
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
        }
    }

    /// <summary>現在のキーバインドに合わせてテキストを更新する。</summary>
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

    /// <summary>キーボード説明タブを表示する。</summary>
    public void SelectKeyboard()
    {
        SetActiveSafe(keyboardRoot, true);
        SetActiveSafe(gamepadRoot, false);
        SetTabInteractable(keyboard: true, gamepad: true);
    }

    /// <summary>ゲームパッド説明タブを表示する。</summary>
    public void SelectGamepad()
    {
        SetActiveSafe(keyboardRoot, false);
        SetActiveSafe(gamepadRoot, true);
        SetTabInteractable(keyboard: true, gamepad: true);
    }

    /// <summary>説明パネルを閉じ、ESCメニューへフォーカスを戻す。</summary>
    public void Close()
    {
        gameObject.SetActive(false);
        TryFocusEscMenu();
    }

    /// <summary>ESCメニュー（GameFlow/GlobalEscMenu）へフォーカスを移す。</summary>
    private void TryFocusEscMenu()
    {
        if (EventSystem.current == null) return;

        var gf = UnityCompat.FindFirst<GameFlow>();
        if (gf != null && gf.escCloseButton != null && gf.escCloseButton.gameObject.activeInHierarchy)
        {
            EventSystem.current.SetSelectedGameObject(gf.escCloseButton.gameObject);
            return;
        }

        var gm = UnityCompat.FindFirst<GlobalEscMenu>();
        if (gm != null && gm.closeButton != null && gm.closeButton.gameObject.activeInHierarchy)
        {
            EventSystem.current.SetSelectedGameObject(gm.closeButton.gameObject);
        }
    }

    /// <summary>null 安全に SetActive を行う。</summary>
    private void SetActiveSafe(GameObject go, bool on)
    { if (go) go.SetActive(on); }

    /// <summary>タブボタンのインタラクティブ状態を設定する（本UIでは常に両方 true）。</summary>
    private void SetTabInteractable(bool keyboard, bool gamepad)
    {
        if (tabKeyboardButton) tabKeyboardButton.interactable = true;
        if (tabGamepadButton) tabGamepadButton.interactable = true;
    }
}
