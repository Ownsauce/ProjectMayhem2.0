using System;
using System.Collections.Generic;
using UnityEngine;

namespace AO.Unity.World
{
    public partial class PrototypeWorldBootstrap
    {
        private WorldGen.Dungeons.NativeRoomRecipe _pendingNativeRoomRecipe;

        public bool TransitionToNativeRoomDungeon(int instanceId, WorldGen.Dungeons.NativeRoomRecipe recipe,
            Transform character, Vector3 destination)
        {
            _pendingNativeRoomRecipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            try
            {
                // A recipe must rebuild even when entering from the unmodified source playfield.
                if (_activePlayfieldId == recipe.Catalog.SourcePlayfield) ClearActivePlayfield();
                return TransitionToNativePlayfieldCopy(instanceId, recipe.Catalog.SourcePlayfield, character, destination);
            }
            finally { _pendingNativeRoomRecipe = null; }
        }

        public bool TransitionToNativePlayfieldCopy(int instanceId, int sourceId,
            Transform character, Vector3 destination)
        {
            bool previous = placeWorldInAoCoordinates;
            placeWorldInAoCoordinates = true;
            try
            {
                bool loaded = TransitionToPlayfield(sourceId,character,destination);
                if (loaded)
                {
                    _activePlayfieldId = instanceId;
                    Debug.Log($"[WorldGen] Native copy instance={instanceId} source={sourceId} loaded.");
                }
                return loaded;
            }
            finally { placeWorldInAoCoordinates = previous; }
        }

        public bool TransitionToProceduralPlayfield(int pf, Transform characterTransform,
            Vector3 aoDestination)
        {
            if (pf <= 0 || characterTransform == null)
                return false;

            BeginEnterWorldLoading();
            ClearPendingTransitionSpawn(restorePhysics: true);
            if (_activePlayfieldId > 0)
                ClearActivePlayfield();
            var root = new GameObject($"Procedural Playfield {pf}");
            root.transform.SetParent(_worldRoot != null ? _worldRoot : transform, false);
            _activePlayfieldRoot = root.transform;
            _activePlayfieldId = pf;
            _activeHorizontalCenter = Vector3.zero;
            _activeCoordinateScale = 1f;
            _activeUseCenteredCoordinates = false;
            _activePlayfieldUsesIndoorRoomSurfaces = true;
            TeleportCharacterTransform(characterTransform, aoDestination);
            CompleteEnterWorldLoadingIfReady(characterTransform);
            Debug.Log($"Transitioned to procedural PF {pf} at AO {aoDestination:F3}.");
            return true;
        }

