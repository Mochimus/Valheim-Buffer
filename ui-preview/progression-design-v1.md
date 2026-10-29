# Class progression draft v1

This is the initial tuning spec for the character-wide Lineage level prototype. The XP curve and rewards below are implemented; playtesting may change their values. Fighter selection and class-based native-skill progression are implemented; consult the class and skill planner workbook for the current post-level-20 profession decisions and draft skill curves.

## Principles

- The mod adds a character-wide Lineage level from 1 to 20. Valheim's existing individual skills continue to work independently.
- XP comes from completed activities, not repeated hits: creature kills, felling trees, depleting ore nodes, and placing building pieces.
- Activity rewards depend on the activity and its difficulty, not the player's level.
- XP required per level rises quadratically. High levels take noticeably longer without exponential growth.
- There is no XP loss on death.

## XP curve

Proposed requirement to advance from level `L` to `L + 1`:

```text
XP = 20 + 5 × L²
```

| Current level | XP to next | Total XP at next level |
| ---: | ---: | ---: |
| 1 | 25 | 25 |
| 2 | 40 | 65 |
| 3 | 65 | 130 |
| 4 | 100 | 230 |
| 5 | 145 | 375 |
| 6 | 200 | 575 |
| 7 | 265 | 840 |
| 8 | 340 | 1,180 |
| 9 | 425 | 1,605 |
| 10 | 520 | 2,125 |
| 11 | 625 | 2,750 |
| 12 | 740 | 3,490 |
| 13 | 865 | 4,355 |
| 14 | 1,000 | 5,355 |
| 15 | 1,145 | 6,500 |
| 16 | 1,300 | 7,800 |
| 17 | 1,465 | 9,265 |
| 18 | 1,640 | 10,905 |
| 19 | 1,825 | 12,730 |
| 20 | — | Level cap |

The last step takes 73 times as much XP as the first. If extended to level 40 with the same curve, the level 39-to-40 step would take 7,625 XP, about 4.2 times the level 19-to-20 step. Reaching level 40 would take about 8.1 times the total XP required for level 20.

## Activity reward starting points

| Activity | Proposed XP | Award condition |
| --- | ---: | --- |
| Weak creature kill | 15 | Once when the creature dies |
| Regular creature kill | 30 | Once when the creature dies |
| Elite or large creature kill | 60 | Once when the creature dies |
| Boss or major threat kill | 200 | Once when the creature dies |
| One-star creature | +50% | Bonus on the creature's kill reward |
| Two-star creature | +100% | Bonus on the creature's kill reward |
| Fell an ordinary tree | 5 | Once per tree, when felled |
| Fell a large or hardwood tree | 10 | Once per tree, when felled |
| Deplete a common ore node | 20 | Once when the node is depleted |
| Deplete an advanced ore node | 30 | Once when the node is depleted |
| Deplete a rare, high-tier ore node | 50 | Once when the node is depleted |
| Place a basic building piece | 2 | Only after a paid placement succeeds |
| Place a stone or advanced piece | 4 | Only after a paid placement succeeds |

Monster and resource tiers should be assigned by data, so new creatures and materials can be added without changing the leveling formula. No XP is awarded per hit or for free/debug building.

## Open balance checks

- Building pieces can be dismantled and rebuilt. Test for XP farming and tune a diminishing-return or construction limit if it becomes exploitable.
- Confirm the activity rewards feel fair for players who mostly fight, gather, mine, or build.
- Decide whether class changes preserve each class's level or share one character level.
- Set the skill-point schedule and map skill unlock levels after the XP curve is approved.
- Play-test levels 1–5 and 15–20 before locking the numbers.
