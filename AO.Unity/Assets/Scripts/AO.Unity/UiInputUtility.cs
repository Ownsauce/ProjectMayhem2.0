using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AO.Unity
{
    public static class UiInputUtility
    {
        public static bool IsTextInputFocused()
        {
            if (EventSystem.current == null)
                return false;

            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected == null)
                return false;

            return selected.GetComponent<InputField>() != null
                || selected.GetComponentInParent<InputField>() != null;
        }
    }
}
