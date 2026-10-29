# Project Status and Agent Roles

## Project goal

Build a Lineage 2-inspired class, skill, and progression mod for Valheim. Keep gameplay progression understandable, skills reliable in single-player and multiplayer, and the UI coherent with the mod's visual style.

## Current status

The project is a working proof of concept with Fighter class selection and specialization, Lineage and profession progression, class-based native skill targets, XP rewards from several activities, configurable hostile creature health scaling, a skill and buff framework, Power Strike, buff casting, procedural VFX, animation trigger fallback, custom inventory and status UI, and hotbars.

The README is the current feature overview and manual test loop. The next coordination need is to turn its open questions into prioritized, verifiable work. No in-game validation was performed as part of the initial agent audits.

## Active agent roles

### Classes and Combat

Own class choice and specialization, combat mechanics tied to class, class/status presentation, native skill mapping, save migration, and multiplayer class identity. Coordinate with XP and Balance before changing shared progression curves.

### Skills, VFX, and Animation

Own active and buff skill definitions/runtime, costs and cooldowns, casts, animation triggers, VFX, skill UI/hotbar integration, and the authoring workflow for adding skills.

### XP and Balance

Own Lineage/profession XP curves, reward values, hostile health scaling, progression pacing, tuning/configuration, and validation criteria. Coordinate shared curve changes with Classes and Combat.

## Initial findings and candidate work

### Classes and Combat

- Status panel may show `Class: Unassigned` regardless of the selected class.
- Class specializations are described in the UI, but distinct combat passives appear limited or absent.
- Validate class identity replication and existing-character migration in game.
- Class target curves are documented as draft values.

### Skills, VFX, and Animation

- Power Strike is currently the only active action skill; it uses a normal weapon attack.
- Review multiplayer ownership and remote cast/VFX presentation.
- Document the active-skill authoring lifecycle and check animator trigger compatibility in game.
- Clarify the role and packaging of `UnityVfxAuthoring` and `VfxBundles`.

### XP and Balance

- XP curve and activity reward values are hard-coded; there is no recorded pacing evidence.
- Validate kill attribution, tree/ore depletion, building rewards, star/boss cases, and health-scaling reward normalization in game.
- Produce a pacing table and agree on progression acceptance targets.
- Debug level decrement resets to the start of the prior level rather than preserving progress.

## Working agreement

Assign each implementation task to one domain owner. Ask the other owner for review when a change crosses domains. Update this document when goals, priorities, or domain status change; update the README when player-facing behavior or the manual test loop changes.
