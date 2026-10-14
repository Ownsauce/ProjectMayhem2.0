using System;
using System.Reflection;
using AODB.Common.RDBObjects;
using AO.Unity.Prototype;
using AO.Unity.World;
using AO.Client.Backends.AORebirth;
using AO.Client.World;
using System.Collections.Generic;
using UnityEngine;

namespace AO.Unity.Editor
{
    /// <summary>Installed-data regression for motor/attack playback ownership; runnable in batch mode.</summary>
    public static class CombatAnimationVerifier
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            string installation = Environment.GetEnvironmentVariable("PROJECTMAYHEM_AO_INSTALL");
            if (string.IsNullOrWhiteSpace(installation)) throw new InvalidOperationException("Set PROJECTMAYHEM_AO_INSTALL.");
            var activeProperty = typeof(PrototypeUiContext).GetProperty("Active", BindingFlags.Static | BindingFlags.Public);
            var previous = PrototypeUiContext.Active;
            using var database = new ResourceDatabase();
            database.Initialize(installation);
            GameObject actor = null;
            try
            {
                var context = new PrototypeUiContext(1, 6);
                typeof(PrototypeUiContext).GetField("_hasNativeInventory", Private).SetValue(context, true);
                VerifyDeadTargetAndFairTrade(database, context);
                foreach (string profile in new[] { "male", "female", "athrox" })
                {
                    int meshId = profile == "female" ? 5934 : profile == "male" ? 5914 : 5900;
                    var mesh = database.Get<RDBCatMesh>(ResourceTypeId.CatMesh, meshId);
                    actor = new GameObject("Combat animation verification");
                    actor.SetActive(false); // Keep scene/bootstrap lifecycle out of this focused check.
                    var appearance = actor.AddComponent<CharacterAppearanceController>();
                    var visual = new GameObject("CAT visual"); visual.transform.SetParent(actor.transform, false);
                    var bones = new Transform[mesh.Joints.Count];
                    for (int i = 0; i < bones.Length; i++)
                    {
                        bones[i] = new GameObject("Bone " + i).transform;
                        bones[i].SetParent(visual.transform, false);
                    }
                    var player = visual.AddComponent<CatAnimPlayer>();
                    player.Initialize(database, bones, 0, animationProfile: profile);
                    typeof(CharacterAppearanceController).GetField("_spawnedPrefabVisual", Private).SetValue(appearance, visual);
                    typeof(CharacterAppearanceController).GetField("attackToggleActive", Private).SetValue(appearance, true);
                    foreach (int set in new[] { 1, 2, 3, 4, 5 })
                    {
                        player.SetAnimSet(set);
                        Require(player.Play("attack-idle", 0), "combat idle resolves");
                        player.SetTime(player.Duration * 0.25f);
                        float idleTime = player.PlaybackTime;
                        for (int frame = 0; frame < 5; frame++) appearance.SetLocalLocomotionIntent(0, 0);
                        Require(player.CurrentLogicalName == "attack-idle" && player.PlaybackTime == idleTime,
                            "motor must preserve combat idle instead of restarting alternating idle clips");
                        foreach (string action in new[] { "attack-start", "attack-hit", "attack-hit-left" })
                        {
                            Require(player.PlayOnce(action, 0, null), $"{profile}/{set}/{action} resolves");
                            float sample = player.CurrentClip.GetOneShotDuration() * 0.25f;
                            Require(sample > 0, "one-shot has a playable duration");
                            player.SetTime(sample);
                            appearance.SetLocalLocomotionIntent(0, 0);
                            appearance.SetLocalLocomotionIntent(1, 0);
                            appearance.SetLocalLocomotionIntent(0, -1);
                            Require(player.IsPlayingOneShot && player.CurrentLogicalName == action && player.PlaybackTime == sample,
                                "motor samples must not cancel, replace or rewind an active attack one-shot");
                            player.CancelOneShot();
                            appearance.SetLocalLocomotionIntent(1, 0);
                            Require(player.CurrentLogicalName == "run", "locomotion resumes after a completed strike");
                        }
                    }
                    UnityEngine.Object.DestroyImmediate(actor); actor = null;
                }
                Debug.Log("[CombatAnimationVerifier] PASS male/female/athrox: five weapon stances, attack start and both hands survive motor input; locomotion resumes.");
            }
            finally
            {
                if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
                activeProperty.SetValue(null, previous);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void VerifyDeadTargetAndFairTrade(ResourceDatabase database, PrototypeUiContext context)
        {
            var host = new GameObject("Gameplay interaction verification"); host.SetActive(false);
            GameObject targetRoot = null;
            using var backend = new AORebirthBackend();
            try
            {
                var session = host.AddComponent<AOGameServerSession>();
                typeof(AOGameServerSession).GetField("_backend", Private).SetValue(session, backend);
                typeof(AOGameServerSession).GetField("_lifetime", Private).SetValue(session, new System.Threading.CancellationTokenSource());
                typeof(AORebirthBackend).GetField("<State>k__BackingField", Private).SetValue(backend, AO.Client.ClientConnectionState.InWorld);
                var objects = (Dictionary<string, WorldObject>)typeof(AORebirthBackend).GetField("_worldObjects", Private).GetValue(backend);
                var placements = AO.Assets.Decoders.AOPlacedDynelDecoder.Decode(database.GetRaw(1000026, 1186));
                int shops = 0;
                foreach (var placement in placements)
                {
                    if (placement.IdentityType != 51035) continue;
                    var template = database.Get<ItemObject>(placement.TemplateId);
                    targetRoot = new GameObject("Native terminal");
                    targetRoot.transform.position = new Vector3(shops * 100, 0, 0);
                    var placed = targetRoot.AddComponent<NativePlacedDynel>();
                    placed.IdentityType = placement.IdentityType; placed.IdentityInstance = placement.IdentityInstance;
                    placed.TemplateId = placement.TemplateId; placed.DisplayName = template.Name;
                    placed.AoPosition = new Vector3(placement.X, placement.Y, placement.Z);
                    var submesh = new GameObject("Terminal submesh"); submesh.transform.SetParent(targetRoot.transform, false);
                    submesh.AddComponent<BoxCollider>();
                    Physics.SyncTransforms();
                    var ray = new Ray(targetRoot.transform.position + Vector3.back * 5, Vector3.forward);
                    Require(session.TryResolveVendorFromRay(ray, out var clicked, out var visual)
                        && visual == targetRoot.transform && clicked.Name == template.Name
                        && clicked.IdentityInstance == placement.IdentityInstance,
                        $"Fair Trade child collider resolves selectable terminal identity and shop name (index={shops}, placement={placement.IdentityType}:{placement.IdentityInstance}, hits={Physics.RaycastAll(ray).Length}, selected={clicked?.Name})");
                    int runtimeId = 900000 + shops;
                    objects.Clear();
                    objects["51035:" + runtimeId] = new WorldObject(51035, runtimeId, WorldObjectKind.VendingMachine,
                        template.Name, 1186, true, placement.X, placement.Y, placement.Z);
                    var resolved = session.ResolveVendorAsync(ray, null).GetAwaiter().GetResult();
                    Require(resolved.IdentityInstance == runtimeId && resolved.IdentityInstance != placement.IdentityInstance,
                        "shop opening binds rendered placement to authoritative runtime vendor identity");
                    var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    blocker.transform.position = targetRoot.transform.position + Vector3.back * 2;
                    Physics.SyncTransforms();
                    Require(!session.TryResolveVendorFromRay(ray, out _, out _), "terminal selection does not pass through a wall");
                    UnityEngine.Object.DestroyImmediate(blocker);
                    UnityEngine.Object.DestroyImmediate(targetRoot); targetRoot = null;
                    shops++;
                }
                Require(shops == 63, "all 63 native Fair Trade shops checked");
                targetRoot = new GameObject("Dead target");
                var target = targetRoot.AddComponent<CharacterRuntimeBridge>();
                target.SetServerEntity(new NearbyEntity(50000, 99, NearbyEntityKind.Npc, "Dead target", 1, 0, 0, 127, 0, 0, 0).WithHealth(0, 0));
                typeof(PrototypeUiContext).GetField("_selectedTarget", Private).SetValue(context, target);
                var appearance = host.AddComponent<CharacterAppearanceController>();
                typeof(CharacterAppearanceController).GetMethod("EnsureAttackCycleUi", Private).Invoke(appearance, null);
                var canvas = (Canvas)typeof(CharacterAppearanceController).GetField("_attackCycleCanvas", Private).GetValue(appearance);
                typeof(CharacterAppearanceController).GetField("attackToggleActive", Private).SetValue(appearance, true);
                typeof(CharacterAppearanceController).GetField("_restoreAttackPoseAfterJump", Private).SetValue(appearance, true);
                typeof(CharacterAppearanceController).GetMethod("UpdateServerAttackProgress", Private).Invoke(appearance, null);
                Require(!appearance.IsAttackToggleActive && !canvas.gameObject.activeSelf,
                    "zero HP hides attack/recharge bar in the same update without waiting for StopFight");
                Require(!(bool)typeof(CharacterAppearanceController).GetField("_restoreAttackPoseAfterJump", Private).GetValue(appearance),
                    "a landing transition cannot restore combat after the target dies");
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
                typeof(PrototypeUiContext).GetField("_selectedTarget", Private).SetValue(context, null);
                Debug.Log("[GameplayInteractionVerifier] PASS all 63 Fair Trade terminal identities/names, child-collider picking, runtime shop rebinding, wall occlusion, immediate zero-HP combat bar shutdown.");
            }
            finally
            {
                if (targetRoot != null) UnityEngine.Object.DestroyImmediate(targetRoot);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
