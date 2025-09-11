using UnityEngine;

/// <summary>
/// 任意の GameObject の表示切り替え（Show/Hide/Toggle）を提供する単純なユーティリティ。
/// </summary>
public class PanelVisibility : MonoBehaviour
{
    public GameObject target;

    /// <summary>対象を表示する。</summary>
    public void Show()
    { if (target) target.SetActive(true); }

    /// <summary>対象を非表示にする。</summary>
    public void Hide()
    { if (target) target.SetActive(false); }

    /// <summary>対象の表示状態を反転する。</summary>
    public void Toggle()
    { if (target) target.SetActive(!target.activeSelf); }
}
