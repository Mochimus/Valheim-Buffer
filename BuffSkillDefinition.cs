using System;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>
    /// Reusable definition for a Lineage-style timed buff.
    /// Add one instance to Plugin.CreateSkills for each new skill.
    /// </summary>
    internal sealed class BuffSkillDefinition : ISkillDefinition
    {
        internal BuffSkillDefinition(
            string effectId,
            string displayName,
            string description,
            string iconResource,
            float durationSeconds,
            Action<SE_Stats> configureEffect,
            float warningWindowSeconds = 60f,
            int plannedLevel = 0,
            bool requiresLearning = false)
        {
            EffectId = effectId;
            DisplayName = displayName;
            Description = description;
            IconResource = iconResource;
            DurationSeconds = durationSeconds;
            ConfigureEffect = configureEffect;
            WarningWindowSeconds = warningWindowSeconds;
            PlannedLevel = plannedLevel;
            RequiresLearning = requiresLearning;
        }

        public string EffectId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string IconResource { get; }
        internal float DurationSeconds { get; }
        internal Action<SE_Stats> ConfigureEffect { get; }
        internal float WarningWindowSeconds { get; }
        public int PlannedLevel { get; }
        public bool RequiresLearning { get; }

        public Sprite Icon { get; set; }
        internal string StatusEffectName => "SE_" + EffectId;
        internal int StatusEffectHash => StringExtensionMethods.GetStableHashCode(StatusEffectName);
    }
}
