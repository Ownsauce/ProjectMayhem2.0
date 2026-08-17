using UnityEngine;

namespace AO.Unity.Prototype
{
    public abstract class PrototypeWindowBase : MonoBehaviour
    {
        protected PrototypeUiContext Context;
        protected Font Font;

        public virtual void Initialize(PrototypeUiContext context, Font font)
        {
            Context = context;
            Font = font;
            Context.StateChanged += RefreshView;
            Build();
            RefreshView();
        }

        protected virtual void OnDestroy()
        {
            if (Context != null)
                Context.StateChanged -= RefreshView;
        }

        protected abstract void Build();
        protected abstract void RefreshView();
    }
}
