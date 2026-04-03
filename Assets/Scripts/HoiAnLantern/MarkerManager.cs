using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HoiAnLantern
{
/// <summary>
/// Manages marker detection UI and triggers paper scan export.
/// Matching legacy repo: no tracker subscription, FoundMarker/LostMarker called
/// directly by PaperScan.CropFrame(). Update() checks allFound + timer to request export.
/// </summary>
public class MarkerManager : MonoBehaviour
{
    public static MarkerManager instance;

    [SerializeField] float gradientDuration = 3f;

    int markerCount = 4;
    [SerializeField] GameObject marker0;
    [SerializeField] MarkerBorder markerBorder0;
    [SerializeField] GameObject marker1;
    [SerializeField] MarkerBorder markerBorder1;
    [SerializeField] GameObject marker2;
    [SerializeField] MarkerBorder markerBorder2;
    [SerializeField] GameObject marker3;
    [SerializeField] MarkerBorder markerBorder3;
    [SerializeField] List<Texture2D> missingMaterials;
    [SerializeField] List<Texture2D> detectedMaterials;

    // Dictionary to track marker status (true = found, false = lost)
    private Dictionary<int, bool> markerStatus = new Dictionary<int, bool>();

    [SerializeField] float timeBeforeScan = 3f;
    float timer = 0f;

    public float GetGradientDuration()
    {
        return gradientDuration;
    }

    void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(this);
        Reset();
    }

    void Update()
    {
        if (PaperScan.instance == null || !PaperScan.instance.isNewClientScan) return;
        // check if 4 markers are found
        bool allFound = true;
        for (int i = 0; i < markerCount; i++)
        {
            if (!markerStatus.ContainsKey(i) || !markerStatus[i])
            {
                allFound = false;
                timer = 0f; // reset timer if any marker is missing
                break;
            }
        }
        if (allFound)
        {
            timer += Time.deltaTime;
            if (timer >= timeBeforeScan)
            {
                // Call scan function
                Debug.Log("All markers found! Scanning...");
                if (PaperScan.instance != null)
                {
                    PaperScan.instance.RequestExport();
                }
                timer = 0f; // reset timer after scanning
            }
        }
    }

    public int GetMarkerCount()
    {
        return markerCount;
    }

    public void FoundMarker(int index)
    {
        if (index < 0 || index >= markerCount) return;

        // Check if status is already "found" to avoid redundant texture application
        if (markerStatus.ContainsKey(index) && markerStatus[index])
            return;

        // Update status
        markerStatus[index] = true;

        switch (index)
        {
            case 0:
                marker0.GetComponent<Renderer>().material.mainTexture = detectedMaterials[0];
                markerBorder0.SetDetected();
                break;
            case 1:
                marker1.GetComponent<Renderer>().material.mainTexture = detectedMaterials[1];
                markerBorder1.SetDetected();
                break;
            case 2:
                marker2.GetComponent<Renderer>().material.mainTexture = detectedMaterials[2];
                markerBorder2.SetDetected();
                break;
            case 3:
                marker3.GetComponent<Renderer>().material.mainTexture = detectedMaterials[3];
                markerBorder3.SetDetected();
                break;
        }
    }

    public void LostMarker(int index)
    {
        if (index < 0 || index >= markerCount) return;

        // Check if status is already "lost" to avoid redundant texture application
        if (markerStatus.ContainsKey(index) && !markerStatus[index])
            return;

        // Update status
        markerStatus[index] = false;

        switch (index)
        {
            case 0:
                marker0.GetComponent<Renderer>().material.mainTexture = missingMaterials[0];
                markerBorder0.SetMissing();
                break;
            case 1:
                marker1.GetComponent<Renderer>().material.mainTexture = missingMaterials[1];
                markerBorder1.SetMissing();
                break;
            case 2:
                marker2.GetComponent<Renderer>().material.mainTexture = missingMaterials[2];
                markerBorder2.SetMissing();
                break;
            case 3:
                marker3.GetComponent<Renderer>().material.mainTexture = missingMaterials[3];
                markerBorder3.SetMissing();
                break;
        }
    }

    public void Reset()
    {
        timer = 0f;

        // Reset all markers to "lost" state with visual update
        // Set status to true first so LostMarker() doesn't early-return,
        // then LostMarker() will set it to false and apply missing textures
        for (int i = 0; i < markerCount; i++)
        {
            markerStatus[i] = true;
            LostMarker(i);
        }
    }

    // Public method to check if a marker is currently found
    public bool IsMarkerFound(int index)
    {
        if (!markerStatus.ContainsKey(index))
            return false;
        return markerStatus[index];
    }
}
} // namespace HoiAnLantern
