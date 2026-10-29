using System;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Data and activation callback for a learned, cooldown-based action skill.</summary>
    internal sealed class ActiveSkillDefinition : ISkillDefinition
    {
        internal ActiveSkillDefinition(string effectId, string displayName, string description,
            string iconResource, int plannedLevel, float staminaCost, float eitrCost,
            float cooldownSeconds, float armedWindowSeconds, Func<Player, float, bool> activate,
            Func<Player, float> getEffectWindowRemaining = null, string animationTrigger = "",
            bool startsAttack = false)
        {
            EffectId = effectId;
            DisplayName = displayName;
            Description = description;
            IconResource = iconResource;
            PlannedLevel = plannedLevel;
            StaminaCost = staminaCost;
            EitrCost = eitrCost;
            CooldownSeconds = cooldownSeconds;
            ArmedWindowSeconds = armedWindowSeconds;
            Activate = activate;
            GetEffectWindowRemaining = getEffectWindowRemaining;
            AnimationTrigger = animationTrigger ?? string.Empty;
            StartsAttack = startsAttack;
        }

        public string EffectId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string IconResource { get; }
        public int PlannedLevel { get; }
        public bool RequiresLearning => true;
        public Sprite Icon { get; set; }
        internal float StaminaCost { get; }
        internal float EitrCost { get; }
        internal float CooldownSeconds { get; }
        internal float ArmedWindowSeconds { get; }
        internal Func<Player, float, bool> Activate { get; }
        internal Func<Player, float> GetEffectWindowRemaining { get; }
        internal string AnimationTrigger { get; }
        internal bool StartsAttack { get; }
    }
}
