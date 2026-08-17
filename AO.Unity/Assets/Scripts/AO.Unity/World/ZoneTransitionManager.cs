using AO.Data.Unity;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AO.Unity.World
{
    public class ZoneTransitionManager : MonoBehaviour
    {
        [SerializeField] private PrototypeWorldBootstrap bootstrap;
        [SerializeField] private string playfieldsSubfolder = "AOData/Playfields";
        [SerializeField] private string zoneLinksFileName = "zone_links.json";
        [SerializeField] private bool rebuildPortalsOnStart = true;
        [SerializeField] private bool logPortalBuild = true;
        [SerializeField] private float minTransitionIntervalSeconds = 0.5f;
        [SerializeField] private float arrivalTriggerBlockSeconds = 2.5f;
        [SerializeField] private float minDistanceFromArrivalToRetrigger = 8f;
        [SerializeField] private float portalArmDelaySecondsAfterBuild = 1.25f;

        private List<ZoneLinkRow> _links = new List<ZoneLinkRow>();
        private bool _transitionInFlight;
        private float _lastTransitionTime = -999f;
        private int _arrivalPlayfieldId = -1;
        private Vector3 _arrivalWorldPosition = Vector3.zero;
        private float _suppressPortalTransitionsUntilTime = -999f;

        public void SuppressPortalTransitions(float seconds, string reason = null)
        {
            _suppressPortalTransitionsUntilTime = Mathf.Max(
                _suppressPortalTransitionsUntilTime,
                Time.unscaledTime + Mathf.Max(0f, seconds));
            if (!string.IsNullOrWhiteSpace(reason))
            {
                Debug.Log($"Zone portal transitions suppressed for {seconds:0.##}s: {reason}");
            }
        }

        private void Start()
        {
            if (bootstrap == null)
                bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();

            LoadLinks();

            if (rebuildPortalsOnStart)
                RebuildPortalsForCurrentPlayfield();
        }

        public void RebuildPortalsForCurrentPlayfield()
        {
            if (bootstrap == null || bootstrap.ActivePlayfieldRoot == null || bootstrap.ActivePlayfieldId <= 0)
                return;

            // Reload links each rebuild so edits to zone_links.json apply without requiring a full restart.
            LoadLinks();

            int pf = bootstrap.ActivePlayfieldId;
            var parent = bootstrap.ActivePlayfieldRoot;
            var existing = parent.Find("_ZonePortals");
            if (existing != null)
                Destroy(existing.gameObject);

            var root = new GameObject("_ZonePortals");
            root.transform.SetParent(parent, false);
            var usedAnchors = new HashSet<int>();

            int created = 0;
            for (int i = 0; i < _links.Count; i++)
            {
                var link = _links[i];
                if (link == null || link.FromPlayfieldId != pf || link.ToPlayfieldId <= 0)
                    continue;

                var anchorCandidates = FindAnchors(parent, link, bootstrap);
                Transform chosen = null;
                for (int a = 0; a < anchorCandidates.Count; a++)
                {
                    var candidate = anchorCandidates[a];
                    if (candidate == null)
                        continue;

                    int id = candidate.GetInstanceID();
                    if (usedAnchors.Contains(id))
                        continue;

                    chosen = candidate;
                    usedAnchors.Add(id);
                    break;
                }

                if (chosen == null)
                {
                    // Fallback: still create a portal at the configured AO source position.
                    // This keeps links usable when statel naming/matching drifts across exports.
                    if (bootstrap != null && link.SourceAOPosition != null)
                    {
                        var fallbackAo = new Vector3(
                            link.SourceAOPosition.X,
                            link.SourceAOPosition.Y,
                            link.SourceAOPosition.Z);
                        var fallbackWorld = bootstrap.ConvertAoToWorld(fallbackAo);

                        var goFallback = new GameObject($"Portal_{link.Id ?? i.ToString()}_Fallback");
                        goFallback.transform.SetParent(root.transform, false);
                        goFallback.transform.position = fallbackWorld;

                        var sphereFallback = goFallback.AddComponent<SphereCollider>();
                        sphereFallback.isTrigger = true;
                        sphereFallback.radius = Mathf.Max(0.5f, link.TriggerRadius);

                        var rbFallback = goFallback.AddComponent<Rigidbody>();
                        rbFallback.isKinematic = true;
                        rbFallback.useGravity = false;

                        var triggerFallback = goFallback.AddComponent<ZonePortalTrigger>();
                        triggerFallback.Initialize(this, link, Mathf.Max(0f, portalArmDelaySecondsAfterBuild));
                        created++;

                        Debug.LogWarning(
                            $"Zone link '{link.Id ?? i.ToString()}' found 0 usable anchors in PF {pf}. " +
                            $"Spawned fallback portal at SourceAOPosition=({fallbackAo.x:F2},{fallbackAo.y:F2},{fallbackAo.z:F2}).");
                        continue;
                    }

                    Debug.LogWarning(
                        $"Zone link '{link.Id ?? i.ToString()}' found 0 usable anchors in PF {pf}. " +
                        $"SourceStatelId={link.SourceStatelId}, SourceMeshName='{link.SourceMeshName ?? ""}', and no SourceAOPosition fallback.");
                    continue;
                }

                var go = new GameObject($"Portal_{link.Id ?? i.ToString()}");
                go.transform.SetParent(root.transform, false);
                Vector3 portalWorldPosition = GetAnchorWorldPosition(chosen);
                if (bootstrap != null && link.SourceAOPosition != null)
                {
                    var sourceAo = new Vector3(
                        link.SourceAOPosition.X,
                        link.SourceAOPosition.Y,
                        link.SourceAOPosition.Z);
                    portalWorldPosition = bootstrap.ConvertAoToWorld(sourceAo);
                }

                go.transform.position = portalWorldPosition;

                var sphere = go.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = Mathf.Max(0.5f, link.TriggerRadius);

                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                var trigger = go.AddComponent<ZonePortalTrigger>();
                trigger.Initialize(this, link, Mathf.Max(0f, portalArmDelaySecondsAfterBuild));
                created++;

                if (logPortalBuild)
                {
                    Debug.Log(
                        $"Zone portal created: id='{link.Id ?? i.ToString()}', PF {link.FromPlayfieldId}->{link.ToPlayfieldId}, " +
                        $"anchor='{chosen.name}', world=({go.transform.position.x:F2},{go.transform.position.y:F2},{go.transform.position.z:F2}), " +
                        $"triggerRadius={sphere.radius:F2}");
                }
            }

            if (logPortalBuild)
                Debug.Log($"Built {created} zone portals for playfield {pf}.");
        }

        internal void TryTransition(ZoneLinkRow link, Collider other)
        {
            if (_transitionInFlight || link == null || bootstrap == null)
                return;
            if (Time.unscaledTime < _suppressPortalTransitionsUntilTime)
                return;
            if (bootstrap.ActivePlayfieldId != link.FromPlayfieldId)
                return;
            if (Time.unscaledTime - _lastTransitionTime < Mathf.Max(0f, minTransitionIntervalSeconds))
                return;

            var bridge = other != null ? other.GetComponentInParent<CharacterRuntimeBridge>() : null;
            var player = bridge != null ? bridge.transform : bootstrap.ResolvePlayerTransform();
            if (player == null)
                return;

            if (bootstrap.ActivePlayfieldId == _arrivalPlayfieldId)
            {
                float sinceArrival = Time.unscaledTime - _lastTransitionTime;
                float planarDist = Vector2.Distance(
                    new Vector2(player.position.x, player.position.z),
                    new Vector2(_arrivalWorldPosition.x, _arrivalWorldPosition.z));

                if (sinceArrival < Mathf.Max(0f, arrivalTriggerBlockSeconds)
                    && planarDist < Mathf.Max(0f, minDistanceFromArrivalToRetrigger))
                {
                    return;
                }
            }

            var networkClient = bridge != null ? bridge.GetComponent<AuthoritativeNetworkClient>() : null;
            if (networkClient == null)
                networkClient = FindFirstObjectByType<AuthoritativeNetworkClient>();
            if (networkClient != null && networkClient.IsAdminTeleportInProgress)
            {
                // Prevent nearby zone triggers from hijacking an in-flight admin teleport.
                return;
            }
            if (networkClient != null && networkClient.IsConnected)
            {
                _transitionInFlight = true;
                Debug.Log(
                    $"Zone transition request sent to AO.Server: {link.Id ?? "unknown"} " +
                    $"{link.FromPlayfieldId} -> {link.ToPlayfieldId}");
                _lastTransitionTime = Time.unscaledTime;
                networkClient.RequestZoneTransition(link.Id, link.FromPlayfieldId);
                return;
            }

            string detail = networkClient == null
                ? "no AuthoritativeNetworkClient found"
                : $"serverAvailable={networkClient.IsServerAvailable}, connected={networkClient.IsConnected}";
            AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(
                $"Zone blocked: server-authoritative zoning requires AO.Server connection ({link.Id ?? "unknown"}, {detail}).");
            _transitionInFlight = false;
        }

        internal void NotifyAuthoritativeTransitionComplete(Transform player)
        {
            _lastTransitionTime = Time.unscaledTime;
            _arrivalPlayfieldId = bootstrap != null ? bootstrap.ActivePlayfieldId : -1;
            _arrivalWorldPosition = player != null ? player.position : Vector3.zero;
            _transitionInFlight = false;
            RebuildPortalsForCurrentPlayfield();
        }

        internal void NotifyAuthoritativeTransitionFailed()
        {
            _transitionInFlight = false;
        }

        private void LoadLinks()
        {
            _links.Clear();

            string path = Path.Combine(Application.streamingAssetsPath, playfieldsSubfolder, zoneLinksFileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"Zone links file not found: {path}");
                return;
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<ZoneLinksFile>(File.ReadAllText(path));
                _links = parsed?.Links ?? new List<ZoneLinkRow>();
                Debug.Log($"Loaded zone links: {_links.Count} from {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed parsing zone links {path}: {ex.Message}");
            }
        }

        private static List<Transform> FindAnchors(Transform playfieldRoot, ZoneLinkRow link, PrototypeWorldBootstrap bootstrap)
        {
            var list = new List<Transform>();
            if (playfieldRoot == null || link == null)
                return list;

            string sourceNameRaw = string.IsNullOrWhiteSpace(link.SourceMeshName)
                ? null
                : Path.GetFileNameWithoutExtension(link.SourceMeshName).Trim();
            bool sourceLooksLikeFullObjectName =
                !string.IsNullOrWhiteSpace(sourceNameRaw)
                && sourceNameRaw.StartsWith("Statel_", StringComparison.OrdinalIgnoreCase);

            string meshKey = sourceLooksLikeFullObjectName ? null : sourceNameRaw;

            var all = playfieldRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null)
                    continue;

                string n = t.name;
                if (string.IsNullOrWhiteSpace(n) || !n.StartsWith("Statel_", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool statelMatch = link.SourceStatelId > 0 && TryGetStatelIdFromName(n, out int statelId) && statelId == link.SourceStatelId;

                bool fullNameMatch = sourceLooksLikeFullObjectName
                    && n.Equals(sourceNameRaw, StringComparison.OrdinalIgnoreCase);

                bool meshMatch = !string.IsNullOrWhiteSpace(meshKey)
                    && n.EndsWith($"_{meshKey}", StringComparison.OrdinalIgnoreCase);

                bool nameMatch = meshMatch || fullNameMatch;

                if ((link.SourceStatelId > 0 && !string.IsNullOrWhiteSpace(sourceNameRaw) && statelMatch && nameMatch)
                    || (link.SourceStatelId > 0 && string.IsNullOrWhiteSpace(sourceNameRaw) && statelMatch)
                    || (link.SourceStatelId <= 0 && !string.IsNullOrWhiteSpace(sourceNameRaw) && nameMatch))
                {
                    list.Add(t);
                }
            }

            if (bootstrap != null && link.SourceAOPosition != null && list.Count > 0)
            {
                Vector3 sourcePos = new Vector3(link.SourceAOPosition.X, link.SourceAOPosition.Y, link.SourceAOPosition.Z);
                float radius = Mathf.Max(0.1f, link.SourceAORadius);
                float r2 = radius * radius;
                var aoCandidates = new List<(Transform tr, float dist2)>();

                for (int i = 0; i < list.Count; i++)
                {
                    Transform tr = list[i];
                    Vector3 sourceWorld = GetAnchorWorldPosition(tr);
                    Vector3 sourceWorldAsAo = bootstrap.ConvertWorldToAo(sourceWorld);
                    var d = new Vector2(sourceWorldAsAo.x - sourcePos.x, sourceWorldAsAo.z - sourcePos.z);
                    float d2 = d.sqrMagnitude;
                    aoCandidates.Add((tr, d2));
                }

                aoCandidates.Sort((a, b) => a.dist2.CompareTo(b.dist2));

                var aoInRadius = new List<Transform>();
                for (int i = 0; i < aoCandidates.Count; i++)
                {
                    if (aoCandidates[i].dist2 <= r2)
                        aoInRadius.Add(aoCandidates[i].tr);
                }
                if (aoInRadius.Count > 0)
                    return aoInRadius;

                // Fallback: treat SourceAOPosition as world-space when users copy from hierarchy.
                var worldCandidates = new List<(Transform tr, float dist2)>();
                for (int i = 0; i < list.Count; i++)
                {
                    Transform tr = list[i];
                    Vector3 sourceWorld = GetAnchorWorldPosition(tr);
                    float d2 = (sourceWorld - sourcePos).sqrMagnitude;
                    worldCandidates.Add((tr, d2));
                }
                worldCandidates.Sort((a, b) => a.dist2.CompareTo(b.dist2));

                var worldInRadius = new List<Transform>();
                for (int i = 0; i < worldCandidates.Count; i++)
                {
                    if (worldCandidates[i].dist2 <= r2)
                        worldInRadius.Add(worldCandidates[i].tr);
                }

                if (worldInRadius.Count > 0)
                {
                    Debug.LogWarning(
                        $"Zone link '{link.Id ?? "unknown"}' SourceAOPosition matched in world-space fallback. " +
                        "Use AO-space coordinates from playfield JSON for deterministic matching.");
                    return worldInRadius;
                }

                // Final fallback: keep link functional by selecting nearest AO candidate even if outside radius.
                if (aoCandidates.Count > 0 && aoCandidates[0].tr != null)
                {
                    Debug.LogWarning(
                        $"Zone link '{link.Id ?? "unknown"}' found no anchors inside SourceAORadius={radius:F2}. " +
                        $"Using nearest AO anchor at distance={Mathf.Sqrt(aoCandidates[0].dist2):F2}. Increase SourceAORadius.");
                    return aoCandidates.ConvertAll(x => x.tr);
                }

                return list;
            }

            return list;
        }

        private static Vector3 GetAnchorWorldPosition(Transform anchor)
        {
            if (anchor == null)
                return Vector3.zero;

            var renderers = anchor.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return anchor.position;

            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);

            return b.center;
        }

        private static bool TryGetStatelIdFromName(string objectName, out int statelId)
        {
            statelId = 0;
            if (string.IsNullOrWhiteSpace(objectName) || !objectName.StartsWith("Statel_", StringComparison.OrdinalIgnoreCase))
                return false;

            string tail = objectName.Substring("Statel_".Length);
            int cut = tail.IndexOf('_');
            string idText = cut >= 0 ? tail.Substring(0, cut) : tail;
            return int.TryParse(idText, out statelId);
        }
    }
}
