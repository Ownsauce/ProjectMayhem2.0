using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    /// <summary>Crops a RawImage like CSS background-size: cover without stretching it.</summary>
    public sealed class RawImageAspectFill : MonoBehaviour
    {
        RawImage _image;
        RectTransform _rect;
        Vector2 _lastSize;

        void Awake()
        {
            _image = GetComponent<RawImage>();
            _rect = transform as RectTransform;
            Refresh();
        }

        void OnRectTransformDimensionsChange() => Refresh();

        void LateUpdate()
        {
            if (_rect != null && _rect.rect.size != _lastSize)
                Refresh();
        }

        void Refresh()
        {
            if (_image == null) _image = GetComponent<RawImage>();
            if (_rect == null) _rect = transform as RectTransform;
            Texture texture = _image != null ? _image.texture : null;
            if (texture == null || _rect == null || texture.width <= 0 || texture.height <= 0)
                return;

            Vector2 size = _rect.rect.size;
            if (size.x <= 0f || size.y <= 0f) return;
            _lastSize = size;
            float viewAspect = size.x / size.y;
            float textureAspect = (float)texture.width / texture.height;
            if (viewAspect > textureAspect)
            {
                float visibleHeight = textureAspect / viewAspect;
                _image.uvRect = new Rect(0f, (1f - visibleHeight) * .5f, 1f, visibleHeight);
            }
            else
            {
                float visibleWidth = viewAspect / textureAspect;
                _image.uvRect = new Rect((1f - visibleWidth) * .5f, 0f, visibleWidth, 1f);
            }
        }
    }
}
