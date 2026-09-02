# ProjectMayhem AI Reference Index

This folder is the fast-start map for future AI collaborators.

Use these files first:

- `docs/Current-Progress-and-Next-Steps.md`: verified implementation status,
  limitations, engine guidance, and recommended build order.
- `docs/ProjectMayhem.Architecture.md`: cross-project boundaries and ownership.
- `docs/AO.Core.md`: shared gameplay rules and runtime domain models.
- `docs/AO.Unity.md`: Unity client, world bootstrap, UI, presentation, and input.
- `docs/AO.Server.md`: authoritative server responsibilities, data ownership, and validation flow.
- `docs/AO.Tools.md`: small utility and experimentation project.
- `docs/AO.Client.md`: backend-neutral client contracts and migration status.
- `DATA_SETUP.md`: clean-clone Unity setup and AO installation configuration.
- `docs/AO.AssetResolution.md`: direct-AODB resource flow and cache boundaries.

Current direction:

- `AO.Core` should remain the reusable gameplay/domain layer.
- `AO.Client` should expose a stable client-domain API over separate adapters for the live service, Ithaca, AORebirth, and other supported backends.
- `AO.Server` is the current Project Mayhem authority implementation and should remain useful as a mock, test harness, or optional compatible backend.
- `AO.Unity` should send intents through `AO.Client` and render server results without parsing backend packets directly.
- Presentation resources are read from the configured AO database; optional
  development/configuration files may live under
  `AO.Unity/Assets/StreamingAssets/AOData`. The connected server remains
  authoritative for live gameplay state.

When changing behavior:

- Prefer putting rules in `AO.Core` if both server and client may need them.
- Put backend-neutral connection/session state in `AO.Client` and backend wire details in isolated adapters.
- Put authority and persistence in `AO.Server` only for behavior owned by the Project Mayhem server implementation.
- Keep `AO.Unity` focused on UI, input, scene setup, visuals, and local prediction if added later.
