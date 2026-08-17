# AO.Tools Reference

## Purpose

`AO.Tools` is a small utility project for experiments, diagnostics, and offline data checks.

## Current Status

- The project references `AO.Core`.
- `Program.cs` still reflects an older flow and calls `ItemLoader.LoadFromJson`, which now throws because loading was moved out of `AO.Core`.

## Recommended Use

- Use this project for offline validation, exports, migrations, sanity checks, and developer tooling.
- Do not treat it as runtime authority.

## AI Guidance

- If you need a one-off console tool for inspecting AO data or validating server snapshots, this is a good place.
- Keep production game authority in `AO.Server`, not here.
