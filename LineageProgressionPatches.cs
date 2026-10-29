using HarmonyLib;
using System.Reflection;

namespace Lineage2Valheim
{
    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class CreatureKillExperiencePatch
    {
        private static readonly FieldInfo LastHitField = AccessTools.Field(typeof(Character), "m_lastHit");

        private static void Postfix(Character __instance) =>
            LineageProgression.AwardMonsterKill(__instance, LastHitField?.GetValue(__instance) as HitData);
    }

    [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
    internal static class TreeFellingExperiencePatch
    {
        private static void Prefix(TreeBase __instance, ref float __state) =>
            __state = __instance != null ? __instance.m_health : 0f;

        private static void Postfix(TreeBase __instance, HitData hit, float __state)
        {
            if (__instance != null && __state > 0f && __instance.m_health <= 0f)
                LineageProgression.AwardTreeFelled(__instance, hit);
        }
    }

    [HarmonyPatch(typeof(MineRock), "RPC_Hit")]
    internal static class MineRockExperiencePatch
    {
        private static void Prefix(MineRock __instance, ref bool __state) =>
            __state = LineageProgression.IsOreNodeDepleted(__instance);

        private static void Postfix(MineRock __instance, HitData hit, bool __state)
        {
            if (__instance != null && !__state && LineageProgression.IsOreNodeDepleted(__instance))
                LineageProgression.AwardOreNode(__instance, hit);
        }
    }

    [HarmonyPatch(typeof(MineRock5), "RPC_Damage")]
    internal static class MineRock5ExperiencePatch
    {
        private static void Prefix(MineRock5 __instance, ref bool __state) =>
            __state = LineageProgression.IsOreNodeDepleted(__instance);

        private static void Postfix(MineRock5 __instance, HitData hit, bool __state)
        {
            if (__instance != null && !__state && LineageProgression.IsOreNodeDepleted(__instance))
                LineageProgression.AwardOreNode(__instance, hit);
        }
    }

    [HarmonyPatch(typeof(Player), "TryPlacePiece")]
    internal static class BuildingExperiencePatch
    {
        private static void Postfix(Player __instance, Piece piece, bool __result)
        {
            if (__result) LineageProgression.AwardBuildingPiece(__instance, piece);
        }
    }
}
