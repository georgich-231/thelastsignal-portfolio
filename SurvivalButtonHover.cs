using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
public sealed class SurvivalButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image Highlight, Border;
    public Text Label;
    public bool IsHovered { get; private set; }
    public void OnPointerEnter(PointerEventData e) { IsHovered=true; Paint(); }
    public void OnPointerExit(PointerEventData e) { IsHovered=false; Paint(); }
    void Paint()
    {
        Highlight.color=IsHovered ? new Color(.055f,.09f,.12f,1f) : Color.clear;
        Border.color=IsHovered ? new Color(.7f,.85f,.92f,.55f) : Color.clear;
        Label.color=IsHovered ? Color.white : SurvivalUITheme.Ivory;
    }
    void OnDisable() { IsHovered=false; if(Highlight!=null && Border!=null && Label!=null) Paint(); }
}