        public bool TransitionToPlayfield(
            int pf,
            Transform characterTransform,
            Vector3? explicitAoDestination = null,
            float? explicitYaw = null,
            bool preferTeleportDefault = false)
        {
            if (pf <= 0 || characterTransform == null)
                return false;

            BeginEnterWorldLoading();

            Vector3? resolvedAoDestination = explicitAoDestination;
            if ((!resolvedAoDestination.HasValue || preferTeleportDefault)
                && TryResolveTeleportDefaultForPlayfield(pf, out var defaultAo, out var defaultHeading, out _))
            {
                resolvedAoDestination = defaultAo;
                if (!explicitYaw.HasValue && defaultHeading.HasValue)
                    explicitYaw = defaultHeading.Value;
            }

            if (_activePlayfieldId == pf && !_activePlayfieldGlbLoadInProgress)
            {
                Vector3 destination = resolvedAoDestination.HasValue
                    ? ConvertAoToWorld(resolvedAoDestination.Value)
                    : characterTransform.position;

                Vector3 finalPosition;
                if (resolvedAoDestination.HasValue)
                {
                    finalPosition = ResolveExplicitSpawnPosition(destination);
                }
                else if (_activePlayfieldUsesIndoorRoomSurfaces)
                {
                    finalPosition = destination + Vector3.up * Mathf.Max(0.5f, safeSpawnHeightOffset);
                }
                else
                {
                    finalPosition = ResolveBestSpawnPosition(destination);
                }

                TeleportCharacterTransform(characterTransform, finalPosition);
                if (explicitYaw.HasValue)
                {
                    var euler = characterTransform.eulerAngles;
                    euler.y = explicitYaw.Value;
                    characterTransform.eulerAngles = euler;
                }
                CompleteEnterWorldLoadingIfReady(characterTransform);
                return true;
            }

            ClearPendingTransitionSpawn(restorePhysics: true);
            if (_activePlayfieldId > 0 && _activePlayfieldId != pf)
                ClearActivePlayfield();

            if (!TryLoadPlayfieldJson(pf, out var centerWorld))
                return false;

            if (_activePlayfieldGlbLoadInProgress)
            {
                QueuePendingTransitionSpawn(
                    characterTransform,
                    resolvedAoDestination,
                    explicitYaw,
                    pf,
                    centerWorld,
                    _activePlayfieldGlbLoadTicket);
                Debug.Log($"Transition spawn PF {pf} deferred until GLB content is ready.");
                return true;
            }

            Vector3 destinationWorld = resolvedAoDestination.HasValue
                ? ConvertAoToWorld(resolvedAoDestination.Value)
                : centerWorld;

            Vector3 finalWorld;
            if (resolvedAoDestination.HasValue)
                finalWorld = ResolveExplicitSpawnPosition(destinationWorld);
            else if (_activePlayfieldUsesIndoorRoomSurfaces)
                finalWorld = destinationWorld + Vector3.up * Mathf.Max(0.5f, safeSpawnHeightOffset);
            else
                finalWorld = ResolveBestSpawnPosition(destinationWorld);

            TeleportCharacterTransform(characterTransform, finalWorld);

            if (explicitYaw.HasValue)
            {
                var e = characterTransform.eulerAngles;
                e.y = explicitYaw.Value;
                characterTransform.eulerAngles = e;
            }
            CompleteEnterWorldLoadingIfReady(characterTransform);

            Debug.Log(
                $"Transition spawn PF {pf}: " +
                $"targetAO={(resolvedAoDestination.HasValue ? resolvedAoDestination.Value.ToString("F3") : "none")} " +
                $"world={finalWorld.ToString("F3")}");
            return true;
        }

        private void QueuePendingTransitionSpawn(
            Transform characterTransform,
            Vector3? targetAoPosition,
            float? targetYaw,
            int pf,
            Vector3 fallbackWorldPosition,
            int loadTicket)
        {
            _pendingTransitionCharacter = characterTransform;
            _pendingTransitionAoPosition = targetAoPosition;
            _pendingTransitionYaw = targetYaw;
            _pendingTransitionPlayfieldId = pf;
            _pendingTransitionLoadTicket = loadTicket;
            _pendingTransitionFallbackWorldPosition = fallbackWorldPosition;
            _pendingTransitionRigidbody = characterTransform != null
                ? characterTransform.GetComponent<Rigidbody>()
                : null;

            if (_pendingTransitionRigidbody != null)
            {
                _pendingTransitionRbUseGravity = _pendingTransitionRigidbody.useGravity;
                _pendingTransitionRbIsKinematic = _pendingTransitionRigidbody.isKinematic;
                _pendingTransitionRigidbody.linearVelocity = Vector3.zero;
                _pendingTransitionRigidbody.angularVelocity = Vector3.zero;
                _pendingTransitionRigidbody.useGravity = false;
                _pendingTransitionRigidbody.isKinematic = true;
            }
        }

