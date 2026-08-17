using System;
using System.Collections.Generic;
using UnityEngine;

namespace AO.Unity.World
{
    public partial class PrototypeWorldBootstrap
    {
        [SerializeField] private bool collapseRuntimeStreamingDebugHud = false;

        private void Update()
        {
            UpdateRuntimeGlbAttachQueue();
            UpdateRuntimeDynelGlbFallbackRecovery();
            UpdateFrameBudgetWatchdog();
        }

        private void UpdateRuntimeGlbAttachQueue()
        {
            if (_runtimeGlbAttachQueue == null || _runtimeGlbAttachQueue.Count == 0)
                return;

            int budget = Mathf.Max(1, maxRuntimeGlbAttachesPerFrame);
            float frameBudgetMs = Mathf.Max(0.25f, runtimeGlbAttachFrameBudgetMs);
            float startedAt = Time.realtimeSinceStartup;
            while (budget > 0 && _runtimeGlbAttachQueue.Count > 0)
            {
                var request = _runtimeGlbAttachQueue.Dequeue();
                if (request == null || request.Host == null || string.IsNullOrWhiteSpace(request.Path))
                {
                    budget--;
                    continue;
                }

                TryAttachCachedRuntimeGlbVisual(request.Host, request.Path);
                budget--;
                float elapsedMs = (Time.realtimeSinceStartup - startedAt) * 1000f;
                if (elapsedMs >= frameBudgetMs)
                    break;
            }
        }

        private void UpdateFrameBudgetWatchdog()
        {
            if (_clientLoadPhase != ClientLoadPhase.InWorld)
                return;
            if (_activePlayfieldId <= 0 || _activePlayfieldRoot == null)
                return;
            if (!Application.isFocused)
                return;
            if (Time.unscaledTime < 2f)
                return;

            float frameMs = Time.unscaledDeltaTime * 1000f;
            if (!float.IsFinite(frameMs))
                return;
            if (Time.unscaledTime < _nextFrameSpikeLogAt)
                return;

            float warn = Mathf.Max(50f, frameSpikeWarnMs);
            float err = Mathf.Max(120f, Mathf.Max(warn, frameSpikeErrorMs));
            if (frameMs >= err)
            {
                _frameBudgetErrorCount++;
                Debug.LogError(
                    $"[PerfBudget] Frame spike ERROR: {frameMs:0.0}ms phase={_clientLoadPhase} pf={_activePlayfieldId}.");
                _nextFrameSpikeLogAt = Time.unscaledTime + Mathf.Max(0.1f, frameSpikeLogCooldownSeconds);
            }
            else if (frameMs >= warn)
            {
                _frameBudgetWarnCount++;
                Debug.LogWarning(
                    $"[PerfBudget] Frame spike WARN: {frameMs:0.0}ms phase={_clientLoadPhase} pf={_activePlayfieldId}.");
                _nextFrameSpikeLogAt = Time.unscaledTime + Mathf.Max(0.1f, frameSpikeLogCooldownSeconds);
            }
        }

        private void OnGUI()
        {
            if (!showRuntimeStreamingDebugHud)
                return;

            const float expandedWidth = 690f;
            const float expandedHeight = 52f;
            const float margin = 12f;
            var expandedRect = new Rect(Mathf.Max(margin, Screen.width - expandedWidth - margin), margin, expandedWidth, expandedHeight);
            var collapsedRect = new Rect(Mathf.Max(margin, Screen.width - 36f - margin), margin, 36f, 24f);

            if (collapseRuntimeStreamingDebugHud)
            {
                if (GUI.Button(collapsedRect, "▸"))
                    collapseRuntimeStreamingDebugHud = false;
                return;
            }

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.Box(expandedRect, GUIContent.none);
            GUI.color = Color.white;
            float activeRadius = Mathf.Max(runtimeObjectRing0Radius, runtimeObjectStreamingRadius);
            string line =
                $"PF={_activePlayfieldId} phase={_clientLoadPhase} " +
                $"deferredQueue(runtime/static)={_runtimeDeferredQueueCount}/{_staticDeferredQueueCount} streamRadius={activeRadius:0.#}m " +
                $"loadBudget(warn/err)={_loadBudgetWarnCount}/{_loadBudgetErrorCount} " +
                $"frameBudget(warn/err)={_frameBudgetWarnCount}/{_frameBudgetErrorCount}";
            GUI.Label(new Rect(expandedRect.x + 8f, expandedRect.y + 16f, expandedRect.width - 30f, 20f), line);
            if (GUI.Button(new Rect(expandedRect.xMax - 24f, expandedRect.y + 4f, 18f, 16f), "▾"))
                collapseRuntimeStreamingDebugHud = true;
        }

