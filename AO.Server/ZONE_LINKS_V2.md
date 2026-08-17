# Zone Links V2 (Server-Authoritative)

This format supports:
- stable `id` values per portal link
- route-aware instancing (`destinationTemplatePlayfieldId`, `destinationRouteKey`)
- return-context behavior for indoor exits (`useReturnContext`)
- fallback destination when explicit routing is not available
- source trigger matching (`sourceStatelId`, `sourceMeshName`, `sourceAOPosition`, `sourceAORadius`, `triggerRadius`)

## Resolution Order
1. Explicit portal destination (`toPlayfieldId`)
2. Return context (`useReturnContext` + stored ticket)
3. Fallback (`fallbackToPlayfieldId`)

## Example
```json
{
  "links": [
    {
      "id": "bor_to_icc_subway",
      "fromPlayfieldId": 655,
      "toPlayfieldId": 127,
      "sourceStatelId": 297642,
      "sourceMeshName": "subway_entrance_icc",
      "sourceAOPosition": { "x": 3303.679, "y": 35.188, "z": 838.277 },
      "sourceAORadius": 8.0,
      "triggerRadius": 5.0,
      "destinationTemplatePlayfieldId": 127,
      "destinationRouteKey": "subway_from_655",
      "maxPlayersPerShard": 20,
      "trackReturnContext": true,
      "useReturnContext": false,
      "targetAOSpawn": { "x": 68.0, "y": 115.6, "z": 319.0 },
      "targetYaw": 180.0
    },
    {
      "id": "icc_to_subway",
      "fromPlayfieldId": 800,
      "toPlayfieldId": 127,
      "sourceStatelId": 297460,
      "sourceMeshName": "whompa_entrance_default",
      "sourceAOPosition": { "x": 678.6595, "y": 73.12207, "z": 546.6343 },
      "sourceAORadius": 8.0,
      "triggerRadius": 5.0,
      "destinationTemplatePlayfieldId": 127,
      "destinationRouteKey": "subway_from_800",
      "maxPlayersPerShard": 20,
      "trackReturnContext": true,
      "useReturnContext": false,
      "targetAOSpawn": { "x": 68.0, "y": 115.6, "z": 319.0 },
      "targetYaw": 180.0
    },
    {
      "id": "subway_exit_return",
      "fromPlayfieldId": 127,
      "toPlayfieldId": 0,
      "sourceStatelId": 123456,
      "sourceMeshName": "subway_exit_door",
      "sourceAOPosition": { "x": 70.0, "y": 115.6, "z": 301.0 },
      "sourceAORadius": 6.0,
      "triggerRadius": 4.0,
      "trackReturnContext": false,
      "useReturnContext": true,
      "fallbackToPlayfieldId": 800,
      "fallbackDestinationTemplatePlayfieldId": 800,
      "fallbackDestinationRouteKey": "default",
      "fallbackTargetAOSpawn": { "x": 643.0, "y": 21.0, "z": 424.0 },
      "fallbackTargetYaw": 0.0
    }
  ]
}


Outdoor PF Examples
{
  "Id": "greater_tir_county_to_icc",
  "FromPlayfieldId": 647,
  "ToPlayfieldId": 655,
  "SourceStatelId": 300818,
  "SourceMeshName": "icc_transport_shuttle_no_doors",
  "SourceAOPosition": { "X": 1813.944, "Y": 22.12, "Z": 2641.38 },
  "SourceAORadius": 16.0,
  "TriggerRadius": 5,
  "TargetAOSpawn": { "X": 3138, "Y": 52.70, "Z": 865.55 },
  "TargetYaw": 0
}

Entering Indoor PF Examples
{
  "Id": "icc_to_subway",
  "FromPlayfieldId": 655,
  "ToPlayfieldId": 127,
  "SourceStatelId": 297642,
  "SourceMeshName": "subway_entrance_icc",
  "SourceAOPosition": { "X": 3303.679, "Y": 35.188, "Z": 838.277 },
  "SourceAORadius": 8,
  "TriggerRadius": 5,
  "destinationTemplatePlayfieldId": 127,
  "destinationRouteKey": "subway_from_655",
  "maxPlayersPerShard": 20,
  "trackReturnContext": true,
  "useReturnContext": false,
  "TargetAOSpawn": { "X": 68, "Y": 115.6, "Z": 319 },
  "TargetYaw": 90
}

Exiting Indoor PF Examples
{
  "Id": "subway_exit_to_icc",
  "FromPlayfieldId": 127,
  "ToPlayfieldId": 655,
  "SourceStatelId": 28485,
  "SourceMeshName": "door_28485_forcefield",
  "SourceAOPosition": { "X": 64.93, "Y": 115.69, "Z": 319.03 },
  "SourceAORadius": 4.0,
  "TriggerRadius": 3.5,
  "trackReturnContext": false,
  "useReturnContext": true,
  "fallbackToPlayfieldId": 655,
  "fallbackDestinationTemplatePlayfieldId": 655,
  "fallbackDestinationRouteKey": "default",
  "fallbackTargetAOSpawn": { "X": 3296.627, "Y": 351.189, "Z": 842.21 },
  "fallbackTargetYaw": 180.0
},
```