        private void ClearPendingTransitionSpawn(bool restorePhysics)
        {
            if (restorePhysics && _pendingTransitionRigidbody != null)
            {
                _pendingTransitionRigidbody.useGravity = _pendingTransitionRbUseGravity;
                _pendingTransitionRigidbody.isKinematic = _pendingTransitionRbIsKinematic;
            }

            _pendingTransitionCharacter = null;
            _pendingTransitionAoPosition = null;
            _pendingTransitionYaw = null;
            _pendingTransitionPlayfieldId = -1;
            _pendingTransitionLoadTicket = -1;
            _pendingTransitionFallbackWorldPosition = Vector3.zero;
            _pendingTransitionRigidbody = null;
            _pendingTransitionRbUseGravity = false;
            _pendingTransitionRbIsKinematic = false;
        }

        private void ApplyPendingTransitionSpawnIfAny(int pf, int loadTicket)
        {
            if (_pendingTransitionCharacter == null
                || _pendingTransitionPlayfieldId != pf
                || _pendingTransitionLoadTicket != loadTicket)
                return;

            Vector3 desiredWorld = _pendingTransitionAoPosition.HasValue
                ? ConvertAoToWorld(_pendingTransitionAoPosition.Value)
                : _pendingTransitionFallbackWorldPosition;
            Vector3 finalWorld;
            if (_pendingTransitionAoPosition.HasValue)
            {
                finalWorld = ResolveExplicitSpawnPosition(desiredWorld);
            }
            else if (_activePlayfieldUsesIndoorRoomSurfaces)
            {
                finalWorld = desiredWorld + Vector3.up * Mathf.Max(0.5f, safeSpawnHeightOffset);
            }
            else
            {
                finalWorld = ResolveBestSpawnPosition(desiredWorld);
            }

            if (_pendingTransitionYaw.HasValue)
            {
                var e = _pendingTransitionCharacter.eulerAngles;
                e.y = _pendingTransitionYaw.Value;
                _pendingTransitionCharacter.eulerAngles = e;
            }

            TeleportCharacterTransform(_pendingTransitionCharacter, finalWorld);
            Debug.Log(
                $"Deferred transition spawn PF {pf}: " +
                $"targetAO={(_pendingTransitionAoPosition.HasValue ? _pendingTransitionAoPosition.Value.ToString("F3") : "none")} " +
                $"world={finalWorld.ToString("F3")}");
            Transform transitionedCharacter = _pendingTransitionCharacter;
            ClearPendingTransitionSpawn(restorePhysics: true);
            CompleteEnterWorldLoadingIfReady(transitionedCharacter);
        }

        private Vector3 ResolveBestSpawnPosition(Vector3 desiredWorldPosition)
        {
            if (_activePlayfieldRoot == null)
                return desiredWorldPosition + Vector3.up * Mathf.Max(0.5f, safeSpawnHeightOffset);

            if (TryFindSurfaceNear(desiredWorldPosition, out var safePosition))
                return safePosition;

            if (TryFindHighestSurfaceInPlayfield(out var highestSafe))
                return highestSafe;

            return desiredWorldPosition + Vector3.up * Mathf.Max(0.5f, safeSpawnHeightOffset);
        }

        private Vector3 ResolveExplicitSpawnPosition(Vector3 explicitWorldPosition)
        {
            // Preserve the authoritative/default X/Z, but indoor AO defaults can use the
            // dungeon's gameplay-space Y while extracted SurfaceResource geometry uses its
            // materialized room-space Y. Ground against the lowest walkable indoor surface
            // at that exact X/Z so the character starts below the ceiling, not on the roof.
            if (_activePlayfieldUsesIndoorRoomSurfaces
                && TryFindLowestIndoorSurfaceAtExactXZ(
                    explicitWorldPosition, out Vector3 indoorGrounded))
            {
                return indoorGrounded;
            }
            return explicitWorldPosition;
        }

