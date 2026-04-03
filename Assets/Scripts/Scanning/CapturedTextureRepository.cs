using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ARMatteCapture.Scanning
{
    /// <summary>
    /// Central repository for captured scan textures.
    /// Loads textures from disk, fires events on new arrivals,
    /// and tracks created materials to prevent memory leaks.
    /// </summary>
    public class CapturedTextureRepository : MonoBehaviour
    {
        #region Singleton

        public static CapturedTextureRepository Instance { get; private set; }

        #endregion

        #region Serialized Fields

        [Tooltip("Subfolder in StreamingAssets where scan images are stored")]
        [SerializeField] private string folderName = "Images";

        #endregion

        #region Events

        /// <summary>
        /// Fired when a new texture is stored (e.g. from export).
        /// </summary>
        public event Action<Texture2D> OnTextureStored;

        /// <summary>
        /// Fired after all textures are reloaded from disk.
        /// </summary>
        public event Action OnTexturesRefreshed;

        #endregion

        #region Private Fields

        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly List<Material> _trackedMaterials = new List<Material>();

        #endregion

        #region Properties

        /// <summary>Most recently captured texture (index 0).</summary>
        public Texture2D LatestTexture => _textures.Count > 0 ? _textures[0] : null;

        /// <summary>Read-only list of all captured textures (newest first).</summary>
        public IReadOnlyList<Texture2D> AllTextures => _textures.AsReadOnly();

        /// <summary>Number of textures currently held.</summary>
        public int TextureCount => _textures.Count;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(this); return; }
        }

        void OnDestroy()
        {
            DestroyAllTrackedMaterials();
        }

        #endregion

        #region Public API — Texture Storage

        /// <summary>
        /// Store a new captured texture. Inserts at index 0 (newest first).
        /// </summary>
        public void StoreTexture(Texture2D tex)
        {
            if (tex == null) return;
            _textures.Insert(0, tex);
            OnTextureStored?.Invoke(tex);
        }

        /// <summary>
        /// Get texture by index (0 = newest).
        /// </summary>
        public Texture2D GetTexture(int index)
        {
            if (index < 0 || index >= _textures.Count) return null;
            return _textures[index];
        }

        /// <summary>
        /// Reload all textures from the images folder on disk.
        /// </summary>
        public void RefreshFromDisk()
        {
            _textures.Clear();

            string folderPath = Path.Combine(Application.streamingAssetsPath, folderName);
            if (!Directory.Exists(folderPath)) return;

            string[] files = Directory.GetFiles(folderPath)
                .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg"))
                .OrderByDescending(f => File.GetCreationTime(f))
                .ToArray();

            foreach (string file in files)
            {
                byte[] data = File.ReadAllBytes(file);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(data);
                _textures.Add(tex);
            }

            OnTexturesRefreshed?.Invoke();
        }

        #endregion

        #region Public API — Material Lifecycle

        /// <summary>
        /// Create a material from a source and track it for cleanup.
        /// Use this instead of <c>new Material(source)</c> to prevent leaks.
        /// </summary>
        public Material CreateTrackedMaterial(Material source)
        {
            Material mat = new Material(source);
            _trackedMaterials.Add(mat);
            return mat;
        }

        /// <summary>
        /// Destroy a previously tracked material and remove from tracking list.
        /// </summary>
        public void DestroyTrackedMaterial(Material mat)
        {
            if (mat == null) return;
            _trackedMaterials.Remove(mat);
            Destroy(mat);
        }

        /// <summary>
        /// Destroy all tracked materials. Called on cleanup / scene unload.
        /// </summary>
        public void DestroyAllTrackedMaterials()
        {
            foreach (var mat in _trackedMaterials)
            {
                if (mat != null) Destroy(mat);
            }
            _trackedMaterials.Clear();
        }

        #endregion
    }
}
