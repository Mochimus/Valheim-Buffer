using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Player-owned skill shortcuts and a separate display of currently active buffs.</summary>
    internal sealed class LineageHotbars
    {
        private const int Columns = 12;
        private const int MaxRows = 3;
        private const float SlotSize = 43f;
        private const float Gap = 3f;
        private const float GripHeight = 16f;
        private const float GripWidth = 74f;
        private const float ControlWidth = 38f;
        private const float FrameCorner = 15f;
        private const string Prefix = "Lineage2Valheim.Hotbar.";
        private const string ItemSlotPrefix = "item:";
        private const string ActiveSkillSlotPrefix = "action:";
        private static readonly KeyCode[] Keys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0
        };
        private readonly string[] _slots = new string[Columns * MaxRows];
        private readonly List<BuffSkillDefinition> _activeBuffs = new List<BuffSkillDefinition>();
        private Player _owner;
        private Rect _skillRect;
        private Rect _buffRect;
        private Vector2 _skillPosition;
        private Vector2 _buffPosition;
        private Vector2 _grabOffset;
        private string _incomingSkill;
        private int _pressedSlot = -1;
        private int _draggedSlot = -1;
        private int _rows = 1;
        private bool _movingSkill;
        private bool _movingBuff;
        private bool _pressedCycle;
        private GUIStyle _keyStyle;
        private GUIStyle _timerStyle;
        private GUIStyle _buffTimerStyle;
        private GUIStyle _tipStyle;
        private GUIStyle _buffTipTitleStyle;
        private GUIStyle _buffTipBodyStyle;
        private Art _emptySlot;
        private Art _highlightSlot;
        private Art _panelFrame;
        private Art _movementGrip;
        private Art _rowControl;

        private sealed class Art
        {
            internal Texture2D Texture;
            internal Rect Source;
        }

        internal bool IsDraggingSkill => _incomingSkill != null;

        internal void DrawStyledFrame(Rect rect, Texture2D pixel) => DrawBarFrame(rect, pixel);
        internal void DrawStyledSlot(Rect rect, Texture2D pixel) => DrawSlot(rect, false, pixel);
        internal void DrawVerticalGrip(Rect rect, Texture2D pixel)
        {
            var previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(90f, rect.center);
            DrawGrip(new Rect(rect.center.x - rect.height * 0.5f,
                rect.center.y - rect.width * 0.5f, rect.height, rect.width), pixel);
            GUI.matrix = previous;
        }

        internal bool TryGetPanelFrame(out Texture2D texture, out Rect source)
        {
            texture = _panelFrame?.Texture;
            source = _panelFrame?.Source ?? default;
            return texture != null;
        }

        internal void PrepareArt()
        {
            _emptySlot = LoadArt("empty-slot-v1.png", 203, 155, 1125, 1032);
            _highlightSlot = LoadArt("highlight-slot-v1.png", 204, 156, 1123, 1030);
            _panelFrame = LoadArt("panel-frame-v1.png", 26, 29, 1228, 1201);
            _movementGrip = LoadArt("movement-grip-v1.png", 298, 228, 1681, 538);
            _rowControl = LoadArt("row-control-v1.png", 148, 262, 646, 1682);
        }

        internal void DisposeArt()
        {
            foreach (var art in new[] { _emptySlot, _highlightSlot, _panelFrame, _movementGrip, _rowControl })
                if (art?.Texture != null) UnityEngine.Object.Destroy(art.Texture);
            _emptySlot = _highlightSlot = _panelFrame = _movementGrip = _rowControl = null;
        }

        internal void BeginSkillDrag(BuffSkillDefinition skill)
        {
            if (skill != null && _owner != null && Plugin.Instance.IsLearned(_owner, skill))
                _incomingSkill = skill.EffectId;
        }

        internal void BeginActiveSkillDrag(ActiveSkillDefinition skill)
        {
            if (skill != null && _owner != null && Plugin.Instance.IsLearned(_owner, skill))
                _incomingSkill = ActiveSkillSlotPrefix + skill.EffectId;
        }

        internal void Update(Player player, Plugin plugin, bool menuOpen)
        {
            if (player == null)
            {
                _owner = null;
                _incomingSkill = null;
                return;
            }
            if (_owner != player) Load(player);
            UpdateRects(plugin);

            if (!menuOpen)
            {
                for (int i = 0; i < Keys.Length; i++)
                    if (Input.GetKeyDown(Keys[i])) Activate(i, plugin);
            }

            Vector2 mouse = MousePosition();
            bool cursorAvailable = menuOpen || plugin.IsCursorReleased;
            if (cursorAvailable && Input.GetMouseButtonDown(0) && _incomingSkill == null)
            {
                var skillGrip = GripRect(_skillRect);
                var buffGrip = GripRect(_buffRect);
                if (skillGrip.Contains(mouse))
                {
                    _movingSkill = true;
                    _grabOffset = mouse - _skillPosition;
                }
                else if (ActiveBuffs(plugin).Count > 0 && buffGrip.Contains(mouse))
                {
                    _movingBuff = true;
                    _grabOffset = mouse - _buffPosition;
                }
                else if (ActiveBuffs(plugin).Count > 0 && _buffRect.Contains(mouse))
                {
                    if (plugin.IsDebugMode && plugin.IsHotbarCursorMode)
                    {
                        var buffs = ActiveBuffs(plugin);
                        for (int i = 0; i < buffs.Count; i++)
                        {
                            if (!BuffSlotRect(i).Contains(mouse)) continue;
                            plugin.RemoveActiveBuff(_owner, buffs[i]);
                            break;
                        }
                    }
                }
                else if (CycleRect().Contains(mouse))
                    _pressedCycle = true;
                else
                    _pressedSlot = SlotAt(mouse);
            }
            if (cursorAvailable && Input.GetMouseButtonDown(1))
            {
                int slot = SlotAt(mouse);
                if (slot >= 0 && !_buffRect.Contains(mouse))
                {
                    _slots[slot] = null;
                    SaveSlots();
                }
            }

            if (Input.GetMouseButton(0))
            {
                if (_movingSkill)
                {
                    _skillPosition = ClampPosition(mouse - _grabOffset, _skillRect.size);
                    UpdateRects(plugin);
                }
                if (_movingBuff)
                {
                    _buffPosition = ClampPosition(mouse - _grabOffset, _buffRect.size);
                    UpdateRects(plugin);
                }
                if (_pressedSlot >= 0 && Vector2.Distance(mouse, SlotRect(_pressedSlot).center) > SlotSize * 0.7f)
                {
                    _draggedSlot = _pressedSlot;
                    _pressedSlot = -1;
                }
            }

            if (!Input.GetMouseButtonUp(0)) return;
            if (_movingSkill || _movingBuff)
            {
                SaveLayout();
                _movingSkill = _movingBuff = false;
            }
            else if (_incomingSkill != null)
            {
                int target = SlotAt(mouse);
                if (target >= 0)
                {
                    _slots[target] = _incomingSkill;
                    SaveSlots();
                }
            }
            else if (_draggedSlot >= 0)
            {
                int target = SlotAt(mouse);
                if (target >= 0 && target != _draggedSlot)
                {
                    string previous = _slots[target];
                    _slots[target] = _slots[_draggedSlot];
                    _slots[_draggedSlot] = previous;
                    SaveSlots();
                }
                else if (target < 0)
                {
                    _slots[_draggedSlot] = null;
                    SaveSlots();
                }
            }
            else if (_pressedCycle && CycleRect().Contains(mouse))
            {
                float previousHeight = BarHeight();
                _rows = _rows == MaxRows ? 1 : _rows + 1;
                // Keep the shortcut row anchored while extra rows open above it.
                _skillPosition.y += previousHeight - BarHeight();
                UpdateRects(plugin);
                SaveLayout();
            }
            else if (_pressedSlot >= 0 && _pressedSlot == SlotAt(mouse))
                Activate(_pressedSlot, plugin);

            _incomingSkill = null;
            _pressedSlot = -1;
            _draggedSlot = -1;
            _pressedCycle = false;
        }

        internal void Draw(Texture2D pixel, Plugin plugin)
        {
            if (_owner == null || pixel == null) return;
            EnsureStyles();
            UpdateRects(plugin);
            DrawBarFrame(_skillRect, pixel);
            DrawGrip(GripRect(_skillRect), pixel);
            for (int index = 0; index < Columns * _rows; index++)
            {
                Rect slot = SlotRect(index);
                var skill = FindSkill(plugin, _slots[index]);
                var activeSkill = FindActiveSkill(plugin, _slots[index]);
                var item = FindAssignedItem(_owner, _slots[index]);
                bool hover = slot.Contains(Event.current.mousePosition);
                DrawSlot(slot, hover || skill != null || activeSkill != null || item != null, pixel);
                if (skill?.Icon != null)
                    GUI.DrawTexture(new Rect(slot.x + 4f, slot.y + 4f, slot.width - 8f, slot.height - 8f),
                        skill.Icon.texture, ScaleMode.ScaleToFit, true);
                else if (item?.GetIcon() != null)
                    GUI.DrawTexture(new Rect(slot.x + 4f, slot.y + 4f, slot.width - 8f, slot.height - 8f),
                        item.GetIcon().texture, ScaleMode.ScaleToFit, true);
                else if (activeSkill?.Icon != null)
                    GUI.DrawTexture(new Rect(slot.x + 4f, slot.y + 4f, slot.width - 8f, slot.height - 8f),
                        activeSkill.Icon.texture, ScaleMode.ScaleToFit, true);
                if (activeSkill != null)
                {
                    float cooldown = plugin.GetActiveSkillCooldownRemaining(_owner, activeSkill);
                    float effectWindow = activeSkill.GetEffectWindowRemaining?.Invoke(_owner) ?? 0f;
                    string timer = cooldown > 0f ? $"CD {Mathf.CeilToInt(cooldown)}" :
                        effectWindow > 0f ? $"HIT {Mathf.CeilToInt(effectWindow)}" : null;
                    if (timer != null)
                    {
                        Fill(new Rect(slot.x + 2f, slot.yMax - 14f, slot.width - 4f, 12f),
                            new Color(0.02f, 0.03f, 0.04f, 0.9f), pixel);
                        GUI.Label(new Rect(slot.x + 2f, slot.yMax - 15f, slot.width - 4f, 14f),
                            timer, _buffTimerStyle);
                    }
                }
                if (index < 10)
                {
                    var badge = new Rect(slot.x + 3f, slot.y + 3f, 15f, 15f);
                    Fill(badge, new Color(0.025f, 0.03f, 0.035f, 0.90f), pixel);
                    GUI.Label(badge,
                        index == 9 ? "0" : (index + 1).ToString(), _keyStyle);
                }
                if (skill != null && hover)
                    DrawTooltip(skill.DisplayName + " — " + skill.Description, slot, pixel);
                else if (item != null && hover)
                    DrawTooltip(Localize(item.m_shared.m_name) + " ×" + item.m_stack, slot, pixel);
                else if (activeSkill != null && hover)
                {
                    float cooldown = plugin.GetActiveSkillCooldownRemaining(_owner, activeSkill);
                    float effectWindow = activeSkill.GetEffectWindowRemaining?.Invoke(_owner) ?? 0f;
                    string stateText = cooldown > 0f ? $" Cooldown {Mathf.CeilToInt(cooldown)}s." :
                        effectWindow > 0f ? $" Attack window {Mathf.CeilToInt(effectWindow)}s." : " Ready.";
                    DrawTooltip(activeSkill.DisplayName + " — " + activeSkill.Description + stateText, slot, pixel);
                }
            }
            Rect cycle = CycleRect();
            if (_rowControl != null) DrawRowControl(cycle, _rowControl);
            else
            {
                Fill(cycle, ColorOf(0x453422), pixel);
                Stroke(cycle, ColorOf(0xD0A66A), pixel);
            }
            var cycleLabel = new Rect(cycle.center.x - 15f, cycle.center.y - 9f, 30f, 18f);
            Fill(cycleLabel, new Color(0.035f, 0.04f, 0.04f, 0.95f), pixel);
            Stroke(cycleLabel, ColorOf(0x9C7745), pixel);
            GUI.Label(cycleLabel, _rows + "/3", _timerStyle);
            if (cycle.Contains(Event.current.mousePosition))
                DrawTooltip("Show " + (_rows == MaxRows ? 1 : _rows + 1) + " skill rows", cycle, pixel);

            var buffs = ActiveBuffs(plugin);
            if (buffs.Count > 0)
            {
                DrawBarFrame(_buffRect, pixel);
                DrawGrip(GripRect(_buffRect), pixel);
                BuffSkillDefinition hoveredBuff = null;
                for (int i = 0; i < buffs.Count; i++)
                {
                    var rect = BuffSlotRect(i);
                    DrawSlot(rect, true, pixel);
                    var skill = buffs[i];
                    if (skill.Icon != null)
                        GUI.DrawTexture(new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f),
                            skill.Icon.texture, ScaleMode.ScaleToFit, true);
                    float remaining = plugin.GetActiveBuffRemainingSeconds(_owner, skill);
                    if (remaining > 0f && remaining < 60f)
                    {
                        Fill(new Rect(rect.x + 1f, rect.yMax - 16f, rect.width - 2f, 15f),
                            new Color(0.02f, 0.03f, 0.04f, 0.85f), pixel);
                        GUI.Label(new Rect(rect.x, rect.yMax - 17f, rect.width, 15f),
                            Mathf.Max(1, Mathf.FloorToInt(remaining)).ToString(), _buffTimerStyle);
                    }
                    if (rect.Contains(Event.current.mousePosition))
                        hoveredBuff = skill;
                }
                if (hoveredBuff != null) DrawBuffTooltip(hoveredBuff, plugin, pixel);
            }

            if (_incomingSkill != null)
            {
                var skill = FindSkill(plugin, _incomingSkill);
                if (skill?.Icon != null)
                {
                    var mouse = Event.current.mousePosition;
                    GUI.DrawTexture(new Rect(mouse.x - 20f, mouse.y - 20f, 40f, 40f),
                        skill.Icon.texture, ScaleMode.ScaleToFit, true);
                }
                else
                {
                    var activeSkill = FindActiveSkill(plugin, _incomingSkill);
                    if (activeSkill?.Icon != null)
                    {
                        var mouse = Event.current.mousePosition;
                        GUI.DrawTexture(new Rect(mouse.x - 20f, mouse.y - 20f, 40f, 40f),
                            activeSkill.Icon.texture, ScaleMode.ScaleToFit, true);
                    }
                }
            }
        }

        private void Load(Player player)
        {
            _owner = player;
            _incomingSkill = null;
            Array.Clear(_slots, 0, _slots.Length);
            var data = player.m_customData;
            if (data != null)
            {
                for (int i = 0; i < _slots.Length; i++)
                    data.TryGetValue(Prefix + "Slot." + i, out _slots[i]);
                if (data.TryGetValue(Prefix + "Rows", out string rows) && int.TryParse(rows, out int count))
                    _rows = Mathf.Clamp(count, 1, MaxRows);
                else _rows = 1;
            }
            float defaultX = Mathf.Max(20f, (Screen.width - BarWidth()) / 2f);
            float defaultY = Mathf.Max(20f, Screen.height - 90f);
            _skillPosition = ReadPosition(data, "Skill", new Vector2(defaultX, defaultY));
            _buffPosition = ReadPosition(data, "Buff", new Vector2(_skillPosition.x,
                Mathf.Max(20f, _skillPosition.y - (SlotSize + 24f))));
        }

        private static Vector2 ReadPosition(Dictionary<string, string> data, string name, Vector2 fallback)
        {
            if (data == null) return fallback;
            if (!data.TryGetValue(Prefix + name + "X", out string x) ||
                !data.TryGetValue(Prefix + name + "Y", out string y) ||
                !float.TryParse(x, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float px) ||
                !float.TryParse(y, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float py)) return fallback;
            return new Vector2(px, py);
        }

        private void SaveSlots()
        {
            var data = _owner?.m_customData;
            if (data == null) return;
            for (int i = 0; i < _slots.Length; i++)
            {
                string key = Prefix + "Slot." + i;
                if (string.IsNullOrEmpty(_slots[i])) data.Remove(key);
                else data[key] = _slots[i];
            }
        }

        private void SaveLayout()
        {
            var data = _owner?.m_customData;
            if (data == null) return;
            data[Prefix + "Rows"] = _rows.ToString();
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            data[Prefix + "SkillX"] = _skillPosition.x.ToString("R", culture);
            data[Prefix + "SkillY"] = _skillPosition.y.ToString("R", culture);
            data[Prefix + "BuffX"] = _buffPosition.x.ToString("R", culture);
            data[Prefix + "BuffY"] = _buffPosition.y.ToString("R", culture);
        }

        private void UpdateRects(Plugin plugin)
        {
            _skillPosition = ClampPosition(_skillPosition, new Vector2(BarWidth(), BarHeight()));
            _skillRect = new Rect(_skillPosition.x, _skillPosition.y, BarWidth(), BarHeight());
            int buffCount = ActiveBuffs(plugin).Count;
            float buffWidth = 18f + buffCount * SlotSize + Mathf.Max(0, buffCount - 1) * Gap;
            _buffPosition = ClampPosition(_buffPosition, new Vector2(buffWidth, SlotSize + 16f));
            _buffRect = new Rect(_buffPosition.x, _buffPosition.y, buffWidth, SlotSize + 16f);
        }

        private static float BarWidth() => 10f + Columns * SlotSize + (Columns - 1) * Gap + 8f + ControlWidth + 10f;
        private float BarHeight() => 16f + _rows * SlotSize + (_rows - 1) * Gap;
        private Rect CycleRect() => new Rect(_skillRect.xMax - ControlWidth - 7f,
            _skillRect.y + 5f, ControlWidth, _skillRect.height - 10f);
        private static Rect GripRect(Rect bar)
        {
            float width = Mathf.Min(GripWidth, bar.width - 8f);
            return new Rect(bar.x + Mathf.Min(28f, (bar.width - width) * 0.5f),
                bar.y - 8f, width, GripHeight);
        }
        private Rect SlotRect(int index) => new Rect(
            _skillRect.x + 10f + (index % Columns) * (SlotSize + Gap),
            _skillRect.y + 8f + (_rows - 1 - index / Columns) * (SlotSize + Gap), SlotSize, SlotSize);
        private Rect BuffSlotRect(int index) => new Rect(
            _buffRect.x + 9f + index * (SlotSize + Gap), _buffRect.y + 8f, SlotSize, SlotSize);
        private int SlotAt(Vector2 point)
        {
            for (int i = 0; i < Columns * _rows; i++)
                if (SlotRect(i).Contains(point)) return i;
            return -1;
        }

        internal bool TryAssignConsumable(ItemDrop.ItemData item, Vector2 guiMouse)
        {
            if (_owner == null || item?.m_shared == null ||
                item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable)
                return false;
            int slot = SlotAt(guiMouse);
            if (slot < 0 || slot >= Columns * _rows) return false;
            _slots[slot] = ItemSlotPrefix + item.m_shared.m_name;
            SaveSlots();
            return true;
        }

        private static ItemDrop.ItemData FindAssignedItem(Player player, string slotValue)
        {
            if (player == null || string.IsNullOrEmpty(slotValue) ||
                !slotValue.StartsWith(ItemSlotPrefix, StringComparison.Ordinal)) return null;
            string sharedName = slotValue.Substring(ItemSlotPrefix.Length);
            foreach (var item in player.GetInventory().GetAllItems())
                if (item?.m_shared != null && item.m_shared.m_name == sharedName &&
                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
                    return item;
            return null;
        }

        private static string Localize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            try
            {
                var type = AccessTools.TypeByName("Localization");
                var property = type?.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var instance = property?.GetValue(null, null);
                var method = AccessTools.Method(type, "Localize", new[] { typeof(string) });
                return method?.Invoke(instance, new object[] { text }) as string ?? text;
            }
            catch { return text; }
        }

        private static Vector2 ClampPosition(Vector2 position, Vector2 size) => new Vector2(
            Mathf.Clamp(position.x, 0f, Mathf.Max(0f, Screen.width - size.x)),
            Mathf.Clamp(position.y, 9f, Mathf.Max(9f, Screen.height - size.y)));

        private static Vector2 MousePosition() => new Vector2(Input.mousePosition.x,
            Screen.height - Input.mousePosition.y);

        private static BuffSkillDefinition FindSkill(Plugin plugin, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var skill in plugin.Skills)
                if (skill.EffectId == id) return skill;
            return null;
        }

        private static ActiveSkillDefinition FindActiveSkill(Plugin plugin, string id)
        {
            if (plugin == null || string.IsNullOrEmpty(id) ||
                !id.StartsWith(ActiveSkillSlotPrefix, StringComparison.Ordinal)) return null;
            string effectId = id.Substring(ActiveSkillSlotPrefix.Length);
            foreach (var skill in plugin.ActiveSkills)
                if (skill.EffectId == effectId) return skill;
            return null;
        }

        private List<BuffSkillDefinition> ActiveBuffs(Plugin plugin)
        {
            _activeBuffs.Clear();
            if (_owner == null || plugin == null) return _activeBuffs;
            foreach (var skill in plugin.Skills)
                if (plugin.GetActiveBuffRemainingSeconds(_owner, skill) > 0f) _activeBuffs.Add(skill);
            return _activeBuffs;
        }

        private void Activate(int index, Plugin plugin)
        {
            if (_owner == null || index < 0 || index >= _slots.Length) return;
            var item = FindAssignedItem(_owner, _slots[index]);
            if (item != null)
            {
                plugin.UseItemFromInventory(_owner, item);
                return;
            }
            var activeSkill = FindActiveSkill(plugin, _slots[index]);
            if (activeSkill != null)
            {
                plugin.UseActiveSkill(_owner, activeSkill);
                return;
            }
            var skill = FindSkill(plugin, _slots[index]);
            if (skill != null && plugin.IsLearned(_owner, skill)) plugin.UseSkill(_owner, skill);
        }

        private void DrawBarFrame(Rect rect, Texture2D pixel)
        {
            if (_panelFrame != null)
            {
                DrawNineSlice(rect, _panelFrame);
                return;
            }
            Fill(rect, new Color(0.035f, 0.047f, 0.052f, 0.92f), pixel);
            Stroke(rect, ColorOf(0xA07A46), pixel);
        }

        private void DrawGrip(Rect rect, Texture2D pixel)
        {
            if (_movementGrip != null)
            {
                DrawArt(rect, _movementGrip);
                return;
            }
            Fill(rect, ColorOf(0x302A22), pixel);
            Stroke(rect, ColorOf(0xB18B51), pixel);
            Fill(new Rect(rect.x + 10f, rect.center.y, rect.width - 20f, 2f),
                ColorOf(0xC9A36D), pixel);
        }

        private void DrawSlot(Rect rect, bool highlighted, Texture2D pixel)
        {
            var art = highlighted ? _highlightSlot : _emptySlot;
            if (art != null)
            {
                DrawArt(rect, art);
                return;
            }
            Fill(rect, highlighted ? ColorOf(0x56432E) : ColorOf(0x222829), pixel);
            Stroke(rect, highlighted ? ColorOf(0xE1BE80) : ColorOf(0x80633C), pixel);
        }

        private static void DrawArt(Rect destination, Art art)
        {
            Rect source = art.Source;
            DrawSource(destination, art.Texture, source.xMin, source.yMin, source.xMax, source.yMax);
        }

        private static void DrawNineSlice(Rect destination, Art art)
        {
            Rect source = art.Source;
            float sourceCorner = Mathf.Min(220f, source.width * 0.2f, source.height * 0.2f);
            float destCorner = Mathf.Min(FrameCorner, destination.width * 0.25f, destination.height * 0.25f);
            float[] sx = { source.xMin, source.xMin + sourceCorner,
                source.xMax - sourceCorner, source.xMax };
            float[] sy = { source.yMin, source.yMin + sourceCorner,
                source.yMax - sourceCorner, source.yMax };
            float[] dx = { destination.xMin, destination.xMin + destCorner,
                destination.xMax - destCorner, destination.xMax };
            float[] dy = { destination.yMin, destination.yMin + destCorner,
                destination.yMax - destCorner, destination.yMax };
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    DrawSource(new Rect(dx[col], dy[row], dx[col + 1] - dx[col],
                        dy[row + 1] - dy[row]), art.Texture,
                        sx[col], sy[row], sx[col + 1], sy[row + 1]);
                }
            }
        }

        private static void DrawSource(Rect destination, Texture2D texture,
            float left, float top, float right, float bottom)
        {
            var uv = new Rect(left / texture.width, (texture.height - bottom) / texture.height,
                (right - left) / texture.width, (bottom - top) / texture.height);
            GUI.DrawTextureWithTexCoords(destination, texture, uv, true);
        }

        private static void DrawRowControl(Rect destination, Art art)
        {
            Rect source = art.Source;
            float sourceEnd = 450f;
            float destEnd = Mathf.Min(18f, destination.height * 0.35f);
            DrawSource(new Rect(destination.x, destination.y, destination.width, destEnd),
                art.Texture, source.xMin, source.yMin, source.xMax, source.yMin + sourceEnd);
            DrawSource(new Rect(destination.x, destination.y + destEnd,
                    destination.width, destination.height - destEnd * 2f),
                art.Texture, source.xMin, source.yMin + sourceEnd,
                source.xMax, source.yMax - sourceEnd);
            DrawSource(new Rect(destination.x, destination.yMax - destEnd,
                    destination.width, destEnd),
                art.Texture, source.xMin, source.yMax - sourceEnd,
                source.xMax, source.yMax);
        }

        private static Art LoadArt(string file, float left, float top, float right, float bottom)
        {
            string resource = "Lineage2Valheim.HotbarAssets." + file;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    Plugin.Log?.LogError("Missing hotbar sprite " + resource);
                    return null;
                }
                using (var memory = new System.IO.MemoryStream())
                {
                    stream.CopyTo(memory);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
                    {
                        name = resource,
                        filterMode = FilterMode.Trilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    if (!ImageConversion.LoadImage(texture, memory.ToArray(), true))
                    {
                        Plugin.Log?.LogError("Could not decode hotbar sprite " + resource);
                        UnityEngine.Object.Destroy(texture);
                        return null;
                    }
                    return new Art { Texture = texture,
                        Source = Rect.MinMaxRect(left, top, right, bottom) };
                }
            }
        }

        private void DrawTooltip(string text, Rect source, Texture2D pixel)
        {
            var rect = new Rect(Mathf.Clamp(source.x, 2f, Mathf.Max(2f, Screen.width - 245f)),
                Mathf.Max(2f, source.y - 30f), 240f, 27f);
            Fill(rect, ColorOf(0x14191A), pixel);
            Stroke(rect, ColorOf(0xAA8350), pixel);
            GUI.Label(new Rect(rect.x + 5f, rect.y + 3f, rect.width - 10f, rect.height - 6f), text, _tipStyle);
        }

        private void DrawBuffTooltip(BuffSkillDefinition skill, Plugin plugin, Texture2D pixel)
        {
            const float width = 255f;
            float height = plugin.IsDebugMode ? 86f : 68f;
            float x = _buffRect.xMax + 7f;
            if (x + width > Screen.width - 2f) x = _buffRect.x - width - 7f;
            x = Mathf.Clamp(x, 2f, Mathf.Max(2f, Screen.width - width - 2f));
            float y = Mathf.Clamp(Event.current.mousePosition.y - 8f, 2f,
                Mathf.Max(2f, Screen.height - height - 2f));
            var rect = new Rect(x, y, width, height);
            Fill(rect, new Color(0.055f, 0.065f, 0.066f, 0.97f), pixel);
            Stroke(rect, ColorOf(0xAA8350), pixel);
            GUI.Label(new Rect(x + 8f, y + 4f, width - 16f, 18f), skill.DisplayName, _buffTipTitleStyle);
            float remaining = plugin.GetActiveBuffRemainingSeconds(_owner, skill);
            int seconds = Mathf.CeilToInt(remaining);
            string time = seconds >= 60
                ? (seconds / 60) + " min " + (seconds % 60) + " sec"
                : seconds + " sec";
            GUI.Label(new Rect(x + 8f, y + 23f, width - 16f, 17f),
                "Time Left: " + time, _buffTipBodyStyle);
            GUI.Label(new Rect(x + 8f, y + 41f, width - 16f, 23f),
                skill.Description, _buffTipBodyStyle);
            if (plugin.IsDebugMode)
                GUI.Label(new Rect(x + 8f, y + 64f, width - 16f, 18f),
                    "Hold Alt + click to remove", _buffTipBodyStyle);
        }

        private void EnsureStyles()
        {
            if (_keyStyle != null) return;
            _keyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0)
            };
            _keyStyle.normal.textColor = ColorOf(0xE8DFC9);
            _timerStyle = new GUIStyle(_keyStyle)
            {
                fontSize = 11, alignment = TextAnchor.MiddleCenter
            };
            _buffTimerStyle = new GUIStyle(_timerStyle);
            _buffTimerStyle.normal.textColor = ColorOf(0xF0BD57);
            _tipStyle = new GUIStyle(_keyStyle)
            {
                fontSize = 11, alignment = TextAnchor.MiddleLeft
            };
            _buffTipTitleStyle = new GUIStyle(_tipStyle)
            {
                fontSize = 12, alignment = TextAnchor.MiddleLeft
            };
            _buffTipTitleStyle.normal.textColor = ColorOf(0xDFBD81);
            _buffTipBodyStyle = new GUIStyle(_tipStyle)
            {
                fontSize = 11, alignment = TextAnchor.UpperLeft, wordWrap = true
            };
            _buffTipBodyStyle.normal.textColor = ColorOf(0xD0D2CB);
        }

        private static void Fill(Rect rect, Color color, Texture2D pixel)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, pixel);
            GUI.color = old;
        }

        private static void Stroke(Rect rect, Color color, Texture2D pixel)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, 1f), color, pixel);
            Fill(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color, pixel);
            Fill(new Rect(rect.x, rect.y, 1f, rect.height), color, pixel);
            Fill(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color, pixel);
        }

        private static Color ColorOf(int rgb) => new Color(
            ((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
    }
}
