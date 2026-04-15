using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using ARMatteCapture.Scanning;
using ARMatteCapture.Webcam;
using HoiAnLantern;

/// <summary>
/// One-shot editor script for bd-085: restructure scene hierarchy and wire new components.
/// Run via Tools > BD-085 > Restructure Scene.
/// SAFE: only reparents root-level objects and existing section headers.
/// Children of functional parents are NEVER detached.
/// </summary>
public static class SceneRestructure_BD085
{
    // Old section names in the existing scene
    static readonly string[] OldSectionNames = {
        "Systems", "Gameplay", "Tracking", "UI", "Environement", "RVM Pipeline"
    };

    [MenuItem("Tools/BD-085/Restructure Scene")]
    public static void Restructure()
    {
        if (!EditorSceneManager.GetActiveScene().name.Contains("HoiAnLantern"))
        {
            Debug.LogError("[BD-085] Open the HoiAnLantern scene first!");
            return;
        }

        Undo.SetCurrentGroupName("BD-085 Scene Restructure");

        // ─── Phase 1: Rename existing root sections ───
        RenameRoot("Systems", "═══ SYSTEM ═══");
        RenameRoot("UI", "═══ UI ═══");
        RenameRoot("Environement", "═══ ENVIRONMENT ═══");

        // ─── Phase 2: Create missing section headers ───
        var sysSection = FindOrCreateRoot("═══ SYSTEM ═══");
        var camSection = FindOrCreateRoot("═══ CAMERAS ═══");
        var arSection = FindOrCreateRoot("═══ AR DISPLAY ═══");
        var scanSection = FindOrCreateRoot("═══ SCANNING ═══");
        var lanternSection = FindOrCreateRoot("═══ LANTERN FLOW ═══");
        var envSection = FindOrCreateRoot("═══ ENVIRONMENT ═══");
        var uiSection = FindOrCreateRoot("═══ UI ═══");
        var fxSection = FindOrCreateRoot("═══ EFFECTS ═══");

        // ─── Phase 3: Move old sections under new parent sections ───
        // "RVM Pipeline" (entire group with children) → under AR DISPLAY
        MoveRootUnder("RVM Pipeline", arSection);
        // "Tracking" (entire group with children) → under AR DISPLAY
        MoveRootUnder("Tracking", arSection);
        // "Gameplay" (entire group with children) → under LANTERN FLOW
        MoveRootUnder("Gameplay", lanternSection);

        // ─── Phase 4: Move root-level objects ONLY ───
        // These are objects at scene root (no parent), safe to move.
        MoveRootUnder("Webcam Manager", sysSection);
        MoveRootUnder("Audio Manager", sysSection);
        MoveRootUnder("WindManager", sysSection);
        MoveRootUnder("Texture Load Manager", sysSection);
        MoveRootUnder("Screenshot Handler", sysSection);

        MoveRootUnder("Main Camera", camSection);
        MoveRootUnder("Camera Target Pos", camSection);

        MoveRootUnder("Main Lantern Tracker", arSection);

        MoveRootUnder("Paper scanner", scanSection);
        MoveRootUnder("Marker Manager", scanSection);
        MoveRootUnder("Paper Scan Webcam Source", scanSection);
        MoveRootUnder("Portrait Webcam Source", scanSection);
        MoveRootUnder("Live feed", scanSection);
        MoveRootUnder("Paper", scanSection);
        MoveRootUnder("Paper Frame", scanSection);
        MoveRootUnder("marker 0", scanSection);
        MoveRootUnder("marker 1", scanSection);
        MoveRootUnder("marker 2", scanSection);
        MoveRootUnder("marker 3", scanSection);
        MoveRootUnder("scan-table", scanSection);

        MoveRootUnder("Scan Latern Manager", lanternSection);
        MoveRootUnder("Check AR Fit Target", lanternSection);
        MoveRootUnder("Main Latern", lanternSection);
        MoveRootUnder("Target Latern", lanternSection);
        MoveRootUnder("Hang Point", lanternSection);

        MoveRootUnder("Directional Light", envSection);
        MoveRootUnder("Reflection Probe", envSection);

        MoveRootUnder("Canvas", uiSection);

        // Effects
        MoveRootUnder("CFXR4 Firework HDR Shoot Single (Random Color)", fxSection);

        Debug.Log("[BD-085] Phase 3-4 complete: sections and root objects reparented.");

        // ─── Phase 5: Add new scanning components ───
        var sessionGO = FindOrCreateChild("Scan Session Controller", scanSection);
        AddComponentIfMissing<ScanSessionController>(sessionGO);

        var paperScannerGO = FindAnywhere("Paper scanner");
        PaperScanDetector detector = null;
        if (paperScannerGO != null)
            detector = AddComponentIfMissing<PaperScanDetector>(paperScannerGO);

        var markerManagerGO = FindAnywhere("Marker Manager");
        MarkerScanPresenter presenter = null;
        if (markerManagerGO != null)
            presenter = AddComponentIfMissing<MarkerScanPresenter>(markerManagerGO);

        var repoGO = FindOrCreateChild("Captured Texture Repository", scanSection);
        AddComponentIfMissing<CapturedTextureRepository>(repoGO);

        var interactionGO = FindOrCreateChild("Lantern Interaction Controller", lanternSection);
        AddComponentIfMissing<LanternInteractionController>(interactionGO);

        Debug.Log("[BD-085] Phase 5 complete: new components added.");

        // ─── Phase 6: Wire serialized references ───
        WireReferences(paperScannerGO, sessionGO, markerManagerGO);
        Debug.Log("[BD-085] Phase 6 complete: references wired.");

        // ─── Phase 7: Set sibling order for sections ───
        SetOrder(sysSection, 0);
        SetOrder(camSection, 1);
        SetOrder(arSection, 2);
        SetOrder(scanSection, 3);
        SetOrder(lanternSection, 4);
        SetOrder(envSection, 5);
        SetOrder(uiSection, 6);
        SetOrder(fxSection, 7);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[BD-085] Scene restructure complete! Save the scene (Ctrl+S).");
    }

