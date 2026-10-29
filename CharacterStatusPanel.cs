using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Read-only character sheet built around values Valheim actually exposes.</summary>
    internal sealed class CharacterStatusPanel
    {
        private static readonly Color Gold = ColorOf(0xAA8350);
        private static readonly Color PaleGold = ColorOf(0xDFBD81);
        private static readonly Color Muted = ColorOf(0xB6B9B1);
        private static readonly Color Panel = ColorOf(0x151B1D);
        private static readonly Color Card = ColorOf(0x222829);
        private static readonly MethodInfo RunSpeedFactor = typeof(Player).GetMethod(
            "GetRunSpeedFactor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly Skills.SkillType[] NativeSkillTypes = GetNativeSkillTypes();
        private readonly Dictionary<string, Texture2D> _icons = new Dictionary<string, Texture2D>();
        private GUIStyle _name, _section, _label, _value, _small;
        private Vector2 _scroll;
        private string _hoverText;

        internal void Draw(Rect area, Texture2D pixel)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            EnsureStyles();
            Fill(area, Panel, pixel);
            Stroke(area, Gold, pixel);

            var skills = player.GetSkills();
            int nativeSkillCount = skills == null ? 0 : NativeSkillTypes.Length;
            const float nativeSkillsTop = 542f;
            const float nativeSkillRowHeight = 24f;
            float designHeight = Mathf.Max(532f, nativeSkillsTop + 38f + nativeSkillCount * nativeSkillRowHeight + 12f);
            float designWidth = Mathf.Max(748f, area.width - 32f);
            var viewport = new Rect(area.x + 10f, area.y + 10f, area.width - 20f, area.height - 20f);
            _scroll = GUI.BeginScrollView(viewport, _scroll,
                new Rect(0f, 0f, designWidth, designHeight),
                designWidth > viewport.width, designHeight > viewport.height);
            _hoverText = null;

            float innerWidth = designWidth - 14f;
            var identity = new Rect(6f, 4f, innerWidth, 57f);
            Fill(identity, ColorOf(0x202626), pixel);
            Stroke(identity, Gold, pixel);
            GUI.Label(new Rect(22f, 8f, innerWidth - 40f, 30f), player.GetPlayerName(), _name);
            GUI.Label(new Rect(23f, 35f, innerWidth - 42f, 20f),
                GetClassSummary(player), _small);

            float vitalsY = 73f;
            float vitalGap = 12f;
            float vitalWidth = (innerWidth - vitalGap * 2f) / 3f;
            DrawVital(new Rect(6f, vitalsY, vitalWidth, 77f), "HEALTH",
                player.GetHealth(), player.GetMaxHealth(), ColorOf(0xB93B39), pixel);
            DrawVital(new Rect(6f + vitalWidth + vitalGap, vitalsY, vitalWidth, 77f), "STAMINA",
                player.GetStamina(), player.GetMaxStamina(), ColorOf(0xD5A346), pixel);
            DrawVital(new Rect(6f + (vitalWidth + vitalGap) * 2f, vitalsY, vitalWidth, 77f), "EITR",
                player.GetEitr(), player.GetMaxEitr(), ColorOf(0x9E78C6), pixel);

            GUI.Label(new Rect(6f, 161f, innerWidth, 30f), "COMBAT", _section);
            Fill(new Rect(6f, 192f, innerWidth, 1f), Gold, pixel);

            float weaponPhysical = 0f;
            var weapon = player.GetCurrentWeapon();
            if (weapon != null)
                weaponPhysical = Mathf.Max(0f, weapon.GetDamage().GetTotalPhysicalDamage());
            float armor = Mathf.Max(0f, player.GetBodyArmor());
            float runRating = 100f;
            if (RunSpeedFactor != null)
                runRating = Mathf.Max(0f, (float)RunSpeedFactor.Invoke(player, null) * 100f);
            var activeEffects = player.GetSEMan()?.GetStatusEffects();
            if (activeEffects != null)
                foreach (var activeEffect in activeEffects)
                    if (activeEffect is SE_Stats stats)
                        runRating *= Mathf.Max(0f, 1f + stats.m_speedModifier);
            float cardWidth = (innerWidth - 12f) / 2f;
            float left = 6f;
            float right = left + cardWidth + 12f;

            DrawStat(new Rect(left, 205f, cardWidth, 83f), "PHYSICAL ATTACK",
                weaponPhysical.ToString("0.#"), "weapon physical damage", "sword",
                "Raw physical damage on the equipped weapon or unarmed attack. Valheim then applies skill, combo, buffs and target resistance.", pixel);
            DrawStat(new Rect(right, 205f, cardWidth, 83f), "PHYSICAL DEFENSE",
                armor.ToString("0.#"), "current body armor", "shield",
                "Current Valheim body armor, including equipment and armor status effects. Damage reduction is nonlinear.", pixel);
            DrawStat(new Rect(left, 300f, cardWidth, 83f), "ATTACK SPEED",
                "—", "primary attack timing", "sword",
                "A reliable attacks-per-minute value needs animation timing measurement. This is not a Valheim character stat yet.", pixel);
            DrawStat(new Rect(right, 300f, cardWidth, 83f), "PHYSICAL EVASION",
                "0%", "bonus hit avoidance", "boots",
                "The mod does not add passive evasion yet. Valheim's manual dodge roll remains separate.", pixel);
            DrawStat(new Rect(left, 395f, cardWidth, 83f), "CRITICAL CHANCE",
                "0%", "bonus random criticals", "might",
                "The mod does not add random critical hits yet. Valheim's combo finisher and backstab are separate mechanics.", pixel);
            DrawStat(new Rect(right, 395f, cardWidth, 83f), "RUN SPEED",
                runRating.ToString("0"), "100 = base run speed", "boots",
                "Relative run-speed factor from the Run skill, equipment, and active status effects.", pixel);

            Fill(new Rect(6f, 492f, innerWidth, 1f), Gold, pixel);
            GUI.Label(new Rect(14f, 499f, innerWidth - 28f, 27f),
                _hoverText ?? "Hover a stat for its source and limits. Attack speed, criticals and evasion await combat integration.",
                _small);

            GUI.Label(new Rect(6f, nativeSkillsTop, innerWidth, 30f), "NATIVE VALHEIM SKILLS", _section);
            Fill(new Rect(6f, nativeSkillsTop + 31f, innerWidth, 1f), Gold, pixel);
            for (int i = 0; i < nativeSkillCount; i++)
            {
                float rowY = nativeSkillsTop + 37f + i * nativeSkillRowHeight;
                if ((i & 1) == 0)
                    Fill(new Rect(6f, rowY, innerWidth, nativeSkillRowHeight), ColorOf(0x1B2223), pixel);
                Skills.SkillType type = NativeSkillTypes[i];
                GUI.Label(new Rect(14f, rowY, innerWidth - 100f, nativeSkillRowHeight), type.ToString(), _small);
                GUI.Label(new Rect(innerWidth - 78f, rowY, 70f, nativeSkillRowHeight), skills.GetSkillLevel(type).ToString("0.#"), _label);
            }
            GUI.EndScrollView();
        }

        private static string GetClassSummary(Player player)
        {
            string className = CharacterClassSystem.GetClass(player);
            if (string.IsNullOrEmpty(className)) return "Class: Unassigned";
            string specialization = CharacterClassSystem.GetProfessionName(player);
            if (specialization == "Fighter") return $"Class: {className}";
            if (specialization == "Rogue")
            {
                string path = CharacterClassSystem.GetRoguePathName(player);
                if (!string.IsNullOrEmpty(path)) specialization += $" / {path}";
            }
            return $"Class: {className} · {specialization}";
        }

        private static Skills.SkillType[] GetNativeSkillTypes()
        {
            var values = new List<Skills.SkillType>();
            foreach (Skills.SkillType type in System.Enum.GetValues(typeof(Skills.SkillType)))
            {
                if (type == Skills.SkillType.All) continue;
                values.Add(type);
            }
            return values.ToArray();
        }

        private void DrawVital(Rect rect, string title, float current, float maximum, Color fill, Texture2D pixel)
        {
            Fill(rect, Card, pixel);
            Stroke(rect, Gold, pixel);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f), title, _label);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 24f),
                $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}", _value);
            var track = new Rect(rect.x + 12f, rect.y + 44f, rect.width - 24f, 15f);
            Fill(track, ColorOf(0x0B0E0F), pixel);
            Stroke(track, Gold, pixel);
            float fraction = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
            if (fraction > 0f)
                Fill(new Rect(track.x + 2f, track.y + 2f, (track.width - 4f) * fraction, track.height - 4f), fill, pixel);
        }

        private void DrawStat(Rect rect, string title, string value, string subtitle,
            string icon, string tooltip, Texture2D pixel)
        {
            Fill(rect, Card, pixel);
            Stroke(rect, rect.Contains(Event.current.mousePosition) ? PaleGold : Gold, pixel);
            var iconBox = new Rect(rect.x + 10f, rect.y + 11f, 59f, 59f);
            Fill(iconBox, ColorOf(0x111719), pixel);
            Stroke(iconBox, Gold, pixel);
            var texture = GetIcon(icon);
            if (texture != null)
                GUI.DrawTexture(new Rect(iconBox.x + 5f, iconBox.y + 5f, 49f, 49f),
                    texture, ScaleMode.ScaleToFit, true);
            GUI.Label(new Rect(rect.x + 80f, rect.y + 10f, rect.width - 168f, 30f), title, _label);
            GUI.Label(new Rect(rect.x + 81f, rect.y + 40f, rect.width - 169f, 27f), subtitle, _small);
            GUI.Label(new Rect(rect.xMax - 87f, rect.y + 16f, 73f, 48f), value, _value);
            if (rect.Contains(Event.current.mousePosition)) _hoverText = tooltip;
        }

        private Texture2D GetIcon(string kind)
        {
            if (_icons.TryGetValue(kind, out var cached)) return cached;
            string resource = kind == "might" ? "Lineage2Valheim.Assets.might-buff.png" :
                "Lineage2Valheim.SlotAssets." + kind + "-slot-v1.png";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
                texture.filterMode = FilterMode.Bilinear;
                _icons[kind] = texture;
                return texture;
            }
        }

        internal void Dispose()
        {
            foreach (var icon in _icons.Values) UnityEngine.Object.Destroy(icon);
            _icons.Clear();
        }

        private void EnsureStyles()
        {
            if (_name != null) return;
            _name = Style(22, PaleGold, TextAnchor.MiddleLeft, true);
            _section = Style(21, PaleGold, TextAnchor.MiddleCenter, true);
            _label = Style(15, PaleGold, TextAnchor.MiddleLeft, true);
            _value = Style(23, PaleGold, TextAnchor.MiddleRight, true);
            _small = Style(12, Muted, TextAnchor.MiddleLeft, false);
            _small.wordWrap = true;
        }

        private static GUIStyle Style(int size, Color color, TextAnchor align, bool bold)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = align,
                wordWrap = false
            };
            style.normal.textColor = color;
            return style;
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

        private static Color ColorOf(int rgb) => new Color(
            ((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
    }
}