        public bool EnsureCharacterOnPlayfieldSurface(Transform characterTransform)
        {
            if (characterTransform == null || _activePlayfieldRoot == null)
                return false;

            Vector3 requested = characterTransform.position;
            bool foundExact = _activePlayfieldUsesIndoorRoomSurfaces
                ? TryFindLowestIndoorSurfaceAtExactXZ(requested, out Vector3 grounded)
                : TryFindSurfaceAtExactXZ(requested, out grounded);
            if (!foundExact
                && !TryFindSurfaceNear(requested, out grounded))
            {
                return false;
            }

            TeleportCharacterTransform(characterTransform, grounded);
            return true;
        }

        private bool TryFindLowestIndoorSurfaceAtExactXZ(
            Vector3 worldPoint,
            out Vector3 groundedPoint)
        {
            groundedPoint = worldPoint;
            float probeHeight = Mathf.Max(200f, safeSpawnProbeHeight);
            Vector3 origin = new Vector3(worldPoint.x, worldPoint.y + probeHeight, worldPoint.z);
            var hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                probeHeight * 4f,
                ~0,
                QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return false;

            bool found = false;
            float lowestY = float.MaxValue;
            for (int index = 0; index < hits.Length; index++)
            {
                RaycastHit hit = hits[index];
                if (hit.collider == null || hit.transform == null)
                    continue;
                if (!_activePlayfieldRoot.IsChildOf(hit.transform)
                    && !hit.transform.IsChildOf(_activePlayfieldRoot))
                {
                    continue;
                }
                if (hit.normal.y < 0.35f || hit.point.y >= lowestY)
                    continue;

                lowestY = hit.point.y;
                found = true;
            }

            if (!found)
                return false;

            groundedPoint = new Vector3(
                worldPoint.x,
                lowestY + Mathf.Max(0.5f, safeSpawnHeightOffset),
                worldPoint.z);
            return true;
        }

        private bool TryFindSurfaceAtExactXZ(Vector3 worldPoint, out Vector3 groundedPoint)
        {
            groundedPoint = worldPoint;
            float probeHeight = Mathf.Max(50f, safeSpawnProbeHeight);
            Vector3 origin = new Vector3(worldPoint.x, worldPoint.y + probeHeight, worldPoint.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, probeHeight * 2f, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return false;

            Array.Sort(hits, (a, b) =>
            {
                int byY = b.point.y.CompareTo(a.point.y);
                if (byY != 0)
                    return byY;
                return a.distance.CompareTo(b.distance);
            });

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || hit.transform == null)
                    continue;
                if (!_activePlayfieldRoot.IsChildOf(hit.transform) && !hit.transform.IsChildOf(_activePlayfieldRoot))
                    continue;
                if (hit.normal.y < 0.35f)
                    continue;

                groundedPoint = new Vector3(worldPoint.x, hit.point.y + Mathf.Max(0.5f, safeSpawnHeightOffset), worldPoint.z);
                return true;
            }

            return false;
        }

