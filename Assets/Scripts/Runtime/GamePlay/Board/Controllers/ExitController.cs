using UnityEngine;

/// <summary>
/// Exit の開閉状態を見た目に反映するコンポーネント（Sprite/Animator/子の有効切替対応）。
/// </summary>
public class ExitController : MonoBehaviour
{
    [Header("Sprite Swap (optional)")]
    public SpriteRenderer spriteRenderer;

    public Sprite closedSprite;
    public Sprite openSprite;

    [Header("GameObject Toggle (optional)")]
    public GameObject closedRoot;

    public GameObject openRoot;

    [Header("Animator (optional)")]
    public Animator animator;

    [Tooltip("Animator の Bool 名（true=Open）")]
    public string openBoolName = "IsOpen";

    [Tooltip("Trigger を使う場合（開く時だけ発火）。閉じる演出が不要な時に便利")]
    public bool useOpenTrigger = false;

    public string openTriggerName = "Open";

    private bool _isOpen = false;

    /// <summary>現在の開閉状態</summary>
    public bool IsOpen => _isOpen;

    /// <summary>
    /// 依存コンポーネントの自動割り当て。
    /// </summary>
    private void Reset()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
    }

    /// <summary>
    /// 開閉状態を設定し、Animator/Sprite/子オブジェクトへ反映する。
    /// </summary>
    public void SetOpen(bool open)
    {
        if (_isOpen == open) return;
        _isOpen = open;

        // Animator 優先
        if (animator != null)
        {
            if (useOpenTrigger)
            {
                if (open && !string.IsNullOrEmpty(openTriggerName))
                    animator.SetTrigger(openTriggerName);
            }
            else if (!string.IsNullOrEmpty(openBoolName))
            {
                animator.SetBool(openBoolName, open);
            }
        }

        // Sprite 差し替え
        if (spriteRenderer != null && openSprite != null && closedSprite != null)
        {
            spriteRenderer.sprite = open ? openSprite : closedSprite;
        }

        // 子の表示切替
        if (closedRoot != null) closedRoot.SetActive(!open);
        if (openRoot != null) openRoot.SetActive(open);
    }
}
