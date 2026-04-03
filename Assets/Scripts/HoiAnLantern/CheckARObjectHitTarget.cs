using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using System;

namespace HoiAnLantern
{
public class CheckARObjectHitTarget : MonoBehaviour
{
    public Camera arCamera;
    public GameObject arGameObject;
    public GameObject pulseLight;
    public GameObject laternObjectOverlay; // Đèn lồng xuất hiện sau khi kích hoạt
    public ArucoMarkerTracker markerTracker; // Reference to the webcam-based ArUco tracker
    public GameObject spawnedLatern;
    public float thresholdPixel = 50f;
    public float holdTime = 3f; // Thời gian giữ đúng vị trí (giây)
    public GameObject fireworkEffect;

    [Header("Stability")]
    [Tooltip("Extra pixels added to exit threshold (hysteresis to prevent flickering)")]
    public float exitMargin = 30f;

    [Tooltip("Grace period in seconds before resetting when leaving the zone")]
    public float gracePeriod = 0.3f;

    private float timer = 0f;
    private float graceTimer = 0f;
    private bool isTriggered = false;
    private bool isInsideZone = false; // hysteresis state
    public event Action OnLanternHangSuccess;
    private GameObject _newLatern;
    private bool _isPlayingCorrectSFX = false;

    public void DestroyNewLantern()
    {
        Destroy(_newLatern);
    }


    void Update()
    {
        if (!markerTracker.IsTracking)
            return;

        Vector3 arScreenPos = arCamera.WorldToScreenPoint(arGameObject.transform.position);
        Vector3 targetScreenPos = arCamera.WorldToScreenPoint(transform.position);

        float screenDist = Vector2.Distance(new Vector2(arScreenPos.x, arScreenPos.y), new Vector2(targetScreenPos.x, targetScreenPos.y));

        // Hysteresis: enter at thresholdPixel, exit at thresholdPixel + exitMargin
        bool wasInside = isInsideZone;
        if (!isInsideZone && screenDist < thresholdPixel && arGameObject.activeSelf)
        {
            isInsideZone = true;
        }
        else if (isInsideZone && screenDist >= thresholdPixel + exitMargin)
        {
            isInsideZone = false;
        }

        if (isInsideZone)
        {
            graceTimer = 0f; // reset grace while inside

            if (!pulseLight.activeSelf)
                pulseLight.SetActive(true);

            timer += Time.deltaTime;
            if (!_isPlayingCorrectSFX)
            {
                AudioManager.Instance.PlaySFX("Correct");
                _isPlayingCorrectSFX = true;
            }
            if (timer >= holdTime && !isTriggered)
            {
                Debug.Log("AR Object hit target and held for required time!");
                isTriggered = true;
                markerTracker.IsTracking = false; // Stop tracking
                markerTracker.PauseDetection(); // Pause the webcam detection
                laternObjectOverlay.SetActive(false);
                pulseLight.SetActive(false);
                arGameObject.SetActive(false);
                arGameObject.transform.position = new Vector3(0, 0, 0);
                timer = 0f;
                AudioManager.Instance.StopSFX("Correct");
                HandleLanternHangSuccess();
            }
        }
        else
        {
            // Grace period: don't reset immediately when leaving the zone
            graceTimer += Time.deltaTime;
            if (graceTimer >= gracePeriod)
            {
                timer = 0f;
                isTriggered = false;
                if (pulseLight.activeSelf)
                    pulseLight.SetActive(false);
                if (_isPlayingCorrectSFX)
                {
                    AudioManager.Instance.StopSFX("Correct");
                    _isPlayingCorrectSFX = false;
                }
            }
            // During grace period: keep pulseLight on, keep timer, just wait
        }
    }

    private void HandleLanternHangSuccess()
    {
        Sequence seq = DOTween.Sequence();
        _newLatern = Instantiate(spawnedLatern, new Vector3(37.7719994f, -0.108999997f, -4.7750001f), arGameObject.transform.rotation);
        seq.JoinCallback(() =>
        {
            _newLatern.transform.localScale = new Vector3(8f, 8f, 8f);
            _newLatern.GetComponent<LanternWindSwing>().enabled = true;
            _newLatern.GetComponent<LanternFlyUp>().enabled = true;
            fireworkEffect.SetActive(true);
            AudioManager.Instance.PlaySFX("Firework");

        });
        seq.AppendInterval(5f);
        seq.AppendCallback(() =>
        {
            fireworkEffect.SetActive(false);
            AudioManager.Instance.StopSFX("Firework");
            _newLatern.GetComponent<LanternFlyUp>().OnLanternFlyComplete += () =>
            {
                OnLanternHangSuccess?.Invoke();
            };
        });
    }
}
} // namespace HoiAnLantern