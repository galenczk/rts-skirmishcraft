# Movement Baseline Experiment

## Purpose

Observe how the current placeholder Godot navigation and movement implementation behaves as the number of moving friendly units increases. This is baseline instrumentation, not an optimization pass and not a decision about final movement or formation design.

Do not treat results from one machine as generally representative. Record the Godot version, build configuration, hardware, display resolution, and any other conditions that could affect comparisons.

## Controls

- `WASD` or arrow keys: pan the camera.
- Mouse wheel: zoom.
- Left click: select one friendly unit.
- Left-click drag: box-select friendly units.
- Right click on ground: issue a move order to selected friendly units.
- Right click an enemy: issue an attack order to selected combat-capable units.
- In `F6`, right click a gold Materials node: selected workers gather, deliver to the nearest friendly drop-off building, and repeat.
- In `F6`, right click a friendly drop-off while workers are carrying: deliver the current load there once, then become idle.
- `F1`: restore the default 8-friendly/8-enemy scene layout.
- `F2`: respawn 20 friendly units.
- `F3`: respawn 100 friendly units.
- `F4`: respawn 250 friendly units.
- `F5`: load the dedicated 500-friendly-unit formation stress battlefield. Its
  960 x 720 playable area, faster test-camera pan, wider test zoom, and sparse
  graybox obstacles are isolated from the normal skirmish battlefield.
- `F6`: load the mixed-role scenario with eight combat units and four workers per team, resetting blue to 1,000 deposited Materials.
- `F7`: reset the complete MVP skirmish used at project startup. Red workers gather, construct one production building, produce four-unit waves, and attack autonomously.
- `B`: enter production-building placement mode.
- Building placement requires a selected blue worker and 30 deposited Materials. A confirmed site assigns the closest selected worker.
- Left click while placing: confirm a valid green placement.
- Right click or `Escape` while placing: cancel without issuing a command.
- Right click an incomplete friendly building with workers selected: assign the closest selected worker to construct or resume it.
- `Delete`: cancel a selected incomplete friendly building and refund 75% of its cost.
- With a completed production building selected, `U` queues the existing combat unit for 15 Materials (three seconds, maximum queue length five).
- With a completed production building selected, `X` cancels the newest queued unit for a full refund.
- With a completed production building selected, right click valid ground to set or change its rally point.

The complete MVP skirmish loads automatically when the project starts; `F7` resets it during an active match. Changing a scenario clears selection and replaces both teams. `F1` through `F4` retain the normal battlefield and existing combat-unit-only layouts. `F5` remains combat-unit-only but temporarily replaces the ground, navigation bounds, camera limits, and test obstacles; switching to any other preset restores the normal battlefield. At movement speed 4, its 1,200-unit corner-to-corner distance is approximately five minutes of unobstructed travel. `F6` is a role/economy behavior check rather than a movement-count benchmark: workers are the short, tapered primitives in the same blue/red team colors as their combat units. The MVP/F7 scenario enables the scripted enemy macro; the debug overlay reports its current state, red Materials, production status, queue, and assembling-wave count. The gold primitives are finite neutral Materials nodes. A small gold marker above a worker means it is carrying Materials, and the top-left overlay shows the blue team's deposited total. The preplaced tall headquarters also serves as the Materials drop-off. Buildings placed with `B` are wider production-building construction sites; after completion they produce combat units but do not accept Materials.

After Phase 6, red and blue units can damage one another when they are within attack range. For a movement-only baseline, issue commands away from the red group and record the run before combat changes either unit count. Combat-enabled runs should be labeled separately rather than compared directly with pre-combat movement baselines.

## Runtime metrics

The top-left debug overlay displays:

- frames per second (FPS), sampled from Godot's runtime counter;
- current friendly unit count;
- current enemy unit count;
- current selected friendly unit count.

The overlay does not currently display CPU time or frame time. Use the Godot debugger/profiler when those measurements are needed and record them below.

## Test procedure

Repeat this procedure for `F2` (20), `F3` (100), `F4` (250), and `F5` (500). On `F5`, additionally test short translations inside the formation footprint, medium lateral/reverse moves, long cross-map moves, disconnected selections, and routes around the graybox obstacles. Use `F1` before or after the sequence to confirm that the normal battlefield and camera are restored.

1. Launch `SkirmishSandbox.tscn` and allow the FPS display to settle for several seconds.
2. Press the preset hotkey and confirm the friendly and enemy counts in the overlay.
3. Pan and zoom across the spawned group. Note input responsiveness and visual readability.
4. Drag a selection rectangle around the entire friendly group. Confirm the selected count and note selection responsiveness.
5. Right-click open ground on one side of the battlefield and let the group move.
6. While units are moving, pan and zoom, change the selection, and issue at least two repeated move orders to different valid ground locations.
7. Observe FPS at rest, shortly after issuing an order, and during sustained movement. If using the Godot profiler, also record CPU/frame-time observations.
8. Record pathing failures, stalls, unusual routes, endpoint behavior, visual bunching, and any input delay. Do not change the movement model during the baseline run.

For comparable runs, use approximately the same camera view, selection rectangle, command locations, observation duration, editor/debug configuration, and machine conditions at every count.

## Observation record

Duplicate this table or add one row per run. Leave unavailable measurements blank rather than estimating them.

| Friendly count | FPS at rest | FPS after order | FPS while moving | Selection responsiveness | Command responsiveness | Pathing problems | Visual bunching | CPU/frame-time observations | General notes |
|---:|---:|---:|---:|---|---|---|---|---|---|
| 20 |  |  |  |  |  |  |  |  |  |
| 100 |  |  |  |  |  |  |  |  |  |
| 250 |  |  |  |  |  |  |  |  |  |
| 500 |  |  |  |  |  |  |  |  |  |

Suggested qualitative terms for responsiveness are `immediate`, `noticeable delay`, and `disruptive delay`, supplemented by specific notes.

## Known placeholder limitations

- Each friendly unit owns a `NavigationAgent3D` and requests its own path.
- Navigation avoidance is disabled, so units can overlap or pass through one another while moving.
- Units move directly toward successive path points at constant speed with no acceleration, turning, animation, or physical collision response.
- A coherent, compact formation receiving a short command translates its existing topology directly. Disconnected, dispersed, medium-distance, and long-distance selections use one centered, near-square destination lattice with deterministic locality-preserving assignment. These rules remain prototype behavior, not a final formation design.
- Destination slots are independently validated and repaired when blocked or outside usable navigation space, which can distort the lattice near obstacles and battlefield edges.
- Source-cluster locality influences slot assignment, but units do not maintain a rigid formation while traveling.
- The F5 battlefield includes sparse graybox routing obstacles, but it does not represent production map geometry or a comprehensive inaccessible-terrain test.
- The runtime overlay provides a convenient FPS reading, not a detailed performance profile; deeper CPU and frame-time analysis requires Godot's profiler or an external tool.
