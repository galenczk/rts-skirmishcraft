# Design Decisions

Record decisions that constrain implementation or gameplay. Do not turn open questions into decisions without project-owner confirmation.

## Decision record format

### D-NNN: Short title

- **Date:** YYYY-MM-DD
- **Status:** Proposed | Accepted | Superseded
- **Context:** What prompted the decision.
- **Decision:** The chosen constraint or direction.
- **Consequences:** What becomes easier, harder, or intentionally deferred.

## Decisions

### D-001: Prototype technology

- **Date:** 2026-08-07
- **Status:** Accepted
- **Context:** The project was initiated as an experimental Godot C# RTS skirmish prototype.
- **Decision:** Use Godot with C# as the prototype technology.
- **Consequences:** A Godot .NET editor and compatible .NET SDK are required.

### D-002: Phase 0 contains no gameplay

- **Date:** 2026-08-07
- **Status:** Accepted
- **Context:** The initial task is limited to repository and project initialization.
- **Decision:** Add only documentation, repository guidance, directory scaffolding, and ignore rules during Phase 0.
- **Consequences:** Godot project files, scenes, scripts, and gameplay systems are deferred until a later approved phase.

### D-003: Engine and renderer baseline

- **Date:** 2026-08-07
- **Status:** Accepted
- **Context:** A reproducible engine baseline is required before implementation begins.
- **Decision:** Use Godot 4.7.1 stable .NET with the Forward+ renderer.
- **Consequences:** Development and verification require the .NET-enabled Godot 4.7.1 editor rather than the standard editor build.

### D-004: Initial platform and input baseline

- **Date:** 2026-08-07
- **Status:** Accepted
- **Context:** The first prototype needs a narrow execution target.
- **Decision:** Target Windows 11 desktop with keyboard and mouse input initially.
- **Consequences:** Other operating systems, touch, controllers, and accessibility alternatives are outside the initial experiment unless separately approved.

### D-005: First gameplay experiment

- **Date:** 2026-08-07
- **Status:** Accepted
- **Context:** The prototype needs a concrete hypothesis and observable pass/fail conditions before gameplay work begins.
- **Decision:** Test whether conventional RTS camera navigation, single and box selection, and movement orders are clear and responsive enough to support later evaluation of movement, group behavior, scale, and combat with roughly 10–20 placeholder units.
- **Consequences:** The experiment prioritizes predictable selection, reliable commands, usability, and ease of iteration. It fails when controls are unreliable or frustrating, navigation breaks down at the target count, or simple changes are disproportionately difficult. Initial movement must not be treated as the final movement or formation design.

### D-006: Phase 1 is a static graybox

- **Date:** 2026-08-07
- **Status:** Accepted
- **Context:** The first implementation phase is limited to a runnable visual baseline.
- **Decision:** Build a static battlefield with primitive ground, lighting, a fixed camera, and visually distinct friendly and enemy placeholders.
- **Consequences:** Movement, selection, combat, AI, and reusable gameplay abstractions remain deferred.
