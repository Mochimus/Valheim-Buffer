using HarmonyLib;

namespace Lineage2Valheim
{
    /// <summary>Raises hostile creature health while preserving Valheim's world and star multipliers.</summary>
    [HarmonyPatch(typeof(Character), "GetMaxHealthBase")]
    internal static class HostileMobHealthPatch
    {
        private static void Postfix(Character __instance, ref float __result)
        {
            if (Plugin.Instance != null && LineageProgression.IsHostileMob(__instance))
                __result *= Plugin.Instance.HostileMobHealthMultiplier;
        }
    }
}
