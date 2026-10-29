using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Runtime state and hit hook for the first active combat skill.</summary>
    internal static class PowerStrikeRuntime
    {
        private static readonly Dictionary<int, float> ArmedUntil = new Dictionary<int, float>();
        private const float PhysicalDamageMultiplier = 1.5f;

        internal static bool Arm(Player player, float durationSeconds)
        {
            if (player == null) return false;
            ArmedUntil[player.GetInstanceID()] = Time.time + Mathf.Max(0.1f, durationSeconds);
            Character target = player.GetHoverCreature();
            bool aimedAtMob = LineageProgression.IsHostileMob(target);
            if (aimedAtMob)
            {
                Vector3 direction = target.transform.position - player.transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.001f) player.SetLookDir(direction.normalized, 0f);
            }

            bool attackLookDirection = player.AttackTowardsPlayerLookDir;
            player.AttackTowardsPlayerLookDir = true;
            bool attackStarted;
            try { attackStarted = player.StartAttack(target, false); }
            finally { player.AttackTowardsPlayerLookDir = attackLookDirection; }
            if (attackStarted)
            {
                PowerStrikeVfx.PlayActivation(player);
                player.Message(MessageHud.MessageType.Center,
                    aimedAtMob
                        ? $"Power Strike! Attacking {target.GetHoverName()} — hit within {Mathf.CeilToInt(durationSeconds)}s"
                        : $"Power Strike! Swing now — hit a creature within {Mathf.CeilToInt(durationSeconds)}s");
                return true;
            }
            ArmedUntil.Remove(player.GetInstanceID());
            player.Message(MessageHud.MessageType.Center, "Power Strike failed: you cannot attack right now");
            return false;
        }

        internal static void Cancel(Player player)
        {
            if (player != null) ArmedUntil.Remove(player.GetInstanceID());
        }

        internal static float GetArmedSeconds(Player player)
        {
            if (player == null || !ArmedUntil.TryGetValue(player.GetInstanceID(), out float expiresAt)) return 0f;
            float remaining = Mathf.Max(0f, expiresAt - Time.time);
            if (remaining <= 0f) ArmedUntil.Remove(player.GetInstanceID());
            return remaining;
        }

        internal static bool Consume(Player player)
        {
            if (player == null) return false;
            int id = player.GetInstanceID();
            if (!ArmedUntil.TryGetValue(id, out float expiresAt)) return false;
            ArmedUntil.Remove(id);
            return Time.time <= expiresAt;
        }

        internal static void ApplyBonus(Character target, HitData hit)
        {
            if (hit == null || !hit.m_damage.HaveDamage()) return;
            if (!(hit.GetAttacker() is Player attacker))
            {
                var localPlayer = Player.m_localPlayer;
                if (LineageProgression.IsHostileMob(target) && localPlayer != null &&
                    ArmedUntil.TryGetValue(localPlayer.GetInstanceID(), out float pending) && Time.time <= pending)
                    Plugin.Log?.LogWarning($"Power Strike could not resolve the hit attacker for target {target.name}.");
                return;
            }
            if (target == null || !LineageProgression.IsHostileMob(target) ||
                target.m_faction.ToString() == "PlayerSpawned")
            {
                if (ArmedUntil.TryGetValue(attacker.GetInstanceID(), out float expiresAt) && Time.time <= expiresAt)
                    Plugin.Log?.LogWarning($"Power Strike ignored non-hostile target {target?.name ?? "<null>"} " +
                        $"(faction {target?.m_faction.ToString() ?? "unknown"}).");
                return;
            }
            if (!Consume(attacker)) return;

            var damage = hit.m_damage;
            damage.m_blunt *= PhysicalDamageMultiplier;
            damage.m_slash *= PhysicalDamageMultiplier;
            damage.m_pierce *= PhysicalDamageMultiplier;
            hit.m_damage = damage;
            PowerStrikeVfx.PlayImpact(target);
            attacker.Message(MessageHud.MessageType.TopLeft, "Power Strike!");
            Plugin.Log?.LogInfo($"Power Strike empowered hit on {target.name} ({target.m_faction}) by 50%.");
        }
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    internal static class PowerStrikeDamagePatch
    {
        private static void Prefix(Character __instance, HitData hit) =>
            PowerStrikeRuntime.ApplyBonus(__instance, hit);
    }
}
