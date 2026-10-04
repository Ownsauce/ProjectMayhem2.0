using UnityEngine;

namespace AO.Unity.World.Procedural
{
    [RequireComponent(typeof(Light))]
    public sealed class SubwayFixtureLight : MonoBehaviour
    {
        public Light Source => GetComponent<Light>();
    }
}
