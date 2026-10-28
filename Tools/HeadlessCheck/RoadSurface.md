# Road surfaces

Highways, arterials and local streets use asphalt. DirtAccess uses dry gravel through the existing Road_Dirt terrain layer. Road_Asphalt.terrainlayer is assigned to WorldGenerationConfig.AsphaltLayer. Road_Asphalt.mat is assigned to WorldBuildSettings.RoadMaterial.

## The asphalt mesh

RoadSurfaceMesh clips the actual voxel terrain triangles to the asphalt corridors. New vertices interpolate the original triangle plane and normal, so the surface lies exactly on whatever terrain LOD it is built for.

**Chains.** Asphalt roads whose ends meet (within 0.5 m, no third end there, a turn under 60°) are joined into one chain: a highway into the arterial past its gateway, an arterial into the local street that continues it. 76 such joints exist on the baked world; every one used to read as a junction and drew a patch on a straight road. Where the widths differ (6 of them, arterial 4 m into street 3 m) the half-width tapers linearly over 10 m per metre of difference, centred on the joint. The marking style changes at the joint itself.

**Mitred joints.** Inside a chain, neighbouring segments share their joint along the angle bisector instead of overlapping with round caps. On the bisector the distance to both segment lines is the same, so the lateral coordinate `u` is continuous; the along coordinate is `v = Along + t * Length` with `t = start / (start + end)`, the distances to the two mitre lines, so `v` is continuous too. With round caps every 12 m highway joint stepped the wheel tracks, the gravel edge and the paint sideways by up to 0.3 m and notched the outer edge by 12 cm. A joint turning more than 40°, or a segment whose mitred outline would not be convex, falls back to round caps (16-sided) on that joint. `t` is not linear across a mitred segment, so a piece is also split while `v` at any fan-triangle edge midpoint or centroid is more than 2 cm off the interpolation.

**Ownership.** A point belongs to the lowest-numbered segment whose outline holds it. Chains are numbered by rank (highway, arterial, street), and the tail of a chain that ends on another road (twice both half-widths plus a metre, so oblique joins are covered) is numbered after everything else. A per-chain order cannot do this: on the baked world a highway ends on a chain that, through a ring of streets, ends back on it. An earlier outline is subtracted only where a separating-axis test says the two convex polygons overlap; cutting a piece by the edge lines of an outline it does not touch multiplied fragments without bound and hung a full generate for half an hour. Mitred neighbours are disjoint by construction and are never subtracted. Bridge footprints are removed the same way.

**Marks.** UV2 carries three weights per vertex. `x` is the junction weight, 0 within 2 m of another chain's carriageway and back to 1 at 10 m; it removes the centre line. `y` and `z` are the left and right edge weights: a probe 1 m outside that edge, 0 when it is half a metre inside another carriageway and 1 a metre clear of it. Only the side facing a joining road loses its gravel, crumble and edge line; the far side of a tee keeps them. A segment of its own chain counts as another road only more than 40 m away along the chain. Each segment keeps the list of foreign segments that can reach it, so a segment with none skips the weights, and a piece is halved while any weight at a fan-triangle edge midpoint or centroid is more than 0.05 off the interpolation, down to 0.5 m, so marking suppression does not depend on terrain LOD. `Build` keeps no shared scratch, so the preview builder clips a whole batch of chunks in parallel. `Sample(point)` evaluates the same surface analytically; the checks compare the mesh against it.

**Fillets.** Every corner between two roads is rounded by an arc tangent to both carriageway edges (`RoadSurfaceMesh.Junctions` finds the corners, `RoadSurfaceMesh.Fillets` paves them). A junction is a chain end within `TEE_SLACK` of another chain's axis, or two chains crossing away from both ends; points within 2 m merge into one. Each road through a junction gives two arms, each road ending there one, with the direction of the chord 8 m out; arms are sorted by angle and every neighbouring pair turning 20°–160° gets a fillet. The radius is 1.5 times the narrower half-width, kept to 0.6 of the mean half-width of depth from the corner, and short enough to leave 0.5 m of each arm past the arc; under 0.5 m there is no fillet. A fillet is a fan of triangles from the corner to the arc in 10° steps, numbered after every road segment, so it only ever paves the ground the roads left. Its frame runs `u` from 1 on the arc inward and `v` along the arc, so the arc carries the strip's gravel band and the inside is clean asphalt. Road segments near a fillet list it, and their edge weights fall to zero where the arc takes over, so the old corner edges fade into the arc instead of crossing it. The baked network has 765 fillets; they move the far-ring split counts slightly and cost nothing measurable.

