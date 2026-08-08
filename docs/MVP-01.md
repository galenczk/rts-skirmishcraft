# MVP-01: RTS Skirmish Prototype

## Status

Phase 0 is approved. Phase 1 establishes a runnable graybox battlefield; it does not yet execute the first gameplay experiment.

## Prototype objective

Establish a small, testable Godot C# project for experimenting with an RTS skirmish.

## Project baseline

- Godot 4.7.1 stable .NET.
- Forward+ renderer.
- Windows 11 desktop target.
- Keyboard and mouse input.

## Experiment 01: conventional RTS command surface

### Hypothesis

A conventional RTS command surface — camera navigation, unit selection, box selection, and movement orders — can provide a clear and responsive enough foundation for us to begin evaluating unit movement, group behavior, scale, and combat later.

### Pass criteria

- Single and box selection behave predictably.
- Move commands are responsive and reliable.
- Controlling roughly 10–20 placeholder units feels usable enough that attention shifts from fighting the controls to evaluating the units' behavior.
- The implementation remains easy to iterate on.

### Fail criteria

- Basic selection or movement is unreliable or frustrating.
- Navigation fails at these small unit counts.
- Simple behavior changes are disproportionately difficult.

### Design guardrail

The initial movement implementation is an experimental control surface, not the final movement or formation design.

## Phase 0 scope

- Establish repository-wide contributor rules.
- Establish a location for design decisions and experiment records.
- Add ignore rules appropriate for a Godot C# project.
- Record unresolved setup and design questions without answering them speculatively.

## Explicitly out of scope

- Gameplay mechanics or balancing.
- Units, controls, selection, movement, combat, AI, economy, objectives, or win/loss rules.
- Scenes, scripts, shaders, art, audio, and other gameplay assets.
- Third-party packages.
- Architecture intended for hypothetical future requirements.

## Phase 0 completion criteria

- The requested repository documentation exists.
- `docs/experiments/` is present and ready for experiment notes.
- Generated Godot, .NET, IDE, OS, and export artifacts are ignored.
- No gameplay behavior has been implemented.

## Phase 1 scope

- A runnable graybox battlefield.
- Flat ground, a fixed Camera3D, and basic lighting.
- Several friendly and enemy units represented by clearly different primitive geometry.
- No movement, selection, combat, or AI.
