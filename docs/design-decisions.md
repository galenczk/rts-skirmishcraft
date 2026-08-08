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
- **Consequences:** A Godot .NET editor and compatible .NET SDK are required. The exact Godot version, renderer, and target platform remain unresolved.

### D-002: Phase 0 contains no gameplay

- **Date:** 2026-08-07
- **Status:** Accepted
- **Context:** The initial task is limited to repository and project initialization.
- **Decision:** Add only documentation, repository guidance, directory scaffolding, and ignore rules during Phase 0.
- **Consequences:** Godot project files, scenes, scripts, and gameplay systems are deferred until a later approved phase.

