using UnityEngine;
using DG.Tweening;
using System;
using System.IO;

namespace HoiAnLantern
{
[System.Serializable]
public class LanternConfig
{
    public float waitTime;
}

public class LanternFlyUp : MonoBehaviour
{
    public Vector3 targetPosition;
    public float waitTime = 60f;      // sẽ override nếu có trong config.json
    public float flyTime = 5f;        // giữ nguyên, không load từ file
    public GameObject starFallParticle;
    public event Action OnLanternFlyComplete;

    private void Start()
    {
        // ---- Load config chỉ cho waitTime ----
        string path = Path.Combine(Application.streamingAssetsPath, "config.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            LanternConfig config = JsonUtility.FromJson<LanternConfig>(json);

            if (config != null)
            {
                waitTime = config.waitTime;
            }
        }
        else
        {
            Debug.LogWarning("Không tìm thấy config.json, dùng giá trị mặc định!");
        }
        // --------------------------------------

        Sequence seq = DOTween.Sequence();

        // 1. Đợi waitTime
        seq.AppendInterval(waitTime);

        // 2. Bật particle
        seq.AppendCallback(() =>
        {
            if (starFallParticle != null)
                starFallParticle.SetActive(true);
        });

        // 3. Bay lên target
        seq.Join(transform.DOMove(targetPosition, flyTime).SetEase(Ease.OutQuad));
        seq.JoinCallback(() =>
        {
            AudioManager.Instance.PlaySFX("Fly up");
        });

        // 4. Xóa object khi xong
        seq.AppendCallback(() =>
        {
            if (starFallParticle != null)
                starFallParticle.SetActive(false);
            OnLanternFlyComplete?.Invoke();
        });
    }
}
} // namespace HoiAnLantern
