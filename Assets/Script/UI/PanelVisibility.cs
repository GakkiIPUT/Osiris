using UnityEngine;

public class PanelVisibility : MonoBehaviour
{
    public GameObject target;

    public void Show()  { if (target) target.SetActive(true); }
    public void Hide()  { if (target) target.SetActive(false); }
    public void Toggle(){ if (target) target.SetActive(!target.activeSelf); }
}