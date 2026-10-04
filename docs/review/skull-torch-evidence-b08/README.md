# Skull-torch evidence review b08

These overlays show the **proposed** source-image part masks and the manually located plaque
landmarks. Red is the outer plaque, green the inner plaque, blue the skull, and yellow the shaft.
The landmark images mark the source-photo coordinates used for camera diagnostics.

The reproducible review recipes are in the skull-torch authoring workspace `evidence/` directory:
`side-mask-review-b04.json`, `plaque-corner-mask-review-b05.json`,
`plaque-corner-landmark-review-b05.json`, `side-edge-landmark-review-b06.json`, and
`model-landmark-map-b05.json`. The original photos are unchanged.

The side mask was corrected at the visible plaque slab and rear skull patch. The three-quarter
major-part outlines were visually inspected, but their boundaries remain approximate. The
elevated-top plaque apex and right corner were claimed as outer-plaque pixels; its other part
boundaries remain approximate. Known object background is now also known negative for each part.
No part mask has been promoted to pixel-verified status.

With the c07 camera, the new model reprojection errors are **125.2 px** at the side plaque apex,
**146.5 px** at the three-quarter plaque apex, and **119.9 px** at the elevated-top right corner.
The side edge midpoints have confidence 0.45 because plaque thickness makes the exact depth
coordinate uncertain; elevated-top corners have confidence 0.55. These errors identify geometry
and camera work still needed. The candidate is not production-ready.
