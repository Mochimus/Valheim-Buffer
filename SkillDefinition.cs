using UnityEngine;

namespace Lineage2Valheim
{
    internal interface ISkillDefinition
    {
        string EffectId { get; }
        string DisplayName { get; }
        string Description { get; }
        string IconResource { get; }
        int PlannedLevel { get; }
        bool RequiresLearning { get; }
        Sprite Icon { get; set; }
    }
}
