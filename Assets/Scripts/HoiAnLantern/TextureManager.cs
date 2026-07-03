using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace HoiAnLantern
{
    public class TextureManager : MonoBehaviour
    {
        public FaceTextureLoader[] priorityObject; // object ưu tiên nhận hình mới nhất
        public FaceTextureLoader[] otherObjects; // các object còn lại
        public string folderName = "Images";     // thư mục con trong StreamingAssets
        public KeyCode refreshKey = KeyCode.R;   // phím bấm để refresh

        /// <summary>
        /// In-memory texture cache. Index 0 = newest, last = oldest.
        /// All textures in this list are owned by TextureManager.
        /// </summary>
        private readonly List<Texture2D> _textureCache = new List<Texture2D>();

        /// <summary>Max textures to keep in cache (1 for priority + otherObjects.Length).</summary>
        private int _maxCacheSize = 8;
        private Camera mainCamera;
        public static TextureManager Instance;
        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void Start()
        {
            mainCamera = Camera.main;

            // Nếu otherObjects chưa gán, tìm tự động
            if (otherObjects == null || otherObjects.Length == 0)
            {
                otherObjects = FindObjectsByType<FaceTextureLoader>(FindObjectsSortMode.None)
                    .Where(o => priorityObject == null || !priorityObject.Contains(o))
                    .ToArray();
            }

            // Sắp xếp otherObjects theo khoảng cách đến camera (gần → xa)
            otherObjects = otherObjects
                .OrderBy(obj => Vector3.Distance(obj.transform.position, mainCamera.transform.position))
                .ToArray();

            _maxCacheSize = 1 + (otherObjects != null ? otherObjects.Length : 0);

            // Load textures từ disk vào cache 1 lần duy nhất khi khởi động
            LoadCacheFromDisk();
            ApplyFromCache();
        }

        void Update()
        {
            if (Input.GetKeyDown(refreshKey))
            {
                RefreshTextures();
            }
        }

        #region Cache Management

        /// <summary>
        /// Load textures from disk into cache. Only called at startup or manual refresh.
        /// </summary>
        private void LoadCacheFromDisk()
        {
            string folderPath = Path.Combine(Application.streamingAssetsPath, folderName);
            if (!Directory.Exists(folderPath))
            {
                Debug.LogError("[TextureManager] Folder not found: " + folderPath);
                return;
            }

            string[] imageFiles = Directory.GetFiles(folderPath)
                .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg"))
                .OrderByDescending(f => File.GetCreationTime(f))
                .ToArray();

            if (imageFiles.Length == 0)
            {
                Debug.LogWarning("[TextureManager] No image files found in folder: " + folderPath);
                return;
            }

            int loadCount = Mathf.Min(imageFiles.Length, _maxCacheSize);
            for (int i = 0; i < loadCount; i++)
            {
                byte[] fileData = File.ReadAllBytes(imageFiles[i]);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(fileData);
                _textureCache.Add(tex);
            }

            Debug.Log($"[TextureManager] Loaded {_textureCache.Count} textures from disk into cache");
        }

        /// <summary>
        /// Apply textures from cache to objects. Same rule as before:
        /// cache[0] → priorityObject[], cache[1..N] → otherObjects[0..N-1] (gần → xa).
        /// No disk I/O — reads only from memory cache.
        /// </summary>
        private void ApplyFromCache()
        {
            int index = 0;

            // Gán texture mới nhất (cache[0]) cho priorityObject
            if (_textureCache.Count > 0 && priorityObject != null)
            {
                foreach (var obj in priorityObject)
                {
                    obj.ApplyTexture(_textureCache[index]);
                }
                index++;
            }

            // Gán các texture còn lại cho otherObjects theo khoảng cách (gần → xa)
            if (otherObjects != null)
            {
                for (int i = 0; i < otherObjects.Length && index < _textureCache.Count; i++, index++)
                {
                    otherObjects[i].ApplyTexture(_textureCache[index]);
                }
            }
        }

        /// <summary>
        /// Remove excess textures beyond _maxCacheSize. Oldest (last) entries are removed first.
        /// </summary>
        private void TrimCache()
        {
            while (_textureCache.Count > _maxCacheSize)
            {
                int lastIdx = _textureCache.Count - 1;
                Texture2D old = _textureCache[lastIdx];
                _textureCache.RemoveAt(lastIdx);
                if (old != null) Destroy(old);
            }
        }

        /// <summary>
        /// Destroy all cached textures and clear the list.
        /// </summary>
        private void ClearCache()
        {
            foreach (var tex in _textureCache)
            {
                if (tex != null) Destroy(tex);
            }
            _textureCache.Clear();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Add a newly captured texture to the front of the cache and re-apply all.
        /// Ownership transfers to TextureManager (will be Destroyed when trimmed).
        /// This is the fast path: no disk I/O, just memory operations.
        /// Preserves old textures on other objects (mới → cũ, gần → xa).
        /// </summary>
        public void AddCapturedTexture(Texture2D tex)
        {
            if (tex == null) return;

            _textureCache.Insert(0, tex);
            TrimCache();
            ApplyFromCache();

            Debug.Log($"[TextureManager] Added captured texture to cache (total: {_textureCache.Count})");
        }

        /// <summary>
        /// Full refresh: clear cache, reload all from disk, re-apply.
        /// Used for manual refresh (R key) or fallback.
        /// </summary>
        public void RefreshTextures()
        {
            ClearCache();
            LoadCacheFromDisk();
            ApplyFromCache();
            Debug.Log("[TextureManager] Textures refreshed from disk!");
        }

        #endregion

        void OnDestroy()
        {
            ClearCache();
        }
    }
} // namespace HoiAnLantern
