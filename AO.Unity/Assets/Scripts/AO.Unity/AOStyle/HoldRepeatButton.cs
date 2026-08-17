using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AO.Unity.AOStyle
{
    public sealed class HoldRepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action PointerDownAction { get; set; }
        public Action PointerUpAction { get; set; }
        public Action PointerExitAction { get; set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            PointerDownAction?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            PointerUpAction?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            PointerExitAction?.Invoke();
        }
    }
}
