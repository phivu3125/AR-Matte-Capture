using UnityEngine;
using System;
using System.IO;

namespace HoiAnLantern
{
public class ScreenshotHandler : MonoBehaviour
{
    public KeyCode screenshotKey = KeyCode.P;

    void Update()
    {
        if (Input.GetKeyDown(screenshotKey))
        {
            SaveScreenshot();
        }
    }

    private void SaveScreenshot()
    {
        // Đường dẫn folder
        string folderPath = Path.Combine(Application.streamingAssetsPath, "Screenshots");

        // Tạo folder nếu chưa có
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        // Tạo tên file theo timestamp (yyyyMMdd_HHmmss)
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"screenshot_{timestamp}.png";
        string filePath = Path.Combine(folderPath, fileName);

        // Chụp và lưu
        ScreenCapture.CaptureScreenshot(filePath);
        Debug.Log($"Screenshot saved to: {filePath}");
    }
}
} // namespace HoiAnLantern
