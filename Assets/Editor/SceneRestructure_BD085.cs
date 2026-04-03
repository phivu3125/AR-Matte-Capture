using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using ARMatteCapture.Scanning;
using ARMatteCapture.Webcam;
using HoiAnLantern;

/// <summary>
/// One-shot editor script for bd-085: restructure scene hierarchy and wire new components.
/// Run via Tools > BD-085 > Restructure Scene.
/// Safe to re-run (checks for existing objects before creating duplicates).
/// </summary>
public static class SceneRestructure_BD085
{
    [MenuItem("Tools/BD-085/Restructure Scene")]
    public static void Restructure()
    {
        if (!EditorSceneManager.GetActiveScene().name.Contains("HoiAnLantern"))
        {
            Debug.LogError("[BD-085] Open the HoiAnLantern scene first!");
            return;
        }

        Undo.SetCurrentGroupName("BD-085 Scene Restructure");

        // ─── Phase 1: Rename existing sections ───
        RenameIfExists("Systems", "═══ SYSTEM ═══");
        RenameIfExists("UI", "═══ UI ═══");
        RenameIfExists("Environement", "═══ ENVIRONMENT ═══");

        // ─── Phase 2: Create missing sections ───
        var sysSection = FindOrCreate("═══ SYSTEM ═══");
        var camSection = FindOrCreate("═══ CAMERAS ═══");
        var arSection = FindOrCreate("═══ AR DISPLAY ═══");
        var scanSection = FindOrCreate("═══ SCANNING ═══");
        var lanternSection = FindOrCreate("═══ LANTERN FLOW ═══");
        var envSection = FindOrCreate("═══ ENVIRONMENT ═══");
        var uiSection = FindOrCreate("═══ UI ═══");
        var fxSection = FindOrCreate("═══ EFFECTS ═══");

        // ─── Phase 3: Reparent existing objects ───
        // System
        SafeReparent("Webcam Manager", sysSection);
        SafeReparent("Audio Manager", sysSection);
        SafeReparent("WindManager", sysSection);
        SafeReparent("Texture Load Manager", sysSection);
        SafeReparent("Screenshot Handler", sysSection);

        // Cameras
        SafeReparent("Main Camera", camSection);
        SafeReparent("Camera Target Pos", camSection);

        // AR Display
        SafeReparent("RVM Pipeline", arSection);
        SafeReparent("User Display Image", arSection);
        SafeReparent("Main Lantern Tracker", arSection);
        SafeReparent("Tracking", arSection);

        // Scanning
        SafeReparent("Paper scanner", scanSection);
        SafeReparent("Marker Manager", scanSection);
        SafeReparent("Paper Scan Webcam Source", scanSection);
        SafeReparent("Portrait Webcam Source", scanSection);
        SafeReparent("Live feed", scanSection);
        SafeReparent("Paper", scanSection);
        SafeReparent("Paper Frame", scanSection);
        SafeReparent("marker 0", scanSection);
        SafeReparent("marker 1", scanSection);
        SafeReparent("marker 2", scanSection);
        SafeReparent("marker 3", scanSection);

        // Lantern Flow
        SafeReparent("Scan Latern Manager", lanternSection);
        SafeReparent("Gameplay", lanternSection);
        SafeReparent("Check AR Fit Target", lanternSection);
        SafeReparent("Main Latern", lanternSection);
        SafeReparent("Target Latern", lanternSection);
        SafeReparent("Hang Point", lanternSection);

        // Environment
        SafeReparent("Directional Light", envSection);
        SafeReparent("Trees", envSection);
        SafeReparent("Roads", envSection);
        SafeReparent("Ropes", envSection);
        SafeReparent("Ropes (1)", envSection);
        SafeReparent("Ropes (2)", envSection);
        SafeReparent("Reflection Probe", envSection);
        SafeReparent("Boat", envSection);
        SafeReparent("Boat (1)", envSection);
        SafeReparent("Boat (2)", envSection);

        // UI
        SafeReparent("Canvas", uiSection);

        Debug.Log("[BD-085] Phase 3 complete: objects reparented.");

        // ─── Phase 4: Add new scanning components ───
        var sessionGO = FindOrCreateChild("Scan Session Controller", scanSection);
        AddComponentIfMissing<ScanSessionController>(sessionGO);

        var paperScannerGO = GameObject.Find("Paper scanner");
        PaperScanDetector detector = null;
        if (paperScannerGO != null)
            detector = AddComponentIfMissing<PaperScanDetector>(paperScannerGO);

        var markerManagerGO = GameObject.Find("Marker Manager");
        MarkerScanPresenter presenter = null;
        if (markerManagerGO != null)
            presenter = AddComponentIfMissing<MarkerScanPresenter>(markerManagerGO);

        var repoGO = FindOrCreateChild("Captured Texture Repository", scanSection);
        AddComponentIfMissing<CapturedTextureRepository>(repoGO);

        var interactionGO = FindOrCreateChild("Lantern Interaction Controller", lanternSection);
        AddComponentIfMissing<LanternInteractionController>(interactionGO);

        Debug.Log("[BD-085] Phase 4 complete: new components added.");

        // ─── Phase 5: Wire serialized references ───
        WireReferences(paperScannerGO, sessionGO, markerManagerGO);

        Debug.Log("[BD-085] Phase 5 complete: references wired.");

        // ─── Phase 6: Set sibling order for sections ───
        SetOrder(sysSection, 0);
        SetOrder(camSection, 1);
        SetOrder(arSection, 2);
        SetOrder(scanSection, 3);
        SetOrder(lanternSection, 4);
        SetOrder(envSection, 5);
        SetOrder(uiSection, 6);
        SetOrder(fxSection, 7);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[BD-085] Scene restructure complete! Save the scene with Ctrl+S.");
    }

