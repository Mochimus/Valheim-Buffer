using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Class choice, saved native-skill baselines, and deterministic native-skill levels.</summary>
    internal static class CharacterClassSystem
    {
        private const string ClassKey = "Lineage2Valheim.Class";
        private const string ProfessionKey = "Lineage2Valheim.Profession";
        private const string RoguePathKey = "Lineage2Valheim.RoguePath";
        private const string ProfessionXpKey = "Lineage2Valheim.ProfessionXP";
        private const string BaselinePrefix = "Lineage2Valheim.SkillBaseline.";
        private const string Fighter = "Fighter";
        private static readonly FieldInfo NetworkViewField = AccessTools.Field(typeof(Character), "m_nview");
        private static readonly FieldInfo SkillsPlayerField = AccessTools.Field(typeof(Skills), "m_player");
        private static readonly FieldInfo SkillLevelField = AccessTools.Field(typeof(Skills.Skill), "m_level");
        private static readonly FieldInfo SkillAccumulatorField = AccessTools.Field(typeof(Skills.Skill), "m_accumulator");
        private static readonly MethodInfo GetSkillMethod = AccessTools.Method(typeof(Skills), "GetSkill", new[] { typeof(Skills.SkillType) });
        private static readonly MethodInfo GetZdoMethod = AccessTools.Method(typeof(ZNetView), "GetZDO");
        private static readonly MethodInfo GetZdoStringMethod = typeof(ZDO).GetMethod("GetString", new[] { typeof(string), typeof(string) });
        private static readonly MethodInfo SetZdoStringMethod = typeof(ZDO).GetMethod("Set", new[] { typeof(string), typeof(string) });

        // Workbook curve proposals; deliberately grouped here so balance edits are straightforward.
        private static readonly int[] CharacterCurveLevels = { 1, 5, 10, 15, 20 };
        private static readonly float[] CharacterCurveTargets = { 5, 10, 20, 30, 40 };
        private static readonly int[] ProfessionCurveLevels = { 1, 5, 10, 15, 20 };
        private static readonly float[] ProfessionCurveTargets = { 5, 15, 30, 45, 60 };
        private static bool _cursorStateSaved;
        private static bool _previousCursorVisible;
        private static CursorLockMode _previousCursorLock;
        private static int _identitySyncedPlayerId;
        private static string _lastSyncedClass, _lastSyncedProfession, _lastSyncedRoguePath;

        internal static bool NeedsSelection(Player player) => player?.m_customData != null &&
            (!player.m_customData.TryGetValue(ClassKey, out var value) || value != Fighter);

        internal static bool NeedsSpecialization(Player player) => GetClass(player) == Fighter &&
            LineageProgression.GetLevel(player) >= LineageProgression.MaxLevel &&
            (player.m_customData == null || !player.m_customData.ContainsKey(ProfessionKey));

        internal static bool IsSelectionOpen => NeedsSelection(Player.m_localPlayer) || NeedsSpecialization(Player.m_localPlayer);
        internal static bool HasClass(Player player) => GetClass(player) == Fighter;
        internal static bool HasProfession(Player player) => !string.IsNullOrEmpty(GetProfession(player));
        internal static string GetProfessionName(Player player) => GetProfession(player) ?? "Fighter";

        internal static string GetClass(Player player)
        {
            if (player?.m_customData != null && player.m_customData.TryGetValue(ClassKey, out var value)) return value;
            return GetSyncedValue(player, ClassKey);
        }

        internal static int GetProfessionLevel(Player player)
        {
            if (!HasProfession(player) || player.m_customData == null ||
                !player.m_customData.TryGetValue(ProfessionXpKey, out var saved) ||
                !int.TryParse(saved, NumberStyles.Integer, CultureInfo.InvariantCulture, out int xp)) return 1;
            int level = 1;
            while (level < LineageProgression.MaxLevel && xp >= LineageProgression.TotalExperienceAtLevel(level + 1)) level++;
            return level;
        }

        internal static int GetProfessionExperience(Player player) => GetProfessionExperienceValue(player);

        internal static void SyncIdentityIfNeeded(Player player)
        {
            if (!HasClass(player)) return;
            string className = GetClass(player) ?? string.Empty;
            string profession = GetProfession(player) ?? string.Empty;
            string roguePath = GetRoguePath(player) ?? string.Empty;
            int playerId = player.GetInstanceID();
            if (_identitySyncedPlayerId == playerId && className == _lastSyncedClass &&
                profession == _lastSyncedProfession && roguePath == _lastSyncedRoguePath) return;
            if (!SyncIdentity(player)) return;
            _identitySyncedPlayerId = playerId;
            _lastSyncedClass = className;
            _lastSyncedProfession = profession;
            _lastSyncedRoguePath = roguePath;
            Plugin.Log?.LogInfo($"Synchronized {player.GetPlayerName()}'s class identity: {className}, {profession}, {roguePath}.");
        }

        internal static void AddProfessionExperience(Player player, int amount)
        {
            if (player != Player.m_localPlayer || !HasClass(player) ||
                LineageProgression.GetLevel(player) < LineageProgression.MaxLevel || player.m_customData == null || amount <= 0) return;
            int current = GetProfessionExperienceValue(player);
            int cap = LineageProgression.TotalExperienceAtLevel(LineageProgression.MaxLevel);
            int next = Mathf.Min(cap, current + amount);
            if (next == current) return;
            int oldLevel = GetProfessionLevel(player);
            player.m_customData[ProfessionXpKey] = next.ToString(CultureInfo.InvariantCulture);
            ApplyMappedSkillLevels(player);
            int newLevel = GetProfessionLevel(player);
            if (HasProfession(player) && newLevel > oldLevel)
                player.Message(MessageHud.MessageType.Center, $"{GetProfession(player)} rank {newLevel} reached!");
        }

        internal static void ApplyMappedSkillLevels(Player player)
        {
            if (!HasClass(player) || player?.m_customData == null) return;
            var skills = player.GetSkills();
            if (skills == null) return;
            CaptureSkillBaseline(player, skills);
            foreach (Skills.SkillType type in Enum.GetValues(typeof(Skills.SkillType)))
            {
                if (type == Skills.SkillType.All) continue;
                float? target = GetTarget(player, type);
                if (!target.HasValue) continue;
                string key = BaselinePrefix + type;
                if (!float.TryParse(player.m_customData[key], NumberStyles.Float, CultureInfo.InvariantCulture, out float baseline)) continue;
                float desired = Mathf.Max(baseline, target.Value); // retain pre-class progress; never lower a saved skill.
                var skill = GetSkillMethod?.Invoke(skills, new object[] { type }) as Skills.Skill;
                if (skill == null || SkillLevelField == null) continue;
                float current = (float)SkillLevelField.GetValue(skill);
                if (Mathf.Abs(current - desired) < 0.001f) continue;
                SkillLevelField.SetValue(skill, desired);
                SkillAccumulatorField?.SetValue(skill, 0f);
            }
        }

        internal static bool ShouldSuppressNativeSkillXp(Skills skills)
        {
            var player = SkillsPlayerField?.GetValue(skills) as Player;
            return HasClass(player);
        }

        internal static bool SelectFighter(Player player)
        {
            if (player == null || player != Player.m_localPlayer || player.m_customData == null || !NeedsSelection(player)) return false;
            CaptureSkillBaseline(player, player.GetSkills());
            player.m_customData[ClassKey] = Fighter;
            SyncIdentityIfNeeded(player);
            ApplyMappedSkillLevels(player);
            Plugin.Log?.LogInfo($"{player.GetPlayerName()} selected the Fighter class.");
            RestoreCursor();
            return true;
        }

        internal static bool SelectProfession(Player player, string profession, string roguePath = null)
        {
            if (player == null || player != Player.m_localPlayer || player.m_customData == null || !NeedsSpecialization(player)) return false;
            if (profession != "Knight" && profession != "Warrior" && profession != "Rogue") return false;
            if (profession == "Rogue" && roguePath != "Assassin" && roguePath != "Archer") return false;
            player.m_customData[ProfessionKey] = profession;
            if (profession == "Rogue") player.m_customData[RoguePathKey] = roguePath;
            if (!player.m_customData.ContainsKey(ProfessionXpKey)) player.m_customData[ProfessionXpKey] = "0";
            SyncIdentityIfNeeded(player);
            ApplyMappedSkillLevels(player);
            Plugin.Log?.LogInfo($"{player.GetPlayerName()} chose Fighter profession {profession}{(roguePath == null ? "" : " / " + roguePath)}.");
            RestoreCursor();
            return true;
        }

        internal static void DrawSelection(Texture2D pixel)
        {
            var player = Player.m_localPlayer;
            if (!IsSelectionOpen || pixel == null) return;
            if (!_cursorStateSaved)
            {
                _previousCursorVisible = Cursor.visible;
                _previousCursorLock = Cursor.lockState;
                _cursorStateSaved = true;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GUI.color = new Color(0.015f, 0.022f, 0.03f, 0.86f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), pixel);
            GUI.color = Color.white;
            const float width = 620f, height = 440f;
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.color = new Color(0.045f, 0.06f, 0.065f, 0.99f);
            GUI.DrawTexture(panel, pixel);
            GUI.color = new Color(0.74f, 0.57f, 0.32f, 1f);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 3f), pixel);
            GUI.color = Color.white;
            bool classChoice = NeedsSelection(player);
            string titleText = classChoice ? "CHOOSE YOUR CLASS" : "CHOOSE YOUR FIGHTER PATH";
            var title = new GUIStyle(GUI.skin.label) { fontSize = 27, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = new Color(0.88f, 0.75f, 0.51f, 1f);
            GUI.Label(new Rect(panel.x + 24, panel.y + 18, panel.width - 48, 44), titleText, title);
            var body = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            body.normal.textColor = new Color(0.8f, 0.82f, 0.8f, 1f);
            GUI.Label(new Rect(panel.x + 36, panel.y + 65, panel.width - 72, 42),
                classChoice ? "Your class is saved to this character. Fighter is the only class available." :
                "Choose a specialization to begin post-level-20 profession progression.", body);

            if (classChoice)
            {
                DrawCard(pixel, panel, 0, "FIGHTER", "A versatile melee combatant with a shared movement foundation and future specialization.",
                    () => SelectFighter(player));
            }
            else
            {
                DrawCard(pixel, panel, 0, "KNIGHT", "Blocking, defense, and ally protection; lower damage burst.", () => SelectProfession(player, "Knight"));
                DrawCard(pixel, panel, 1, "WARRIOR", "Heavy melee damage and stagger; higher stamina commitment and less defense.", () => SelectProfession(player, "Warrior"));
                DrawCard(pixel, panel, 2, "ROGUE · ASSASSIN", "Fast precision with knives and stealth; lower durability.", () => SelectProfession(player, "Rogue", "Assassin"));
                DrawCard(pixel, panel, 3, "ROGUE · ARCHER", "Ranged precision with bows and crossbows; lower durability.", () => SelectProfession(player, "Rogue", "Archer"));
            }
        }

        private static void DrawCard(Texture2D pixel, Rect panel, int index, string heading, string description, Action choose)
        {
            int count = NeedsSelection(Player.m_localPlayer) ? 1 : 4;
            float top = panel.y + 122f;
            float h = count == 1 ? 170f : 65f;
            var rect = count == 1
                ? new Rect(panel.x + 65f, top + 18f, panel.width - 130f, h)
                : new Rect(panel.x + 35f, top + index * (h + 7f), panel.width - 70f, h);
            GUI.color = new Color(0.12f, 0.15f, 0.15f, 1f);
            GUI.DrawTexture(rect, pixel);
            GUI.color = Color.white;
            var header = new GUIStyle(GUI.skin.label) { fontSize = count == 1 ? 23 : 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            header.normal.textColor = new Color(0.88f, 0.75f, 0.51f, 1f);
            GUI.Label(new Rect(rect.x + 14, rect.y + 5, count == 1 ? rect.width - 30 : 175, count == 1 ? 38 : rect.height - 10), heading, header);
            var text = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = count == 1 ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, wordWrap = true };
            text.normal.textColor = new Color(0.8f, 0.82f, 0.8f, 1f);
            GUI.Label(new Rect(rect.x + (count == 1 ? 18 : 190), rect.y + 4, count == 1 ? rect.width - 36 : rect.width - 340, rect.height - 8), description, text);
            var buttonRect = count == 1
                ? new Rect(rect.center.x - 105, rect.yMax - 48, 210, 38)
                : new Rect(rect.xMax - 135, rect.y + 13, 118, 38);
            var buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            buttonStyle.normal.textColor = new Color(0.91f, 0.79f, 0.56f, 1f);
            if (GUI.Button(buttonRect, count == 1 ? "SELECT FIGHTER" : "SELECT", buttonStyle)) choose();
        }

        private static float? GetTarget(Player player, Skills.SkillType skill)
        {
            int characterLevel = LineageProgression.GetLevel(player);
            switch (skill)
            {
                case Skills.SkillType.Jump:
                case Skills.SkillType.Run:
                case Skills.SkillType.Swim:
                case Skills.SkillType.Dodge:
                    return Interpolate(characterLevel, CharacterCurveLevels, CharacterCurveTargets);
            }
            if (!HasProfession(player)) return null;
            string profession = GetProfession(player);
            bool mapped = skill == Skills.SkillType.Blocking && profession == "Knight";
            if (profession == "Knight" && skill == Skills.SkillType.Swords) mapped = true;
            if (profession == "Warrior" && (skill == Skills.SkillType.Axes || skill == Skills.SkillType.Clubs || skill == Skills.SkillType.Swords)) mapped = true;
            if (profession == "Rogue")
            {
                string path = GetRoguePath(player);
                if (path == "Assassin" && (skill == Skills.SkillType.Knives || skill == Skills.SkillType.Sneak)) mapped = true;
                if (path == "Archer" && (skill == Skills.SkillType.Bows || skill == Skills.SkillType.Crossbows)) mapped = true;
            }
            return mapped ? Interpolate(GetProfessionLevel(player), ProfessionCurveLevels, ProfessionCurveTargets) : (float?)null;
        }

        private static float Interpolate(int level, int[] levels, float[] values)
        {
            if (level <= levels[0]) return values[0];
            for (int i = 1; i < levels.Length; i++)
            {
                if (level > levels[i]) continue;
                float ratio = (level - levels[i - 1]) / (float)(levels[i] - levels[i - 1]);
                return Mathf.Lerp(values[i - 1], values[i], ratio);
            }
            return values[values.Length - 1];
        }

        private static void CaptureSkillBaseline(Player player, Skills skills)
        {
            if (player?.m_customData == null || skills == null) return;
            foreach (Skills.SkillType type in Enum.GetValues(typeof(Skills.SkillType)))
            {
                if (type == Skills.SkillType.All) continue;
                string key = BaselinePrefix + type;
                if (!player.m_customData.ContainsKey(key))
                    player.m_customData[key] = skills.GetSkillLevel(type).ToString("R", CultureInfo.InvariantCulture);
            }
        }

        private static string GetProfession(Player player)
        {
            if (player?.m_customData != null && player.m_customData.TryGetValue(ProfessionKey, out var value)) return value;
            return GetSyncedValue(player, ProfessionKey);
        }

        private static string GetRoguePath(Player player)
        {
            if (player?.m_customData != null && player.m_customData.TryGetValue(RoguePathKey, out var value)) return value;
            return GetSyncedValue(player, RoguePathKey);
        }

        private static string GetSyncedValue(Player player, string key)
        {
            var view = player != null ? NetworkViewField?.GetValue(player) as ZNetView : null;
            var zdo = view != null ? GetZdoMethod?.Invoke(view, null) as ZDO : null;
            return zdo != null && GetZdoStringMethod != null
                ? GetZdoStringMethod.Invoke(zdo, new object[] { key, string.Empty }) as string : null;
        }

        private static int GetProfessionExperienceValue(Player player) =>
            player?.m_customData != null && player.m_customData.TryGetValue(ProfessionXpKey, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int xp) ?
                Mathf.Clamp(xp, 0, LineageProgression.TotalExperienceAtLevel(LineageProgression.MaxLevel)) : 0;

        private static bool SyncIdentity(Player player)
        {
            var view = NetworkViewField?.GetValue(player) as ZNetView;
            var zdo = view != null ? GetZdoMethod?.Invoke(view, null) as ZDO : null;
            if (zdo == null || SetZdoStringMethod == null) return false;
            zdo.Set(ClassKey, GetClass(player) ?? string.Empty);
            zdo.Set(ProfessionKey, GetProfession(player) ?? string.Empty);
            zdo.Set(RoguePathKey, GetRoguePath(player) ?? string.Empty);
            return true;
        }

        private static void RestoreCursor()
        {
            if (!_cursorStateSaved) return;
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
            _cursorStateSaved = false;
        }
    }

    [HarmonyPatch(typeof(Skills), "RaiseSkill")]
    internal static class ClassSkillExperienceSuppressionPatch
    {
        private static bool Prefix(Skills __instance) => !CharacterClassSystem.ShouldSuppressNativeSkillXp(__instance);
    }
}
