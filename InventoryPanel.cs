using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Renders and operates on the local player's real Valheim inventory.</summary>
    internal sealed class InventoryPanel
    {
        private const int PreviewLayer = 31;
        private const float PanelGap = 12f;

        private readonly List<ItemDrop.ItemData> _items = new List<ItemDrop.ItemData>();
        private readonly List<ItemDrop.ItemData> _equippedItems = new List<ItemDrop.ItemData>();
        private ItemDrop.ItemData _selectedItem;
        private ItemDrop.ItemData _dragItem;
        private bool _isDraggingItem;
        private float _lastClickTime;
        private ItemDrop.ItemData _lastClickedItem;
        private Vector2 _gridScroll;
        private GUIStyle _headingStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _itemCountStyle;
        private GUIStyle _selectedNameStyle;
        private GUIStyle _slotLabelStyle;
        private Texture2D _pixel;
        private Texture2D _placeholderTexture;
        private GameObject _previewModel;
        private Transform _sourceVisual;
        private Camera _previewCamera;
        private Light _previewLight;
        private RenderTexture _previewTexture;
        private bool _previewFailed;
        private readonly Dictionary<Transform, Transform> _previewTransforms = new Dictionary<Transform, Transform>();

        internal void Update(Texture2D pixel)
        {
            _pixel = pixel;
            if (_previewCamera != null && _previewModel != null)
                RenderPreview();
        }

        internal void Draw(Rect bounds, Texture2D pixel)
        {
            _pixel = pixel;
            EnsureStyles();

            var player = Player.m_localPlayer;
            if (player == null)
            {
                GUI.Label(new Rect(bounds.x + 16f, bounds.y + 16f, bounds.width - 32f, 30f), "Player inventory is unavailable.", _smallStyle);
                return;
            }

            if (Event.current.type == EventType.MouseDrag && _dragItem != null)
                _isDraggingItem = true;

            EnsurePreview(player);
            var inventory = player.GetInventory();
            _items.Clear();
            _items.AddRange(inventory.GetAllItemsInGridOrder());
            _equippedItems.Clear();
            _equippedItems.AddRange(inventory.GetEquippedItems());
            if (_selectedItem != null && !inventory.ContainsItem(_selectedItem))
                _selectedItem = null;

            var leftWidth = Mathf.Clamp(bounds.width * 0.30f, 205f, 270f);
            var left = new Rect(bounds.x, bounds.y, leftWidth, bounds.height);
            var right = new Rect(left.xMax + PanelGap, bounds.y, bounds.width - leftWidth - PanelGap, bounds.height);
            DrawCharacterPanel(left, player);
            DrawItemsPanel(right, player, inventory);
            if (Event.current.type == EventType.MouseUp)
            {
                _dragItem = null;
                _isDraggingItem = false;
            }
        }

        internal void Close()
        {
            if (_previewModel != null)
                UnityEngine.Object.Destroy(_previewModel);
            if (_previewCamera != null)
                UnityEngine.Object.Destroy(_previewCamera.gameObject);
            if (_previewLight != null)
                UnityEngine.Object.Destroy(_previewLight.gameObject);
            if (_previewTexture != null)
            {
                _previewTexture.Release();
                UnityEngine.Object.Destroy(_previewTexture);
            }

            _previewCamera = null;
            _previewLight = null;
            _previewModel = null;
            _sourceVisual = null;
            _previewTexture = null;
            _previewFailed = false;
            _previewTransforms.Clear();
        }

        private void DrawCharacterPanel(Rect bounds, Player player)
        {
            DrawRect(bounds, new Color(0.035f, 0.044f, 0.048f, 0.98f));
            DrawRect(new Rect(bounds.x, bounds.y, bounds.width, 2f), new Color(0.58f, 0.46f, 0.28f, 0.9f));
            GUI.Label(new Rect(bounds.x + 10f, bounds.y + 7f, bounds.width - 20f, 20f), "CHARACTER", _headingStyle);

            var stage = new Rect(bounds.x + 5f, bounds.y + 32f, bounds.width - 10f, Mathf.Min(360f, Mathf.Max(210f, bounds.height - 102f)));
            var slotColumnWidth = Mathf.Clamp((stage.width - 100f) * 0.5f, 42f, 60f);
            var center = new Rect(stage.x + slotColumnWidth + 4f, stage.y, stage.width - slotColumnWidth * 2f - 8f, stage.height);
            DrawRect(center, new Color(0.025f, 0.032f, 0.035f, 1f));
            if (_previewTexture != null)
                GUI.DrawTexture(new Rect(center.x + 1f, center.y + 1f, center.width - 2f, center.height - 2f), _previewTexture, ScaleMode.ScaleToFit, true);
            else
                DrawVikingSilhouette(center);

            var armorSlots = new[]
            {
                new EquipmentSlot("Head", FindEquipped(ItemDrop.ItemData.ItemType.Helmet), EquipmentKind.Helmet),
                new EquipmentSlot("Chest", FindEquipped(ItemDrop.ItemData.ItemType.Chest), EquipmentKind.Chest),
                new EquipmentSlot("Legs", FindEquipped(ItemDrop.ItemData.ItemType.Legs), EquipmentKind.Legs),
                new EquipmentSlot("Gloves", FindEquipped(ItemDrop.ItemData.ItemType.Hands), EquipmentKind.Hands),
                new EquipmentSlot("Boots", null, EquipmentKind.Boots),
                new EquipmentSlot("Back", FindEquipped(ItemDrop.ItemData.ItemType.Shoulder), EquipmentKind.Shoulder)
            };
            var jewelrySlots = new[]
            {
                new EquipmentSlot("Neck", FindEquipped(ItemDrop.ItemData.ItemType.Trinket), EquipmentKind.Trinket),
                new EquipmentSlot("Ear", null, EquipmentKind.Earring),
                new EquipmentSlot("Ear", null, EquipmentKind.Earring),
                new EquipmentSlot("Ring", null, EquipmentKind.Ring),
                new EquipmentSlot("Ring", null, EquipmentKind.Ring),
                new EquipmentSlot("Bracelet", FindEquipped(ItemDrop.ItemData.ItemType.Utility), EquipmentKind.Utility)
            };
            DrawEquipmentColumn(armorSlots, new Rect(stage.x, stage.y, slotColumnWidth, stage.height), player);
            DrawEquipmentColumn(jewelrySlots, new Rect(stage.xMax - slotColumnWidth, stage.y, slotColumnWidth, stage.height), player);

            var weaponY = stage.yMax + 5f;
            var weaponWidth = Mathf.Min(48f, (bounds.width - 28f) / 3f);
            var weaponGap = 7f;
            var weaponTotal = weaponWidth * 3f + weaponGap * 2f;
            var weaponStart = bounds.center.x - weaponTotal * 0.5f;
            weaponY += 2f;
            DrawEquipmentSlot(new EquipmentSlot("Weapon", GetHandItem(player, "GetRightItem"), EquipmentKind.Weapon),
                new Rect(weaponStart, weaponY, weaponWidth, 48f), player);
            DrawEquipmentSlot(new EquipmentSlot("Offhand", GetHandItem(player, "GetLeftItem"), EquipmentKind.Offhand),
                new Rect(weaponStart + weaponWidth + weaponGap, weaponY, weaponWidth, 48f), player);
            DrawEquipmentSlot(new EquipmentSlot("Ammo", FindEquipped(ItemDrop.ItemData.ItemType.Ammo), EquipmentKind.Ammo),
                new Rect(weaponStart + 2f * (weaponWidth + weaponGap), weaponY, weaponWidth, 48f), player);
        }

        private void DrawEquipmentColumn(EquipmentSlot[] slots, Rect bounds, Player player)
        {
            var rowHeight = bounds.height / slots.Length;
            for (var i = 0; i < slots.Length; i++)
            {
                var row = new Rect(bounds.x, bounds.y + i * rowHeight, bounds.width, rowHeight - 1f);
                DrawEquipmentSlot(slots[i], row, player);
            }
        }

        private void DrawEquipmentSlot(EquipmentSlot slot, Rect rect, Player player)
        {
            var hovered = rect.Contains(Event.current.mousePosition);
            var hasItem = slot.Item != null;
            var color = hovered ? new Color(0.26f, 0.23f, 0.16f, 1f) : new Color(0.10f, 0.12f, 0.12f, 0.98f);
            DrawRect(rect, color);
            DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), new Color(0.45f, 0.38f, 0.25f, hasItem ? 0.95f : 0.6f));
            if (hasItem)
            {
                var iconSize = Mathf.Max(18f, Mathf.Min(rect.width - 10f, rect.height - 10f));
                DrawItemIcon(slot.Item.GetIcon(), new Rect(rect.center.x - iconSize * 0.5f,
                    rect.center.y - iconSize * 0.5f, iconSize, iconSize));
            }
            DrawSlotGlyph(slot.Kind, rect, hasItem ? 0.28f : 0.62f);

            var current = Event.current;
            if (_isDraggingItem && _dragItem != null && current.type == EventType.MouseUp && rect.Contains(current.mousePosition))
            {
                if (CanEquipInSlot(slot.Kind, _dragItem) && ToggleEquipped(player, _dragItem))
                {
                    _selectedItem = _dragItem;
                    RefreshPreview(player);
                }
                _dragItem = null;
                _isDraggingItem = false;
                current.Use();
                return;
            }

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                if (slot.Item != null)
                    _selectedItem = slot.Item;
            }

            if (hovered)
                GUI.Label(new Rect(rect.x, rect.yMax + 1f, 170f, 18f), hasItem ? DisplayName(slot.Item) : slot.Label, _smallStyle);
        }

        private void DrawItemsPanel(Rect bounds, Player player, Inventory inventory)
        {
            DrawRect(bounds, new Color(0.035f, 0.044f, 0.048f, 0.98f));
            DrawRect(new Rect(bounds.x, bounds.y, bounds.width, 2f), new Color(0.58f, 0.46f, 0.28f, 0.9f));
            GUI.Label(new Rect(bounds.x + 10f, bounds.y + 8f, bounds.width - 20f, 22f), "INVENTORY", _headingStyle);

            var cols = Mathf.Max(1, inventory.GetWidth());
            var rows = Mathf.Max(1, inventory.GetHeight());
            var gridArea = new Rect(bounds.x + 10f, bounds.y + 36f, bounds.width - 20f, Mathf.Max(100f, bounds.height - 142f));
            var cell = Mathf.Clamp(Mathf.Min((gridArea.width - 8f) / cols, 50f), 32f, 50f);
            var gridWidth = cols * cell;
            var gridHeight = rows * cell;
            var scrollContent = new Rect(0f, 0f, Mathf.Max(gridArea.width - 10f, gridWidth), gridHeight + 6f);

            _gridScroll = GUI.BeginScrollView(gridArea, _gridScroll, scrollContent, false, rows * cell > gridArea.height);
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < cols; x++)
                {
                    var tile = new Rect(4f + x * cell, 3f + (rows - 1 - y) * cell, cell - 3f, cell - 3f);
                    var item = inventory.GetItemAt(x, y);
                    if (item != null && item.m_equipped)
                        item = null;
                    DrawItemSlot(tile, item, player);
                }
            }
            GUI.EndScrollView();

            var detailY = gridArea.yMax + 7f;
            var detailHeight = Mathf.Max(45f, bounds.yMax - detailY - 34f);
            var detail = new Rect(bounds.x + 10f, detailY, bounds.width - 20f, detailHeight);
            DrawRect(detail, new Color(0.025f, 0.032f, 0.035f, 1f));
            if (_selectedItem != null)
            {
                GUI.Label(new Rect(detail.x + 8f, detail.y + 4f, detail.width - 16f, 18f), DisplayName(_selectedItem), _selectedNameStyle);
                var summary = $"{_selectedItem.m_shared.m_itemType}   Stack {_selectedItem.m_stack}   Weight {_selectedItem.GetWeight(_selectedItem.m_stack):0.##}";
                if (_selectedItem.m_shared.m_useDurability)
                    summary += $"   Durability {_selectedItem.GetDurabilityPercentage() * 100f:0}%";
                GUI.Label(new Rect(detail.x + 8f, detail.y + 23f, detail.width - 16f, 18f), summary, _smallStyle);
                var description = LocalizationText(_selectedItem.m_shared.m_description);
                if (!string.IsNullOrEmpty(description) && detail.height > 50f)
                    GUI.Label(new Rect(detail.x + 8f, detail.y + 40f, detail.width - 16f, detail.height - 42f), description, _smallStyle);
            }
            else
            {
                GUI.Label(new Rect(detail.x + 8f, detail.y + 5f, detail.width - 16f, 20f), "Select an item to view its details.", _smallStyle);
            }

            var footerY = bounds.yMax - 25f;
            GUI.Label(new Rect(bounds.x + 8f, footerY, bounds.width * 0.46f, 20f),
                $"{inventory.NrOfItemsIncludingStacks()} items   {inventory.GetTotalWeight():0.#} / {player.GetMaxCarryWeight():0} weight", _smallStyle);
            var actionWidth = Mathf.Min(88f, Mathf.Max(65f, bounds.width * 0.23f));
            var dropRect = new Rect(bounds.xMax - actionWidth - 6f, footerY - 2f, actionWidth, 23f);
            var useRect = new Rect(dropRect.x - actionWidth - 5f, dropRect.y, actionWidth, 23f);
            var canUseSelected = _selectedItem != null && (_selectedItem.IsEquipable() ||
                _selectedItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable);
            GUI.enabled = canUseSelected;
            if (GUI.Button(useRect, _selectedItem != null && _selectedItem.m_equipped ? "Unequip" :
                (_selectedItem != null && _selectedItem.IsEquipable() ? "Equip" : "Use")))
                UseOrEquipSelected(player, inventory);
            GUI.enabled = _selectedItem != null;
            if (GUI.Button(dropRect, "Drop 1"))
            {
                player.DropItem(inventory, _selectedItem, 1);
                _selectedItem = null;
            }
            GUI.enabled = true;
        }

        private void DrawItemSlot(Rect rect, ItemDrop.ItemData item, Player player)
        {
            var selected = item != null && item == _selectedItem;
            var hovered = rect.Contains(Event.current.mousePosition);
            DrawRect(rect, selected ? new Color(0.49f, 0.40f, 0.25f, 1f) :
                (hovered ? new Color(0.24f, 0.28f, 0.27f, 1f) : new Color(0.12f, 0.15f, 0.15f, 0.98f)));
            DrawRect(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), new Color(0.035f, 0.045f, 0.048f, 1f));
            if (item != null)
            {
                DrawItemIcon(item.GetIcon(), new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f));
                if (item.m_stack > 1)
                    GUI.Label(new Rect(rect.x + 2f, rect.yMax - 17f, rect.width - 4f, 15f), item.m_stack.ToString(), _itemCountStyle);
                if (item.m_equipped)
                    DrawRect(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, 2f), new Color(0.94f, 0.74f, 0.30f, 1f));

                var current = Event.current;
                if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
                {
                    _selectedItem = item;
                    _dragItem = item.IsEquipable() ? item : null;
                    _isDraggingItem = false;
                }

                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    var doubleClick = item == _lastClickedItem && Time.unscaledTime - _lastClickTime < 0.32f;
                    _selectedItem = item;
                    if (doubleClick)
                        UseOrEquipSelected(Player.m_localPlayer, Player.m_localPlayer.GetInventory());
                    _lastClickedItem = item;
                    _lastClickTime = Time.unscaledTime;
                }
            }
        }

        private void UseOrEquipSelected(Player player, Inventory inventory)
        {
            if (_selectedItem == null || player == null)
                return;

            if (_selectedItem.IsEquipable())
            {
                if (ToggleEquipped(player, _selectedItem))
                    RefreshPreview(player);
            }
            else if (_selectedItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
            {
                Plugin.Instance?.UseItemFromInventory(player, _selectedItem);
            }
        }

        private ItemDrop.ItemData FindEquipped(ItemDrop.ItemData.ItemType itemType)
        {
            for (var i = 0; i < _equippedItems.Count; i++)
            {
                var item = _equippedItems[i];
                if (item.m_shared.m_itemType == itemType)
                    return item;
            }
            return null;
        }

        private void EnsurePreview(Player player)
        {
            if (_previewModel != null || _previewFailed || player == null)
                return;

            try
            {
                _sourceVisual = player.transform;
                _previewModel = BuildRendererOnlyModel(_sourceVisual, null);
                BindPreviewSkinnedMeshes();
                _previewModel.name = "Lineage2Valheim.InventoryPreviewModel";
                _previewModel.hideFlags = HideFlags.HideAndDontSave;
                _previewModel.transform.position = new Vector3(10000f, 10000f, 10000f);
                _previewModel.transform.rotation = player.transform.rotation;

                var cameraObject = new GameObject("Lineage2Valheim.InventoryPreviewCamera");
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                _previewCamera = cameraObject.AddComponent<Camera>();
                _previewCamera.enabled = false;
                _previewCamera.clearFlags = CameraClearFlags.SolidColor;
                _previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _previewCamera.cullingMask = 1 << PreviewLayer;
                _previewCamera.orthographic = true;
                _previewCamera.aspect = 0.55f;
                _previewCamera.orthographicSize = 1.1f;
                _previewCamera.nearClipPlane = 0.05f;
                _previewCamera.farClipPlane = 10f;

                var lightObject = new GameObject("Lineage2Valheim.InventoryPreviewLight");
                lightObject.hideFlags = HideFlags.HideAndDontSave;
                _previewLight = lightObject.AddComponent<Light>();
                _previewLight.type = LightType.Directional;
                _previewLight.intensity = 1.2f;
                _previewLight.cullingMask = 1 << PreviewLayer;
                lightObject.transform.rotation = Quaternion.Euler(35f, -25f, 0f);

                _previewTexture = new RenderTexture(256, 512, 16, RenderTextureFormat.ARGB32)
                {
                    name = "Lineage2Valheim.InventoryPreviewTexture",
                    antiAliasing = 2
                };
                _previewTexture.Create();
                _previewCamera.targetTexture = _previewTexture;
                _previewCamera.transform.position = _previewModel.transform.position + _previewModel.transform.forward * 3f + Vector3.up;
                _previewCamera.transform.LookAt(_previewModel.transform.position + Vector3.up);
                RenderPreview();
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning($"Could not create inventory character preview: {exception.Message}");
                Close();
                _previewFailed = true;
            }
        }

        private void RenderPreview()
        {
            if (_sourceVisual == null || _previewModel == null || _previewCamera == null)
                return;
            foreach (var pair in _previewTransforms)
            {
                if (pair.Key == null || pair.Value == null || pair.Key == _sourceVisual)
                    continue;
                pair.Value.localPosition = pair.Key.localPosition;
                pair.Value.localRotation = pair.Key.localRotation;
                pair.Value.localScale = pair.Key.localScale;
            }
            _previewCamera.transform.position = _previewModel.transform.position + _previewModel.transform.forward * 3f + Vector3.up;
            _previewCamera.transform.LookAt(_previewModel.transform.position + Vector3.up);
            _previewCamera.Render();
        }

        private GameObject BuildRendererOnlyModel(Transform source, Transform parent)
        {
            var clone = new GameObject(source.name);
            clone.layer = PreviewLayer;
            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = source.localPosition;
            clone.transform.localRotation = source.localRotation;
            clone.transform.localScale = source.localScale;
            clone.SetActive(true);
            _previewTransforms[source] = clone.transform;

            var skinned = source.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null && skinned.sharedMesh != null)
            {
                var renderer = clone.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = skinned.sharedMesh;
                renderer.sharedMaterials = skinned.sharedMaterials;
                renderer.localBounds = skinned.localBounds;
                renderer.updateWhenOffscreen = true;
                renderer.enabled = true;
            }
            else
            {
                var filter = source.GetComponent<MeshFilter>();
                var meshRenderer = source.GetComponent<MeshRenderer>();
                if (filter != null && filter.sharedMesh != null && meshRenderer != null)
                {
                    clone.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var renderer = clone.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = meshRenderer.sharedMaterials;
                    renderer.enabled = true;
                }
            }

            for (var i = 0; i < source.childCount; i++)
                BuildRendererOnlyModel(source.GetChild(i), clone.transform);
            return clone;
        }

        private void BindPreviewSkinnedMeshes()
        {
            foreach (var pair in _previewTransforms)
            {
                var sourceRenderer = pair.Key.GetComponent<SkinnedMeshRenderer>();
                var previewRenderer = pair.Value.GetComponent<SkinnedMeshRenderer>();
                if (sourceRenderer == null || previewRenderer == null)
                    continue;
                var sourceBones = sourceRenderer.bones;
                var previewBones = new Transform[sourceBones.Length];
                for (var i = 0; i < sourceBones.Length; i++)
                    if (sourceBones[i] != null && _previewTransforms.TryGetValue(sourceBones[i], out var bone))
                        previewBones[i] = bone;
                previewRenderer.bones = previewBones;
                if (sourceRenderer.rootBone != null && _previewTransforms.TryGetValue(sourceRenderer.rootBone, out var rootBone))
                    previewRenderer.rootBone = rootBone;
            }
        }

        private void DrawSlotGlyph(EquipmentKind kind, Rect rect, float opacity)
        {
            var center = rect.center;
            var scale = Mathf.Clamp(Mathf.Min(rect.width, rect.height) / 56f, 0.65f, 1.2f);
            var c = new Color(0.72f, 0.70f, 0.62f, opacity);
            var oldMatrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), center);
            switch (kind)
            {
                case EquipmentKind.Helmet:
                    DrawRect(new Rect(center.x - 7f, center.y - 8f, 14f, 12f), c);
                    DrawRect(new Rect(center.x - 9f, center.y + 3f, 18f, 2f), c);
                    break;
                case EquipmentKind.Chest:
                    DrawRect(new Rect(center.x - 7f, center.y - 8f, 14f, 16f), c);
                    DrawRect(new Rect(center.x - 11f, center.y - 6f, 4f, 10f), c);
                    DrawRect(new Rect(center.x + 7f, center.y - 6f, 4f, 10f), c);
                    break;
                case EquipmentKind.Legs:
                    DrawRect(new Rect(center.x - 8f, center.y - 8f, 16f, 6f), c);
                    DrawRect(new Rect(center.x - 7f, center.y - 2f, 6f, 11f), c);
                    DrawRect(new Rect(center.x + 1f, center.y - 2f, 6f, 11f), c);
                    break;
                case EquipmentKind.Boots:
                    DrawRect(new Rect(center.x - 8f, center.y - 7f, 7f, 13f), c);
                    DrawRect(new Rect(center.x + 1f, center.y - 7f, 7f, 13f), c);
                    DrawRect(new Rect(center.x - 10f, center.y + 4f, 10f, 4f), c);
                    DrawRect(new Rect(center.x, center.y + 4f, 10f, 4f), c);
                    break;
                case EquipmentKind.Hands:
                    DrawRect(new Rect(center.x - 8f, center.y - 3f, 16f, 7f), c);
                    DrawRect(new Rect(center.x - 10f, center.y - 8f, 5f, 7f), c);
                    DrawRect(new Rect(center.x + 5f, center.y - 8f, 5f, 7f), c);
                    break;
                case EquipmentKind.Shoulder:
                    DrawRect(new Rect(center.x - 8f, center.y - 6f, 16f, 12f), c);
                    DrawRect(new Rect(center.x - 11f, center.y - 8f, 6f, 5f), c);
                    DrawRect(new Rect(center.x + 5f, center.y - 8f, 6f, 5f), c);
                    break;
                case EquipmentKind.Trinket:
                case EquipmentKind.Utility:
                    DrawRect(new Rect(center.x - 7f, center.y - 7f, 14f, 14f), c);
                    DrawRect(new Rect(center.x - 4f, center.y - 4f, 8f, 8f), new Color(0.10f, 0.12f, 0.12f, 1f));
                    break;
                case EquipmentKind.Earring:
                    DrawRect(new Rect(center.x - 5f, center.y - 7f, 10f, 2f), c);
                    DrawRect(new Rect(center.x - 1f, center.y - 5f, 2f, 4f), c);
                    DrawRect(new Rect(center.x - 4f, center.y - 1f, 8f, 8f), c);
                    DrawRect(new Rect(center.x - 2f, center.y + 1f, 4f, 4f), new Color(0.10f, 0.12f, 0.12f, 1f));
                    break;
                case EquipmentKind.Ring:
                    DrawRect(new Rect(center.x - 7f, center.y - 5f, 14f, 10f), c);
                    DrawRect(new Rect(center.x - 4f, center.y - 2f, 8f, 4f), new Color(0.10f, 0.12f, 0.12f, 1f));
                    break;
                case EquipmentKind.Weapon:
                    DrawGlyphBar(center.x - 7f, center.y + 8f, center.x + 7f, center.y - 8f, c);
                    DrawRect(new Rect(center.x - 2f, center.y + 2f, 10f, 2f), c);
                    break;
                case EquipmentKind.Offhand:
                    DrawRect(new Rect(center.x - 7f, center.y - 8f, 14f, 16f), c);
                    DrawRect(new Rect(center.x - 3f, center.y - 4f, 6f, 8f), new Color(0.10f, 0.12f, 0.12f, 1f));
                    break;
                case EquipmentKind.Ammo:
                    DrawGlyphBar(center.x - 7f, center.y + 8f, center.x + 7f, center.y - 8f, c);
                    DrawGlyphBar(center.x - 4f, center.y + 8f, center.x + 6f, center.y - 2f, c);
                    break;
                default:
                    GUI.Label(new Rect(center.x - 10f, center.y - 10f, 20f, 20f), "◇", _slotLabelStyle);
                    break;
            }
            GUI.matrix = oldMatrix;
        }

        private void DrawGlyphBar(float x1, float y1, float x2, float y2, Color color)
        {
            var length = Vector2.Distance(new Vector2(x1, y1), new Vector2(x2, y2));
            var angle = Mathf.Atan2(y2 - y1, x2 - x1) * Mathf.Rad2Deg;
            var center = new Vector2((x1 + x2) * 0.5f, (y1 + y2) * 0.5f);
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, center);
            DrawRect(new Rect(center.x - length * 0.5f, center.y - 1f, length, 2f), color);
            GUI.matrix = old;
        }

        private bool CanEquipInSlot(EquipmentKind slot, ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null || !item.IsEquipable())
                return false;
            var type = item.m_shared.m_itemType;
            switch (slot)
            {
                case EquipmentKind.Helmet: return type == ItemDrop.ItemData.ItemType.Helmet;
                case EquipmentKind.Chest: return type == ItemDrop.ItemData.ItemType.Chest;
                case EquipmentKind.Legs: return type == ItemDrop.ItemData.ItemType.Legs;
                case EquipmentKind.Hands: return type == ItemDrop.ItemData.ItemType.Hands;
                case EquipmentKind.Shoulder: return type == ItemDrop.ItemData.ItemType.Shoulder;
                case EquipmentKind.Trinket: return type == ItemDrop.ItemData.ItemType.Trinket;
                case EquipmentKind.Utility: return type == ItemDrop.ItemData.ItemType.Utility;
                case EquipmentKind.Ammo: return type == ItemDrop.ItemData.ItemType.Ammo;
                case EquipmentKind.Weapon:
                    return type == ItemDrop.ItemData.ItemType.OneHandedWeapon || type == ItemDrop.ItemData.ItemType.Bow ||
                        type == ItemDrop.ItemData.ItemType.TwoHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ||
                        type == ItemDrop.ItemData.ItemType.Attach_Atgeir || type == ItemDrop.ItemData.ItemType.Tool ||
                        type == ItemDrop.ItemData.ItemType.Torch;
                case EquipmentKind.Offhand:
                    return type == ItemDrop.ItemData.ItemType.Shield || type == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
                        type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft || type == ItemDrop.ItemData.ItemType.Torch;
                default: return false;
            }
        }

        private void RefreshPreview(Player player)
        {
            Close();
            EnsurePreview(player);
        }

        private bool ToggleEquipped(Player player, ItemDrop.ItemData item)
        {
            var method = AccessTools.Method(typeof(Player), "ToggleEquipped", new[] { typeof(ItemDrop.ItemData) });
            if (method == null)
                return false;
            return method.Invoke(player, new object[] { item }) is bool result && result;
        }

        private ItemDrop.ItemData GetHandItem(Player player, string methodName)
        {
            var method = AccessTools.Method(typeof(Humanoid), methodName);
            return method?.Invoke(player, null) as ItemDrop.ItemData;
        }

        private void DrawVikingSilhouette(Rect bounds)
        {
            if (_placeholderTexture == null)
            {
                _placeholderTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _placeholderTexture.SetPixel(0, 0, Color.white);
                _placeholderTexture.Apply();
            }

            var color = new Color(0.46f, 0.49f, 0.45f, 0.92f);
            var center = bounds.center.x;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center - 20f, bounds.y + 27f, 40f, 40f), _placeholderTexture);
            GUI.DrawTexture(new Rect(center - 13f, bounds.y + 65f, 26f, 16f), _placeholderTexture);
            GUI.DrawTexture(new Rect(center - 43f, bounds.y + 77f, 86f, 110f), _placeholderTexture);
            GUI.DrawTexture(new Rect(center - 40f, bounds.y + 180f, 30f, 48f), _placeholderTexture);
            GUI.DrawTexture(new Rect(center + 10f, bounds.y + 180f, 30f, 48f), _placeholderTexture);
            GUI.color = Color.white;
        }

        private string DisplayName(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
                return string.Empty;
            return LocalizationText(item.m_shared.m_name);
        }

        private string LocalizationText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            try
            {
                var localizationType = AccessTools.TypeByName("Localization");
                var instanceProperty = localizationType?.GetProperty("instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var instance = instanceProperty?.GetValue(null, null);
                var method = localizationType?.GetMethod("Localize", new[] { typeof(string) });
                if (instance != null && method != null)
                    return method.Invoke(instance, new object[] { text }) as string ?? text;
            }
            catch
            {
                // Fall back to the item's stored display string when localization is unavailable.
            }

            return text.StartsWith("$item_", StringComparison.Ordinal) ? text.Substring(6).Replace('_', ' ') : text;
        }

        private void DrawItemIcon(Sprite sprite, Rect rect)
        {
            if (sprite == null || sprite.texture == null)
                return;

            var texture = sprite.texture;
            var spriteRect = sprite.textureRect;
            var uv = new Rect(spriteRect.x / texture.width, spriteRect.y / texture.height,
                spriteRect.width / texture.width, spriteRect.height / texture.height);
            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
        }

        private void EnsureStyles()
        {
            if (_headingStyle != null)
                return;
            _headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            _headingStyle.normal.textColor = new Color(0.82f, 0.70f, 0.47f, 1f);
            _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _smallStyle.normal.textColor = new Color(0.77f, 0.80f, 0.78f, 1f);
            _itemCountStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            _itemCountStyle.normal.textColor = Color.white;
            _selectedNameStyle = new GUIStyle(_smallStyle) { fontStyle = FontStyle.Bold, fontSize = 12 };
            _selectedNameStyle.normal.textColor = new Color(0.94f, 0.88f, 0.72f, 1f);
            _slotLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 8, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            _slotLabelStyle.normal.textColor = new Color(0.83f, 0.78f, 0.66f, 1f);
        }

        private void DrawRect(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _pixel, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        private enum EquipmentKind
        {
            Unsupported,
            Helmet,
            Chest,
            Legs,
            Hands,
            Shoulder,
            Boots,
            Trinket,
            Utility,
            Earring,
            Ring,
            Weapon,
            Offhand,
            Ammo
        }

        private struct EquipmentSlot
        {
            internal readonly string Label;
            internal readonly ItemDrop.ItemData Item;
            internal readonly EquipmentKind Kind;
            internal EquipmentSlot(string label, ItemDrop.ItemData item, EquipmentKind kind)
            {
                Label = label;
                Item = item;
                Kind = kind;
            }
        }
    }
}
