// ============================================================
//  PlaceObject.cs  –  MASTER AR Furniture Placement Controller
//  Unity AR Foundation + ARCore / ARKit
//
//  CAPTURE FIX:
//  - Saves to persistent path first
//  - Uses Android MediaStore to properly insert into Gallery
//  - Toast confirms save
// ============================================================

using System.Collections;
using System.Collections.Generic;
using System.IO;
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

    [Header("Capture")]
    [Tooltip("Drag your Canvas GameObject here")]
    public GameObject uiCanvas;

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

    private float _pinchStartDist;
    private float _pinchStartScale;
    private float _twistStartAngle;
    private float _twistStartY;
    private bool _twoFingerActive;

    private GUIStyle _removeButtonStyle;
    private GUIStyle _toastStyle;
    private bool _styleReady = false;
    private bool _hideOnGUI = false;
    private string _toastMsg = "";
    private float _toastTimer = 0f;

    // ═══════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ═══════════════════════════════════════════════════════

    private void Awake()
    {
        _arRaycast = GetComponent<ARRaycastManager>();
        if (_arRaycast == null)
            Debug.LogError("[PlaceObject] ✖ ARRaycastManager missing!");
    }

    private void Start()
    {
        if (chairPrefab == null) Debug.LogWarning("[PlaceObject] ✖ chairPrefab not assigned!");
        if (sofaPrefab == null) Debug.LogWarning("[PlaceObject] ✖ sofaPrefab not assigned!");
        if (tablePrefab == null) Debug.LogWarning("[PlaceObject] ✖ tablePrefab not assigned!");
        if (uiCanvas == null) Debug.LogWarning("[PlaceObject] ✖ uiCanvas not assigned!");
        Debug.Log("[PlaceObject] ✓ Ready.");
    }

    private void Update()
    {
        if (_toastTimer > 0f) _toastTimer -= Time.deltaTime;

        if (Input.touchCount == 0) { _twoFingerActive = false; return; }

        if (Input.touchCount >= 2)
        {
            HandlePinchTwist(Input.GetTouch(0), Input.GetTouch(1));
            return;
        }

        HandleSingleTouch(Input.GetTouch(0));
    }

    // ═══════════════════════════════════════════════════════
    //  ON GUI
    // ═══════════════════════════════════════════════════════

    private void OnGUI()
    {
        if (_hideOnGUI) return;

        if (!_styleReady)
        {
            _removeButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _removeButtonStyle.normal.textColor = Color.white;
            _removeButtonStyle.hover.textColor = Color.white;
            _removeButtonStyle.active.textColor = Color.white;

            _toastStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _toastStyle.normal.textColor = Color.white;
            _styleReady = true;
        }

        // Remove button — bottom center
        if (_activePrefab != null && CurrentInstance() != null)
        {
            float btnW = 260f, btnH = 80f;
            float btnX = (Screen.width - btnW) / 2f;
            float btnY = Screen.height - btnH - 40f;

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 0.1f, 0.1f, 1f);
            if (GUI.Button(new Rect(btnX, btnY, btnW, btnH),
                           "🗑  Remove " + _activePrefab.name, _removeButtonStyle))
                DeleteCurrent();
            GUI.backgroundColor = prev;
        }

        // Toast — center screen
        if (_toastTimer > 0f)
        {
            float tw = 380f, th = 70f;
            float tx = (Screen.width - tw) / 2f;
            float ty = (Screen.height - th) / 2f;

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.1f, 0.55f, 0.1f, 0.93f);
            GUI.Box(new Rect(tx, ty, tw, th), _toastMsg, _toastStyle);
            GUI.backgroundColor = prev;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  PUBLIC SELECT BUTTONS
    // ═══════════════════════════════════════════════════════

    public void SelectChair() { _activePrefab = chairPrefab; _selectedInstance = _chairInstance; }
    public void SelectSofa() { _activePrefab = sofaPrefab; _selectedInstance = _sofaInstance; }
    public void SelectTable() { _activePrefab = tablePrefab; _selectedInstance = _tableInstance; }

    // ═══════════════════════════════════════════════════════
    //  CAPTURE — wire CaptureButton OnClick to this
    // ═══════════════════════════════════════════════════════

    public void CapturePhoto()
    {
        StartCoroutine(TakeScreenshot());
    }

    private IEnumerator TakeScreenshot()
    {
        // 1. Hide UI
        _hideOnGUI = true;
        if (uiCanvas != null) uiCanvas.SetActive(false);

        // 2. Wait for UI to fully disappear
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame(); // two frames to be safe

        // 3. Capture to texture
        Texture2D screenTex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        screenTex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        screenTex.Apply();
        byte[] imgBytes = screenTex.EncodeToJPG(95);
        Destroy(screenTex);

        // 4. Save to persistent path first (always works)
        string fileName = "ARFurniture_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg";
        string tempPath = Path.Combine(Application.persistentDataPath, fileName);
        File.WriteAllBytes(tempPath, imgBytes);
        Debug.Log($"[PlaceObject] Saved temp file: {tempPath}");

        // 5. Restore UI immediately
        if (uiCanvas != null) uiCanvas.SetActive(true);
        _hideOnGUI = false;

#if UNITY_ANDROID
        // 6. Use Android MediaStore to insert properly into Gallery
        yield return new WaitForSeconds(0.1f); // small delay for file flush

        try
        {
            using (AndroidJavaClass mediaStoreImages = new AndroidJavaClass("android.provider.MediaStore$Images$Media"))
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver");

                // Insert image into MediaStore
                string insertedPath = mediaStoreImages.CallStatic<string>(
                    "insertImage",
                    resolver,
                    tempPath,
                    fileName,
                    "AR Furniture App Screenshot"
                );

                if (!string.IsNullOrEmpty(insertedPath))
                {
                    Debug.Log($"[PlaceObject] ✓ Inserted into Gallery: {insertedPath}");
                    _toastMsg = "📷 Photo saved to Gallery!";
                    _toastTimer = 2.5f;
                }
                else
                {
                    Debug.LogWarning("[PlaceObject] MediaStore insert returned null.");
                    _toastMsg = "⚠️ Saved but check Files app";
                    _toastTimer = 2.5f;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[PlaceObject] Gallery insert failed: {ex.Message}");
            _toastMsg = "⚠️ Saved to app folder";
            _toastTimer = 2.5f;
        }
#else
        _toastMsg   = "📷 Photo saved!";
        _toastTimer = 2.5f;
#endif
    }

    // ═══════════════════════════════════════════════════════
    //  SINGLE TOUCH
    // ═══════════════════════════════════════════════════════

    private void HandleSingleTouch(Touch touch)
    {
        if (IsTouchOnUI(touch)) return;
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
                Debug.Log("[PlaceObject] No plane hit.");
                return;
            }

            Vector3 hitPos = Hits[0].pose.position;
            Quaternion hitRot = Hits[0].pose.rotation;
            GameObject existing = CurrentInstance();

            if (existing == null) SpawnFurniture(hitPos, hitRot);
            else { existing.transform.position = hitPos; _selectedInstance = existing; }
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
    //  TWO-FINGER
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
        Debug.Log($"[PlaceObject] ✓ Spawned '{obj.name}' | scale={scale}");
    }

    // ═══════════════════════════════════════════════════════
    //  DELETE
    // ═══════════════════════════════════════════════════════

    private void DeleteCurrent()
    {
        GameObject toDelete = CurrentInstance();
        if (toDelete == null) return;

        if (_activePrefab == chairPrefab) _chairInstance = null;
        else if (_activePrefab == sofaPrefab) _sofaInstance = null;
        else if (_activePrefab == tablePrefab) _tableInstance = null;

        if (_selectedInstance == toDelete) _selectedInstance = null;
        Destroy(toDelete);
        Debug.Log("[PlaceObject] 🗑️ Deleted.");
    }

    // ═══════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════

    private bool IsRemoveButtonArea(Vector2 touchPos)
    {
        if (_activePrefab == null || CurrentInstance() == null) return false;
        float btnW = 260f, btnH = 80f;
        float btnX = (Screen.width - btnW) / 2f;
        float btnY = Screen.height - btnH - 40f;
        float guiY = Screen.height - touchPos.y;
        return new Rect(btnX, btnY, btnW, btnH).Contains(new Vector2(touchPos.x, guiY));
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