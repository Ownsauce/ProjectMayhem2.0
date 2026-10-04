# Subway safety strip authoring instructions

Create a modular yellow tactile warning strip for the TOP surface of a subway platform.
Use a straight rectangular module, 1 metre long, 0.35 metres wide, and 0.025 metres thick.
Use Y-up, length along X, and width along Z. Put the pivot at the centre of the bottom face.

The top has evenly spaced raised tactile studs and a worn yellow finish. Keep all studs
inside the rectangular footprint. Wear is in the texture: edges stay straight and intact.
The bottom is flat. Both lengthwise ends are flat, square, and exactly parallel, so copies
meet end-to-end with no gap, overlap, visible end bevel, or repeating dark end-cap seam.
Make the texture repeat seamlessly along the length. Keep the width and thickness constant.
Use a closed mesh with outward-facing normals and UVs. Export a game-ready GLB with embedded
PBR textures. Include no platform wall, stair, rail, background, large base, broken corners,
ragged silhouette, or holes. Show three copies touching end-to-end in the review image.

The runtime supplies the retaining wall separately. The strip is interrupted deliberately
at stair openings; it must not cross the walkable opening. Asset changes should replace
`WorldGen/Assets/subway/architecture/saftey-strip/game.glb`, preserving the existing folder
spelling, followed by Tools > WorldGen > Refresh Subway Kit with Play mode stopped.

A text/image generation prompt cannot guarantee exact dimensions or watertight joins.
Check those in the exported mesh before replacing the asset.
