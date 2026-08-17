using AO.Unity.Prototype;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    internal sealed class ItemCooldownOverlay : MonoBehaviour
    {
        public PrototypeUiContext Context;
        public long InstanceId;
        public RectTransform OverlayRect;
        public Image OverlayImage;
        public Text CountdownText;

        private void Update()
        {
            if (Context == null || OverlayImage == null || OverlayRect == null || InstanceId == 0)
            {
                if (OverlayImage != null)
                    OverlayImage.enabled = false;
                if (CountdownText != null)
                    CountdownText.enabled = false;
                return;
            }

            if (!Context.TryGetItemCooldown(InstanceId, out float remaining, out float total) || total <= 0f)
            {
                OverlayImage.enabled = false;
                if (CountdownText != null)
                    CountdownText.enabled = false;
                return;
            }

            float ratio = Mathf.Clamp01(remaining / total);
            bool visible = ratio > 0f;
            OverlayImage.enabled = visible;
            if (CountdownText != null)
                CountdownText.enabled = visible;
            if (!visible)
                return;

            var parentRect = OverlayRect.parent as RectTransform;
            float fullWidth = parentRect != null ? parentRect.rect.width : OverlayRect.rect.width;
            OverlayRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, fullWidth * ratio));

            if (CountdownText != null)
            {
                int seconds = Mathf.CeilToInt(remaining);
                CountdownText.text = seconds > 0 ? seconds.ToString() : string.Empty;
            }
        }
    }
}
