using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildPowerStrikeVfx
{
    private const string OutputDir = "../VfxBundles";
    private const string PrefabPath = "Assets/PowerStrikeVfx.prefab";
    private const string MaterialPath = "Assets/PowerStrikeParticle.mat";

    [MenuItem("Lineage2Valheim/Build Power Strike VFX")]
    public static void Build()
    {
        Directory.CreateDirectory(OutputDir);
        var root = new GameObject("PowerStrikeVfx");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            var shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            material = new Material(shader) { name = "PowerStrikeParticle" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        AddEffect(root.transform, "Activation", false, material);
        AddEffect(root.transform, "Impact", true, material);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        var importer = AssetImporter.GetAtPath(PrefabPath);
        importer.assetBundleName = "powerstrike-vfx.bundle";
        AssetDatabase.SaveAssets();
        var manifest = BuildPipeline.BuildAssetBundles(OutputDir, BuildAssetBundleOptions.None,
            BuildTarget.NoTarget);
        if (manifest == null) throw new System.Exception("AssetBundle build failed.");
        var bundlePath = Path.Combine(OutputDir, "powerstrike-vfx.bundle");
        if (!File.Exists(bundlePath) || new FileInfo(bundlePath).Length < 4096)
            throw new System.Exception("Unity returned an invalid or empty AssetBundle; no bundle should be deployed.");
        var builtBundle = AssetBundle.LoadFromFile(bundlePath);
        if (builtBundle == null) throw new System.Exception("Unity could not load its generated Power Strike bundle.");
        var builtPrefab = builtBundle.LoadAsset<GameObject>("PowerStrikeVfx");
        if (builtPrefab == null) throw new System.Exception("PowerStrikeVfx prefab was not present in the generated bundle.");
        builtBundle.Unload(true);
        Debug.Log("Power Strike VFX AssetBundle built at " + Path.GetFullPath(OutputDir));
    }

    private static void AddEffect(Transform root, string name, bool impact, Material material)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root, false);
        var system = child.AddComponent<ParticleSystem>();
        var main = system.main;
        main.playOnAwake = false;
        main.duration = impact ? 0.38f : 0.78f;
        main.loop = false;
        main.startLifetime = impact ? new ParticleSystem.MinMaxCurve(0.3f, 0.66f) : new ParticleSystem.MinMaxCurve(0.5f, 0.92f);
        main.startSpeed = impact ? new ParticleSystem.MinMaxCurve(2.4f, 4.6f) : new ParticleSystem.MinMaxCurve(0.12f, 0.42f);
        main.startSize = impact ? new ParticleSystem.MinMaxCurve(0.1f, 0.23f) : new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.08f), new Color(1f, 0.88f, 0.35f));
        main.maxParticles = impact ? 36 : 24;
        var emission = system.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(impact ? 30 : 20)) });
        var shape = system.shape;
        shape.shapeType = impact ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Circle;
        shape.radius = impact ? 0.24f : 0.38f;
        shape.arc = impact ? 360f : 320f;
        var color = system.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.95f, 0.58f), 0f), new GradientColorKey(new Color(1f, 0.28f, 0.035f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var renderer = child.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        if (!impact) child.SetActive(false);
    }
}
