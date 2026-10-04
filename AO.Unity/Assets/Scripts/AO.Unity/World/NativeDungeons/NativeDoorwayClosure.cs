using UnityEngine;

namespace AO.Unity.World
{
    /// <summary>Recessed metal closure trim, fitted to the shared physical seal.</summary>
    internal static class NativeDoorwayClosure
    {
        public static void AddFrame(Transform seal, Material material)
        {
            // Child dimensions are fractions of the fitted seal. The shared parent
            // cube alone supplies collision; decorative strips never create obstacles.
            foreach (float side in new[] { -1f, 1f })
            {
                Strip(seal, new Vector3(side * .48f, 0, 0), new Vector3(.04f, 1, 1.12f), material);
                Strip(seal, new Vector3(0, side * .48f, 0), new Vector3(1, .04f, 1.12f), material);
            }
        }
        private static void Strip(Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = "MetalClosureTrim";
            part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
            part.GetComponent<Collider>().enabled = false; part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
