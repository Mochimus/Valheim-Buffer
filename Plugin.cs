using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Lineage2Valheim
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.lineage2valheim.buffs";
        public const string PluginName = "Lineage 2 Valheim Buffs";
        public const string PluginVersion = "0.23.0";
        private const string LearnedSkillPrefix = "Lineage2Valheim.Learned.";
        private const string ActiveSkillCooldownPrefix = "Lineage2Valheim.Cooldown.";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        private static readonly FieldInfo[] SuppressedActionFields =
        {
            AccessTools.Field(typeof(Character), "m_attack"),
            AccessTools.Field(typeof(Character), "m_attackHold"),
            AccessTools.Field(typeof(Character), "m_secondaryAttack"),
            AccessTools.Field(typeof(Character), "m_secondaryAttackHold"),
            AccessTools.Field(typeof(Character), "m_blocking")
        };
        private static readonly FieldInfo[] SuppressedQueueFields =
        {
            AccessTools.Field(typeof(Player), "m_queuedAttackTimer"),
            AccessTools.Field(typeof(Player), "m_queuedSecondAttackTimer"),
            AccessTools.Field(typeof(Player), "m_queuedDodgeTimer")
        };
        private static readonly List<GameObject> HiddenVanillaHotbars = new List<GameObject>();
        private static readonly List<GameObject> HiddenVanillaVitals = new List<GameObject>();
        private static readonly List<Graphic> HiddenVanillaVitalGraphics = new List<Graphic>();
        private static readonly Dictionary<GameObject, Graphic[]> VitalGraphicsByRoot = new Dictionary<GameObject, Graphic[]>();
        private static readonly FieldInfo[] VanillaVitalRoots =
        {
            AccessTools.Field(typeof(Hud), "m_healthBarRoot"),
            AccessTools.Field(typeof(Hud), "m_foodBarRoot"),
            AccessTools.Field(typeof(Hud), "m_foodBaseBar"),
            AccessTools.Field(typeof(Hud), "m_foodBars"),
            AccessTools.Field(typeof(Hud), "m_foodIcons"),
            AccessTools.Field(typeof(Hud), "m_foodTime"),
            AccessTools.Field(typeof(Hud), "m_foodIcon"),
            AccessTools.Field(typeof(Hud), "m_foodText"),
            AccessTools.Field(typeof(Hud), "m_staminaBar2Root"),
            AccessTools.Field(typeof(Hud), "m_eitrBarRoot"),
            AccessTools.Field(typeof(Hud), "m_eitrAnimator"),
            AccessTools.Field(typeof(Hud), "m_statusEffectListRoot")
        };
        private static readonly FieldInfo VanillaStatusEffectListRoot =
            AccessTools.Field(typeof(Hud), "m_statusEffectListRoot");
        private static readonly FieldInfo CharacterAnimatorField =
            AccessTools.Field(typeof(Character), "m_animator");

        private readonly List<BuffSkillDefinition> _skills = new List<BuffSkillDefinition>();
        private readonly List<ActiveSkillDefinition> _activeSkills = new List<ActiveSkillDefinition>();
        private readonly Dictionary<string, float> _activeSkillReadyAt = new Dictionary<string, float>();
        private Harmony _harmony;
        private Texture2D _pixel;
        private readonly LineageHotbars _hotbars = new LineageHotbars();
        private readonly LineageVitalsPanel _vitals = new LineageVitalsPanel();
        private LineageMenuUi _menuUi;
        private BuffCastingController _buffCasting;
        private bool _debugMode;
        private bool _usingItemFromInventory;
        private bool _performingSkillAction;
        private Player _skillAttackPlayer;
        private float _skillAttackKeepAliveUntil;
        private ConfigEntry<float> _hostileMobHealthMultiplier;
        private ConfigEntry<float> _buffCastDuration;
        private ConfigEntry<bool> _buffCastingVfxEnabled;
        private ConfigEntry<string> _buffVfxIntensity;
        private ConfigEntry<int> _buffEnergyRibbonCount;
        private ConfigEntry<float> _buffParticleDensity;
        private ConfigEntry<bool> _buffMagicCircleEnabled;
        private ConfigEntry<bool> _buffCastingLightEnabled;
        private ConfigEntry<float> _buffResidualFade;

    internal bool IsCustomUiOpen => _menuUi?.IsOpen == true || CharacterClassSystem.IsSelectionOpen;
        internal bool IsHotbarCursorMode => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        internal bool IsDebugMode => _debugMode;
        internal bool IsCursorReleased => IsHotbarCursorMode || IsDebugMode;
        internal bool IsUsingItemFromInventory => _usingItemFromInventory;
        internal bool IsPerformingSkillAction => _performingSkillAction;
        internal BuffCastingController BuffCasting => _buffCasting;
        internal bool ShouldPreserveSkillAttack(Player player)
        {
            if (player != _skillAttackPlayer) return false;
            if (player.InAttack() || Time.time < _skillAttackKeepAliveUntil) return true;
            _skillAttackPlayer = null;
            return false;
        }
        internal float HostileMobHealthMultiplier => Mathf.Clamp(_hostileMobHealthMultiplier?.Value ?? 4f, 1f, 10f);
        internal LineageHotbars Hotbars => _hotbars;
        internal IReadOnlyList<BuffSkillDefinition> Skills => _skills;
        internal IReadOnlyList<ActiveSkillDefinition> ActiveSkills => _activeSkills;

        internal bool IsLearned(Player player, ISkillDefinition skill)
        {
            return skill != null && (!skill.RequiresLearning ||
                (player != null && player.m_customData != null &&
                 player.m_customData.TryGetValue(LearnedSkillPrefix + skill.EffectId, out var value) && value == "1"));
        }

        internal bool CanLearnSkill(Player player, ISkillDefinition skill)
        {
            return player != null && skill != null && LineageProgression.CanLearnSkill(player, skill);
        }

        internal bool LearnSkill(Player player, ISkillDefinition skill)
        {
            if (player == null || skill == null || !skill.RequiresLearning || IsLearned(player, skill) ||
                !CanLearnSkill(player, skill) || player.m_customData == null) return false;
            player.m_customData[LearnedSkillPrefix + skill.EffectId] = "1";
            Logger.LogInfo($"{player.GetPlayerName()} learned {skill.DisplayName}.");
            return true;
        }

        internal float GetActiveSkillCooldownRemaining(Player player, ActiveSkillDefinition skill)
        {
            if (player == null || skill == null || player.m_customData == null) return 0f;
            string savedKey = ActiveSkillCooldownPrefix + skill.EffectId;
            string runtimeKey = player.GetInstanceID() + ":" + savedKey;
            if (!_activeSkillReadyAt.TryGetValue(runtimeKey, out float readyAt))
            {
                if (!player.m_customData.TryGetValue(savedKey, out string saved) ||
                    !long.TryParse(saved, out long expiresUtcTicks)) return 0f;
                float remaining = (float)Math.Max(0d,
                    (expiresUtcTicks - DateTime.UtcNow.Ticks) / (double)TimeSpan.TicksPerSecond);
                readyAt = Time.time + Mathf.Max(0f, remaining);
                _activeSkillReadyAt[runtimeKey] = readyAt;
            }
            float seconds = Mathf.Max(0f, readyAt - Time.time);
            if (seconds <= 0f)
            {
                player.m_customData.Remove(savedKey);
                _activeSkillReadyAt.Remove(runtimeKey);
            }
            return seconds;
        }

        internal bool UseActiveSkill(Player player, ActiveSkillDefinition skill)
        {
            if (player == null || player != Player.m_localPlayer || skill == null) return false;
            if (!IsLearned(player, skill))
            {
                player.Message(MessageHud.MessageType.Center, $"Learn {skill.DisplayName} first");
                Logger.LogWarning($"Activation denied for {skill.DisplayName}: skill is not learned.");
                return false;
            }
            if (!CanLearnSkill(player, skill))
            {
                player.Message(MessageHud.MessageType.Center, $"Requires level {skill.PlannedLevel}");
                Logger.LogWarning($"Activation denied for {skill.DisplayName}: level requirement not met.");
                return false;
            }
            float cooldown = GetActiveSkillCooldownRemaining(player, skill);
            if (cooldown > 0f)
            {
                player.Message(MessageHud.MessageType.Center, $"{skill.DisplayName}: {cooldown:0}s cooldown");
                return false;
            }
            if (skill.StaminaCost > 0f && !player.HaveStamina(skill.StaminaCost))
            {
                player.Message(MessageHud.MessageType.Center, $"Not enough stamina — {skill.DisplayName} needs {skill.StaminaCost:0}");
                return false;
            }
            if (skill.EitrCost > 0f && !player.HaveEitr(skill.EitrCost))
            {
                player.Message(MessageHud.MessageType.Center, $"Not enough eitr — {skill.DisplayName} needs {skill.EitrCost:0}");
                return false;
            }
            if (skill.StaminaCost > 0f) player.UseStamina(skill.StaminaCost);
            if (skill.EitrCost > 0f) player.UseEitr(skill.EitrCost);
            bool activated = false;
            _performingSkillAction = true;
            try { activated = skill.Activate != null && skill.Activate(player, skill.ArmedWindowSeconds); }
            finally { _performingSkillAction = false; }
            if (!activated)
            {
                if (skill.StaminaCost > 0f) player.AddStamina(skill.StaminaCost);
                if (skill.EitrCost > 0f) player.AddEitr(skill.EitrCost);
                Logger.LogWarning($"Activation failed for {skill.DisplayName}; its action callback did not start.");
                return false;
            }
            if (skill.StartsAttack)
            {
                _skillAttackPlayer = player;
                _skillAttackKeepAliveUntil = Time.time + 1.25f;
            }
            float readyAt = Time.time + skill.CooldownSeconds;
            string savedKey = ActiveSkillCooldownPrefix + skill.EffectId;
            string runtimeKey = player.GetInstanceID() + ":" + savedKey;
            _activeSkillReadyAt[runtimeKey] = readyAt;
            if (player.m_customData != null)
                player.m_customData[savedKey] = DateTime.UtcNow.AddSeconds(skill.CooldownSeconds).Ticks.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            PlaySkillAnimation(player, skill.AnimationTrigger);
            Logger.LogInfo($"Used active skill {skill.DisplayName}; cooldown {skill.CooldownSeconds:0}s.");
            player.Message(MessageHud.MessageType.Center,
                $"{skill.DisplayName} activated — cooldown {skill.CooldownSeconds:0}s");
            return true;
        }

        private void PlaySkillAnimation(Player player, string triggerName)
        {
            if (!TryPlaySkillAnimation(player, triggerName))
                Logger.LogWarning($"Animation trigger '{triggerName}' is not available on the character animator.");
        }

        private bool TryPlaySkillAnimation(Player player, string triggerName)
        {
            if (player == null || string.IsNullOrEmpty(triggerName)) return false;
            var animator = CharacterAnimatorField?.GetValue(player) as Animator;
            if (animator == null) return false;
            int hash = Animator.StringToHash(triggerName);
            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash != hash || parameter.type != AnimatorControllerParameterType.Trigger)
                    continue;
                animator.ResetTrigger(hash);
                animator.SetTrigger(hash);
                return true;
            }
            return false;
        }

        internal bool UseSkill(Player player, BuffSkillDefinition skill)
        {
            if (player == null || skill == null || !IsLearned(player, skill)) return false;
            if (_buffCasting?.IsCasting == true)
            {
                player.Message(MessageHud.MessageType.Center, "Already casting a buff");
                return false;
            }
            RegisterEffects(ObjectDB.instance);
            var manager = player.GetSEMan();
            var template = ObjectDB.instance?.GetStatusEffect(skill.StatusEffectHash);
            if (manager == null || template == null)
            {
                Logger.LogWarning($"Could not find the {skill.DisplayName} status effect.");
                return false;
            }
            float intensity = string.Equals(_buffVfxIntensity?.Value, "Low", StringComparison.OrdinalIgnoreCase) ? 0.8f
                : string.Equals(_buffVfxIntensity?.Value, "Medium", StringComparison.OrdinalIgnoreCase) ? 1.15f : 1.5f;
            return _buffCasting.Begin(player, skill, _buffCastDuration?.Value ?? 2.5f,
                _buffResidualFade?.Value ?? 0.4f, _buffCastingVfxEnabled?.Value ?? true, intensity,
                _buffEnergyRibbonCount?.Value ?? 4, _buffParticleDensity?.Value ?? 1f,
                _buffMagicCircleEnabled?.Value ?? true, _buffCastingLightEnabled?.Value ?? true);
        }

        internal bool ApplyBuffEffect(Player player, BuffSkillDefinition skill)
        {
            if (player == null || skill == null || player != Player.m_localPlayer) return false;
            RegisterEffects(ObjectDB.instance);
            var manager = player.GetSEMan();
            var template = ObjectDB.instance?.GetStatusEffect(skill.StatusEffectHash);
            if (manager == null || template == null)
            {
                Logger.LogWarning($"Could not find the {skill.DisplayName} status effect at cast completion.");
                return false;
            }
            manager.AddStatusEffect(skill.StatusEffectHash, true);
            if (!manager.HaveStatusEffect(skill.StatusEffectHash))
            {
                // A local character can lack a valid network view while the UI is open.
                // Applying its registered effect directly still gives the player the buff.
                manager.AddStatusEffect(template, true);
            }
            if (!manager.HaveStatusEffect(skill.StatusEffectHash))
            {
                Logger.LogWarning($"Could not apply {skill.DisplayName} to the local player.");
                return false;
            }
            var activeEffect = manager.GetStatusEffect(skill.StatusEffectHash);
            activeEffect.m_hidden = true;
            if (activeEffect is SE_Stats activeStats)
                skill.ConfigureEffect?.Invoke(activeStats);
            Logger.LogInfo($"Applied {skill.DisplayName} to the local player for {skill.DurationSeconds:0} seconds.");
            return true;
        }

        internal void BeginBuffCastingAnimation(Player player)
        {
            // staff_summon is an attack animation in Valheim. It sets InAttack(), which made the
            // casting controller cancel the cast on its next update and destroy the VFX instantly.
            // Use the networked, non-combat emote while the custom spellcasting animation is not
            // available in the player's animator.
            if (!player.StartEmote("cheer", true))
                Logger.LogWarning("Buff cast emote could not start; the casting VFX will still play.");
        }

        internal void LogBuffCast(string message) => Logger.LogInfo(message);

        internal float GetActiveBuffRemainingSeconds(Player player, BuffSkillDefinition skill)
        {
            if (player == null || skill == null) return 0f;
            var effect = player.GetSEMan()?.GetStatusEffect(skill.StatusEffectHash);
            return effect != null ? Mathf.Max(0f, effect.GetRemaningTime()) : 0f;
        }

        internal bool RemoveActiveBuff(Player player, BuffSkillDefinition skill)
        {
            if (!_debugMode || player == null || player != Player.m_localPlayer || skill == null) return false;
            var manager = player.GetSEMan();
            if (manager?.GetStatusEffect(skill.StatusEffectHash) == null) return false;
            bool removed = manager.RemoveStatusEffect(skill.StatusEffectHash, true);
            if (removed) Logger.LogInfo($"Debug mode removed {skill.DisplayName} from {player.GetPlayerName()}.");
            return removed;
        }

        internal bool UseItemFromInventory(Player player, ItemDrop.ItemData item)
        {
            if (player == null || player != Player.m_localPlayer || item == null ||
                !player.GetInventory().ContainsItem(item) ||
                item.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.Consumable)
                return false;

            _usingItemFromInventory = true;
            try
            {
                player.UseItem(player.GetInventory(), item, false);
                var activeFoods = player.GetFoods();
                Logger.LogInfo($"Used {item.m_shared.m_name}; active food slots: {activeFoods?.Count ?? 0}/3.");
                return true;
            }
            finally
            {
                _usingItemFromInventory = false;
            }
        }

        internal static void FreezeLocalPlayer(Player player)
        {
            if (player == null || player != Player.m_localPlayer ||
                (Instance?.IsCustomUiOpen != true && Instance?.IsCursorReleased != true))
                return;
            player.m_autoRun = false;
            player.SetMoveDir(Vector3.zero);
            player.SetRun(false);
            player.StopMovementXZ();
            bool preserveAttack = Instance?.ShouldPreserveSkillAttack(player) == true;
            foreach (var field in SuppressedActionFields)
            {
                if (preserveAttack && (field?.Name == "m_attack" || field?.Name == "m_attackHold")) continue;
                field?.SetValue(player, false);
            }
            foreach (var field in SuppressedQueueFields)
            {
                if (preserveAttack && (field?.Name == "m_queuedAttackTimer" || field?.Name == "m_queuedSecondAttackTimer")) continue;
                field?.SetValue(player, 0f);
            }
        }

        internal void OpenCustomInventoryFromGame()
        {
            _menuUi?.OpenInventoryFromGame();
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _hostileMobHealthMultiplier = Config.Bind("Balance", "HostileMobHealthMultiplier", 4f,
                "Multiplier for max health of hostile, untamed creatures. Set to 1 to disable.");
            _buffCastDuration = Config.Bind("Buff Casting", "CastDuration", 2.5f,
                new ConfigDescription("Buff cast time in seconds.", new AcceptableValueRange<float>(0.5f, 10f)));
            _buffCastingVfxEnabled = Config.Bind("Buff Casting", "VfxEnabled", true,
                "Show the temporary casting visuals. Buff gameplay still works when disabled.");
            _buffVfxIntensity = Config.Bind("Buff Casting", "VfxIntensity", "High",
                "Visual intensity: Low, Medium, or High.");
            _buffEnergyRibbonCount = Config.Bind("Buff Casting", "EnergyRibbonCount", 4,
                new ConfigDescription("Number of procedural energy ribbons.", new AcceptableValueRange<int>(1, 8)));
            _buffParticleDensity = Config.Bind("Buff Casting", "ParticleDensity", 1f,
                new ConfigDescription("Particle amount multiplier.", new AcceptableValueRange<float>(0.25f, 2f)));
            _buffMagicCircleEnabled = Config.Bind("Buff Casting", "MagicCircleEnabled", true,
                "Draw a ground-aligned transient arcane circle during casting.");
            _buffCastingLightEnabled = Config.Bind("Buff Casting", "CastingLightEnabled", true,
                "Use one short-lived point light during the spell build and release.");
            _buffResidualFade = Config.Bind("Buff Casting", "ResidualFade", 0.4f,
                new ConfigDescription("Fade duration after the cast completes.", new AcceptableValueRange<float>(0.3f, 0.6f)));
            _buffCasting = new BuffCastingController(this);
            CreateSkills();
            foreach (var skill in _skills)
                skill.Icon = LoadIcon(skill.IconResource);
            foreach (var skill in _activeSkills)
                skill.Icon = LoadIcon(skill.IconResource);
            _hotbars.PrepareArt();

            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            _menuUi = new LineageMenuUi(Config);
            Logger.LogInfo($"{PluginName} loaded. Menu shortcuts: {_menuUi.DescribeShortcuts()}.");
        }

        private void OnDestroy()
        {
            _buffCasting?.Cancel("mod shutdown", false);
            _menuUi?.Dispose();
            _hotbars.DisposeArt();
            foreach (var bar in HiddenVanillaHotbars)
                if (bar != null) bar.SetActive(true);
            HiddenVanillaHotbars.Clear();
            foreach (var root in HiddenVanillaVitals)
                if (root != null) root.SetActive(true);
            HiddenVanillaVitals.Clear();
            foreach (var graphic in HiddenVanillaVitalGraphics)
                if (graphic != null) graphic.enabled = true;
            HiddenVanillaVitalGraphics.Clear();
            VitalGraphicsByRoot.Clear();
            BuffCastingVfx.Cleanup();
            _harmony?.UnpatchSelf();
        }

        internal static void HideVanillaHotbar(HotkeyBar bar)
        {
            if (bar == null || !bar.gameObject.activeSelf) return;
            if (!HiddenVanillaHotbars.Contains(bar.gameObject))
                HiddenVanillaHotbars.Add(bar.gameObject);
            bar.gameObject.SetActive(false);
        }

        internal static void HideVanillaVitals(Hud hud)
        {
            if (hud == null) return;
            foreach (var field in VanillaVitalRoots)
            {
                var value = field?.GetValue(hud);
                HideVitalValue(value);
            }
        }

        internal static void HideVanillaStatusEffects(Hud hud)
        {
            if (hud == null) return;
            HideVitalValue(VanillaStatusEffectListRoot?.GetValue(hud));
        }

        private static void HideVitalValue(object value)
        {
            if (value == null) return;
            if (value is Component component)
            {
                HideVitalComponent(component);
                return;
            }
            if (value is GameObject gameObject)
            {
                HideVitalObject(gameObject);
                foreach (var graphic in gameObject.GetComponentsInChildren<Graphic>(true))
                {
                    if (graphic == null) continue;
                    if (graphic.enabled && !HiddenVanillaVitalGraphics.Contains(graphic))
                        HiddenVanillaVitalGraphics.Add(graphic);
                    graphic.enabled = false;
                }
                return;
            }
            if (value is IEnumerable values)
                foreach (var element in values)
                    HideVitalValue(element);
        }

        private static void HideVitalComponent(Component component)
        {
            if (component == null) return;
            // Valheim and other HUD patches can turn individual food objects back on.
            // Disable their graphics as well so a later SetActive cannot draw them.
            var root = component.gameObject;
            if (!VitalGraphicsByRoot.TryGetValue(root, out var graphics))
            {
                graphics = component.GetComponentsInChildren<Graphic>(true);
                VitalGraphicsByRoot[root] = graphics;
            }
            foreach (var graphic in graphics)
            {
                if (graphic == null || !graphic.enabled) continue;
                if (!HiddenVanillaVitalGraphics.Contains(graphic))
                    HiddenVanillaVitalGraphics.Add(graphic);
                graphic.enabled = false;
            }
            HideVitalObject(root);
        }

        private static void HideVitalObject(GameObject obj)
        {
            if (obj == null || !obj.activeSelf) return;
            if (!HiddenVanillaVitals.Contains(obj)) HiddenVanillaVitals.Add(obj);
            obj.SetActive(false);
        }

        private void CreateSkills()
        {
            _activeSkills.Add(new ActiveSkillDefinition(
                effectId: "PowerStrike",
                displayName: "Power Strike",
                description: "Automatically swings once. If it hits a hostile creature within 3 seconds, it deals 50% bonus physical damage.",
                iconResource: "Lineage2Valheim.Assets.might-buff.png",
                plannedLevel: 1,
                staminaCost: 5f,
                eitrCost: 0f,
                cooldownSeconds: 12f,
                armedWindowSeconds: 3f,
                activate: (player, duration) => PowerStrikeRuntime.Arm(player, duration),
                getEffectWindowRemaining: PowerStrikeRuntime.GetArmedSeconds,
                startsAttack: true));
            _skills.Add(new BuffSkillDefinition(
                effectId: "L2WindWalk",
                displayName: "Wind Walk",
                description: "Run and movement speed increased by 20%.",
                iconResource: "Lineage2Valheim.Assets.wind-walk.png",
                durationSeconds: 300f,
                configureEffect: effect => effect.m_speedModifier = 0.20f,
                warningWindowSeconds: 60f));
            _skills.Add(new BuffSkillDefinition(
                effectId: "L2Might",
                displayName: "Might",
                description: "Physical damage increased by 10%.",
                iconResource: "Lineage2Valheim.Assets.might-buff.png",
                durationSeconds: 300f,
                configureEffect: effect =>
                {
                    effect.m_percentigeDamageModifiers.m_blunt = 0.10f;
                    effect.m_percentigeDamageModifiers.m_slash = 0.10f;
                    effect.m_percentigeDamageModifiers.m_pierce = 0.10f;
                    effect.m_percentigeDamageModifiers.m_chop = 0.10f;
                    effect.m_percentigeDamageModifiers.m_pickaxe = 0.10f;
                },
                plannedLevel: 5,
                requiresLearning: true));
            _skills.Add(new BuffSkillDefinition(
                effectId: "L2Shield",
                displayName: "Shield",
                description: "Armor increased by 10%.",
                iconResource: "Lineage2Valheim.Assets.shield-buff.png",
                durationSeconds: 300f,
                configureEffect: effect => effect.m_armorMultiplier = 0.10f,
                plannedLevel: 10,
                requiresLearning: true));
        }

        internal void RegisterEffects(ObjectDB database)
        {
            if (database == null || database.m_StatusEffects == null) return;

            foreach (var skill in _skills)
            {
                var existing = database.GetStatusEffect(skill.StatusEffectHash);
                if (existing != null)
                {
                    existing.m_hidden = true;
                    if (existing is SE_Stats existingStats)
                    {
                        existingStats.m_ttl = skill.DurationSeconds;
                        skill.ConfigureEffect?.Invoke(existingStats);
                    }
                    continue;
                }
                var effect = ScriptableObject.CreateInstance<SE_Stats>();
                effect.name = skill.StatusEffectName;
                effect.m_name = skill.DisplayName;
                effect.m_tooltip = skill.Description;
                effect.m_ttl = skill.DurationSeconds;
                effect.m_icon = skill.Icon;
                effect.m_hidden = true;
                skill.ConfigureEffect?.Invoke(effect);
                database.m_StatusEffects.Add(effect);
                Logger.LogInfo($"Registered {skill.DisplayName} ({skill.DurationSeconds:0} seconds).");
            }
        }

        private void Update()
        {
            var player = Player.m_localPlayer;
            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                _debugMode = !_debugMode;
                player?.Message(MessageHud.MessageType.Center,
                    "Mod debug mode " + (_debugMode ? "ON" : "OFF"));
            }
            _menuUi?.Update();
            _buffCasting?.Update();
            _hotbars.Update(player, this, IsCustomUiOpen);
            _vitals.Update(player, IsCustomUiOpen || IsCursorReleased);
            BuffCastingVfx.RegisterPlayer(player);
            BuffCastingVfx.RegisterNearbyPlayers();
            if (player == null)
                return;

            CharacterClassSystem.SyncIdentityIfNeeded(player);
            CharacterClassSystem.ApplyMappedSkillLevels(player);
            LineageInventoryCanvas.EnsureCapacity(player);
        }

        private void OnGUI()
        {
            if (Player.m_localPlayer == null || _pixel == null)
                return;

            _vitals.Draw(Player.m_localPlayer, _pixel, _hotbars);
            _menuUi?.OnGUI(_pixel);
            _hotbars.Draw(_pixel, this);
            LineageProgression.DrawXpBar(Player.m_localPlayer, _pixel, _hotbars);
            CharacterClassSystem.DrawSelection(_pixel);
        }

        private Sprite LoadIcon(string resourceName)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Logger.LogError($"Could not find embedded icon resource {resourceName}.");
                    return null;
                }

                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    Logger.LogError($"Could not decode icon resource {resourceName}.");
                    Destroy(texture);
                    return null;
                }

                texture.name = resourceName;
                texture.filterMode = FilterMode.Bilinear;
                return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }

    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDbAwakePatch
    {
        private static void Postfix(ObjectDB __instance)
        {
            Plugin.Instance?.RegisterEffects(__instance);
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class ObjectDbCopyPatch
    {
        private static void Postfix(ObjectDB __instance)
        {
            Plugin.Instance?.RegisterEffects(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class PlayerTakeInputPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (Plugin.Instance?.IsUsingItemFromInventory == true ||
                Plugin.Instance?.IsPerformingSkillAction == true) return true;
            if (Plugin.Instance == null ||
                (!Plugin.Instance.IsCustomUiOpen && !Plugin.Instance.IsCursorReleased))
                return true;

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class PlayerUpdateFreezePatch
    {
        private static void Prefix(Player __instance) => Plugin.FreezeLocalPlayer(__instance);
    }

    [HarmonyPatch(typeof(Player), "FixedUpdate")]
    internal static class PlayerFixedUpdateFreezePatch
    {
        private static void Prefix(Player __instance) => Plugin.FreezeLocalPlayer(__instance);
    }

    [HarmonyPatch(typeof(Humanoid), "DropInvalidItems")]
    internal static class PreserveInventoryOnSpawnPatch
    {
        private static bool Prefix(Humanoid __instance)
        {
            if (!(__instance is Player player)) return true;
            // Valheim calls this while temporarily using its smaller inventory height.
            // Expand before validation so saved rows 9-17 are not dropped on spawn.
            return LineageInventoryCanvas.EnsureCapacity(player);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Show")]
    internal static class InventoryGuiShowPatch
    {
        private static bool Prefix(Container container)
        {
            if (container != null || Plugin.Instance == null)
                return true;

            Plugin.Instance.OpenCustomInventoryFromGame();
            return false;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
    internal static class GameCameraMouseCapturePatch
    {
        private static bool Prefix()
        {
            if (Plugin.Instance == null ||
                (!Plugin.Instance.IsCustomUiOpen && !Plugin.Instance.IsCursorReleased))
                return true;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), "GetMouseScrollWheel")]
    internal static class CameraInventoryScrollPatch
    {
        private static bool Prefix(ref float __result)
        {
            if (Plugin.Instance == null ||
                (!Plugin.Instance.IsCustomUiOpen && !Plugin.Instance.IsCursorReleased))
                return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "SetMouseLook")]
    internal static class PlayerSetMouseLookPatch
    {
        private static bool Prefix()
        {
            return Plugin.Instance == null ||
                (!Plugin.Instance.IsCustomUiOpen && !Plugin.Instance.IsCursorReleased);
        }
    }

    [HarmonyPatch(typeof(HotkeyBar), "Update")]
    internal static class HideVanillaHotbarPatch
    {
        private static void Postfix(HotkeyBar __instance) => Plugin.HideVanillaHotbar(__instance);
    }

    [HarmonyPatch(typeof(Hud), "Update")]
    internal static class HideVanillaVitalsPatch
    {
        private static void Postfix(Hud __instance) => Plugin.HideVanillaVitals(__instance);
    }

    [HarmonyPatch(typeof(Hud), "LateUpdate")]
    internal static class HideVanillaVitalsLatePatch
    {
        private static void Postfix(Hud __instance) => Plugin.HideVanillaVitals(__instance);
    }

    [HarmonyPatch(typeof(Hud), "UpdateStatusEffects")]
    internal static class HideVanillaStatusEffectsPatch
    {
        private static void Postfix(Hud __instance) => Plugin.HideVanillaStatusEffects(__instance);
    }

    [HarmonyPatch]
    internal static class DisableVanillaHotbarItemsPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Player), "UseHotbarItem") ??
            AccessTools.Method(typeof(Humanoid), "UseHotbarItem");

        private static bool Prefix(Humanoid __instance) =>
            Plugin.Instance == null || __instance != Player.m_localPlayer;
    }
}
