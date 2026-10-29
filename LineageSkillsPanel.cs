using System.Collections.Generic;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Skills window body. The player's saved custom data owns the learned state.</summary>
    internal sealed class LineageSkillsPanel
    {
        private enum Tab { Active, Passive, Learn }

        private static readonly Color Gold = ColorOf(0xAA8350);
        private static readonly Color PaleGold = ColorOf(0xDFBD81);
        private static readonly Color Muted = ColorOf(0xB6B9B1);
        private static readonly Color Panel = ColorOf(0x151B1D);
        private static readonly Color Card = ColorOf(0x222829);
        private Tab _tab = Tab.Learn;
        private Vector2 _scroll;
        private GUIStyle _heading, _name, _body, _small, _button;
        private string _notice;
        private ISkillDefinition _pressedActiveIcon;
        private Vector2 _activeIconPressPosition;
        private bool _activeIconDragged;

        internal void Draw(Rect area, Texture2D pixel)
        {
            EnsureStyles();
            Fill(area, Panel, pixel);
            Stroke(area, Gold, pixel);
            var tabs = new[] { "Active Skills", "Passive Skills", "Learn Skills" };
            float tabWidth = (area.width - 32f) / 3f;
            for (int i = 0; i < tabs.Length; i++)
            {
                var rect = new Rect(area.x + 16f + i * tabWidth, area.y + 14f, tabWidth - 4f, 42f);
                bool selected = (int)_tab == i;
                Fill(rect, selected ? ColorOf(0x4A3926) : ColorOf(0x252A29), pixel);
                Stroke(rect, selected ? PaleGold : Gold, pixel);
                GUI.Label(rect, tabs[i], _heading);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && !selected)
                {
                    _tab = (Tab)i;
                    _scroll = Vector2.zero;
                    _notice = null;
                }
            }

            var viewport = new Rect(area.x + 16f, area.y + 68f, area.width - 32f, area.height - 113f);
            Fill(viewport, ColorOf(0x101617), pixel);
            var player = Player.m_localPlayer;
            var plugin = Plugin.Instance;
            if (player == null || plugin == null) return;
            var entries = new List<ISkillDefinition>();
            foreach (var skill in plugin.Skills)
            {
                bool learned = plugin.IsLearned(player, skill);
                if ((_tab == Tab.Active && learned) || (_tab == Tab.Learn && skill.RequiresLearning && !learned))
                    entries.Add(skill);
            }
            foreach (var skill in plugin.ActiveSkills)
            {
                bool learned = plugin.IsLearned(player, skill);
                if ((_tab == Tab.Active && learned) || (_tab == Tab.Learn && skill.RequiresLearning && !learned))
                    entries.Add(skill);
            }

            float contentHeight;
            int columns = 3;
            if (_tab == Tab.Learn) contentHeight = 18f + entries.Count * 172f;
            else if (_tab == Tab.Active) contentHeight = 55f + Mathf.CeilToInt(entries.Count / (float)columns) * 200f;
            else contentHeight = 90f;
            bool overflow = contentHeight > viewport.height;
            float viewWidth = viewport.width - (overflow ? 20f : 0f);
            _scroll = GUI.BeginScrollView(viewport, _scroll,
                new Rect(0, 0, viewWidth, Mathf.Max(contentHeight, viewport.height)), false, overflow);
            if (_tab == Tab.Learn)
            {
                if (entries.Count == 0)
                    GUI.Label(new Rect(22, 30, viewWidth - 44, 70),
                        "All available skills are learned. Find them under Active Skills.", _body);
                for (int i = 0; i < entries.Count; i++)
                    DrawLearnCard(entries[i], player, plugin, pixel,
                        new Rect(12, 12 + i * 172, viewWidth - 24, 158));
            }
            else if (_tab == Tab.Active)
            {
                GUI.Label(new Rect(18, 13, viewWidth - 36, 30), "BUFFS & ACTIVE SKILLS", _name);
                Fill(new Rect(18, 45, viewWidth - 36, 1), Gold, pixel);
                float cellWidth = (viewWidth - 48f) / columns;
                for (int i = 0; i < entries.Count; i++)
                    DrawActiveCard(entries[i], player, plugin, pixel,
                        new Rect(12 + (i % columns) * (cellWidth + 12),
                            57 + (i / columns) * 200, cellWidth, 188));
            }
            else
                GUI.Label(new Rect(22, 30, viewWidth - 44, 70),
                    "Passive skills will appear here as the class system grows.", _body);
            GUI.EndScrollView();

            Fill(new Rect(area.x + 16, area.yMax - 39, area.width - 32, 1), Gold, pixel);
            GUI.Label(new Rect(area.x + 19, area.yMax - 33, area.width - 38, 27),
                _notice ?? (_tab == Tab.Learn
                    ? "Earn XP to reach each skill's required level. Learned buffs move to Active Skills → Buffs."
                    : _tab == Tab.Active ? "Click a skill icon or USE to activate it; drag an icon onto the hotbar to assign it." : ""), _small);
        }

        private void DrawLearnCard(ISkillDefinition skill, Player player, Plugin plugin, Texture2D pixel, Rect rect)
        {
            Fill(rect, Card, pixel);
            Stroke(rect, Gold, pixel);
            DrawIcon(skill, new Rect(rect.x + 12, rect.y + 12, 132, 132));
            GUI.Label(new Rect(rect.x + 160, rect.y + 14, rect.width - 340, 38), skill.DisplayName.ToUpperInvariant(), _name);
            GUI.Label(new Rect(rect.x + 162, rect.y + 56, rect.width - 340, 26), skill.Description, _body);
            string requirement = skill is ActiveSkillDefinition active
                ? $"Requires level {skill.PlannedLevel}  •  {active.StaminaCost:0} stamina  •  {active.CooldownSeconds:0}s cooldown"
                : $"Requires level {skill.PlannedLevel}  •  {((BuffSkillDefinition)skill).DurationSeconds / 60f:0.#} min duration";
            GUI.Label(new Rect(rect.x + 162, rect.y + 96, rect.width - 340, 24), requirement, _small);
            var button = new Rect(rect.xMax - 156, rect.y + 55, 138, 45);
            bool canLearn = plugin.CanLearnSkill(player, skill);
            DrawButton(button, canLearn ? "LEARN" : $"LEVEL {skill.PlannedLevel}", pixel);
            if (canLearn && GUI.Button(button, GUIContent.none, GUIStyle.none) && plugin.LearnSkill(player, skill))
                _notice = $"{skill.DisplayName} learned. Open Active Skills to use it.";
        }

        private void DrawActiveCard(ISkillDefinition skill, Player player, Plugin plugin, Texture2D pixel, Rect rect)
        {
            Fill(rect, Card, pixel);
            Stroke(rect, Gold, pixel);
            float iconSize = Mathf.Min(90, rect.width - 24);
            var iconRect = new Rect(rect.center.x - iconSize / 2f, rect.y + 10, iconSize, iconSize);
            DrawIcon(skill, iconRect);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                iconRect.Contains(Event.current.mousePosition))
            {
                _pressedActiveIcon = skill;
                _activeIconPressPosition = Event.current.mousePosition;
                _activeIconDragged = false;
                if (skill is BuffSkillDefinition buff) plugin.Hotbars.BeginSkillDrag(buff);
                else if (skill is ActiveSkillDefinition action) plugin.Hotbars.BeginActiveSkillDrag(action);
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseDrag && _pressedActiveIcon == skill &&
                Vector2.Distance(_activeIconPressPosition, Event.current.mousePosition) > 7f)
                _activeIconDragged = true;
            if (Event.current.type == EventType.MouseUp && Event.current.button == 0 &&
                _pressedActiveIcon == skill)
            {
                bool clickToUse = !_activeIconDragged && iconRect.Contains(Event.current.mousePosition);
                _pressedActiveIcon = null;
                if (clickToUse)
                {
                    if (skill is ActiveSkillDefinition activeSkill) plugin.UseActiveSkill(player, activeSkill);
                    else if (skill is BuffSkillDefinition buffSkill) plugin.UseSkill(player, buffSkill);
                    Event.current.Use();
                }
            }
            GUI.Label(new Rect(rect.x + 6, rect.y + 103, rect.width - 12, 27), skill.DisplayName, _heading);
            float actionCooldown = skill is ActiveSkillDefinition cooldownSkill
                ? plugin.GetActiveSkillCooldownRemaining(player, cooldownSkill) : 0f;
            float effectWindow = skill is ActiveSkillDefinition effectSkill
                ? (effectSkill.GetEffectWindowRemaining?.Invoke(player) ?? 0f) : 0f;
            float buffCastRemaining = skill is BuffSkillDefinition buffCastingSkill
                ? plugin.BuffCasting?.GetRemainingSeconds(player, buffCastingSkill) ?? 0f : 0f;
            string status = skill is ActiveSkillDefinition active
                ? (actionCooldown > 0f ? $"Cooldown: {Mathf.CeilToInt(actionCooldown)}s"
                    : effectWindow > 0f ? $"Attack window: {Mathf.CeilToInt(effectWindow)}s"
                    : $"Ready  •  {active.StaminaCost:0} stamina")
                : (buffCastRemaining > 0f ? $"Casting: {Mathf.CeilToInt(buffCastRemaining)}s"
                : plugin.GetActiveBuffRemainingSeconds(player, (BuffSkillDefinition)skill) > 0f
                    ? $"Active: {plugin.GetActiveBuffRemainingSeconds(player, (BuffSkillDefinition)skill):0}s"
                    : skill.Description);
            GUI.Label(new Rect(rect.x + 6, rect.y + 128, rect.width - 12, 20), status, _small);
            var button = new Rect(rect.x + 10, rect.yMax - 34, rect.width - 20, 27);
            bool coolingDown = actionCooldown > 0f;
            DrawButton(button, coolingDown ? $"COOLDOWN {Mathf.CeilToInt(actionCooldown)}s" : "USE", pixel);
            if (!coolingDown && GUI.Button(button, GUIContent.none, GUIStyle.none))
            {
                if (skill is ActiveSkillDefinition activeSkill) plugin.UseActiveSkill(player, activeSkill);
                else plugin.UseSkill(player, (BuffSkillDefinition)skill);
            }
        }

        private static void DrawIcon(ISkillDefinition skill, Rect rect)
        {
            if (skill.Icon != null) GUI.DrawTexture(rect, skill.Icon.texture, ScaleMode.ScaleToFit, true);
        }

        private void DrawButton(Rect rect, string label, Texture2D pixel)
        {
            Fill(rect, rect.Contains(Event.current.mousePosition) ? ColorOf(0x59452D) : ColorOf(0x342C22), pixel);
            Stroke(rect, PaleGold, pixel);
            GUI.Label(rect, label, _button);
        }

        private static void Fill(Rect rect, Color color, Texture2D pixel)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, pixel);
            GUI.color = previous;
        }

        private static void Stroke(Rect rect, Color color, Texture2D pixel)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, 1), color, pixel);
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color, pixel);
            Fill(new Rect(rect.x, rect.y, 1, rect.height), color, pixel);
            Fill(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color, pixel);
        }

        private void EnsureStyles()
        {
            if (_heading != null) return;
            _heading = Style(18, PaleGold, TextAnchor.MiddleCenter, true);
            _name = Style(25, PaleGold, TextAnchor.MiddleLeft, true);
            _body = Style(15, Muted, TextAnchor.MiddleLeft, false);
            _small = Style(12, Muted, TextAnchor.MiddleLeft, false);
            _button = Style(15, PaleGold, TextAnchor.MiddleCenter, true);
        }

        private static GUIStyle Style(int size, Color color, TextAnchor align, bool bold)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = align,
                wordWrap = true
            };
            style.normal.textColor = color;
            return style;
        }

        private static Color ColorOf(int rgb) => new Color(
            ((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
    }
}
