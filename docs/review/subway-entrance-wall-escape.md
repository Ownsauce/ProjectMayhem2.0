# Subway entrance wall escape — October 2, 2026

The live ZoneEngine_New journal shows repeated `Rejected CharDCMove through
collision` for character 37 in playfield 900001 during the reported entrance
snag. This is a server rejection loop, not proof of a client GLB collider snag.

The current `PlayfieldWorldSimulation.AddQuad` emits both triangle windings for
every face of a closed box. `CharacterMotor.IsClientMoveClear` probes at torso
height and rejects a reported position if any offset segment intersects a surface.
A probe starting inside a wall therefore also hits the reversed face on escape.
The previous Unity capsule-only regression did not exercise this server gate.

A focused diagnostic using the installed N3Lite 0.1.3 assembly and the server's
box vertices/winding reproduced the behavior:

| Box faces | Enter from front | Enter from back | Escape from inside | Escape from skin overlap |
| --- | --- | --- | --- | --- |
| Current, both windings | blocked | blocked | blocked | blocked |
| Original winding only | blocked | blocked | allowed | allowed |

The prepared patch is `tools/patches/subway-wall-escape.patch`. It removes only
reversed triangles from the procedural box bake. Each closed box retains all six
exterior faces. The change applies to shared procedural structural and door box
construction; it does not remove solid walls, unlock doors, or alter layout data.

The user explicitly authorized applying the fix directly to the local server,
without cloud deployment or Git pushes. The patch is applied to the local
AORebirth source. `LinuxBuild/publish-zoneengine-new.sh` built and published the
local artifact, and startup validation passed. Deployment is a separate step;
admin authentication is required to install it and restart the local service.

## Unity asset refresh recovery

The user's live Unity editor is Hub-launched with process name `Unity Main Thre`,
not `Unity`. Earlier batch verification misidentified it as absent and opened the
same project concurrently. The live editor log then recorded Unity Data Store
read-handle errors. This invalidates the assumption that a successful separate
batch import established valid live-editor asset references.

After the user saved and closed Unity, the Subway importer was rerun alone with
forced GLB/kit imports. All eight roles resolved and the saved seed-123 preview
placed 2,053 authored GLB objects. Runtime code now validates kit mesh references
and logs either the verified role count or a concrete missing-kit error. On entry,
presentation completion logs the actual GLB placement count. The importer refuses
to rewrite assets while Play mode is active.

Reopen the Unity project after the repair batch has exited. Expected Console lines:

```text
[WorldGen] Subway GLB kit loaded: 8 verified roles.
[WorldGen] Subway presentation ready: GLB pieces=<positive count>, kit=loaded
```

The existing live dungeon cannot be called fixed until the server collision
change has been deployed and the user has repeated the live approach/retreat.

## Local installation

The reviewed deployment command is:

```bash
bash /tmp/deploy-local-subway-zone.sh
```

It uses the existing immutable-release/current-symlink installation model,
preserves the existing live Config.xml, validates the replacement, restarts only
ZoneEngine_New, and restores the prior release if startup fails. It records the
local uncommitted change explicitly. No Git push, cloud deployment, or schema
change is part of this task. The agent's first install attempt stopped before any
service/release changes because sudo required terminal authentication; the user
was asked to run the script in their terminal.

## Deployment result

The user ran the installation script locally. The active release is
`/opt/ao-rebirth/zoneengine-new/releases/local-subway-wall-escape-20261002T214411Z`.
The zone service restarted at 15:44:14 MDT with PID 58512, startup validation
passed, schema preflight reported no writes, and the listener reached
`ZONEENGINE_NEW_READY` on 127.0.0.1:7501. The published production assembly's
actual private box/mesh builder was inspected in isolation: it retains 12 exterior
triangles, rejects entry/crossing, and permits inside/skin-overlap retreat.
No cloud deployment or Git push occurred.

The restart clears in-memory procedural instances. Recreate `subway-glb-test`
with the previous seed/room/profile/theme parameters before entering it.
The player's live approach/retreat remains the final acceptance check.
