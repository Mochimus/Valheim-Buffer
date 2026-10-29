# Inventory design preview

Open [index.html](index.html) in a browser to inspect the inventory without launching Valheim. The page is self-contained and works offline. It contains sample items so you can try selection, double-click equipping, dragging an item onto a compatible equipment slot, and clicking equipped gear to remove it.

This page is the visual design target. The character image is a cutout based on the supplied Valheim screenshot; the game build will render the current player model. Inventory data in the page is only sample data and cannot change game items.

All equipment slots now use individual painted PNG category backgrounds (`assets/*-slot-v1.png`). Their colors are desaturated in the empty slots, matching the approved helmet example. The vector icon file remains in `assets/` as an earlier design source.

The current game inventory uses Unity IMGUI and hand-drawn rectangles. To reproduce this design in game, build the production window with a Unity Canvas/uGUI layout and sprites exported from the approved design. The browser preview remains a fast place to adjust dimensions, colors, icons, and interactions before compiling the mod.
