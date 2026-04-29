// ============================================================
//  PlaceObject.cs  –  MASTER AR Furniture Placement Controller
//  Unity AR Foundation + ARCore / ARKit
//
//  SETUP CHECKLIST (do this before building):
//  ──────────────────────────────────────────
//  1. Attach this script to your "PlacementManager" GameObject
//  2. PlacementManager must also have ARRaycastManager component
//  3. In Inspector, drag prefabs into: Chair Prefab, Sofa Prefab, Table Prefab
//  4. Wire buttons:
//       ChairButton  OnClick → PlaceObject.SelectChair()
//       SofaButton   OnClick → PlaceObject.SelectSofa()
//       TableButton  OnClick → PlaceObject.SelectTable()
//  5. Canvas must have a GraphicRaycaster  (yours should already)
//  6. EventSystem must exist in scene      (yours already does ✓)
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
    [Tooltip("Drag Chair_1 prefab here")]
    public GameObject chairPrefab;

    [Tooltip("Drag Sofa3 or Sofa_Fixed prefab here")]
    public GameObject sofaPrefab;

    [Tooltip("Drag Table_Rou prefab here")]
    public GameObject tablePrefab;

    [Header("Spawn Tuning")]
    [Tooltip("Scale every object gets on spawn. Increase if models appear tiny.")]
    public float spawnScale = 1.0f;

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

    private GameObject _activePrefab;      // chosen via button
    private GameObject _chairInstance;
    private GameObject _sofaInstance;
    private GameObject _tableInstance;
    private GameObject _selectedInstance;  // responds to pinch / twist

    // Two-finger gesture state
    private float _pinchStartDist;
    private float _pinchStartScale;
    private float _twistStartAngle;
    private float _twistStartY;
    private bool _twoFingerActive;

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
    //  PUBLIC BUTTON METHODS
    // ═══════════════════════════════════════════════════════

    public void SelectChair()
    {
        _activePrefab = chairPrefab;
        _selectedInstance = _chairInstance;
        Debug.Log("[PlaceObject] Chair selected — tap the floor.");
    }

    public void SelectSofa()
    {
        _activePrefab = sofaPrefab;
        _selectedInstance = _sofaInstance;
        Debug.Log("[PlaceObject] Sofa selected — tap the floor.");
    }

    public void SelectTable()
    {
        _activePrefab = tablePrefab;
        _selectedInstance = _tableInstance;
        Debug.Log("[PlaceObject] Table selected — tap the floor.");
    }

    // ═══════════════════════════════════════════════════════
    //  SINGLE TOUCH — tap to place, drag to move
    // ═══════════════════════════════════════════════════════

    private void HandleSingleTouch(Touch touch)
    {
        // Block touches that land on UI buttons
        if (IsTouchOnUI(touch)) return;

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
                Debug.Log($"[PlaceObject] Moved '{existing.name}' → {hitPos}");
            }
        }
        else if (touch.phase == TouchPhase.Moved && _selectedInstance != null)
        {
            // Smooth drag along detected planes
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

        // Scale
        if (!Mathf.Approximately(_pinchStartDist, 0f))
        {
            float newS = Mathf.Clamp(_pinchStartScale * (curDist / _pinchStartDist), minScale, maxScale);
            _selectedInstance.transform.localScale = Vector3.one * newS;
        }

        // Rotate Y
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

        // Force-reset local position (fixes off-pivot "invisible" spawns)
        obj.transform.localPosition = Vector3.zero;
        obj.transform.position = pos;
        obj.transform.localScale = Vector3.one * spawnScale;
        obj.name = _activePrefab.name;

        // Store in the right slot
        if (_activePrefab == chairPrefab) _chairInstance = obj;
        else if (_activePrefab == sofaPrefab) _sofaInstance = obj;
        else if (_activePrefab == tablePrefab) _tableInstance = obj;

        _selectedInstance = obj;
        Debug.Log($"[PlaceObject] ✓ Spawned '{obj.name}' at {pos} | scale={spawnScale}");
    }

    // ═══════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════

    private GameObject CurrentInstance()
    {
        if (_activePrefab == chairPrefab) return _chairInstance;
        if (_activePrefab == sofaPrefab) return _sofaInstance;
        if (_activePrefab == tablePrefab) return _tableInstance;
        return null;
    }

    private static bool IsTouchOnUI(Touch touch)
    {
        return EventSystem.current != null &&
               EventSystem.current.IsPointerOverGameObject(touch.fingerId);
    }
}