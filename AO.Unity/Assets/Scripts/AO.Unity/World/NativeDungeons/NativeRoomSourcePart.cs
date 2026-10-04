using UnityEngine;

namespace AO.Unity.World
{
    public enum NativeRoomPartKind { Collision, Architecture, Models }
    /// <summary>Explicit source ownership; object names are only for human inspection.</summary>
    public sealed class NativeRoomSourcePart : MonoBehaviour
    {
        [SerializeField] private int sourceIndex;
        [SerializeField] private NativeRoomPartKind kind;
        public int SourceIndex => sourceIndex;
        public NativeRoomPartKind Kind => kind;
        public void Configure(int index, NativeRoomPartKind partKind) { sourceIndex = index; kind = partKind; }
    }
}