    // ─── Core Helpers ───

    /// <summary>
    /// ONLY moves an object if it's currently at scene root (parent == null)
    /// or already a direct child of an old/new section header.
    /// NEVER detaches children from functional parents.
    /// </summary>
    static void MoveRootUnder(string name, GameObject newParent)
    {
        var go = FindAnywhere(name);
        if (go == null) return;
        if (go.transform.parent == newParent.transform) return; // already there

        // Only move if at root level OR direct child of an old/new section
        if (go.transform.parent == null || IsSection(go.transform.parent.gameObject))
        {
            Undo.SetTransformParent(go.transform, newParent.transform, "Move " + name);
        }
        else
        {
            Debug.Log($"[BD-085] Skipped '{name}' — child of '{go.transform.parent.name}', not reparenting.");
        }
    }

    static bool IsSection(GameObject go)
    {
        if (go.name.Contains("═══")) return true;
        foreach (var old in OldSectionNames)
            if (go.name == old) return true;
        return false;
    }

    static GameObject FindAnywhere(string name)
    {
        // GameObject.Find only finds active objects. Try that first.
        var go = GameObject.Find(name);
        if (go != null) return go;

        // Also search inactive objects
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            var found = FindInChildren(root.transform, name);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    static Transform FindInChildren(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindInChildren(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    static void RenameRoot(string oldName, string newName)
    {
        // Only rename root-level objects
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == oldName && FindAnywhere(newName) == null)
            {
                Undo.RecordObject(root, "Rename " + oldName);
                root.name = newName;
                return;
            }
        }
    }

    static GameObject FindOrCreateRoot(string name)
    {
        var go = FindAnywhere(name);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        }
        return go;
    }

    static GameObject FindOrCreateChild(string name, GameObject parent)
    {
        var go = FindAnywhere(name);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        }
        if (parent != null && go.transform.parent != parent.transform)
            Undo.SetTransformParent(go.transform, parent.transform, "Reparent " + name);
        return go;
    }

    static T AddComponentIfMissing<T>(GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        if (existing != null) return existing;
        return Undo.AddComponent<T>(go);
    }

    static T FindComponent<T>(string gameObjectName) where T : Component
    {
        var go = FindAnywhere(gameObjectName);
        return go != null ? go.GetComponent<T>() : null;
    }

    static void SetOrder(GameObject go, int index)
    {
        if (go != null && go.transform.parent == null)
            go.transform.SetSiblingIndex(index);
    }

    // ─── Wiring ───

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
            var liveFeedGO = FindAnywhere("Live feed");
            if (liveFeedGO != null)
                SetRef(so, "previewRenderer", liveFeedGO.GetComponent<Renderer>());
            so.ApplyModifiedProperties();
        }

        // Wire LanternInteractionController
        var interactionGO = FindAnywhere("Lantern Interaction Controller");
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
        var slmGO = FindAnywhere("Scan Latern Manager");
        if (slmGO != null)
        {
            var slm = slmGO.GetComponent<ScanLanternManager>();
            if (slm != null)
            {
                var so = new SerializedObject(slm);
                var rvmDisplay = FindAnywhere("User Display Image");
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
        var mm = mmGO.GetComponent<MarkerManager>();
        if (mm == null) return;

        var mmSO = new SerializedObject(mm);
        var prSO = new SerializedObject(presenter);

        CopyProperty(mmSO, "marker0", prSO, "marker0");
        CopyProperty(mmSO, "markerBorder0", prSO, "markerBorder0");
        CopyProperty(mmSO, "marker1", prSO, "marker1");
        CopyProperty(mmSO, "markerBorder1", prSO, "markerBorder1");
        CopyProperty(mmSO, "marker2", prSO, "marker2");
        CopyProperty(mmSO, "markerBorder2", prSO, "markerBorder2");
        CopyProperty(mmSO, "marker3", prSO, "marker3");
        CopyProperty(mmSO, "markerBorder3", prSO, "markerBorder3");

        CopyArray(mmSO, "missingMaterials", prSO, "missingTextures");
        CopyArray(mmSO, "detectedMaterials", prSO, "detectedTextures");

        prSO.ApplyModifiedProperties();
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

    static void CopyArray(SerializedObject from, string fromProp, SerializedObject to, string toProp)
    {
        var src = from.FindProperty(fromProp);
        var dst = to.FindProperty(toProp);
        if (src == null || dst == null || !src.isArray || !dst.isArray) return;

        dst.arraySize = src.arraySize;
        for (int i = 0; i < src.arraySize; i++)
            dst.GetArrayElementAtIndex(i).objectReferenceValue =
                src.GetArrayElementAtIndex(i).objectReferenceValue;
    }
}
