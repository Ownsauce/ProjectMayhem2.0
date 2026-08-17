# AO.Client Reference

## Purpose

`AO.Client` appears reserved for a future client-side assembly outside the Unity project.

## Current Status

- It is effectively a placeholder.
- `AO.Client/Class1.cs` is empty.
- It currently should not be treated as the main runtime client.

## Recommended Direction

- Either repurpose this for shared client networking/domain glue later, or retire it if Unity remains the only client.

## AI Guidance

- Do not assume `AO.Client` contains active gameplay flow.
- Prefer `AO.Unity` for current client behavior and `AO.Server` for authority.
