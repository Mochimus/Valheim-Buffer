using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>
    /// Renders the local player's visible meshes into a texture for the inventory portrait.
    /// Only Unity rendering components are created; player scripts and animation events are
    /// never instantiated in the preview scene.
    /// </summary>
    internal sealed class CharacterPreviewRenderer : IDisposable
    {
        private const int PreviewLayer = 31;
        private const float RefreshInterval = 0.08f;

        private readonly Dictionary<Renderer, PreviewPart> _parts = new Dictionary<Renderer, PreviewPart>();
        private readonly List<Renderer> _sources = new List<Renderer>();
        private readonly List<Renderer> _removed = new List<Renderer>();
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();

        private GameObject _root;
        private Camera _camera;
        private Light _light;
        private RenderTexture _texture;
        private Player _player;
        private float _nextRefresh;
        private bool _disposed;

        internal RenderTexture Texture => _texture;
        internal bool HasVisibleModel { get; private set; }

        internal void Update(Player player)
        {
            if (_disposed || player == null)
            {
                HasVisibleModel = false;
                return;
            }

            if (_root == null)
                CreateResources();

            // The inventory may call Update from both its frame and layout paths.
            if (player == _player && Time.unscaledTime < _nextRefresh)
                return;

            _player = player;
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            SynchronizeMeshes(player);
            FrameAndRender();
        }

        private void CreateResources()
        {
            _root = new GameObject("Lineage2Valheim.CharacterPreview");
            _root.hideFlags = HideFlags.HideAndDontSave;
            _root.layer = PreviewLayer;
            _root.transform.position = new Vector3(10000f, 10000f, 10000f);

            var cameraObject = new GameObject("Lineage2Valheim.CharacterPreviewCamera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            _camera = cameraObject.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.cullingMask = 1 << PreviewLayer;
            _camera.orthographic = true;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 20f;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;

            var lightObject = new GameObject("Lineage2Valheim.CharacterPreviewLight");
            lightObject.hideFlags = HideFlags.HideAndDontSave;
            _light = lightObject.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.intensity = 1.45f;
            _light.cullingMask = 1 << PreviewLayer;
            lightObject.transform.rotation = Quaternion.Euler(32f, -30f, 0f);

            _texture = new RenderTexture(320, 640, 16, RenderTextureFormat.ARGB32)
            {
                name = "Lineage2Valheim.CharacterPreviewTexture",
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _texture.Create();
            _camera.targetTexture = _texture;
            _camera.aspect = (float)_texture.width / _texture.height;
        }

        private void SynchronizeMeshes(Player player)
        {
            _sources.Clear();
            player.GetComponentsInChildren(true, _sources);
            _removed.Clear();
            foreach (var pair in _parts)
            {
                if (pair.Key == null || !_sources.Contains(pair.Key) || !IsVisibleSource(pair.Key))
                    _removed.Add(pair.Key);
            }
            foreach (var source in _removed)
            {
                if (_parts.TryGetValue(source, out var part))
                    DestroyPart(part);
                _parts.Remove(source);
            }

            var inverseRotation = Quaternion.Inverse(player.transform.rotation);
            foreach (var source in _sources)
            {
                if (!IsVisibleSource(source))
                    continue;

                if (!_parts.TryGetValue(source, out var part))
                {
                    part = CreatePart(source);
                    if (part == null)
                        continue;
                    _parts[source] = part;
                }

                var sourceTransform = source.transform;
                part.Transform.localPosition = inverseRotation * (sourceTransform.position - player.transform.position);
                part.Transform.localRotation = inverseRotation * sourceTransform.rotation;
                part.Transform.localScale = sourceTransform.lossyScale;

                var skinned = source as SkinnedMeshRenderer;
                if (skinned != null)
                {
                    try
                    {
                        skinned.BakeMesh(part.Mesh);
                        part.Filter.sharedMesh = part.Mesh;
                    }
                    catch (Exception)
                    {
                        part.Renderer.enabled = false;
                        continue;
                    }
                }
                else
                {
                    var filter = source.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                    {
                        part.Renderer.enabled = false;
                        continue;
                    }
                    part.Filter.sharedMesh = filter.sharedMesh;
                }

                part.Renderer.sharedMaterials = source.sharedMaterials;
                source.GetPropertyBlock(_propertyBlock);
                part.Renderer.SetPropertyBlock(_propertyBlock);
                part.Renderer.enabled = true;
            }
        }

        private static bool IsVisibleSource(Renderer source)
        {
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy)
                return false;
            if (source is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh != null;
            if (source is MeshRenderer)
            {
                var filter = source.GetComponent<MeshFilter>();
                return filter != null && filter.sharedMesh != null;
            }
            return false;
        }

        private PreviewPart CreatePart(Renderer source)
        {
            var go = new GameObject("Preview " + source.name);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.layer = PreviewLayer;
            go.transform.SetParent(_root.transform, false);
            var part = new PreviewPart
            {
                Transform = go.transform,
                Filter = go.AddComponent<MeshFilter>(),
                Renderer = go.AddComponent<MeshRenderer>()
            };
            if (source is SkinnedMeshRenderer)
            {
                part.Mesh = new Mesh { name = "Preview pose " + source.name };
                part.Mesh.MarkDynamic();
            }
            return part;
        }

        private void FrameAndRender()
        {
            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var part in _parts.Values)
            {
                if (part.Renderer == null || !part.Renderer.enabled || part.Filter.sharedMesh == null)
                    continue;
                var partBounds = part.Renderer.bounds;
                if (!hasBounds)
                {
                    bounds = partBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(partBounds);
                }
            }

            HasVisibleModel = hasBounds;
            if (_camera == null)
                return;

            // A fixed minimum keeps small outfits from causing an extreme close-up.
            var center = hasBounds ? bounds.center : _root.transform.position + Vector3.up;
            var height = hasBounds ? Mathf.Clamp(bounds.size.y, 1.8f, 4f) : 2f;
            var width = hasBounds ? Mathf.Clamp(bounds.size.x, 0.7f, 3f) : 1f;
            _camera.orthographicSize = Mathf.Max(height * 0.58f, width / (_camera.aspect * 2f) * 1.18f);
            _camera.transform.position = center + Vector3.forward * 5f;
            _camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            _camera.Render();
        }

        private static void DestroyPart(PreviewPart part)
        {
            if (part.Mesh != null)
                UnityEngine.Object.Destroy(part.Mesh);
            if (part.Transform != null)
                UnityEngine.Object.Destroy(part.Transform.gameObject);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var part in _parts.Values)
                DestroyPart(part);
            _parts.Clear();
            if (_camera != null)
            {
                _camera.targetTexture = null;
                UnityEngine.Object.Destroy(_camera.gameObject);
            }
            if (_light != null)
                UnityEngine.Object.Destroy(_light.gameObject);
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            if (_texture != null)
            {
                _texture.Release();
                UnityEngine.Object.Destroy(_texture);
            }
            _camera = null;
            _light = null;
            _root = null;
            _texture = null;
            _player = null;
            HasVisibleModel = false;
        }

        private sealed class PreviewPart
        {
            internal Transform Transform;
            internal MeshFilter Filter;
            internal MeshRenderer Renderer;
            internal Mesh Mesh;
        }
    }
}