    static void WireReferences(GameObject paperScannerGO, GameObject sessionGO, GameObject markerManagerGO)
    {
        if (paperScannerGO == null) return;

        var paperScan = paperScannerGO.GetComponent<PaperScan>();
        var detector = paperScannerGO.GetComponent<PaperScanDetector>();
        var sessionCtrl = sessionGO?.GetComponent<ScanSessionController>();
        var presenter = markerManagerGO?.GetComponent<MarkerScanPresenter>();

        // Wire PaperScan orchestrator
        if (paperScan != null)
        {
            var so = new SerializedObject(paperScan);
            SetRef(so, "detector", detector);
            SetRef(so, "sessionController", sessionCtrl);
            SetRef(so, "markerPresenter", presenter);
            so.ApplyModifiedProperties();
        }

        // Wire PaperScanDetector
        if (detector != null)
        {
            var so = new SerializedObject(detector);
            var webcamSrc = FindComponent<WebcamSource>("Paper Scan Webcam Source");
            SetRef(so, "webcamSource", webcamSrc);
            var liveFeedGO = GameObject.Find("Live feed");
            if (liveFeedGO != null)
                SetRef(so, "previewRenderer", liveFeedGO.GetComponent<Renderer>());
            so.ApplyModifiedProperties();
        }

        // Wire LanternInteractionController
        var interactionGO = GameObject.Find("Lantern Interaction Controller");
        if (interactionGO != null)
        {
            var ctrl = interactionGO.GetComponent<LanternInteractionController>();
            if (ctrl != null)
            {
                var so = new SerializedObject(ctrl);
                var hitTarget = FindComponent<CheckARObjectHitTarget>("Check AR Fit Target");
                SetRef(so, "checkARObjectHitTarget", hitTarget);
                so.ApplyModifiedProperties();
            }
        }

        // Wire ScanLanternManager.rvmDisplay
        var slmGO = GameObject.Find("Scan Latern Manager");
        if (slmGO != null)
        {
            var slm = slmGO.GetComponent<ScanLanternManager>();
            if (slm != null)
            {
                var so = new SerializedObject(slm);
                var rvmDisplay = GameObject.Find("User Display Image");
                SetRef(so, "rvmDisplay", rvmDisplay);
                so.ApplyModifiedProperties();
            }
        }

        // Wire MarkerScanPresenter from existing MarkerManager data
        if (presenter != null && markerManagerGO != null)
        {
            WirePresenterFromMarkerManager(presenter, markerManagerGO);
        }
    }

