# Adding a buff skill

Each skill is described by one `BuffSkillDefinition` entry in `Plugin.CreateSkills()`. The entry supplies the status-effect ID, display text, embedded icon, duration, warning window, and a callback that sets the Valheim status-effect stats.

## Template

1. Add a square PNG icon under `Assets/`, for example `Assets/bless-the-body.png`. PNG files in this folder are automatically embedded in the plugin.
2. Add a definition to `CreateSkills()`:

```csharp
_skills.Add(new BuffSkillDefinition(
    effectId: "L2BlessTheBody",
    displayName: "Bless the Body",
    description: "Describe the buff effect here.",
    iconResource: "Lineage2Valheim.Assets.bless-the-body.png",
    durationSeconds: 300f,
    configureEffect: effect =>
    {
        // Set the SE_Stats fields for this buff here.
    },
    warningWindowSeconds: 60f));
```

3. Use a unique `effectId` and icon resource for every skill.
4. Build and launch the `My-Mod` profile. Learn the skill if needed, then drag it from Active Skills onto an empty skill hotbar slot.

The warning window is the final number of seconds before expiration. Active buffs appear in a separate display bar with their remaining duration.