The runtime streamer and preview builder create the surface beside every terrain chunk and release it with GeneratedMesh. Collision remains the terrain collision. An unassigned RoadMaterial keeps terrain-layer rendering available.

## The shader

RoadAsphalt.shader draws white edge lines on highways and arterials, a dashed yellow center on highways, a solid yellow center on arterials and no paint on local streets. UV0 stores lateral fraction and longitudinal metres; UV1 stores half-width and style; UV2 the three marks above.

The strip texture tiles along the road on its own (its first and last rows differ by 8.0 in luma against 7.1 between neighbouring rows), so albedo, normal and mask are sampled once. The previous shader cross-faded a second copy shifted by half a tile with a cosine weight, which blurred cracks and stones every 4 m and made a long road look banded.

There is no separate junction texture any more. Where an edge weight drops, the strip's gravel band (`|u|` past 0.78) is replaced by asphalt from the strip's own clean centre (`|u|` under 0.2, shifted by `Fill Source Shift` and offset a third of a tile along), cross-faded between `Fill Start` and `Fill End`; wheel tracks stay where they are. A fillet (style 0) inverts it: its gravel band stays on the arc and everything inside `Fill Start` is clean centre asphalt, shifted by `Fillet Source Shift`. A world-projected junction texture of a different stone and brightness used to cover every junction and every end-to-end joint.

World noise varies tint and wears paint. Edge crumble is an alpha clip against noise that fades out with the paint detail distance, so a far road keeps a stable edge instead of shimmering. All three passes use the same clipping and depth bias. The shader receives shadows and emits no shadow caster pass.

The strip mask stores cracks in R, repairs in G and dust in B. Its alpha is opaque; edge solidity is reduced procedurally. Color maps use sRGB; normal and mask use linear import. The terrain layer uses the project's flat normal. The strip uses its own OpenGL normal. Source PNGs are 1254 square; terrain imports are capped at 1024, strip imports retain their original dimensions. `Road_Asphalt.png` remains the splat layer under and around the mesh.

## Applying in Unity

1. Allow the changed scripts and shader to import.
2. Rebuild the world preview (or enter play mode): the road surface is built at load from `RoadNetwork.asset`, so no regeneration or rebake is needed.
3. `Road_Asphalt.mat` keeps two stale entries (`_JunctionMap`, `_JunctionTileSize`) that the shader no longer declares; they are ignored.
4. Check the shader, visual seams, distant depth and GPU Resident Drawer in Unity. Offline checks do not exercise the renderer or asset importer.

## Offline checks

`dotnet run --project Tools/HeadlessCheck/HeadlessCheck.csproj -c Release -- --road-paint` checks material fallbacks, road weights, crossing union, terrain coplanarity, bends, bridge holes and four terrain resolutions, then:

- a 120° highway curve of 6 m segments: the surface stays within 1% of a half-width of the true corridor (no notches at joints) and neither `u` nor `v` jumps between samples 2 cm apart;
- an arterial continuing into a local street: no junction marks, 4 m to 3 m over 10 m, the style switching at the joint, `uv` continuous across it;
- a tee whose joining road is listed first: the through road owns the mouth, only the facing edge is suppressed, both corners are filleted, the fillet stops at its arc, the edges hand over to it and are kept past it, and the fillet's `uv` stays continuous and between its gravel band and the clean centre;
- the crossing and the bridge span measure exactly the corridor union plus four fillets;
- 24 dense street grids crossed by winding highways at three terrain resolutions against `Sample`: exactly one layer inside and none outside, the pavement within the corridor union or a fillet, `uv` and a 99th-percentile marking error under 0.06, and a minute in total, so a clipper that starts fragmenting again fails here.

`--preview-cost` times the shoreline refinement, meshing and the road surface over the real baked world (1.2 s and 514 thousand road triangles single-threaded). `--road-view [dir]` finds a sharp highway curve, a width taper, a gateway, a highway tee, a street tee and a crossing on the baked network, prints their coordinates and draws each top-down at 1 m and 8 m terrain cells through the same shading rules as the shader.

`--road-shader --net` compiles 24 Direct3D stages against installed URP package headers using Windows D3DCompiler. The legacy compiler adapter rewrites empty macro arguments and uses the DOTS default reflection-probe HDR value; per-instance reflection-probe HDR and Unity's newer preprocessor still require engine validation. Package files are read without modification.

The splat and ground-appearance checks verify terrain paint. UnityCompileCheck and UnityTestCompileCheck type-check production code against installed Unity assemblies without launching the editor.

## Texture generation

The textures were made with the built-in imagegen tool. The exact generation prompts are in RoadTexturePrompts.md.