        private void UpdateRuntimeDynelGlbFallbackRecovery()
        {
            if (!loadRuntimeObjectVisualsFromGlbFallback || _activePlayfieldRoot == null)
                return;
            if (Time.unscaledTime < _nextRuntimeDynelGlbRetrySweepAt)
                return;

            _nextRuntimeDynelGlbRetrySweepAt = Time.unscaledTime + Mathf.Max(0.25f, runtimeDynelGlbRetryIntervalSeconds);
            var states = _activePlayfieldRoot.GetComponentsInChildren<RuntimeDynelGlbFallbackState>(true);
            if (states == null || states.Length == 0)
                return;

            int budget = Mathf.Max(1, runtimeDynelGlbRetryBudgetPerSweep);
            for (int i = 0; i < states.Length && budget > 0; i++)
            {
                var state = states[i];
                if (state == null || state.gameObject == null)
                    continue;
                if (state.RetryCount >= Mathf.Max(1, runtimeDynelGlbMaxRetriesPerHost))
                {
                    ShowRuntimePlaceholderVisuals(state.gameObject);
                    Destroy(state);
                    continue;
                }
                if (Time.unscaledTime < state.NextRetryAt)
                    continue;
                if (HasRuntimeVisualGlbChild(state.gameObject))
                {
                    Destroy(state);
                    continue;
                }

                string selectedPath = null;
                var candidates = state.CandidatePaths;
                for (int c = 0; candidates != null && c < candidates.Count; c++)
                {
                    string path = candidates[c];
                    if (string.IsNullOrWhiteSpace(path))
                        continue;
                    if (_runtimeGlbVisualTemplateByPath.ContainsKey(path) || System.IO.File.Exists(path))
                    {
                        selectedPath = path;
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    state.RetryCount++;
                    state.NextRetryAt = Time.unscaledTime + Mathf.Max(1f, runtimeDynelGlbRetryIntervalSeconds);
                    continue;
                }

                if (!TryAttachCachedRuntimeGlbVisual(state.gameObject, selectedPath))
                {
                    EnqueueRuntimeGlbLoad(state.gameObject, selectedPath, _activePlayfieldGlbLoadTicket, _activePlayfieldRoot);
                    state.RetryCount++;
                    float backoff = Mathf.Min(8f, 1f + state.RetryCount * 0.5f);
                    state.NextRetryAt = Time.unscaledTime + backoff;
                }
                else
                {
                    Destroy(state);
                }

                budget--;
            }
        }

        private static bool HasRuntimeVisualGlbChild(GameObject host)
        {
            if (host == null)
                return false;

            var t = host.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child != null && string.Equals(child.name, "RuntimeVisual_GLB", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public Vector3 ConvertAoToWorld(Vector3 aoPosition)
        {
            float scale = Mathf.Max(0.0001f, _activeCoordinateScale);
            if (_activeUseCenteredCoordinates)
                return (aoPosition - _activeHorizontalCenter) * scale;

            return aoPosition * scale;
        }

        public Vector3 ConvertWorldToAo(Vector3 worldPosition)
        {
            float scale = Mathf.Max(0.0001f, _activeCoordinateScale);
            if (_activeUseCenteredCoordinates)
                return (worldPosition / scale) + _activeHorizontalCenter;

            return worldPosition / scale;
        }

        private void TeleportCharacterTransform(Transform t, Vector3 worldPosition)
        {
            if (t == null)
                return;

            var rb = t.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = worldPosition;
            }

            t.position = worldPosition;
        }

        private void ClearActivePlayfield()
        {
            _activePlayfieldGlbLoadInProgress = false;
            _activePlayfieldGlbLoadTicket++;
            ClearPendingTransitionSpawn(restorePhysics: true);

            if (_activePlayfieldRoot != null)
            {
                Destroy(_activePlayfieldRoot.gameObject);
                _activePlayfieldRoot = null;
            }

            for (int i = 0; i < _activePlayfieldGlbImporters.Count; i++)
            {
                var importer = _activePlayfieldGlbImporters[i];
                if (importer != null)
                    DisposeImporter(importer);
            }
            _activePlayfieldGlbImporters.Clear();

            _activePlayfieldId = -1;
            _activeHorizontalCenter = Vector3.zero;
            _activePlayfieldUsesIndoorRoomSurfaces = false;
            _activeRuntimePrefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            _activeMeshRotationOverrides = new MeshRotationOverrideSet();
            _activePlayfieldLoadedFromPackage = false;
            _runtimeGlbLoadQueue.Clear();
            _runtimeGlbAttachQueue.Clear();
            _runtimeGlbPathBlockedUntil.Clear();
            _runtimeGlbLoadsInFlight = 0;
            if (_runtimeGlbLoadPumpCoroutine != null)
            {
                StopCoroutine(_runtimeGlbLoadPumpCoroutine);
                _runtimeGlbLoadPumpCoroutine = null;
            }
            if (_runtimeObjectBackgroundSpawnCoroutine != null)
            {
                StopCoroutine(_runtimeObjectBackgroundSpawnCoroutine);
                _runtimeObjectBackgroundSpawnCoroutine = null;
            }
            if (_staticStatelBackgroundSpawnCoroutine != null)
            {
                StopCoroutine(_staticStatelBackgroundSpawnCoroutine);
                _staticStatelBackgroundSpawnCoroutine = null;
            }
            _deferredRuntimeSpawnStartedAt = -1f;
            _runtimeDeferredQueueCount = 0;
            _staticDeferredQueueCount = 0;
            if (!persistRuntimeGlbTemplateCacheAcrossZones)
            {
                _runtimeGlbVisualTemplateByPath.Clear();
                if (_runtimeGlbVisualTemplateRoot != null)
                {
                    Destroy(_runtimeGlbVisualTemplateRoot.gameObject);
                    _runtimeGlbVisualTemplateRoot = null;
                }
            }
        }

        public GameObject SpawnAuthoritativeRuntimeEntity(
            Transform parent,
            int identityInstance,
            int? templateId,
            int? meshId,
            string objectType,
            string displayName,
            string importKey,
            string meshName,
            Vector3 aoPosition,
            float yawDegrees)
        {
            if (parent == null)
                parent = _activePlayfieldRoot != null ? _activePlayfieldRoot : _worldRoot;
            if (parent == null)
                return null;

            string resolvedMeshName = ResolveAuthoritativeRuntimeMeshName(
                meshName,
                templateId,
                meshId,
                identityInstance,
                displayName);

            string meshKey = string.IsNullOrWhiteSpace(resolvedMeshName)
                ? null
                : SafeGetFileNameWithoutExtension(resolvedMeshName);

            if (!string.IsNullOrWhiteSpace(meshKey) && ShouldSkipGenericMeshPlaceholder(meshKey))
                return null;

            Vector3 perMeshRotationEuler = Vector3.zero;
            bool perMeshRotationIsAbsolute = false;
            Vector3 perMeshPositionOffset = Vector3.zero;
            if (!string.IsNullOrWhiteSpace(meshKey))
            {
                if (!TryGetMeshVectorOverride(_activeMeshRotationOverrides.MeshRotationByName, meshKey, out perMeshRotationEuler))
                {
                    if (enableBuiltInMeshRotationOverrides
                        && TryGetBuiltInMeshRotationOverride(meshKey, out perMeshRotationEuler))
                    {
                        perMeshRotationIsAbsolute = false;
                    }
                }
                else
                {
                    perMeshRotationIsAbsolute = TryGetMeshRotationMode(
                        _activeMeshRotationOverrides.MeshRotationModeByName,
                        meshKey,
                        out var resolvedMode)
                        ? resolvedMode == RotationOverrideMode.Absolute
                        : true;
                }

                TryGetMeshVectorOverride(
                    _activeMeshRotationOverrides.MeshPositionOffsetByName,
                    meshKey,
                    out perMeshPositionOffset);
            }

            var runtimeStatel = new StatelData
            {
                StatelId = Mathf.Max(0, templateId ?? meshId ?? identityInstance),
                Position = aoPosition,
                Rotation = new Vector3(0f, yawDegrees, 0f),
                Scale = 1f
            };

            GameObject go = SpawnStatelObject(
                parent,
                runtimeStatel,
                resolvedMeshName,
                _activeHorizontalCenter,
                _activeUseCenteredCoordinates,
                Mathf.Max(0.0001f, _activeCoordinateScale),
                1f,
                loadMeshPrefabsFromResources,
                meshPrefabResourcesFolder,
                _activeRuntimePrefabCache,
                applyGlobalMeshBasisCorrection,
                globalMeshBasisCorrectionEuler,
                meshPrefabRotationOffsetEuler,
                perMeshRotationEuler,
                perMeshRotationIsAbsolute,
                perMeshPositionOffset,
                meshKey,
                runtimeStatel.MeshParts,
                false,
                false,
                false,
                false,
                false,
                _activeMeshRotationOverrides.NodeRotationByMeshAndNode,
                _activeMeshRotationOverrides.NodePositionOffsetByMeshAndNode,
                enableBuiltInMeshPlacementCorrection,
                addStatelColliders,
                statelCollidersConvex,
                statelCollidersIsTrigger,
                Mathf.Max(1, maxMeshCollidersPerStatel));

            if (go == null)
                return null;

            string typeLabel = string.IsNullOrWhiteSpace(objectType) ? "RuntimeEntity" : objectType.Trim();
            string runtimeName = !string.IsNullOrWhiteSpace(importKey)
                ? importKey
                : $"{typeLabel}_{templateId ?? meshId ?? identityInstance}";
            go.name = $"Runtime_{runtimeName}";

            ConfigureRuntimeDynelTargetingAndColliders(
                go,
                typeLabel,
                string.IsNullOrWhiteSpace(displayName) ? runtimeName : displayName);

            TryQueueRuntimeObjectGlbVisualFallback(
                go,
                typeLabel,
                string.IsNullOrWhiteSpace(displayName) ? runtimeName : displayName,
                resolvedMeshName,
                identityInstance);

            return go;
        }
    }
}
