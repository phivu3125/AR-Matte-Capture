using UnityEngine;
using System.IO;
using System.Linq;

namespace HoiAnLantern
{
    public class TextureManager : MonoBehaviour
    {
        public FaceTextureLoader[] priorityObject; // object ưu tiên nhận hình mới nhất
        public FaceTextureLoader[] otherObjects; // các object còn lại
        public string folderName = "Images";     // thư mục con trong StreamingAssets
        public KeyCode refreshKey = KeyCode.R;   // phím bấm để refresh

        private string[] imageFiles;             // cache danh sách file ảnh
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

            // Tìm tất cả file trong folder
            string folderPath = Path.Combine(Application.streamingAssetsPath, folderName);
            if (!Directory.Exists(folderPath))
            {
                Debug.LogError("Folder not found: " + folderPath);
                return;
            }

            imageFiles = Directory.GetFiles(folderPath)
                .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg"))
                .OrderByDescending(f => File.GetCreationTime(f)) // mới → cũ
                .ToArray();

            if (imageFiles.Length == 0)
            {
                Debug.LogWarning("No image files found in folder: " + folderPath);
                return;
            }

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

            // Load texture lần đầu
            ApplyTextures();
        }

        void Update()
        {
            if (Input.GetKeyDown(refreshKey))
            {
                RefreshTextures();
            }
        }

        private void ApplyTextures()
        {
            int totalObjects = 1 + otherObjects.Length; // priorityObject + others
            int totalImages = imageFiles.Length;

            int index = 0;

            // Gán hình mới nhất cho priorityObject
            if (totalImages > 0 && priorityObject != null)
            {
                byte[] fileData = File.ReadAllBytes(imageFiles[index]);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(fileData);
                foreach (var obj in priorityObject)
                {
                    obj.ApplyTexture(tex);
                    Debug.Log("Applied texture to priority object: " + obj.name);
                }
                index++;
            }

            // Gán các hình còn lại cho otherObjects theo khoảng cách
            for (int i = 0; i < otherObjects.Length && index < totalImages; i++, index++)
            {
                byte[] fileData = File.ReadAllBytes(imageFiles[index]);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(fileData);
                otherObjects[i].ApplyTexture(tex);
            }
        }

        public void RefreshTextures()
        {
            string folderPath = Path.Combine(Application.streamingAssetsPath, folderName);
            imageFiles = Directory.GetFiles(folderPath)
                .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg"))
                .OrderByDescending(f => File.GetCreationTime(f))
                .ToArray();

            ApplyTextures();
            Debug.Log("Textures refreshed!");
        }
    }
} // namespace HoiAnLantern
