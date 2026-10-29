using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Loads the authored Power Strike prefab bundle, with a procedural fallback.</summary>
    internal static class PowerStrikeVfx
    {
        private const string BundleFileName = "powerstrike-vfx.bundle";
        private const string PrefabName = "PowerStrikeVfx";
        private static AssetBundle _bundle;
        private static GameObject _prefab;
        private static bool _loadAttempted;

        internal static void PlayActivation(Player player)
        {
            if (player == null) return;
            Vector3 position = player.transform.position + player.transform.forward * 0.35f + Vector3.up * 1.05f;
            Spawn(position, Quaternion.identity, false);
        }

        internal static void PlayImpact(Character target)
        {
            if (target == null) return;
            Spawn(target.transform.position + Vector3.up * 0.8f, Quaternion.identity, true);
        }

        private static void Spawn(Vector3 position, Quaternion rotation, bool impact)
        {
            LoadPrefab();
            if (_prefab != null)
            {
                GameObject instance = UnityEngine.Object.Instantiate(_prefab, position, rotation);
                instance.name = impact ? "PowerStrikeImpact" : "PowerStrikeActivation";
                Transform activation = instance.transform.Find("Activation");
                Transform hit = instance.transform.Find("Impact");
                if (activation != null) activation.gameObject.SetActive(!impact);
                if (hit != null) hit.gameObject.SetActive(impact);
                var selected = impact ? hit : activation;
                if (selected != null)
                    foreach (var system in selected.GetComponentsInChildren<ParticleSystem>(true)) system.Play(true);
                UnityEngine.Object.Destroy(instance, 3.5f);
                return;
            }
            SpawnFallback(position, impact);
        }

        private static void LoadPrefab()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;
            try
            {
                string path = Path.Combine(Paths.PluginPath, "Lineage2Valheim", BundleFileName);
                if (!File.Exists(path))
                {
                    Plugin.Log?.LogWarning("Power Strike VFX bundle is missing; using the built-in fallback effect.");
                    return;
                }
                _bundle = AssetBundle.LoadFromFile(path);
                if (_bundle == null)
                {
                    Plugin.Log?.LogWarning("Power Strike VFX bundle could not be loaded (possibly a Unity version mismatch); using fallback.");
                    return;
                }
                _prefab = _bundle.LoadAsset<GameObject>(PrefabName);
                if (_prefab == null)
                    Plugin.Log?.LogWarning($"Power Strike prefab '{PrefabName}' was not found; using fallback.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"Power Strike VFX bundle unavailable; using fallback: {ex.Message}");
                _bundle?.Unload(false);
                _bundle = null;
            }
        }

        private static void SpawnFallback(Vector3 position, bool impact)
        {
            var root = new GameObject(impact ? "PowerStrikeImpactFallback" : "PowerStrikeActivationFallback");
            root.transform.position = position;
            var system = root.AddComponent<ParticleSystem>();
            var main = system.main;
            main.duration = impact ? 0.45f : 0.8f;
            main.loop = false;
            main.startLifetime = impact ? new ParticleSystem.MinMaxCurve(0.3f, 0.65f) : new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = impact ? new ParticleSystem.MinMaxCurve(2.5f, 5f) : new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.startSize = impact ? new ParticleSystem.MinMaxCurve(0.12f, 0.26f) : new ParticleSystem.MinMaxCurve(0.07f, 0.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.38f, 0.06f), new Color(1f, 0.83f, 0.28f));
            main.maxParticles = impact ? 36 : 28;
            var emission = system.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(impact ? 30 : 22)) });
            var shape = system.shape;
            shape.shapeType = impact ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Circle;
            shape.radius = impact ? 0.3f : 0.42f;
            shape.arc = impact ? 360f : 300f;
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.9f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.24f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            Shader shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var material = new Material(shader) { color = Color.white };
                renderer.material = material;
            }
            system.Play();
            UnityEngine.Object.Destroy(root, 2f);
        }
    }
}
