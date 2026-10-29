# Hotbar and inventory slot sprite sources

These PNGs are the source assets for the custom hotbar and inventory canvas. The game UI embeds and renders them from the plugin DLL. Existing Might, Shield, and Wind Walk skill icons remain the art placed inside hotbar slots.

| File | Use | Suggested display |
| --- | --- | --- |
| `empty-slot-v1.png` | Default skill and buff cell | 43 × 43 px |
| `highlight-slot-v1.png` | Hover, selection, or active cell | 43 × 43 px |
| `inventory-slot-clean-v1.png` | Clean inventory, equipment, and trash slot background | About 54 × 54 px |
| `panel-frame-v1.png` | Shared stretchable outer frame for skill and buff bars | Fit to each bar; preserve corner motifs |
| `movement-grip-v1.png` | Draggable tab centered above each bar | About 70 × 22 px |
| `row-control-v1.png` | Three-row cycle button base | About 38 × 115 px at three rows; show the current 1/2/3 count in UI |

The source files include transparent padding. Crop to their visible alpha before creating Unity sprites; do not scale the whole source canvas directly into a UI rect. Approximate visible bounds in top-left image coordinates at alpha > 32:

| File | Source size | Visible bounds (left, top, right, bottom) |
| --- | --- | --- |
| `empty-slot-v1.png` | 1327 × 1186 | 203, 155, 1125, 1032 |
| `highlight-slot-v1.png` | 1327 × 1186 | 204, 156, 1123, 1030 |
| `inventory-slot-clean-v1.png` | 1254 × 1254 | 181, 204, 1074, 1048 |
| `panel-frame-v1.png` | 1254 × 1254 | 26, 29, 1228, 1201 |
| `movement-grip-v1.png` | 1983 × 793 | 298, 228, 1681, 538 |
| `row-control-v1.png` | 793 × 1983 | 148, 262, 646, 1682 |

The game renders the frame as nine slices so its corners keep their shape when switching between one, two, and three rows. It draws the brighter active row marks over the neutral control sprite.
