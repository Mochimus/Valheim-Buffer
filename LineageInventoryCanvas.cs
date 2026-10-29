using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Lineage2Valheim
{
    /// <summary>The inventory's runtime Canvas; Valheim remains the source of truth for all items.</summary>
    internal sealed class LineageInventoryCanvas
    {
        private static readonly Color Gold = Hex(0xAA8350);
        private static readonly Color PaleGold = Hex(0xDFBD81);
        private static readonly Color Muted = Hex(0xB6B9B1);
        private static readonly Color SlotFill = Hex(0x222829);
        private static readonly Vector3[] HitCorners = new Vector3[4];
        private readonly List<SlotView> _equipment = new List<SlotView>();
        private readonly List<CellView> _cells = new List<CellView>();
        private readonly List<ItemDrop.ItemData> _gridItems = new List<ItemDrop.ItemData>();
        private List<Recipe> _recipes = new List<Recipe>();
        private readonly List<RecipeRowView> _recipeRows = new List<RecipeRowView>();
        private readonly Dictionary<string, Sprite> _art = new Dictionary<string, Sprite>();
        private readonly List<Texture2D> _ownedTextures = new List<Texture2D>();
        private const int GridColumns = 6;
        private const int GridRows = 18;
        private const int VisibleGridRows = 8;
        private const int CellSize = 58;
        private const int CellStep = 65;
        private const int VisibleGridHeight = 510;
        private const int VisibleRecipeRows = 7;
        private const int RecipeRowHeight = 44;
        private const int WindowWidth = 1290;
        private const int ScaledWindowWidth = 1032;
        private static readonly FieldInfo WidthField = AccessTools.Field(typeof(Inventory), "m_width");
        private static readonly FieldInfo HeightField = AccessTools.Field(typeof(Inventory), "m_height");
        private static readonly MethodInfo ChangedMethod = AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });
        private static Inventory _overflowInventory;
        private static readonly MethodInfo DoCraftingMethod = AccessTools.Method(typeof(InventoryGui), "DoCrafting");
        private static readonly FieldInfo CraftRecipeField = AccessTools.Field(typeof(InventoryGui), "m_craftRecipe");
        private static readonly FieldInfo CraftUpgradeField = AccessTools.Field(typeof(InventoryGui), "m_craftUpgradeItem");
        private static readonly FieldInfo MultiCraftField = AccessTools.Field(typeof(InventoryGui), "m_multiCrafting");
        private static readonly FieldInfo CraftVariantField = AccessTools.Field(typeof(InventoryGui), "m_craftVariant");
        private GameObject _root;
        private Canvas _canvas;
        private Image _fallbackImage;
        private Sprite _fallbackCharacter;
        private Sprite _unarmoredCharacter;
        private Sprite _emptySlotSprite, _highlightSlotSprite;
        private Texture2D _panelFrameTexture;
        private Rect _panelFrameSource;
        private Text _itemCount, _weight, _tooltipName, _tooltipDescription, _tooltipStats, _tooltipMeta;
        private Image _weightFill, _dragIcon, _scrollThumb, _trashBackground;
        private RectTransform _windowRect, _closeRect, _inventoryPaneRect, _gridViewport, _gridContent, _titleRect, _tooltipRect;
        private RectTransform _recipeViewport, _craftButtonRect, _trashRect;
        private Text _recipeName, _recipeDetails, _stationName, _craftButtonText, _emptyRecipes, _recipeCount;
        private Image _recipeIcon, _craftButtonBackground;
        private Recipe _selectedRecipe;
        private int _recipeScroll;
        private GameObject _tooltip;
        private int _scrollRow;
        private bool _movingWindow;
        private Vector2 _windowDragStart, _windowStart;
        private ItemDrop.ItemData _selected, _dragged;
        private ItemDrop.ItemData _hoveredItem;
        private SlotView _pressedSlot;
        private CellView _pressedCell;
        private Vector2 _pressPosition;
        private float _lastClickTime;
        private ItemDrop.ItemData _lastClickedItem;
        private bool _dragging;
        private Action _close;
        private float _nextRefresh;
        private float _nextRecipeRefresh;
        private Font _font;

        internal bool HasCanvas => _root != null;

        internal static bool EnsureCapacity(Player player)
        {
            if (player == null || WidthField == null || HeightField == null) return false;
            var inventory = player.GetInventory();
            if (inventory == null) return false;
            if (inventory.GetWidth() == GridColumns && inventory.GetHeight() == GridRows) return true;
            if (ReferenceEquals(_overflowInventory, inventory) && inventory.GetAllItems().Count > GridColumns * GridRows)
                return false;
            var items = inventory.GetAllItemsInGridOrder();
            if (items.Count > GridColumns * GridRows)
            {
                if (!ReferenceEquals(_overflowInventory, inventory))
                    Plugin.Log?.LogWarning("Inventory has more than 108 items; preserving all item positions and skipping the invalid-item drop check.");
                _overflowInventory = inventory;
                return false;
            }
            _overflowInventory = null;
            var used = new HashSet<int>();
            var relocate = new List<ItemDrop.ItemData>();
            foreach (var item in items)
            {
                var position = item.m_gridPos;
                int key = position.y * GridColumns + position.x;
                if (position.x >= 0 && position.x < GridColumns && position.y >= 0 &&
                    position.y < GridRows && used.Add(key)) continue;
                relocate.Add(item);
            }
            foreach (var item in relocate)
            {
                for (int index = 0; index < GridColumns * GridRows; index++)
                    if (used.Add(index))
                    {
                        item.m_gridPos = new Vector2i(index % GridColumns, index / GridColumns);
                        break;
                    }
            }
            WidthField.SetValue(inventory, GridColumns);
            HeightField.SetValue(inventory, GridRows);
            ChangedMethod?.Invoke(inventory, new object[] { false, false });
            Plugin.Log?.LogInfo("Player inventory expanded to 6 × 18 slots.");
            return true;
        }

        internal void Open(Action close)
        {
            // Unity destroys scene objects on world changes. Cached C# views then
            // compare as destroyed Unity objects, even though their lists remain.
            if (_root == null && (_cells.Count != 0 || _recipeRows.Count != 0 || _ownedTextures.Count != 0))
                Dispose();
            _close = close;
            if (_root != null)
            {
                _canvas.enabled = true;
                _root.GetComponent<GraphicRaycaster>().enabled = true;
                _nextRefresh = 0f;
                _nextRecipeRefresh = 0f;
                Update();
                return;
            }
            Build();
            Canvas.ForceUpdateCanvases();
            var overlay = (RectTransform)_windowRect.parent;
            _windowRect.anchoredPosition = new Vector2(
                (overlay.rect.width - ScaledWindowWidth) * 0.5f,
                -(overlay.rect.height - 579.2f) * 0.5f);
            _nextRefresh = 0f;
            _nextRecipeRefresh = 0f;
            Update();
        }

        internal void Close()
        {
            if (_root == null) return;
            _canvas.enabled = false;
            _root.GetComponent<GraphicRaycaster>().enabled = false;
            if (_tooltip != null) _tooltip.SetActive(false);
            if (_dragIcon != null) _dragIcon.enabled = false;
            SetTrashHighlight(false);
            _close = null;
            _hoveredItem = null;
            _dragged = null;
            _pressedSlot = null;
            _pressedCell = null;
            _dragging = false;
            _movingWindow = false;
        }

        internal void Dispose()
        {
            if (_root == null && _cells.Count == 0 && _recipeRows.Count == 0 && _ownedTextures.Count == 0)
                return;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _canvas = null;
            foreach (var sprite in _art.Values) if (sprite != null) UnityEngine.Object.Destroy(sprite);
            foreach (var texture in _ownedTextures) if (texture != null) UnityEngine.Object.Destroy(texture);
            _ownedTextures.Clear();
            _art.Clear();
            _fallbackImage = null;
            _fallbackCharacter = null;
            _unarmoredCharacter = null;
            _emptySlotSprite = _highlightSlotSprite = null;
            _panelFrameTexture = null;
            _equipment.Clear();
            _cells.Clear();
            _gridItems.Clear();
            _recipes.Clear();
            _recipeRows.Clear();
            _selected = null;
            _selectedRecipe = null;
            _hoveredItem = null;
            _dragged = null;
            _pressedSlot = null;
            _pressedCell = null;
            _dragging = false;
            _windowRect = null;
            _closeRect = null;
            _inventoryPaneRect = null;
            _recipeViewport = null;
            _craftButtonRect = null;
            _trashRect = null;
            _trashBackground = null;
            _dragIcon = null;
            _tooltip = null;
            _close = null;
            _scrollRow = 0;
            _recipeScroll = 0;
            _movingWindow = false;
        }

        internal void Update()
        {
            if (_root == null) return;
            var player = Player.m_localPlayer;
            if (player == null) { _close?.Invoke(); return; }
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.35f;
                Refresh(player);
            }
            if (Time.unscaledTime >= _nextRecipeRefresh)
            {
                _nextRecipeRefresh = Time.unscaledTime + 1f;
                RefreshRecipes(player);
            }
            UpdateTooltip((Vector2)Input.mousePosition);
        }

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _emptySlotSprite = LoadHotbarSlotSprite("inventory-slot-clean-v1.png", 181, 204, 1074, 1048);
            _highlightSlotSprite = _emptySlotSprite;
            Plugin.Instance?.Hotbars.TryGetPanelFrame(out _panelFrameTexture, out _panelFrameSource);
            _root = new GameObject("Lineage2Valheim.InventoryCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = _root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 30000;
            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var overlay = Panel("Overlay", _root.transform, new Rect(0, 0, 1600, 900), new Color(0, 0, 0, 0.45f));
            Stretch(overlay.rectTransform);
            var window = Panel("Inventory and Crafting", overlay.transform, new Rect(0, 0, WindowWidth, 724), Hex(0x101617));
            _windowRect = window.rectTransform;
            _windowRect.anchoredPosition = new Vector2(400, -160);
            _windowRect.localScale = new Vector3(0.80f, 0.80f, 1f);
            Border(window.transform, new Rect(0, 0, WindowWidth, 724), Gold, 2);
            _titleRect = Panel("Title Bar", window.transform, new Rect(2, 2, WindowWidth - 4, 50), Hex(0x202423)).rectTransform;
            Panel("Title Rule", window.transform, new Rect(0, 50, WindowWidth, 2), Gold);
            Label("INVENTORY", window.transform, new Rect(320, 10, 300, 34), 25, PaleGold, TextAnchor.MiddleCenter, true);
            Label("CRAFTING", window.transform, new Rect(965, 10, 300, 34), 25, PaleGold, TextAnchor.MiddleCenter, true);
            Label("◆", window.transform, new Rect(15, 7, 30, 34), 25, Gold, TextAnchor.MiddleCenter);
            var close = Panel("Close", window.transform, new Rect(1233, 10, 35, 32), Hex(0x30312B));
            _closeRect = close.rectTransform;
            Border(window.transform, new Rect(1233, 10, 35, 32), Gold, 1);
            Label("×", close.transform, new Rect(0, 0, 35, 32), 18, PaleGold, TextAnchor.MiddleCenter);

            Panel("Character Background", window.transform, new Rect(3, 52, 472, 669), Hex(0x191F20));
            Panel("Divider", window.transform, new Rect(475, 52, 2, 669), Gold);
            _inventoryPaneRect = Panel("Inventory Background", window.transform,
                new Rect(477, 52, 461, 669), Hex(0x121819)).rectTransform;
            Panel("Crafting Divider", window.transform, new Rect(938, 52, 2, 669), Gold);
            Panel("Crafting Background", window.transform, new Rect(940, 52, 348, 669), Hex(0x191F20));
            Label("RECIPES", window.transform, new Rect(963, 68, 280, 28), 19, PaleGold);
            _recipeCount = Label("", window.transform, new Rect(1160, 68, 105, 28), 13, Muted, TextAnchor.MiddleRight);
            Panel("Crafting Rule", window.transform, new Rect(963, 100, 302, 1), Hex(0x705438));
            _recipeViewport = Panel("Recipe List", window.transform,
                new Rect(963, 111, 302, VisibleRecipeRows * RecipeRowHeight), Hex(0x121819)).rectTransform;
            _emptyRecipes = Label("No recipes available here.", _recipeViewport,
                new Rect(9, 9, 280, 30), 15, Muted);
            for (int row = 0; row < VisibleRecipeRows; row++)
            {
                int y = row * RecipeRowHeight;
                var background = Panel("Recipe Row", _recipeViewport,
                    new Rect(0, y, 302, RecipeRowHeight - 2), Hex(0x222829));
                var icon = NewImage("Recipe Icon", background.transform, new Rect(4, 4, 34, 34), null);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                var name = Label("", background.transform, new Rect(45, 3, 246, 24), 15, PaleGold);
                var status = Label("", background.transform, new Rect(45, 25, 246, 15), 11, Muted);
                _recipeRows.Add(new RecipeRowView { HitArea = background.rectTransform,
                    Background = background, Icon = icon, Name = name, Status = status });
            }
            Panel("Recipe Details Rule", window.transform, new Rect(963, 434, 302, 1), Hex(0x705438));
            _recipeIcon = NewImage("Selected Recipe Icon", window.transform, new Rect(963, 449, 48, 48), null);
            _recipeIcon.preserveAspect = true;
            _recipeIcon.raycastTarget = false;
            _recipeIcon.enabled = false;
            _recipeName = Label("Select a recipe", window.transform, new Rect(1020, 449, 242, 48), 17, PaleGold);
            _recipeDetails = Label("", window.transform, new Rect(963, 508, 302, 103), 14, Muted);
            _stationName = Label("", window.transform, new Rect(963, 609, 302, 24), 13, Muted);
            _craftButtonBackground = Panel("Craft Button", window.transform,
                new Rect(1014, 644, 200, 45), Hex(0x30312B));
            _craftButtonRect = _craftButtonBackground.rectTransform;
            Border(window.transform, new Rect(1014, 644, 200, 45), Gold, 1);
            _craftButtonText = Label("CRAFT", _craftButtonBackground.transform,
                new Rect(0, 0, 200, 45), 18, PaleGold, TextAnchor.MiddleCenter, true);
            Label("CHARACTER", window.transform, new Rect(27, 68, 230, 28), 19, PaleGold);

            var stage = Panel("Character Stage", window.transform, new Rect(104, 109, 288, 493), Hex(0x202627));
            stage.raycastTarget = false;
            var fallback = NewImage("Character Illustration", stage.transform, new Rect(9, 0, 270, 490), null);
            fallback.preserveAspect = true;
            fallback.raycastTarget = false;
            _fallbackCharacter = LoadSprite("character-preview", false);
            _unarmoredCharacter = LoadSprite("character-preview-unarmored", false);
            fallback.sprite = _unarmoredCharacter ?? _fallbackCharacter;
            fallback.color = new Color(1, 1, 1, 0.45f);
            _fallbackImage = fallback;
            Panel("Stage Rule", window.transform, new Rect(103, 600, 289, 1), Hex(0x705438));

            var left = new[] { "helmet", "chest", "legs", "gloves", "boots", "cloak" };
            var right = new[] { "necklace", "earring", "earring", "ring", "ring", "bracelet" };
            for (int i = 0; i < 6; i++)
            {
                AddEquipmentSlot(window.transform, left[i], i, 28, 110 + i * 81, 67);
                AddEquipmentSlot(window.transform, right[i], i, 398, 110 + i * 81, 67);
            }
            AddEquipmentSlot(window.transform, "sword", 0, 145, 626, 69);
            AddEquipmentSlot(window.transform, "shield", 0, 218, 626, 69);
            AddEquipmentSlot(window.transform, "ammo", 0, 291, 626, 69);

            _gridViewport = Panel("Item Grid Viewport", window.transform,
                new Rect(500, 91, 390, VisibleGridHeight), new Color(0, 0, 0, 0)).rectTransform;
            _gridViewport.gameObject.AddComponent<RectMask2D>();
            _gridContent = NewObject("Item Grid Content", _gridViewport,
                new Rect(0, 0, 390, VisibleGridRows * CellStep)).GetComponent<RectTransform>();
            for (int row = 0; row < VisibleGridRows; row++)
                for (int col = 0; col < GridColumns; col++)
                    AddItemCell(_gridContent, col * CellStep, row * CellStep, CellSize);
            Panel("Scroll Track", window.transform, new Rect(902, 91, 13, VisibleGridHeight), Hex(0x101617));
            Border(window.transform, new Rect(902, 91, 13, VisibleGridHeight), Gold, 1);
            _scrollThumb = Panel("Scroll Thumb", window.transform, new Rect(905, 94, 7, 220), Hex(0xAD8754));

            _itemCount = Label("0 / 108", window.transform, new Rect(500, 629, 126, 26), 17, Muted);
            var weightIcon = NewImage("Carry Weight Icon", window.transform,
                new Rect(633, 628, 24, 24), LoadSprite("weight", false));
            weightIcon.preserveAspect = true;
            weightIcon.raycastTarget = false;
            weightIcon.enabled = weightIcon.sprite != null;
            _weight = Label("0 / 300", window.transform, new Rect(662, 627, 130, 25), 14, Muted);
            Panel("Weight Track", window.transform, new Rect(635, 659, 190, 12), Hex(0x090E0E));
            _weightFill = Panel("Weight Fill", window.transform, new Rect(638, 662, 2, 6), Hex(0xD3A45F));
            Border(window.transform, new Rect(635, 659, 190, 12), Gold, 1);
            _trashBackground = Panel("Trash Drop Target", window.transform,
                new Rect(851, 623, 54, 54), _emptySlotSprite != null ? Color.white : SlotFill);
            _trashRect = _trashBackground.rectTransform;
            _trashBackground.sprite = _emptySlotSprite;
            if (_emptySlotSprite == null)
                Border(window.transform, new Rect(851, 623, 54, 54), Hex(0x756044), 1);
            var trashIcon = NewImage("Trash Icon", _trashBackground.transform,
                new Rect(8, 8, 38, 38), LoadSprite("trash", false));
            trashIcon.preserveAspect = true;
            trashIcon.raycastTarget = false;
            trashIcon.enabled = trashIcon.sprite != null;

            _tooltip = NewObject("Item Tooltip", overlay.transform, new Rect(0, 0, 290, 155));
            _tooltipRect = _tooltip.GetComponent<RectTransform>();
            Panel("Tooltip Background", _tooltip.transform, new Rect(0, 0, 290, 155), Hex(0x1B2121));
            Border(_tooltip.transform, new Rect(0, 0, 290, 155), Gold, 1);
            _tooltipName = Label("", _tooltip.transform, new Rect(12, 8, 266, 25), 18, PaleGold);
            _tooltipDescription = Label("", _tooltip.transform, new Rect(12, 37, 266, 56), 13, Muted);
            _tooltipStats = Label("", _tooltip.transform, new Rect(12, 91, 266, 19), 12, PaleGold, TextAnchor.MiddleLeft, true);
            _tooltipMeta = Label("", _tooltip.transform, new Rect(12, 114, 266, 34), 12, Muted);
            _tooltip.SetActive(false);
            _dragIcon = NewImage("Dragged Item", overlay.transform, new Rect(0, 0, 56, 56), null);
            _dragIcon.preserveAspect = true;
            _dragIcon.raycastTarget = false;
            _dragIcon.enabled = false;
            if (_panelFrameTexture != null) AddPaintedTrim(window.transform);
        }

        private void AddPaintedTrim(Transform parent)
        {
            var source = _panelFrameSource;
            float corner = Mathf.Min(220f, source.width * 0.2f, source.height * 0.2f);
            float[] sx = { source.xMin, source.xMin + corner, source.xMax - corner, source.xMax };
            float[] sy = { source.yMin, source.yMin + corner, source.yMax - corner, source.yMax };
            float[] dx = { 0f, 18f, WindowWidth - 18f, WindowWidth };
            float[] dy = { 0f, 18f, 724f - 18f, 724f };
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    if (row == 1 && col == 1) continue;
                    var rect = new Rect(dx[col], dy[row], dx[col + 1] - dx[col],
                        dy[row + 1] - dy[row]);
                    if (row == 0 && col == 1) rect.height = 9f;
                    if (row == 2 && col == 1) { rect.y = 715f; rect.height = 9f; }
                    if (col == 0 && row == 1) rect.width = 9f;
                    if (col == 2 && row == 1) { rect.x = WindowWidth - 9f; rect.width = 9f; }
                    AddFramePatch(parent, "Outer Frame", rect, sx[col], sy[row], sx[col + 1], sy[row + 1]);
                }
            AddFramePatch(parent, "Title Trim", new Rect(18, 49, WindowWidth - 36, 5),
                sx[1], sy[0], sx[2], sy[1]);
            AddFramePatch(parent, "Character Divider Trim", new Rect(474, 53, 5, 652),
                sx[0], sy[1], sx[1], sy[2]);
            AddFramePatch(parent, "Crafting Divider Trim", new Rect(937, 53, 5, 652),
                sx[0], sy[1], sx[1], sy[2]);
            AddFramePatch(parent, "Character Stage Trim", new Rect(113, 599, 280, 4),
                sx[1], sy[0], sx[2], sy[1]);
            AddFramePatch(parent, "Recipe List Trim", new Rect(963, 100, 302, 4),
                sx[1], sy[0], sx[2], sy[1]);
            AddFramePatch(parent, "Recipe Details Trim", new Rect(963, 433, 302, 4),
                sx[1], sy[0], sx[2], sy[1]);
        }

        private void AddFramePatch(Transform parent, string name, Rect destination,
            float left, float top, float right, float bottom)
        {
            var image = NewObject(name, parent, destination).AddComponent<RawImage>();
            image.texture = _panelFrameTexture;
            image.uvRect = new Rect(left / _panelFrameTexture.width,
                (_panelFrameTexture.height - bottom) / _panelFrameTexture.height,
                (right - left) / _panelFrameTexture.width,
                (bottom - top) / _panelFrameTexture.height);
            image.raycastTarget = false;
        }

        private void AddEquipmentSlot(Transform parent, string category, int occurrence, int x, int y, int size)
        {
            var background = Panel(category + " Slot", parent, new Rect(x, y, size, size),
                _emptySlotSprite != null ? Color.white : SlotFill);
            background.sprite = _emptySlotSprite;
            if (_emptySlotSprite == null)
                Border(parent, new Rect(x, y, size, size), Hex(0x756044), 1);
            var art = NewImage(category + " Art", background.transform, new Rect(5, 5, size - 10, size - 10), LoadSprite(category, true));
            art.preserveAspect = true;
            art.color = new Color(1, 1, 1, 0.76f);
            art.raycastTarget = false;
            var equipped = NewImage("Equipped Item", background.transform, new Rect(5, 5, size - 10, size - 10), null);
            equipped.preserveAspect = true;
            equipped.raycastTarget = false;
            equipped.enabled = false;
            var view = new SlotView { Category = category, Occurrence = occurrence, Art = art, Icon = equipped };
            view.HitArea = background.rectTransform;
            _equipment.Add(view);
        }

        private void AddItemCell(Transform parent, int x, int y, int size)
        {
            var bg = Panel("Inventory Item", parent, new Rect(x, y, size, size),
                _emptySlotSprite != null ? Color.white : SlotFill);
            bg.sprite = _emptySlotSprite;
            if (_emptySlotSprite == null)
                Border(parent, new Rect(x, y, size, size), Hex(0x5D513E), 1);
            var icon = NewImage("Item Icon", bg.transform, new Rect(5, 5, size - 10, size - 10), null);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;
            var stack = Label("", bg.transform, new Rect(24, size - 20, size - 29, 17), 12, Color.white, TextAnchor.LowerRight, true);
            stack.raycastTarget = false;
            var view = new CellView { Background = bg, Icon = icon, Stack = stack, HitArea = bg.rectTransform };
            _cells.Add(view);
        }

        internal void OnGUI(Event current)
        {
            if (_root == null || current == null) return;
            var mouse = new Vector2(current.mousePosition.x, Screen.height - current.mousePosition.y);
            if (current.type == EventType.ScrollWheel && Hit(_gridViewport, mouse))
            {
                _scrollRow = Mathf.Clamp(_scrollRow + (current.delta.y > 0 ? 2 : -2),
                    0, GridRows - VisibleGridRows);
                UpdateScroll();
                Refresh(Player.m_localPlayer);
                current.Use();
                return;
            }
            if (current.type == EventType.ScrollWheel && Hit(_recipeViewport, mouse))
            {
                _recipeScroll = Mathf.Clamp(_recipeScroll + (current.delta.y > 0 ? 2 : -2),
                    0, Mathf.Max(0, _recipes.Count - VisibleRecipeRows));
                RefreshRecipes(Player.m_localPlayer);
                current.Use();
                return;
            }
            if (current.type == EventType.MouseDown && current.button == 0)
            {
                if (Hit(_closeRect, mouse)) { current.Use(); _close?.Invoke(); return; }
                if (Hit(_titleRect, mouse))
                {
                    _movingWindow = true;
                    _windowDragStart = mouse;
                    _windowStart = _windowRect.anchoredPosition;
                    current.Use();
                    return;
                }
                if (Hit(_craftButtonRect, mouse))
                {
                    CraftSelected(Player.m_localPlayer);
                    current.Use();
                    return;
                }
                foreach (var row in _recipeRows)
                    if (row.Recipe != null && Hit(row.HitArea, mouse))
                    {
                        _selectedRecipe = row.Recipe;
                        RefreshRecipes(Player.m_localPlayer);
                        current.Use();
                        return;
                    }
                if (!Hit(_windowRect, mouse)) return;
                _dragging = false;
                _pressedSlot = FindSlotAt(mouse);
                _pressedCell = _pressedSlot == null ? FindCellAt(mouse) : null;
                _dragged = _pressedSlot != null ? _pressedSlot.Item : _pressedCell?.Item;
                _pressPosition = mouse;
                current.Use();
                return;
            }
            if (current.type == EventType.MouseDrag && current.button == 0)
            {
                if (_movingWindow)
                {
                    var delta = (mouse - _windowDragStart) / Mathf.Max(0.01f, _canvas.scaleFactor);
                    var parent = (RectTransform)_windowRect.parent;
                    var position = _windowStart + delta;
                    position.x = Mathf.Clamp(position.x, 0, Mathf.Max(0, parent.rect.width - ScaledWindowWidth));
                    position.y = Mathf.Clamp(position.y, -Mathf.Max(0, parent.rect.height - 579), 0);
                    _windowRect.anchoredPosition = position;
                    current.Use();
                    return;
                }
                if (_dragged != null && Vector2.Distance(mouse, _pressPosition) > 7f)
                {
                    _dragging = true;
                    SetTrashHighlight(Hit(_trashRect, mouse));
                    _dragIcon.sprite = _dragged.GetIcon();
                    _dragIcon.enabled = _dragIcon.sprite != null;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        (RectTransform)_dragIcon.transform.parent, mouse, null, out var local);
                    _dragIcon.rectTransform.anchoredPosition = local + new Vector2(-28, 28);
                    current.Use();
                    return;
                }
            }
            if (current.type == EventType.MouseUp && current.button == 0)
            {
                if (_movingWindow) { _movingWindow = false; current.Use(); return; }
                if (_pressedSlot == null && _pressedCell == null) return;
                var player = Player.m_localPlayer;
                if (_dragging && _dragged != null)
                {
                    var target = FindSlotAt(mouse);
                    if (Hit(_trashRect, mouse)) TrashDragged(player, _dragged);
                    else if (target != null && CanEquip(target.Category, _dragged))
                    {
                        if (!_dragged.m_equipped) Toggle(_dragged);
                    }
                    else if (Plugin.Instance?.Hotbars.TryAssignConsumable(_dragged,
                        new Vector2(mouse.x, Screen.height - mouse.y)) == true) { }
                    else if (!Hit(_windowRect, mouse)) DropDragged(player, _dragged);
                    else if (_pressedSlot != null && _dragged.m_equipped && Hit(_inventoryPaneRect, mouse))
                        Toggle(_dragged);
                }
                else if (_dragged != null && ((_pressedCell != null && Hit(_pressedCell.HitArea, mouse)) ||
                    (_pressedSlot != null && Hit(_pressedSlot.HitArea, mouse))))
                {
                    _selected = _dragged;
                    bool doubleClick = _lastClickedItem == _dragged && Time.unscaledTime - _lastClickTime <= 0.42f;
                    _lastClickedItem = _dragged;
                    _lastClickTime = Time.unscaledTime;
                    if (doubleClick) UseSelected();
                }
                _pressedSlot = null;
                _pressedCell = null;
                _dragged = null;
                _dragging = false;
                SetTrashHighlight(false);
                if (_dragIcon != null) _dragIcon.enabled = false;
                Refresh(player);
                current.Use();
                return;
            }
            if (current.type == EventType.MouseMove)
                UpdateTooltip(mouse);
        }

        private SlotView FindSlotAt(Vector2 point)
        {
            foreach (var slot in _equipment) if (Hit(slot.HitArea, point)) return slot;
            return null;
        }

        private CellView FindCellAt(Vector2 point)
        {
            if (!Hit(_gridViewport, point)) return null;
            foreach (var cell in _cells) if (Hit(cell.HitArea, point)) return cell;
            return null;
        }

        private void UpdateScroll()
        {
            if (_gridContent == null || _scrollThumb == null) return;
            float ratio = _scrollRow / (float)(GridRows - VisibleGridRows);
            _scrollThumb.rectTransform.anchoredPosition = new Vector2(905, -(94 + ratio * 284));
        }

        private void SetTrashHighlight(bool highlighted)
        {
            if (_trashBackground == null) return;
            _trashBackground.sprite = highlighted
                ? (_highlightSlotSprite ?? _emptySlotSprite) : _emptySlotSprite;
            _trashBackground.color = _emptySlotSprite != null
                ? highlighted ? Hex(0xFFE3AE) : Color.white :
                highlighted ? Hex(0x51422E) : SlotFill;
        }

        private void UpdateTooltip(Vector2 point)
        {
            if (_tooltip == null) return;
            if (_dragging || _movingWindow || !Hit(_windowRect, point))
            {
                _tooltip.SetActive(false);
                _hoveredItem = null;
                return;
            }
            var item = FindCellAt(point)?.Item ?? FindSlotAt(point)?.Item;
            if (item == null)
            {
                _tooltip.SetActive(false);
                _hoveredItem = null;
                return;
            }
            if (_hoveredItem != item)
            {
                _hoveredItem = item;
                _tooltipName.text = Localize(item.m_shared.m_name);
                _tooltipDescription.text = Localize(item.m_shared.m_description);
                _tooltipMeta.text = $"Stack {item.m_stack}   Weight {item.GetWeight(item.m_stack):0.##}" +
                    (item.m_shared.m_useDurability
                        ? $"\nDurability {item.GetDurabilityPercentage() * 100f:0}%" : "");
                _tooltipStats.text = BuildItemStats(item);
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_tooltip.transform.parent, point, null, out var local);
            var parent = (RectTransform)_tooltip.transform.parent;
            _tooltipRect.anchoredPosition = new Vector2(
                Mathf.Clamp(local.x + 18, 0, parent.rect.width - 290),
                Mathf.Clamp(local.y - 18, -parent.rect.height + 155, 0));
            _tooltip.SetActive(true);
        }

        private static string BuildItemStats(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return "";
            var stats = new List<string>();
            float armor = item.GetArmor();
            if (armor > 0f) stats.Add($"Armor {armor:0.#}");
            float damage = item.GetDamage().GetTotalDamage();
            if (damage > 0f) stats.Add($"Damage {damage:0.#}");
            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
            {
                float block = item.GetBaseBlockPower();
                if (block > 0f) stats.Add($"Block {block:0.#}");
            }
            if (item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f || item.m_shared.m_foodEitr > 0f)
            {
                if (item.m_shared.m_food > 0f) stats.Add($"Health +{item.m_shared.m_food:0.#}");
                if (item.m_shared.m_foodStamina > 0f) stats.Add($"Stamina +{item.m_shared.m_foodStamina:0.#}");
                if (item.m_shared.m_foodEitr > 0f) stats.Add($"Eitr +{item.m_shared.m_foodEitr:0.#}");
            }
            if (item.m_shared.m_maxQuality > 1) stats.Add($"Quality {item.m_quality}");
            if (item.m_shared.m_movementModifier != 0f)
                stats.Add($"Move {item.m_shared.m_movementModifier * 100f:+0;-0}%");
            return string.Join("   ", stats);
        }

        private static bool Hit(RectTransform rect, Vector2 point)
        {
            if (rect == null) return false;
            rect.GetWorldCorners(HitCorners);
            var lower = RectTransformUtility.WorldToScreenPoint(null, HitCorners[0]);
            var upper = RectTransformUtility.WorldToScreenPoint(null, HitCorners[2]);
            return point.x >= lower.x && point.x <= upper.x && point.y >= lower.y && point.y <= upper.y;
        }

        private static void DropDragged(Player player, ItemDrop.ItemData item)
        {
            if (player == null || item == null) return;
            if (item.m_equipped && !Toggle(item)) return;
            player.DropItem(player.GetInventory(), item, item.m_stack);
        }

        private static void TrashDragged(Player player, ItemDrop.ItemData item)
        {
            if (player == null || item == null) return;
            if (item.m_equipped && !Toggle(item)) return;
            player.GetInventory().RemoveItem(item);
        }

        private void Refresh(Player player)
        {
            if (player == null || _root == null) return;
            var inventory = player.GetInventory();
            if (_selected != null && !inventory.ContainsItem(_selected)) _selected = null;
            var equipped = inventory.GetEquippedItems();
            if (_fallbackImage != null)
            {
                var portrait = FindEquipped(player, equipped, "chest") != null
                    ? _fallbackCharacter : (_unarmoredCharacter ?? _fallbackCharacter);
                if (_fallbackImage.sprite != portrait) _fallbackImage.sprite = portrait;
            }
            foreach (var slot in _equipment)
            {
                var item = FindEquipped(player, equipped, slot.Category);
                if (slot.Item == item) continue;
                slot.Item = item;
                var sprite = item?.GetIcon();
                slot.Icon.sprite = sprite;
                slot.Icon.enabled = sprite != null;
                slot.Art.color = new Color(1, 1, 1, item == null ? 0.76f : 0f);
            }
            var items = inventory.GetAllItemsInGridOrder();
            _gridItems.Clear();
            foreach (var item in items)
            {
                if (item.m_equipped) continue;
                _gridItems.Add(item);
            }
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                int itemIndex = _scrollRow * GridColumns + i;
                cell.Item = itemIndex < _gridItems.Count ? _gridItems[itemIndex] : null;
                var sprite = cell.Item?.GetIcon();
                if (cell.Icon.sprite != sprite) cell.Icon.sprite = sprite;
                bool showIcon = sprite != null;
                if (cell.Icon.enabled != showIcon) cell.Icon.enabled = showIcon;
                string stack = cell.Item != null && cell.Item.m_stack > 1 ? cell.Item.m_stack.ToString() : "";
                if (cell.Stack.text != stack) cell.Stack.text = stack;
                bool selected = cell.Item == _selected && _selected != null;
                if (cell.Selected != selected)
                {
                    cell.Selected = selected;
                    cell.Background.sprite = selected ? (_highlightSlotSprite ?? _emptySlotSprite) : _emptySlotSprite;
                    cell.Background.color = _emptySlotSprite != null
                        ? selected ? Hex(0xFFE3AE) : Color.white :
                        selected ? Hex(0x51422E) : SlotFill;
                }
            }
            _itemCount.text = $"{items.Count - equipped.Count} / {inventory.GetWidth() * inventory.GetHeight()}";
            var weight = inventory.GetTotalWeight();
            var max = player.GetMaxCarryWeight();
            _weight.text = $"{weight:0.#} / {max:0.#}";
            var rt = _weightFill.rectTransform;
            rt.sizeDelta = new Vector2(189 * Mathf.Clamp01(max > 0 ? weight / max : 0), 6);
        }

        private void RefreshRecipes(Player player)
        {
            if (player == null || _recipeViewport == null) return;
            player.GetAvailableRecipes(ref _recipes);
            _recipes.RemoveAll(recipe => recipe == null || recipe.m_item == null || recipe.m_noCraftOnlyUpgrade);
            if (_selectedRecipe == null || !_recipes.Contains(_selectedRecipe))
                _selectedRecipe = _recipes.Count > 0 ? _recipes[0] : null;
            _recipeScroll = Mathf.Clamp(_recipeScroll, 0, Mathf.Max(0, _recipes.Count - VisibleRecipeRows));
            _emptyRecipes.enabled = _recipes.Count == 0;
            _recipeCount.text = _recipes.Count > VisibleRecipeRows
                ? $"{_recipeScroll + 1}-{Mathf.Min(_recipeScroll + VisibleRecipeRows, _recipes.Count)} / {_recipes.Count}"
                : _recipes.Count.ToString();
            for (int i = 0; i < _recipeRows.Count; i++)
            {
                var row = _recipeRows[i];
                int index = _recipeScroll + i;
                row.Recipe = index < _recipes.Count ? _recipes[index] : null;
                row.Background.enabled = row.Recipe != null;
                row.Icon.enabled = row.Recipe != null;
                row.Name.enabled = row.Recipe != null;
                row.Status.enabled = row.Recipe != null;
                if (row.Recipe == null) continue;
                row.Icon.sprite = row.Recipe.m_item.m_itemData.GetIcon();
                row.Icon.enabled = row.Icon.sprite != null;
                row.Name.text = Localize(row.Recipe.m_item.m_itemData.m_shared.m_name);
                bool canCraft = CanCraftRecipe(player, row.Recipe);
                row.Status.text = canCraft ? "Ready to craft" : "Requirements missing";
                row.Status.color = canCraft ? PaleGold : Muted;
                row.Background.color = row.Recipe == _selectedRecipe ? Hex(0x51422E) : SlotFill;
            }
            _recipeIcon.sprite = _selectedRecipe?.m_item?.m_itemData?.GetIcon();
            _recipeIcon.enabled = _recipeIcon.sprite != null;
            _recipeName.text = _selectedRecipe != null
                ? Localize(_selectedRecipe.m_item.m_itemData.m_shared.m_name) : "No recipes available";
            _recipeDetails.text = _selectedRecipe != null
                ? DescribeRequirements(player, _selectedRecipe) : "Collect materials to discover recipes.";
            var station = _selectedRecipe?.GetRequiredStation(1);
            _stationName.text = station != null
                ? $"{Localize(station.m_name)}  Level {_selectedRecipe.GetRequiredStationLevel(1)}"
                : "Craft by hand";
            bool ready = CanCraftRecipe(player, _selectedRecipe);
            _craftButtonBackground.color = ready ? Hex(0x655030) : Hex(0x30312B);
            _craftButtonText.color = ready ? PaleGold : Muted;
        }

        private static string DescribeRequirements(Player player, Recipe recipe)
        {
            var details = new StringBuilder();
            details.Append("Creates ").Append(recipe.m_amount).Append("\n");
            if (recipe.m_resources != null)
                foreach (var requirement in recipe.m_resources)
                {
                    if (requirement?.m_resItem == null) continue;
                    int needed = requirement.GetAmount(1);
                    if (needed <= 0) continue;
                    string name = requirement.m_resItem.m_itemData.m_shared.m_name;
                    int owned = player.GetInventory().CountItems(name, -1, true);
                    details.Append(Localize(name)).Append("  ").Append(owned).Append(" / ").Append(needed).Append("\n");
                }
            return details.ToString();
        }

        private static bool CanCraftRecipe(Player player, Recipe recipe)
        {
            if (player == null || recipe == null || recipe.m_item == null) return false;
            if (!player.HaveRequirements(recipe, false, 1, 1) && !player.NoCostCheat()) return false;
            return player.GetInventory().CanAddItem(recipe.m_item.gameObject, Mathf.Max(1, recipe.m_amount));
        }

        private void CraftSelected(Player player)
        {
            if (!CanCraftRecipe(player, _selectedRecipe)) return;
            var gui = InventoryGui.instance;
            if (gui == null || DoCraftingMethod == null || CraftRecipeField == null ||
                CraftUpgradeField == null || MultiCraftField == null || CraftVariantField == null)
            {
                Plugin.Log?.LogWarning("Valheim crafting controls are unavailable.");
                return;
            }
            var previousRecipe = CraftRecipeField.GetValue(gui);
            var previousUpgrade = CraftUpgradeField.GetValue(gui);
            var previousMulti = MultiCraftField.GetValue(gui);
            var previousVariant = CraftVariantField.GetValue(gui);
            try
            {
                CraftRecipeField.SetValue(gui, _selectedRecipe);
                CraftUpgradeField.SetValue(gui, null);
                MultiCraftField.SetValue(gui, false);
                CraftVariantField.SetValue(gui, 0);
                DoCraftingMethod.Invoke(gui, new object[] { player });
            }
            catch (Exception error)
            {
                Plugin.Log?.LogWarning("Crafting failed: " + (error.InnerException?.Message ?? error.Message));
            }
            finally
            {
                CraftRecipeField.SetValue(gui, previousRecipe);
                CraftUpgradeField.SetValue(gui, previousUpgrade);
                MultiCraftField.SetValue(gui, previousMulti);
                CraftVariantField.SetValue(gui, previousVariant);
                Refresh(player);
            }
        }

        private void UseSelected()
        {
            var player = Player.m_localPlayer;
            if (_selected == null || player == null) return;
            if (_selected.IsEquipable()) Toggle(_selected);
            else if (_selected.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
                Plugin.Instance?.UseItemFromInventory(player, _selected);
            Refresh(player);
        }

        private static bool Toggle(ItemDrop.ItemData item)
        {
            var player = Player.m_localPlayer;
            if (player == null || item == null || !player.GetInventory().ContainsItem(item)) return false;
            try
            {
                if (player.IsItemEquiped(item))
                {
                    player.UnequipItem(item, true);
                    return !player.IsItemEquiped(item);
                }
                return player.EquipItem(item, true);
            }
            catch (Exception error)
            {
                Plugin.Log?.LogWarning("Could not change equipment: " + error.Message);
                return false;
            }
        }

        private static ItemDrop.ItemData FindEquipped(Player player, List<ItemDrop.ItemData> equipped, string category)
        {
            if (category == "sword" || category == "shield")
            {
                var method = AccessTools.Method(typeof(Humanoid), category == "sword" ? "GetRightItem" : "GetLeftItem");
                return method?.Invoke(player, null) as ItemDrop.ItemData;
            }
            ItemDrop.ItemData.ItemType type;
            switch (category)
            {
                case "helmet": type = ItemDrop.ItemData.ItemType.Helmet; break;
                case "chest": type = ItemDrop.ItemData.ItemType.Chest; break;
                case "legs": type = ItemDrop.ItemData.ItemType.Legs; break;
                case "gloves": type = ItemDrop.ItemData.ItemType.Hands; break;
                case "cloak": type = ItemDrop.ItemData.ItemType.Shoulder; break;
                case "necklace": type = ItemDrop.ItemData.ItemType.Trinket; break;
                case "bracelet": type = ItemDrop.ItemData.ItemType.Utility; break;
                case "ammo": type = ItemDrop.ItemData.ItemType.Ammo; break;
                default: return null; // Reserved slots for later jewelry and boots systems.
            }
            foreach (var item in equipped) if (item.m_shared.m_itemType == type) return item;
            return null;
        }

        private static bool CanEquip(string category, ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null || !item.IsEquipable()) return false;
            var type = item.m_shared.m_itemType;
            switch (category)
            {
                case "helmet": return type == ItemDrop.ItemData.ItemType.Helmet;
                case "chest": return type == ItemDrop.ItemData.ItemType.Chest;
                case "legs": return type == ItemDrop.ItemData.ItemType.Legs;
                case "gloves": return type == ItemDrop.ItemData.ItemType.Hands;
                case "cloak": return type == ItemDrop.ItemData.ItemType.Shoulder;
                case "necklace": return type == ItemDrop.ItemData.ItemType.Trinket;
                case "bracelet": return type == ItemDrop.ItemData.ItemType.Utility;
                case "ammo": return type == ItemDrop.ItemData.ItemType.Ammo;
                case "shield": return type == ItemDrop.ItemData.ItemType.Shield || type == ItemDrop.ItemData.ItemType.OneHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || type == ItemDrop.ItemData.ItemType.Torch;
                case "sword": return type == ItemDrop.ItemData.ItemType.OneHandedWeapon || type == ItemDrop.ItemData.ItemType.Bow || type == ItemDrop.ItemData.ItemType.TwoHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || type == ItemDrop.ItemData.ItemType.Attach_Atgeir || type == ItemDrop.ItemData.ItemType.Tool || type == ItemDrop.ItemData.ItemType.Torch;
                default: return false;
            }
        }

        private Sprite LoadHotbarSlotSprite(string file, int left, int top, int right, int bottom)
        {
            string key = "hotbar-" + file;
            if (_art.TryGetValue(key, out var cached)) return cached;
            string resource = "Lineage2Valheim.HotbarAssets." + file;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    Plugin.Log?.LogWarning("Missing inventory slot art: " + resource);
                    return null;
                }
                using (var memory = new System.IO.MemoryStream())
                {
                    stream.CopyTo(memory);
                    var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(source, memory.ToArray()))
                    {
                        UnityEngine.Object.Destroy(source);
                        return null;
                    }
                    const int size = 128;
                    var small = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    var pixels = new Color[size * size];
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            float u = (left + (x + 0.5f) * (right - left) / size) / source.width;
                            float v = (source.height - bottom + (y + 0.5f) * (bottom - top) / size) / source.height;
                            pixels[y * size + x] = source.GetPixelBilinear(u, v);
                        }
                    small.SetPixels(pixels);
                    small.Apply(false, true);
                    UnityEngine.Object.Destroy(source);
                    _ownedTextures.Add(small);
                    var sprite = Sprite.Create(small, new Rect(0, 0, size, size),
                        new Vector2(0.5f, 0.5f), 100f);
                    _art[key] = sprite;
                    return sprite;
                }
            }
        }

        private Sprite LoadSprite(string name, bool grayscale)
        {
            string key = name + (grayscale ? "-gray" : "");
            if (_art.TryGetValue(key, out var cached)) return cached;
            bool portrait = name.StartsWith("character-preview", StringComparison.Ordinal);
            bool footerIcon = name == "trash" || name == "weight";
            string resource = "Lineage2Valheim.SlotAssets." + name +
                (portrait ? "" : footerIcon ? "-icon-v1" : "-slot-v1") + ".png";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) { Plugin.Log?.LogWarning("Missing inventory art: " + resource); return null; }
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                ImageConversion.LoadImage(source, bytes);
                int size = portrait ? 512 : 128;
                int width = portrait ? Mathf.RoundToInt(size * source.width / (float)source.height) : size;
                var small = new Texture2D(width, size, TextureFormat.RGBA32, false);
                var pixels = new Color[width * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < width; x++)
                    {
                        var c = source.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / size);
                        if (grayscale)
                        {
                            float l = (c.r * 0.24f + c.g * 0.58f + c.b * 0.18f) * 1.32f;
                            c = new Color(l, l, l, c.a);
                        }
                        pixels[y * width + x] = c;
                    }
                small.SetPixels(pixels);
                small.Apply(false, true);
                UnityEngine.Object.Destroy(source);
                _ownedTextures.Add(small);
                var sprite = Sprite.Create(small, new Rect(0, 0, width, size), new Vector2(0.5f, 0.5f), 100f);
                _art[key] = sprite;
                return sprite;
            }
        }

        private static string Localize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                var type = AccessTools.TypeByName("Localization");
                var property = type?.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var instance = property?.GetValue(null, null);
                var method = AccessTools.Method(type, "Localize", new[] { typeof(string) });
                return method?.Invoke(instance, new object[] { value }) as string ?? value;
            }
            catch { return value; }
        }

        private static GameObject NewObject(string name, Transform parent, Rect rect)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rt = (RectTransform)obj.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(rect.x, -rect.y);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            return obj;
        }

        private static Image Panel(string name, Transform parent, Rect rect, Color color)
        {
            var image = NewObject(name, parent, rect).AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static Image NewImage(string name, Transform parent, Rect rect, Sprite sprite)
        {
            var image = Panel(name, parent, rect, Color.white);
            image.sprite = sprite;
            return image;
        }

        private Text Label(string value, Transform parent, Rect rect, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
        {
            var label = NewObject("Text", parent, rect).AddComponent<Text>();
            label.font = _font;
            label.fontSize = size;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.alignment = align;
            label.color = color;
            label.text = value;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private static void Border(Transform parent, Rect rect, Color color, int thick)
        {
            var a = Panel("Top Border", parent, new Rect(rect.x, rect.y, rect.width, thick), color);
            var b = Panel("Bottom Border", parent, new Rect(rect.x, rect.y + rect.height - thick, rect.width, thick), color);
            var c = Panel("Left Border", parent, new Rect(rect.x, rect.y, thick, rect.height), color);
            var d = Panel("Right Border", parent, new Rect(rect.x + rect.width - thick, rect.y, thick, rect.height), color);
            a.raycastTarget = b.raycastTarget = c.raycastTarget = d.raycastTarget = false;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static Color Hex(int value) => new Color(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f, 1);

        private sealed class SlotView
        {
            internal string Category;
            internal int Occurrence;
            internal Image Art, Icon;
            internal RectTransform HitArea;
            internal ItemDrop.ItemData Item;
        }

        private sealed class CellView
        {
            internal Image Background, Icon;
            internal Text Stack;
            internal RectTransform HitArea;
            internal ItemDrop.ItemData Item;
            internal bool Selected;
        }

        private sealed class RecipeRowView
        {
            internal RectTransform HitArea;
            internal Image Background, Icon;
            internal Text Name, Status;
            internal Recipe Recipe;
        }
    }
}