    static void WirePresenterFromMarkerManager(MarkerScanPresenter presenter, GameObject mmGO)
    {
        var mmSO = new SerializedObject(mmGO.GetComponent<MarkerManager>());
        var prSO = new SerializedObject(presenter);

        // Copy marker refs
        CopyProperty(mmSO, "marker0", prSO, "marker0");
        CopyProperty(mmSO, "markerBorder0", prSO, "markerBorder0");
        CopyProperty(mmSO, "marker1", prSO, "marker1");
        CopyProperty(mmSO, "markerBorder1", prSO, "markerBorder1");
        CopyProperty(mmSO, "marker2", prSO, "marker2");
        CopyProperty(mmSO, "markerBorder2", prSO, "markerBorder2");
        CopyProperty(mmSO, "marker3", prSO, "marker3");
        CopyProperty(mmSO, "markerBorder3", prSO, "markerBorder3");

        // Copy texture arrays — MarkerManager uses Lists, Presenter uses arrays
        // These need manual assignment in Inspector if types differ
        var mmMissing = mmSO.FindProperty("missingMaterials");
        var prMissing = prSO.FindProperty("missingTextures");
        if (mmMissing != null && prMissing != null && mmMissing.isArray && prMissing.isArray)
        {
            prMissing.arraySize = mmMissing.arraySize;
            for (int i = 0; i < mmMissing.arraySize; i++)
                prMissing.GetArrayElementAtIndex(i).objectReferenceValue =
                    mmMissing.GetArrayElementAtIndex(i).objectReferenceValue;
        }

        var mmDetected = mmSO.FindProperty("detectedMaterials");
        var prDetected = prSO.FindProperty("detectedTextures");
        if (mmDetected != null && prDetected != null && mmDetected.isArray && prDetected.isArray)
        {
            prDetected.arraySize = mmDetected.arraySize;
            for (int i = 0; i < mmDetected.arraySize; i++)
                prDetected.GetArrayElementAtIndex(i).objectReferenceValue =
                    mmDetected.GetArrayElementAtIndex(i).objectReferenceValue;
        }

        prSO.ApplyModifiedProperties();
    }

    // ─── Helpers ───

    static GameObject FindOrCreate(string name)
    {
        var go = GameObject.Find(name);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        }
        return go;
    }

    static GameObject FindOrCreateChild(string name, GameObject parent)
    {
        var go = GameObject.Find(name);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        }
        if (parent != null && go.transform.parent != parent.transform)
            Undo.SetTransformParent(go.transform, parent.transform, "Reparent " + name);
        return go;
    }

    static void RenameIfExists(string oldName, string newName)
    {
        var go = GameObject.Find(oldName);
        if (go != null && GameObject.Find(newName) == null)
        {
            Undo.RecordObject(go, "Rename " + oldName);
            go.name = newName;
        }
    }

    static void SafeReparent(string name, GameObject parent)
    {
        var go = GameObject.Find(name);
        if (go != null && go.transform.parent != parent.transform)
            Undo.SetTransformParent(go.transform, parent.transform, "Reparent " + name);
    }

    static T AddComponentIfMissing<T>(GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        if (existing != null) return existing;
        return Undo.AddComponent<T>(go);
    }

    static T FindComponent<T>(string gameObjectName) where T : Component
    {
        var go = GameObject.Find(gameObjectName);
        return go != null ? go.GetComponent<T>() : null;
    }

    static void SetRef(SerializedObject so, string propName, Object value)
    {
        var prop = so.FindProperty(propName);
        if (prop != null && value != null)
            prop.objectReferenceValue = value;
        else if (prop == null)
            Debug.LogWarning($"[BD-085] Property '{propName}' not found on {so.targetObject.GetType().Name}");
    }

    static void CopyProperty(SerializedObject from, string fromProp, SerializedObject to, string toProp)
    {
        var src = from.FindProperty(fromProp);
        var dst = to.FindProperty(toProp);
        if (src != null && dst != null)
            dst.objectReferenceValue = src.objectReferenceValue;
    }

    static void SetOrder(GameObject go, int index)
    {
        if (go != null)
            go.transform.SetSiblingIndex(index);
    }
}
