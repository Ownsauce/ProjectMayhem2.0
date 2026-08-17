# ProjectMayhem AI Reference Index

This folder is the fast-start map for future AI collaborators.

Use these files first:

- `docs/ProjectMayhem.Architecture.md`: cross-project boundaries and ownership.
- `docs/AO.Core.md`: shared gameplay rules and runtime domain models.
- `docs/AO.Unity.md`: Unity client, world bootstrap, UI, presentation, and input.
- `docs/AO.Server.md`: authoritative server responsibilities, data ownership, and validation flow.
- `docs/AO.Tools.md`: small utility and experimentation project.
- `docs/AO.Client.md`: current placeholder client project status.

Current direction:

- `AO.Core` should remain the reusable gameplay/domain layer.
- `AO.Server` should become the authoritative runtime and own validation, simulation, and persistence decisions.
- `AO.Unity` should act as a client that sends intents and renders results.
- Data files currently live under `AO.Unity/Assets/StreamingAssets/AOData`, but the server must treat them as authoritative gameplay data.

When changing behavior:

- Prefer putting rules in `AO.Core` if both server and client may need them.
- Prefer putting orchestration, authority, connection/session state, and persistence in `AO.Server`.
- Keep `AO.Unity` focused on UI, input, scene setup, visuals, and local prediction if added later.
