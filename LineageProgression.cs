using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Character-wide Lineage level and XP for the current class-system prototype.</summary>
    internal static class LineageProgression
    {
        internal const int MaxLevel = 20;
        private const string ExperienceKey = "Lineage2Valheim.Progression.XP";
        private static readonly FieldInfo NoPlacementCostField =
            AccessTools.Field(typeof(Player), "m_noPlacementCost");
        private static readonly FieldInfo TamedField = AccessTools.Field(typeof(Character), "m_tamed");
        private static readonly FieldInfo CreatureLevelField = AccessTools.Field(typeof(Character), "m_level");
        private static readonly MethodInfo MineRockAllDestroyedMethod = AccessTools.Method(typeof(MineRock), "AllDestroyed");
        private static readonly MethodInfo MineRock5AllDestroyedMethod = AccessTools.Method(typeof(MineRock5), "AllDestroyed");
        private static GUIStyle _barText;
        private static GUIStyle _debugLevelButton;

        internal static int GetLevel(Player player) => GetLevelFromExperience(GetTotalExperience(player));

        internal static bool CanLearnSkill(Player player, ISkillDefinition skill)
        {
            return skill != null && (skill.PlannedLevel <= 0 || GetLevel(player) >= skill.PlannedLevel);
        }

        internal static int GetTotalExperience(Player player)
        {
            if (player?.m_customData == null ||
                !player.m_customData.TryGetValue(ExperienceKey, out var saved) ||
                !int.TryParse(saved, NumberStyles.Integer, CultureInfo.InvariantCulture, out int xp))
                return 0;
            return Mathf.Clamp(xp, 0, TotalExperienceAtLevel(MaxLevel));
        }

        internal static int ExperienceToNextLevel(int level)
        {
            if (level >= MaxLevel) return 0;
            level = Mathf.Clamp(level, 1, MaxLevel - 1);
            return 75 + 25 * level * level;
        }

        internal static int TotalExperienceAtLevel(int level)
        {
            int total = 0;
            for (int current = 1; current < Mathf.Clamp(level, 1, MaxLevel); current++)
                total += ExperienceToNextLevel(current);
            return total;
        }

        private static int GetLevelFromExperience(int xp)
        {
            int level = 1;
            while (level < MaxLevel && xp >= TotalExperienceAtLevel(level + 1)) level++;
            return level;
        }

        internal static void AddExperience(Player player, int amount, string reason)
        {
            if (player == null || player != Player.m_localPlayer || amount <= 0 || player.m_customData == null)
                return;
            int before = GetTotalExperience(player);
            int cap = TotalExperienceAtLevel(MaxLevel);
            if (before >= cap)
            {
                CharacterClassSystem.AddProfessionExperience(player, amount);
                return;
            }
            int after = Mathf.Min(cap, before + amount);
            int overflow = Mathf.Max(0, before + amount - cap);
            if (after <= before) return;

            int previousLevel = GetLevelFromExperience(before);
            int currentLevel = GetLevelFromExperience(after);
            player.m_customData[ExperienceKey] = after.ToString(CultureInfo.InvariantCulture);
            CharacterClassSystem.ApplyMappedSkillLevels(player);
            if (overflow > 0) CharacterClassSystem.AddProfessionExperience(player, overflow);
            if (currentLevel > previousLevel)
            {
                player.Message(MessageHud.MessageType.Center,
                    $"Lineage level {currentLevel} reached!");
                Plugin.Log?.LogInfo($"{player.GetPlayerName()} reached Lineage level {currentLevel} from {reason} XP.");
            }
        }

        private static void SetDebugLevel(Player player, int change)
        {
            if (player == null || player != Player.m_localPlayer || player.m_customData == null ||
                Plugin.Instance?.IsDebugMode != true || change == 0)
                return;

            int currentLevel = GetLevel(player);
            int targetLevel = Mathf.Clamp(currentLevel + change, 1, MaxLevel);
            // A minus click clears progress at level 1 too; on higher levels it
            // drops one level and starts that level with zero progress.
            if (targetLevel == currentLevel && (change > 0 || GetTotalExperience(player) == 0)) return;
            int xp = TotalExperienceAtLevel(targetLevel);
            player.m_customData[ExperienceKey] = xp.ToString(CultureInfo.InvariantCulture);
            CharacterClassSystem.ApplyMappedSkillLevels(player);
            Plugin.Log?.LogInfo($"Debug mode changed {player.GetPlayerName()} to Lineage level {targetLevel}.");
            player.Message(MessageHud.MessageType.Center, $"Debug: Lineage level {targetLevel}");
        }

        internal static void AwardMonsterKill(Character victim, HitData hit)
        {
            if (!IsHostileMob(victim) ||
                hit?.GetAttacker() != Player.m_localPlayer || !IsMonsterFaction(victim.m_faction.ToString()))
                return;

            int reward;
            if (victim.m_boss) reward = 200;
            else
            {
                float healthMultiplier = Plugin.Instance != null
                    ? Plugin.Instance.HostileMobHealthMultiplier
                    : 1f;
                float baseHealth = victim.GetMaxHealthBase() / Mathf.Max(1f, healthMultiplier);
                reward = baseHealth < 25f ? 15 : baseHealth < 100f ? 30 : 60;
                int creatureLevel = CreatureLevelField?.GetValue(victim) is int value ? value : 1;
                int stars = Mathf.Clamp(creatureLevel - 1, 0, 2);
                reward = Mathf.RoundToInt(reward * (1f + 0.5f * stars));
            }
            AddExperience(Player.m_localPlayer, reward, "creature kill");
        }

        private static bool IsMonsterFaction(string faction)
        {
            switch (faction)
            {
                case "ForestMonsters":
                case "Undead":
                case "Demon":
                case "MountainMonsters":
                case "SeaMonsters":
                case "PlainsMonsters":
                case "Boss":
                case "MistlandsMonsters":
                case "DeepNorth":
                case "PlayerSpawned":
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsHostileMob(Character character)
        {
            return character != null && !(character is Player) &&
                !(TamedField?.GetValue(character) is bool tamed && tamed) &&
                IsMonsterFaction(character.m_faction.ToString());
        }

        internal static void AwardTreeFelled(TreeBase tree, HitData hit)
        {
            if (tree == null || hit?.GetAttacker() != Player.m_localPlayer) return;
            string name = tree.name.ToLowerInvariant();
            int reward = name.Contains("oak") || name.Contains("birch") || name.Contains("pine") ? 10 : 5;
            AddExperience(Player.m_localPlayer, reward, "tree felling");
        }

        internal static void AwardOreNode(MineRock rock, HitData hit)
        {
            if (rock == null || hit?.GetAttacker() != Player.m_localPlayer) return;
            AddExperience(Player.m_localPlayer, OreReward(rock.name, rock.m_name), "mining");
        }

        internal static void AwardOreNode(MineRock5 rock, HitData hit)
        {
            if (rock == null || hit?.GetAttacker() != Player.m_localPlayer) return;
            AddExperience(Player.m_localPlayer, OreReward(rock.name, rock.m_name), "mining");
        }

        internal static bool IsOreNodeDepleted(MineRock rock) =>
            rock != null && MineRockAllDestroyedMethod != null &&
            MineRockAllDestroyedMethod.Invoke(rock, null) is bool destroyed && destroyed;

        internal static bool IsOreNodeDepleted(MineRock5 rock) =>
            rock != null && MineRock5AllDestroyedMethod != null &&
            MineRock5AllDestroyedMethod.Invoke(rock, null) is bool destroyed && destroyed;

        private static int OreReward(string objectName, string displayName)
        {
            string name = ((objectName ?? "") + " " + (displayName ?? "")).ToLowerInvariant();
            if (name.Contains("blackmetal") || name.Contains("flametal") || name.Contains("mistlands")) return 50;
            if (name.Contains("iron") || name.Contains("silver") || name.Contains("obsidian")) return 30;
            return 20;
        }

        internal static void AwardBuildingPiece(Player player, Piece piece)
        {
            if (player == null || piece == null || player != Player.m_localPlayer ||
                (NoPlacementCostField?.GetValue(player) is bool noCost && noCost) ||
                piece.m_resources == null || piece.m_resources.Length == 0)
                return;

            bool hasCost = false;
            bool advanced = false;
            foreach (var requirement in piece.m_resources)
            {
                if (requirement == null || requirement.m_resItem == null || requirement.m_amount <= 0) continue;
                hasCost = true;
                string resource = requirement.m_resItem.name.ToLowerInvariant();
                if (resource.Contains("stone") || resource.Contains("iron") || resource.Contains("blackmetal") ||
                    resource.Contains("marble") || resource.Contains("flametal"))
                    advanced = true;
            }
            if (hasCost) AddExperience(player, advanced ? 4 : 2, "building");
        }

        internal static void DrawXpBar(Player player, Texture2D pixel, LineageHotbars art)
        {
            if (player == null || pixel == null || art == null) return;
            EnsureBarStyle();

            float width = Mathf.Min(Screen.width * 0.74f, Screen.width - 24f);
            const float height = 18f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height - height - 8f;
            var frame = new Rect(x, y, width, height);
            art.DrawStyledFrame(frame, pixel);
            var track = new Rect(frame.x + 4f, frame.y + 3f, frame.width - 8f, frame.height - 6f);
            Fill(track, new Color(0.025f, 0.035f, 0.04f, 0.96f), pixel);

            int level = GetLevel(player);
            int total = GetTotalExperience(player);
            bool professionTrack = level >= MaxLevel && CharacterClassSystem.HasProfession(player);
            int trackLevel = professionTrack ? CharacterClassSystem.GetProfessionLevel(player) : level;
            int trackTotal = professionTrack ? CharacterClassSystem.GetProfessionExperience(player) : total;
            if (level >= MaxLevel && !professionTrack || trackLevel >= MaxLevel)
                Fill(track, ColorOf(0xC69A56), pixel);
            else
            {
                int intoLevel = trackTotal - TotalExperienceAtLevel(trackLevel);
                int required = ExperienceToNextLevel(trackLevel);
                float ratio = Mathf.Clamp01(intoLevel / (float)required);
                if (ratio > 0f)
                    Fill(new Rect(track.x, track.y, track.width * ratio, track.height), ColorOf(0xC69A56), pixel);
            }

            string label = professionTrack
                ? (trackLevel >= MaxLevel ? $"{CharacterClassSystem.GetProfessionName(player).ToUpperInvariant()} RANK {MaxLevel}   MAX" :
                    $"{CharacterClassSystem.GetProfessionName(player).ToUpperInvariant()} RANK {trackLevel}   {trackTotal - TotalExperienceAtLevel(trackLevel)} / {ExperienceToNextLevel(trackLevel)} XP")
                : level >= MaxLevel ? $"LEVEL {MaxLevel}   MAX" :
                    $"LEVEL {level}   {total - TotalExperienceAtLevel(level)} / {ExperienceToNextLevel(level)} XP";
            float labelInset = 2f;
            if (Plugin.Instance?.IsDebugMode == true)
            {
                EnsureDebugButtonStyle();
                const float buttonSize = 14f;
                var minus = new Rect(frame.x + 5f, frame.y + 2f, buttonSize, frame.height - 4f);
                var plus = new Rect(frame.xMax - buttonSize - 5f, frame.y + 2f, buttonSize, frame.height - 4f);
                if (GUI.Button(minus, "−", _debugLevelButton)) SetDebugLevel(player, -1);
                if (GUI.Button(plus, "+", _debugLevelButton)) SetDebugLevel(player, 1);
                labelInset = buttonSize + 8f;
            }
            GUI.Label(new Rect(track.x + labelInset, track.y - 1f,
                track.width - labelInset * 2f, track.height + 2f), label, _barText);
        }

        private static void EnsureDebugButtonStyle()
        {
            if (_debugLevelButton != null) return;
            _debugLevelButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            _debugLevelButton.normal.textColor = ColorOf(0xE0B56B);
            _debugLevelButton.hover.textColor = Color.white;
            _debugLevelButton.active.textColor = Color.white;
        }

        private static void EnsureBarStyle()
        {
            if (_barText != null) return;
            _barText = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };
            _barText.normal.textColor = Color.white;
        }

        private static void Fill(Rect rect, Color color, Texture2D pixel)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, pixel);
            GUI.color = previous;
        }

        private static Color ColorOf(int hex) => new Color(
            ((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f,
            (hex & 255) / 255f, 1f);
    }
}
