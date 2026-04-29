// ============================================================
//  PlaceObject.cs  –  MASTER AR Furniture Placement Controller
//  Unity AR Foundation + ARCore / ARKit
//
//  DELETE SYSTEM:
//  - Select a furniture button (Chair/Sofa/Table)
//  - If that object is already placed, a red "🗑 Remove" button
//    appears at the bottom of the screen automatically
//  - Tap "🗑 Remove" → object is instantly deleted
//  - No Unity UI changes needed — button is drawn by OnGUI
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.EventSystems;

[RequireComponent(typeof(ARRaycastManager))]
public class PlaceObject : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════
    //  INSPECTOR FIELDS
    // ═══════════════════════════════════════════════════════

    [Header("Furniture Prefabs")]
    public GameObject chairPrefab;
    public GameObject sofaPrefab;
    public GameObject tablePrefab;

    [Header("Per-Object Spawn Scale")]
    public float chairScale = 1.0f;
    public float sofaScale = 1.0f;
    public float tableScale = 1.0f;

    [Tooltip("Rotation fix. Try (-90,0,0) if models spawn lying flat.")]
    public Vector3 rotationOffset = new Vector3(0f, 0f, 0f);

    [Header("Gesture Limits")]
    public float minScale = 0.05f;
    public float maxScale = 5.0f;

    // ═══════════════════════════════════════════════════════
    //  PRIVATE STATE
    // ═══════════════════════════════════════════════════════

    private ARRaycastManager _arRaycast;
    private static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();

    private GameObject _activePrefab;
    private GameObject _chairInstance;
    private GameObject _sofaInstance;
    private GameObject _tableInstance;
    private GameObject _selectedInstance;

    // Two-finger gesture state
    private float _pinchStartDist;
    private float _pinchStartScale;
    private float _twistStartAngle;
    private float _twistStartY;
    private bool _twoFingerActive;

    // OnGUI style
    private GUIStyle _removeButtonStyle;
    private bool _styleReady = false;

    // ═══════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ═══════════════════════════════════════════════════════

    private void Awake()
    {
        _arRaycast = GetComponent<ARRaycastManager>();
        if (_arRaycast == null)
            Debug.LogError("[PlaceObject] ✖ ARRaycastManager missing on PlacementManager!");
    }

    private void Start()
    {
        if (chairPrefab == null) Debug.LogWarning("[PlaceObject] ✖ chairPrefab not assigned!");
        if (sofaPrefab == null) Debug.LogWarning("[PlaceObject] ✖ sofaPrefab not assigned!");
        if (tablePrefab == null) Debug.LogWarning("[PlaceObject] ✖ tablePrefab not assigned!");
        Debug.Log("[PlaceObject] ✓ Ready — tap a button then tap the floor.");
    }

    private void Update()
    {
        if (Input.touchCount == 0) { _twoFingerActive = false; return; }

        if (Input.touchCount >= 2)
        {
            HandlePinchTwist(Input.GetTouch(0), Input.GetTouch(1));
            return;
        }

        HandleSingleTouch(Input.GetTouch(0));
    }

    // ═══════════════════════════════════════════════════════
    //  ON GUI — draws the Remove button when relevant
    // ═══════════════════════════════════════════════════════

    private void OnGUI()
    {
        // Only show Remove button if the currently selected type is already placed
        if (_activePrefab == null) return;
        if (CurrentInstance() == null) return;

        // Build style once
        if (!_styleReady)
        {
            _removeButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _removeButtonStyle.normal.textColor = Color.white;
            _removeButtonStyle.hover.textColor = Color.white;
            _removeButtonStyle.active.textColor = Color.white;
            _styleReady = true;
        }

        // Button size & position — bottom center of screen
        float btnW = 260f;
        float btnH = 80f;
        float btnX = (Screen.width - btnW) / 2f;
        float btnY = Screen.height - btnH - 40f;

        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.85f, 0.1f, 0.1f, 1f); // red

        if (GUI.Button(new Rect(btnX, btnY, btnW, btnH),
                       "🗑  Remove " + _activePrefab.name, _removeButtonStyle))
        {
            DeleteCurrent();
        }

        GUI.backgroundColor = prev;
    }

    // ═══════════════════════════════════════════════════════
    //  PUBLIC SELECT BUTTONS (wire to ChairButton etc.)
    // ═══════════════════════════════════════════════════════

    public void SelectChair()
    {
        _activePrefab = chairPrefab;
        _selectedInstance = _chairInstance;
        Debug.Log("[PlaceObject] Chair selected.");
    }

    public void SelectSofa()
    {
        _activePrefab = sofaPrefab;
        _selectedInstance = _sofaInstance;
        Debug.Log("[PlaceObject] Sofa selected.");
    }

    public void SelectTable()
    {
        _activePrefab = tablePrefab;
        _selectedInstance = _tableInstance;
        Debug.Log("[PlaceObject] Table selected.");
    }

    // ═══════════════════════════════════════════════════════
    //  SINGLE TOUCH — tap to place, drag to move
    // ═══════════════════════════════════════════════════════

    private void HandleSingleTouch(Touch touch)
    {
        if (IsTouchOnUI(touch)) return;

        // Block touches in the OnGUI Remove button area
        if (IsRemoveButtonArea(touch.position)) return;

        if (_activePrefab == null)
        {
            if (touch.phase == TouchPhase.Began)
                Debug.Log("[PlaceObject] Select a furniture type first!");
            return;
        }

        if (touch.phase == TouchPhase.Began)
        {
            if (!_arRaycast.Raycast(touch.position, Hits, TrackableType.PlaneWithinPolygon))
            {
                Debug.Log("[PlaceObject] No plane hit — move phone slowly to scan floor.");
                return;
            }

            Vector3 hitPos = Hits[0].pose.position;
            Quaternion hitRot = Hits[0].pose.rotation;
            GameObject existing = CurrentInstance();

            if (existing == null)
                SpawnFurniture(hitPos, hitRot);
            else
            {
                existing.transform.position = hitPos;
                _selectedInstance = existing;
                Debug.Log($"[PlaceObject] Moved '{existing.name}'");
            }
        }
        else if (touch.phase == TouchPhase.Moved && _selectedInstance != null)
        {
            if (!_arRaycast.Raycast(touch.position, Hits, TrackableType.PlaneWithinPolygon)) return;
            _selectedInstance.transform.position = Vector3.Lerp(
                _selectedInstance.transform.position,
                Hits[0].pose.position,
                Time.deltaTime * 15f);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  TWO-FINGER — pinch = scale, twist = rotate Y
    // ═══════════════════════════════════════════════════════

    private void HandlePinchTwist(Touch t0, Touch t1)
    {
        if (_selectedInstance == null) return;

        float curDist = Vector2.Distance(t0.position, t1.position);
        float curAngle = Mathf.Atan2(t1.position.y - t0.position.y,
                                     t1.position.x - t0.position.x) * Mathf.Rad2Deg;

        if (!_twoFingerActive)
        {
            _pinchStartDist = curDist;
            _pinchStartScale = _selectedInstance.transform.localScale.x;
            _twistStartAngle = curAngle;
            _twistStartY = _selectedInstance.transform.eulerAngles.y;
            _twoFingerActive = true;
            return;
        }

        if (!Mathf.Approximately(_pinchStartDist, 0f))
        {
            float newS = Mathf.Clamp(_pinchStartScale * (curDist / _pinchStartDist), minScale, maxScale);
            _selectedInstance.transform.localScale = Vector3.one * newS;
        }

        float delta = curAngle - _twistStartAngle;
        Vector3 e = _selectedInstance.transform.eulerAngles;
        e.y = _twistStartY - delta;
        _selectedInstance.transform.eulerAngles = e;
    }

    // ═══════════════════════════════════════════════════════
    //  SPAWN
    // ═══════════════════════════════════════════════════════

    private void SpawnFurniture(Vector3 pos, Quaternion surfaceRot)
    {
        Quaternion rot = surfaceRot * Quaternion.Euler(rotationOffset);
        GameObject obj = Instantiate(_activePrefab, pos, rot);
        float scale = GetScaleForActivePrefab();

        obj.transform.localPosition = Vector3.zero;
        obj.transform.position = pos;
        obj.transform.localScale = Vector3.one * scale;
        obj.name = _activePrefab.name;

        if (_activePrefab == chairPrefab) _chairInstance = obj;
        else if (_activePrefab == sofaPrefab) _sofaInstance = obj;
        else if (_activePrefab == tablePrefab) _tableInstance = obj;

        _selectedInstance = obj;
        Debug.Log($"[PlaceObject] ✓ Spawned '{obj.name}' at {pos} | scale={scale}");
    }

    // ═══════════════════════════════════════════════════════
    //  DELETE
    // ═══════════════════════════════════════════════════════

    private void DeleteCurrent()
    {
        GameObject toDelete = CurrentInstance();
        if (toDelete == null) return;

        string name = toDelete.name;

        if (_activePrefab == chairPrefab) _chairInstance = null;
        else if (_activePrefab == sofaPrefab) _sofaInstance = null;
        else if (_activePrefab == tablePrefab) _tableInstance = null;

        if (_selectedInstance == toDelete) _selectedInstance = null;

        Destroy(toDelete);
        Debug.Log($"[PlaceObject] 🗑️ Deleted '{name}'.");
    }

    // ═══════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Checks if a screen touch lands inside the OnGUI Remove button area
    /// so it doesn't accidentally move the object when tapping Remove.
    /// NOTE: OnGUI Y=0 is top; Input.touch Y=0 is bottom — flip Y here.
    /// </summary>
    private bool IsRemoveButtonArea(Vector2 touchPos)
    {
        if (_activePrefab == null || CurrentInstance() == null) return false;

        float btnW = 260f;
        float btnH = 80f;
        float btnX = (Screen.width - btnW) / 2f;
        float btnY = Screen.height - btnH - 40f; // OnGUI coords (Y from top)

        // Convert touch Y (from bottom) to GUI Y (from top)
        float guiY = Screen.height - touchPos.y;

        Rect btnRect = new Rect(btnX, btnY, btnW, btnH);
        return btnRect.Contains(new Vector2(touchPos.x, guiY));
    }

    private GameObject CurrentInstance()
    {
        if (_activePrefab == chairPrefab) return _chairInstance;
        if (_activePrefab == sofaPrefab) return _sofaInstance;
        if (_activePrefab == tablePrefab) return _tableInstance;
        return null;
    }

    private float GetScaleForActivePrefab()
    {
        if (_activePrefab == chairPrefab) return chairScale;
        if (_activePrefab == sofaPrefab) return sofaScale;
        if (_activePrefab == tablePrefab) return tableScale;
        return 1.0f;
    }

    private static bool IsTouchOnUI(Touch touch)
    {
        return EventSystem.current != null &&
               EventSystem.current.IsPointerOverGameObject(touch.fingerId);
    }
}