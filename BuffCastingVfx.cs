using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Creates finite, locally rendered buff casts and relays only cast events over Valheim RPC.</summary>
    internal static class BuffCastingVfx
    {
        private const string CastRpc = "Lineage2Valheim_BuffCastVfx";
        private static readonly System.Reflection.FieldInfo NetworkViewField =
            AccessTools.Field(typeof(Character), "m_nview");
        private static readonly List<GameObject> ActiveEffects = new List<GameObject>();
        private static readonly List<Player> PlayerScratch = new List<Player>();
        private static readonly Dictionary<int, Player> RegisteredPlayers = new Dictionary<int, Player>();
        private static Material _ribbonMaterial;
        private static Material _lineMaterial;
        private static Material _particleMaterial;
        private static Texture2D _particleTexture;
        private static float _nextPlayerRegistration;

        private static Shader FindGlowShader() =>
            Shader.Find("Legacy Shaders/Particles/Additive") ??
            Shader.Find("Particles/Additive") ??
            Shader.Find("Particles/Standard Unlit") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Unlit/Color");

        internal static GameObject Begin(Player player, string effectId, float castDuration,
            float residualFade, float intensity, int ribbonCount, float particleDensity,
            bool showCircle, bool showLight)
        {
            if (player == null) return null;
            var effect = Create(player, effectId, castDuration, residualFade, intensity,
                ribbonCount, particleDensity, showCircle, showLight, false);
            var networkView = GetNetworkView(player);
            if (effect != null && networkView != null && networkView.IsValid())
                networkView.InvokeRPC(CastRpc, effectId, castDuration);
            return effect;
        }

        private static GameObject Create(Player player, string effectId, float castDuration,
            float residualFade, float intensity, int ribbonCount, float particleDensity,
            bool showCircle, bool showLight, bool remote)
        {
            if (player == null) return null;
            var root = new GameObject("LineageBuffCastVfx_" + effectId);
            root.transform.SetParent(player.transform, false);
            root.transform.localPosition = Vector3.zero;
            var controller = root.AddComponent<BuffCastingVfxController>();
            controller.Initialize(player, effectId, castDuration, residualFade, intensity,
                ribbonCount, particleDensity, showCircle, showLight, remote);
            ActiveEffects.Add(root);
            return root;
        }

        internal static void Stop(GameObject effect)
        {
            if (effect == null) return;
            ActiveEffects.Remove(effect);
            UnityEngine.Object.Destroy(effect);
        }

        internal static void RegisterPlayer(Player player)
        {
            if (player == null) return;
            var networkView = GetNetworkView(player);
            if (networkView == null || !networkView.IsValid()) return;
            int id = player.GetInstanceID();
            if (RegisteredPlayers.ContainsKey(id)) return;
            RegisteredPlayers.Add(id, player);
            networkView.Register<string, float>(CastRpc, (sender, effectId, duration) =>
            {
                if (player == null || player == Player.m_localPlayer) return;
                Create(player, effectId, duration, 0.4f, 1.15f, 4, 1f, true, true, true);
            });
        }

        internal static void RegisterNearbyPlayers()
        {
            if (Time.time < _nextPlayerRegistration || Player.m_localPlayer == null) return;
            _nextPlayerRegistration = Time.time + 2f;
            PlayerScratch.Clear();
            Player.GetPlayersInRange(Player.m_localPlayer.transform.position, 50000f, PlayerScratch);
            foreach (var player in PlayerScratch) RegisterPlayer(player);
            PlayerScratch.Clear();
        }

        internal static void UnregisterPlayer(Player player)
        {
            if (player == null) return;
            RegisteredPlayers.Remove(player.GetInstanceID());
            var networkView = GetNetworkView(player);
            if (networkView != null && networkView.IsValid()) networkView.Unregister(CastRpc);
        }

        internal static void OnEffectDestroyed(GameObject effect) => ActiveEffects.Remove(effect);

        private static ZNetView GetNetworkView(Player player) =>
            NetworkViewField?.GetValue(player) as ZNetView;

        internal static void Cleanup()
        {
            for (int i = ActiveEffects.Count - 1; i >= 0; i--)
                if (ActiveEffects[i] != null) UnityEngine.Object.Destroy(ActiveEffects[i]);
            ActiveEffects.Clear();
            foreach (var player in RegisteredPlayers.Values)
            {
                var networkView = GetNetworkView(player);
                if (networkView != null && networkView.IsValid()) networkView.Unregister(CastRpc);
            }
            RegisteredPlayers.Clear();
            PlayerScratch.Clear();
            _nextPlayerRegistration = 0f;
            Destroy(ref _ribbonMaterial);
            Destroy(ref _lineMaterial);
            Destroy(ref _particleMaterial);
            if (_particleTexture != null) UnityEngine.Object.Destroy(_particleTexture);
            _particleTexture = null;
        }

        private static void Destroy(ref Material material)
        {
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
        }

        internal static Material RibbonMaterial
        {
            get
            {
                if (_ribbonMaterial != null) return _ribbonMaterial;
                Shader shader = FindGlowShader();
                if (shader != null) _ribbonMaterial = new Material(shader) { color = Color.white };
                return _ribbonMaterial;
            }
        }

        internal static Material LineMaterial
        {
            get
            {
                if (_lineMaterial != null) return _lineMaterial;
                Shader shader = FindGlowShader();
                if (shader != null) _lineMaterial = new Material(shader) { color = Color.white };
                return _lineMaterial;
            }
        }

        internal static Material ParticleMaterial
        {
            get
            {
                if (_particleMaterial != null) return _particleMaterial;
                Shader shader = FindGlowShader();
                if (shader == null) return null;
                _particleMaterial = new Material(shader) { color = Color.white };
                _particleTexture = MakeSoftSparkTexture();
                if (_particleMaterial.HasProperty("_MainTex")) _particleMaterial.mainTexture = _particleTexture;
                return _particleMaterial;
            }
        }

        private static Texture2D MakeSoftSparkTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "LineageBuffSoftMote",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f - size * 0.5f) / (size * 0.5f);
                float v = (y + 0.5f - size * 0.5f) / (size * 0.5f);
                float r = Mathf.Sqrt(u * u + v * v);
                float softCore = Mathf.Pow(Mathf.Clamp01(1f - r), 2.4f);
                float glint = Mathf.Clamp01(1f - Mathf.Min(Mathf.Abs(u), Mathf.Abs(v)) * 12f) *
                    Mathf.Clamp01(1f - r * 1.2f) * 0.18f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(softCore * 0.8f + glint));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        internal static Vector3 GetGroundLocalPosition(Player player)
        {
            Vector3 local = Vector3.zero;
            if (player == null) return local;
            int count = Physics.RaycastNonAlloc(player.transform.position + Vector3.up * 1.5f,
                Vector3.down, GroundHits, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var collider = GroundHits[i].collider;
                if (collider == null || collider.transform == player.transform ||
                    collider.transform.IsChildOf(player.transform)) continue;
                if (GroundHits[i].distance < nearest)
                {
                    nearest = GroundHits[i].distance;
                    local.y = GroundHits[i].point.y - player.transform.position.y + 0.035f;
                }
            }
            if (nearest == float.MaxValue) local.y = -0.9f;
            return local;
        }

        private static readonly RaycastHit[] GroundHits = new RaycastHit[20];

        [HarmonyPatch(typeof(Player), "Start")]
        private static class RegisterBuffCastRpcPatch
        {
            private static void Postfix(Player __instance) => RegisterPlayer(__instance);
        }

        [HarmonyPatch(typeof(Player), "OnDestroy")]
        private static class UnregisterBuffCastRpcPatch
        {
            private static void Prefix(Player __instance) => UnregisterPlayer(__instance);
        }
    }

    /// <summary>Finite casting VFX. All geometry, emission and cleanup are driven by cast time, never buff time.</summary>
    internal sealed class BuffCastingVfxController : MonoBehaviour
    {
        private const int RibbonSegments = 40;
        private Player _owner;
        private string _effectId;
        private float _castDuration;
        private float _fadeDuration;
        private float _intensity;
        private float _particleDensity;
        private float _elapsed;
        private float _arcClock;
        private float _initialHealth;
        private Vector3 _startPosition;
        private bool _remote;
        private bool _showCircle;
        private bool _releaseEmitted;
        private Color _primary;
        private Color _secondary;
        private RibbonMesh[] _ribbons;
        private LineRenderer[] _circleLines;
        private LineRenderer[] _glyphLines;
        private LineRenderer[] _arcs;
        private Vector3[][] _arcPoints;
        private ParticleSystem _motes;
        private ParticleSystem _dust;
        private ParticleSystem _leftHandSparks;
        private ParticleSystem _rightHandSparks;
        private ParticleSystem _releaseParticles;
        private Light _castLight;
        private Transform _circleRoot;

        internal void Initialize(Player owner, string effectId, float castDuration, float fadeDuration,
            float intensity, int ribbonCount, float particleDensity, bool showCircle, bool showLight, bool remote)
        {
            _owner = owner;
            _effectId = effectId ?? string.Empty;
            _castDuration = Mathf.Clamp(castDuration, 0.5f, 10f);
            _fadeDuration = Mathf.Clamp(fadeDuration, 0.3f, 0.6f);
            _intensity = Mathf.Clamp(intensity, 0.35f, 1.65f);
            _particleDensity = Mathf.Clamp(particleDensity, 0.25f, 2f);
            _showCircle = showCircle;
            _remote = remote;
            _startPosition = owner != null ? owner.transform.position : Vector3.zero;
            _initialHealth = owner != null ? owner.GetHealth() : 0f;
            SetPalette(_effectId, out _primary, out _secondary);
            BuildVisuals(Mathf.Clamp(ribbonCount, 1, 8), showLight);
        }

        private void SetPalette(string effectId, out Color primary, out Color secondary)
        {
            if (effectId.IndexOf("Shield", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // A glacial, pearl-blue palette with restrained warm accents. Keeping the blue
                // slightly desaturated avoids the hard cyan tubing seen in the earlier effect.
                primary = new Color(0.72f, 0.88f, 1f, 1f);
                secondary = new Color(1f, 0.83f, 0.57f, 1f);
            }
            else if (effectId.IndexOf("Wind", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                primary = new Color(0.52f, 0.92f, 0.76f, 1f);
                secondary = new Color(1f, 0.86f, 0.54f, 1f);
            }
            else
            {
                primary = new Color(1f, 0.67f, 0.26f, 1f);
                secondary = new Color(1f, 0.9f, 0.61f, 1f);
            }
        }

        private void BuildVisuals(int ribbonCount, bool showLight)
        {
            if (_showCircle) BuildMagicCircle();
            int total = ribbonCount + Mathf.Max(1, ribbonCount / 2);
            _ribbons = new RibbonMesh[total];
            for (int i = 0; i < total; i++)
            {
                bool counter = i >= ribbonCount;
                int index = counter ? i - ribbonCount : i;
                var go = new GameObject("EnergyRibbon_" + i);
                go.transform.SetParent(transform, false);
                _ribbons[i] = new RibbonMesh(go, index, counter, counter ? 0.034f : 0.046f,
                    counter ? _secondary : _primary);
            }

            _arcs = new LineRenderer[5];
            _arcPoints = new Vector3[5][];
            for (int i = 0; i < _arcs.Length; i++)
            {
                var arc = new GameObject("GatheringArc_" + i);
                arc.transform.SetParent(transform, false);
                var line = arc.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = 7;
                line.widthMultiplier = 0.012f;
                line.numCapVertices = 2;
                line.sharedMaterial = BuffCastingVfx.LineMaterial;
                line.startColor = Color.white;
                line.endColor = new Color(_primary.r, _primary.g, _primary.b, 0f);
                _arcs[i] = line;
                _arcPoints[i] = new Vector3[7];
            }

            _motes = CreateParticles("AwakeningMotes", new Vector3(0f, 0.12f, 0f), 90, 0.095f,
                new Vector2(0.45f, 0.78f), 0.6f, _primary, _secondary, 12f);
            _dust = CreateParticles("ArcaneDust", new Vector3(0f, 0.82f, 0f), 72, 0.052f,
                new Vector2(0.4f, 0.72f), 0.74f, _primary, _secondary, 0f);
            _leftHandSparks = CreateParticles("LeftHandSparks", new Vector3(-0.38f, 0.98f, 0.17f), 52, 0.105f,
                new Vector2(0.18f, 0.48f), 0.14f, Color.white, _primary, 0f);
            _rightHandSparks = CreateParticles("RightHandSparks", new Vector3(0.38f, 0.98f, 0.17f), 52, 0.105f,
                new Vector2(0.18f, 0.48f), 0.14f, Color.white, _secondary, 0f);
            _releaseParticles = CreateParticles("ReleaseParticles", new Vector3(0f, 0.9f, 0f), 150, 0.115f,
                new Vector2(0.5f, 0.9f), 0.34f, Color.white, _secondary, 0f);
            if (showLight)
            {
                var lightObject = new GameObject("TemporaryCastLight");
                lightObject.transform.SetParent(transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 0.95f, 0f);
                _castLight = lightObject.AddComponent<Light>();
                _castLight.type = LightType.Point;
                _castLight.color = _primary;
                _castLight.range = 4.5f;
                _castLight.intensity = 0f;
                _castLight.shadows = LightShadows.None;
            }
        }

        private ParticleSystem CreateParticles(string name, Vector3 position, int maxParticles, float maxSize,
            Vector2 lifetime, float radius, Color start, Color end, float initialRate)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 2f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = maxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.06f, name.Contains("Release") ? 1.8f : 0.32f);
            main.startSize = new ParticleSystem.MinMaxCurve(maxSize * 0.35f, maxSize);
            main.startColor = new ParticleSystem.MinMaxGradient(start, end);
            main.gravityModifier = -0.035f;
            var emission = system.emission;
            emission.rateOverTime = initialRate;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = BuildFadeGradient(start, end);
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            // Unity requires the X/Y/Z velocity curves to use the same mode. Assign all three
            // as two-constant curves; setting only Y this way logs an error and drops particles.
            velocity.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.12f, 0.42f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = BuffCastingVfx.ParticleMaterial;
            system.Play();
            return system;
        }

        private void BuildMagicCircle()
        {
            var circle = new GameObject("TransientArcaneCircle");
            circle.transform.SetParent(transform, false);
            circle.transform.localPosition = BuffCastingVfx.GetGroundLocalPosition(_owner);
            _circleRoot = circle.transform;
            _circleLines = new LineRenderer[3];
            float[] radii = { 0.86f, 0.72f, 0.46f };
            for (int i = 0; i < radii.Length; i++)
            {
                var ringObject = new GameObject("CircleRing_" + i);
                ringObject.transform.SetParent(circle.transform, false);
                var line = ringObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = 97;
                line.widthMultiplier = i == 0 ? 0.012f : 0.008f;
                line.numCapVertices = 2;
                line.sharedMaterial = BuffCastingVfx.LineMaterial;
                line.startColor = new Color(_primary.r, _primary.g, _primary.b, 0f);
                line.endColor = line.startColor;
                for (int point = 0; point < line.positionCount; point++)
                {
                    float a = point * Mathf.PI * 2f / (line.positionCount - 1);
                    line.SetPosition(point, new Vector3(Mathf.Cos(a) * radii[i], 0f, Mathf.Sin(a) * radii[i]));
                }
                _circleLines[i] = line;
            }

            _glyphLines = new LineRenderer[12];
            for (int i = 0; i < _glyphLines.Length; i++)
            {
                var glyphObject = new GameObject("ArcaneGlyph_" + i);
                glyphObject.transform.SetParent(circle.transform, false);
                var line = glyphObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = 5;
                line.widthMultiplier = 0.007f;
                line.sharedMaterial = BuffCastingVfx.LineMaterial;
                line.startColor = new Color(_secondary.r, _secondary.g, _secondary.b, 0f);
                line.endColor = line.startColor;
                float a = i * Mathf.PI * 2f / _glyphLines.Length;
                Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 tangent = new Vector3(-outward.z, 0f, outward.x);
                Vector3[] glyph =
                {
                    outward * 0.68f,
                    outward * 0.81f + tangent * 0.045f,
                    outward * 0.75f,
                    outward * 0.81f - tangent * 0.045f,
                    outward * 0.68f
                };
                line.SetPositions(glyph);
                _glyphLines[i] = line;
            }
        }

        private void Update()
        {
            if (_owner == null || _owner.IsDead())
            {
                Destroy(gameObject);
                return;
            }
            if (_remote && WasInterrupted())
            {
                Destroy(gameObject);
                return;
            }

            _elapsed += Time.deltaTime;
            float phase = Mathf.Clamp01(_elapsed / _castDuration);
            float build = phase < 0.2f ? Mathf.SmoothStep(0f, 0.2f, phase) * 0.6f
                : phase < 0.48f ? Mathf.Lerp(0.12f, 0.58f, Mathf.InverseLerp(0.2f, 0.48f, phase))
                : phase < 0.76f ? Mathf.Lerp(0.58f, 1f, Mathf.InverseLerp(0.48f, 0.76f, phase))
                : phase < 0.88f ? Mathf.Lerp(1f, 0.72f, Mathf.InverseLerp(0.76f, 0.88f, phase))
                : Mathf.Lerp(0.72f, 0.25f, Mathf.InverseLerp(0.88f, 1f, phase));
            float fade = _elapsed <= _castDuration ? 1f :
                1f - Mathf.Clamp01((_elapsed - _castDuration) / _fadeDuration);
            float strength = Mathf.Clamp01(build * fade * _intensity);

            UpdateRibbons(strength);
            UpdateCircle(phase, strength);
            UpdateArcs(phase, strength);
            UpdateParticles(phase, strength);
            if (_castLight != null)
                _castLight.intensity = (0.18f + strength * 2.25f) * fade;

            if (phase >= 0.76f && !_releaseEmitted)
            {
                _releaseEmitted = true;
                int count = Mathf.Clamp(Mathf.RoundToInt(60f * _particleDensity * _intensity), 12, 160);
                _releaseParticles.Emit(count);
                _leftHandSparks.Emit(Mathf.Max(4, count / 5));
                _rightHandSparks.Emit(Mathf.Max(4, count / 5));
            }
            if (_elapsed >= _castDuration + _fadeDuration) Destroy(gameObject);
        }

        private bool WasInterrupted()
        {
            if (_owner == null || _owner.IsDead() || _owner.IsTeleporting() || _owner.InDodge() || _owner.InAttack())
                return true;
            if (_owner.GetHealth() < _initialHealth - 0.01f) return true;
            return (_owner.transform.position - _startPosition).sqrMagnitude > 0.09f;
        }

        private void UpdateRibbons(float strength)
        {
            if (_ribbons == null) return;
            float time = _elapsed;
            for (int i = 0; i < _ribbons.Length; i++)
                _ribbons[i].UpdateMesh(time, strength, _castDuration, i < 2 ? 1.1f : 0.76f);
        }

        private void UpdateCircle(float phase, float strength)
        {
            if (_circleRoot == null) return;
            _circleRoot.Rotate(Vector3.up, (phase < 0.48f ? 9f : -18f) * Time.deltaTime, Space.Self);
            for (int i = 0; i < _circleLines.Length; i++)
            {
                Color c = _circleLines[i].startColor;
                c.a = (i == 0 ? 0.24f : 0.15f) * strength;
                _circleLines[i].startColor = c;
                _circleLines[i].endColor = c;
            }
            for (int i = 0; i < _glyphLines.Length; i++)
            {
                Color c = _glyphLines[i].startColor;
                c.a = 0.2f * strength;
                _glyphLines[i].startColor = c;
                _glyphLines[i].endColor = c;
            }
        }

        private void UpdateArcs(float phase, float strength)
        {
            bool active = phase >= 0.48f && phase < 0.88f;
            _arcClock += Time.deltaTime;
            if (_arcClock < 0.065f && active) return;
            _arcClock = 0f;
            for (int i = 0; i < _arcs.Length; i++)
            {
                var line = _arcs[i];
                line.enabled = active && strength > 0.12f && (i < 2 || UnityEngine.Random.value < 0.5f);
                if (!line.enabled) continue;
                bool left = i % 2 == 0;
                float handX = left ? -0.42f : 0.42f;
                Vector3 start = new Vector3(handX, 0.9f + (i % 3) * 0.12f, 0.2f);
                Vector3 end = new Vector3(handX * 0.45f, 1.38f + (i % 2) * 0.14f, 0.08f);
                for (int p = 0; p < _arcPoints[i].Length; p++)
                {
                    float t = p / (float)(_arcPoints[i].Length - 1);
                    Vector3 point = Vector3.Lerp(start, end, t);
                    float jag = Mathf.Sin(t * Mathf.PI) * strength * 0.055f;
                    point += new Vector3(UnityEngine.Random.Range(-jag, jag),
                        UnityEngine.Random.Range(-jag, jag), UnityEngine.Random.Range(-jag, jag));
                    _arcPoints[i][p] = point;
                }
                line.SetPositions(_arcPoints[i]);
                Color c = Color.Lerp(Color.white, _primary, 0.28f);
                c.a = 0.78f * strength;
                line.startColor = c;
                c.a = 0.06f * strength;
                line.endColor = c;
            }
        }

        private void UpdateParticles(float phase, float strength)
        {
            SetEmission(_motes, phase < 0.88f ? (10f + strength * 18f) * _particleDensity : 0f);
            SetEmission(_dust, phase >= 0.2f && phase < 0.88f ? (5f + strength * 10f) * _particleDensity : 0f);
            float handRate = phase >= 0.08f && phase < 0.76f ? (5f + strength * 24f) * _particleDensity : 0f;
            SetEmission(_leftHandSparks, handRate);
            SetEmission(_rightHandSparks, handRate);
            SetEmission(_releaseParticles, 0f);
        }

        private static void SetEmission(ParticleSystem system, float rate)
        {
            if (system == null) return;
            var emission = system.emission;
            emission.rateOverTime = rate;
        }

        private static Gradient BuildFadeGradient(Color start, Color end)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.18f),
                    new GradientAlphaKey(0.55f, 0.7f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        private void OnDestroy()
        {
            BuffCastingVfx.OnEffectDestroyed(gameObject);
            if (_ribbons != null)
                foreach (var ribbon in _ribbons) ribbon?.Dispose();
        }

        private sealed class RibbonMesh
        {
            private const int CrossSectionPoints = 5;
            private readonly int _index;
            private readonly bool _counterRotate;
            private readonly float _width;
            private readonly Color _color;
            private readonly Mesh _mesh;
            private readonly Vector3[] _vertices;
            private readonly Vector2[] _uv;
            private readonly Color[] _colors;
            private readonly Vector3[] _normals;
            private readonly int[] _triangles;

            internal RibbonMesh(GameObject root, int index, bool counterRotate, float width, Color color)
            {
                _index = index;
                _counterRotate = counterRotate;
                _width = width;
                _color = color;
                int vertexCount = (RibbonSegments + 1) * CrossSectionPoints;
                _vertices = new Vector3[vertexCount];
                _uv = new Vector2[vertexCount];
                _colors = new Color[vertexCount];
                _normals = new Vector3[vertexCount];
                _triangles = new int[RibbonSegments * (CrossSectionPoints - 1) * 6];
                for (int i = 0; i <= RibbonSegments; i++)
                {
                    for (int side = 0; side < CrossSectionPoints; side++)
                    {
                        int vertex = i * CrossSectionPoints + side;
                        _uv[vertex] = new Vector2(side / (float)(CrossSectionPoints - 1), i / (float)RibbonSegments);
                        _normals[vertex] = Vector3.back;
                    }
                    if (i == RibbonSegments) continue;
                    for (int side = 0; side < CrossSectionPoints - 1; side++)
                    {
                        int a = i * CrossSectionPoints + side;
                        int b = a + CrossSectionPoints;
                        int tri = (i * (CrossSectionPoints - 1) + side) * 6;
                        _triangles[tri] = a; _triangles[tri + 1] = b; _triangles[tri + 2] = a + 1;
                        _triangles[tri + 3] = a + 1; _triangles[tri + 4] = b; _triangles[tri + 5] = b + 1;
                    }
                }
                _mesh = new Mesh { name = "TransientEnergyRibbon" };
                _mesh.MarkDynamic();
                _mesh.vertices = _vertices;
                _mesh.uv = _uv;
                _mesh.colors = _colors;
                _mesh.normals = _normals;
                _mesh.triangles = _triangles;
                _mesh.bounds = new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(2.4f, 2.6f, 2.4f));
                root.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = BuffCastingVfx.RibbonMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            internal void UpdateMesh(float time, float opacity, float castDuration, float speed)
            {
                float direction = _counterRotate ? -1f : 1f;
                int layerCount = _counterRotate ? 3 : 8;
                float offset = _index * Mathf.PI * 2f / Mathf.Max(1, layerCount) + (_counterRotate ? 0.55f : 0f);
                // Each strip is an open, rising sweep around part of the body, rather than a
                // closed, repeated helix. Different lengths and rates keep the layers organic.
                float sweep = Mathf.PI * (0.72f + (_index % 3) * 0.13f);
                float drift = time * speed * 0.3f * direction;
                for (int i = 0; i <= RibbonSegments; i++)
                {
                    float t = i / (float)RibbonSegments;
                    float angle = offset + drift + (t - 0.5f) * sweep * direction +
                        Mathf.Sin(time * 0.72f + t * 4.8f + offset) * 0.11f;
                    float radius = 0.43f + Mathf.Sin(t * Mathf.PI * 2f + time * 0.48f + offset) * 0.055f;
                    float y = 0.12f + t * 1.48f + Mathf.Sin(t * Mathf.PI + time * 0.52f + offset) * 0.16f;
                    Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Vector3 tangent = new Vector3(-Mathf.Sin(angle) * sweep * direction, 1.48f,
                        Mathf.Cos(angle) * sweep * direction);
                    Vector3 side = Vector3.Cross(radial, tangent).normalized;
                    if (side.sqrMagnitude < 0.01f) side = new Vector3(-radial.z, 0f, radial.x);
                    float taper = Mathf.Pow(Mathf.Sin(Mathf.PI * t), 0.9f);
                    float wave = 0.78f + 0.22f * Mathf.Sin(t * Mathf.PI * 3f + time * 1.2f + offset);
                    float halfWidth = _width * taper * wave * (0.28f + opacity * 0.58f);
                    Vector3 center = new Vector3(radial.x * radius, y, radial.z * radius);
                    float flow = 0.68f + 0.32f * Mathf.Sin(t * Mathf.PI * 2f - time * 2.1f + offset);
                    float alpha = taper * opacity * (_counterRotate ? 0.28f : 0.48f) * wave * flow;
                    Color c = Color.Lerp(_color, Color.white, Mathf.Clamp01(0.22f + opacity * 0.42f));
                    for (int across = 0; across < CrossSectionPoints; across++)
                    {
                        float u = across / (float)(CrossSectionPoints - 1) * 2f - 1f;
                        float feather = across == 0 || across == CrossSectionPoints - 1 ? 0f
                            : across == 1 || across == CrossSectionPoints - 2 ? 0.22f : 1f;
                        int vertex = i * CrossSectionPoints + across;
                        _vertices[vertex] = center + side * (u * halfWidth);
                        c.a = alpha * feather;
                        _colors[vertex] = c;
                    }
                }
                _mesh.vertices = _vertices;
                _mesh.colors = _colors;
            }

            internal void Dispose()
            {
                if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            }
        }
    }
}
