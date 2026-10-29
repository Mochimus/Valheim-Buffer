using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Owns the timed cast, interruption checks, and gameplay-effect completion.</summary>
    internal sealed class BuffCastingController
    {
        private readonly Plugin _plugin;
        private Player _player;
        private BuffSkillDefinition _skill;
        private GameObject _vfx;
        private Vector3 _startPosition;
        private float _startHealth;
        private float _startedAt;
        private float _duration;

        internal BuffCastingController(Plugin plugin) => _plugin = plugin;

        internal bool IsCasting => _skill != null;

        internal bool Begin(Player player, BuffSkillDefinition skill, float duration, float residualFade,
            bool vfxEnabled, float intensity, int ribbonCount, float particleDensity,
            bool magicCircle, bool castingLight)
        {
            if (IsCasting || player == null || player != Player.m_localPlayer || skill == null) return false;
            _player = player;
            _skill = skill;
            _duration = Mathf.Clamp(duration, 0.5f, 10f);
            _startedAt = Time.time;
            _startPosition = player.transform.position;
            _startHealth = player.GetHealth();
            _plugin.BeginBuffCastingAnimation(player);
            if (vfxEnabled)
                _vfx = BuffCastingVfx.Begin(player, skill.EffectId, _duration, residualFade,
                    intensity, ribbonCount, particleDensity, magicCircle, castingLight);
            player.Message(MessageHud.MessageType.Center,
                $"Casting {skill.DisplayName} — {Mathf.CeilToInt(_duration)}s");
            _plugin.LogBuffCast($"{player.GetPlayerName()} began casting {skill.DisplayName} ({_duration:0.0}s).");
            return true;
        }

        internal float GetRemainingSeconds(Player player, BuffSkillDefinition skill)
        {
            if (!IsCasting || player != _player || skill != _skill) return 0f;
            return Mathf.Max(0f, _duration - (Time.time - _startedAt));
        }

        internal void Update()
        {
            if (!IsCasting) return;
            string interruption = GetInterruptionReason();
            if (interruption != null)
            {
                Cancel(interruption, true);
                return;
            }
            if (Time.time - _startedAt < _duration) return;

            var player = _player;
            var skill = _skill;
            var vfx = _vfx;
            ClearState(false);
            if (_plugin.ApplyBuffEffect(player, skill))
            {
                player.Message(MessageHud.MessageType.Center, $"{skill.DisplayName} applied");
                _plugin.LogBuffCast($"{player.GetPlayerName()} completed casting {skill.DisplayName}.");
            }
            else
            {
                BuffCastingVfx.Stop(vfx);
                player.Message(MessageHud.MessageType.Center, $"{skill.DisplayName} cast failed");
            }
        }

        internal void Cancel(string reason, bool notifyPlayer)
        {
            if (!IsCasting) return;
            var player = _player;
            string name = _skill.DisplayName;
            ClearState(true);
            if (notifyPlayer && player != null && !player.IsDead())
                player.Message(MessageHud.MessageType.Center, $"{name} cast {reason}");
            _plugin.LogBuffCast($"{name} cast {reason}.");
        }

        private string GetInterruptionReason()
        {
            if (_player == null || _player.IsDead()) return "interrupted: death";
            if (_player.IsTeleporting()) return "interrupted: teleport";
            if (_player.InDodge()) return "interrupted: dodge";
            if (_player.InAttack()) return "interrupted: attack state";
            if (_player.GetHealth() < _startHealth - 0.01f) return "interrupted: damage";
            if ((_player.transform.position - _startPosition).sqrMagnitude > 0.09f)
                return "interrupted: movement";
            return null;
        }

        private void ClearState(bool cancelVfx)
        {
            if (cancelVfx) BuffCastingVfx.Stop(_vfx);
            _vfx = null;
            _player = null;
            _skill = null;
        }
    }
}