        private bool TryFindSurfaceNear(Vector3 desiredWorldPosition, out Vector3 safePosition)
        {
            safePosition = desiredWorldPosition;

            float probeHeight = Mathf.Max(50f, safeSpawnProbeHeight);
            float step = Mathf.Max(1f, safeSpawnSearchStep);
            int rings = Mathf.Max(0, safeSpawnSearchRings);

            var offsets = new List<Vector2> { Vector2.zero };
            for (int ring = 1; ring <= rings; ring++)
            {
                float d = ring * step;
                offsets.Add(new Vector2(d, 0f));
                offsets.Add(new Vector2(-d, 0f));
                offsets.Add(new Vector2(0f, d));
                offsets.Add(new Vector2(0f, -d));
                offsets.Add(new Vector2(d, d));
                offsets.Add(new Vector2(d, -d));
                offsets.Add(new Vector2(-d, d));
                offsets.Add(new Vector2(-d, -d));
            }

            bool found = false;
            float bestScore = float.MinValue;
            Vector3 bestPoint = desiredWorldPosition;

            for (int i = 0; i < offsets.Count; i++)
            {
                Vector2 offset = offsets[i];
                Vector3 origin = new Vector3(
                    desiredWorldPosition.x + offset.x,
                    desiredWorldPosition.y + probeHeight,
                    desiredWorldPosition.z + offset.y);

                var hits = Physics.RaycastAll(origin, Vector3.down, probeHeight * 2f, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) =>
                {
                    int byY = b.point.y.CompareTo(a.point.y);
                    if (byY != 0)
                        return byY;
                    return a.distance.CompareTo(b.distance);
                });
                for (int h = 0; h < hits.Length; h++)
                {
                    var hit = hits[h];
                    if (hit.collider == null || hit.transform == null)
                        continue;
                    if (!_activePlayfieldRoot.IsChildOf(hit.transform) && !hit.transform.IsChildOf(_activePlayfieldRoot))
                        continue;
                    if (hit.normal.y < 0.35f)
                        continue;
                    if (hit.collider is MeshCollider meshCollider && meshCollider.convex && statelCollidersIsTrigger)
                        continue;
                    if (hit.collider.isTrigger)
                        continue;

                    float planarDistance = Vector2.Distance(
                        new Vector2(hit.point.x, hit.point.z),
                        new Vector2(desiredWorldPosition.x, desiredWorldPosition.z));

                    float score =
                        (hit.point.y * 1000f)
                        - (planarDistance * 10f)
                        + (hit.normal.y * 5f);

                    string colliderName = hit.collider.gameObject.name;
                    if (colliderName.IndexOf("TerrainChunk_", StringComparison.OrdinalIgnoreCase) >= 0)
                        score += 250f;
                    else if (colliderName.IndexOf("WaterMesh_", StringComparison.OrdinalIgnoreCase) >= 0)
                        score -= 500f;

                    if (score > bestScore)
                    {
                        found = true;
                        bestScore = score;
                        bestPoint = hit.point;
                    }
                }
            }

            if (!found)
                return false;

            safePosition = bestPoint + Vector3.up * Mathf.Max(0.5f, safeSpawnHeightOffset);
            return true;
        }

        private bool TryFindHighestSurfaceInPlayfield(out Vector3 safePosition)
        {
            safePosition = Vector3.zero;
            if (_activePlayfieldRoot == null)
                return false;

            Collider[] colliders = _activePlayfieldRoot.GetComponentsInChildren<Collider>(true);
            if (colliders == null || colliders.Length == 0)
                return false;

            bool found = false;
            float bestY = float.MinValue;
            Vector3 bestPoint = Vector3.zero;
            float probeHeight = Mathf.Max(50f, safeSpawnProbeHeight);

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null || !c.enabled || c.isTrigger)
                    continue;

                Bounds b = c.bounds;
                if (!float.IsFinite(b.center.x) || !float.IsFinite(b.center.y) || !float.IsFinite(b.center.z))
                    continue;

                Vector3 origin = new Vector3(b.center.x, b.max.y + probeHeight, b.center.z);
                var hits = Physics.RaycastAll(origin, Vector3.down, probeHeight * 2f, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) =>
                {
                    int byY = b.point.y.CompareTo(a.point.y);
                    if (byY != 0)
                        return byY;
                    return a.distance.CompareTo(b.distance);
                });
                for (int h = 0; h < hits.Length; h++)
                {
                    var hit = hits[h];
                    if (hit.collider == null || hit.transform == null)
                        continue;
                    if (!_activePlayfieldRoot.IsChildOf(hit.transform) && !hit.transform.IsChildOf(_activePlayfieldRoot))
                        continue;
                    if (hit.normal.y < 0.35f)
                        continue;

                    if (hit.point.y > bestY)
                    {
                        bestY = hit.point.y;
                        bestPoint = hit.point;
                        found = true;
                    }
                }
            }

            if (!found)
                return false;

            safePosition = bestPoint + Vector3.up * Mathf.Max(0.5f, safeSpawnHeightOffset);
            return true;
        }
    }
}
