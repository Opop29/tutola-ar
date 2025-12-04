using UnityEngine;
using TMPro;
using UnityEngine.Android;
using System.Collections;
using System.Collections.Generic;

public class POI3D : MonoBehaviour
{
    [Header("References")]
    public ARControl arControl;
    public GameObject poi3DPrefab;

    [Header("Settings")]
    public float baseSize = 2f;
    public float sizeScaleFactor = 0.05f;


    private List<GameObject> poiInstances = new List<GameObject>();
    private Dictionary<GameObject, POI> poiData = new Dictionary<GameObject, POI>();
    private bool isActive = false;
    private bool poisCreated = false;
    private Vector2 userGPSOrigin;
    private Vector2 prevGPSOrigin; // Track previous origin to prevent jitter
    private bool hasGPSOrigin = false;
    private float smoothHeading = 0f; // Smoothed compass heading
    private Vector3 arWorldOrigin; 
    private Vector3 initialARCameraPosition; // AR camera position when POIs were placed
    private bool arFusionInitialized = false; // Whether AR fusion has been set up
    private float lastGPSUpdateTime = 0f; // Track when GPS was last updated
    private const float GPS_UPDATE_INTERVAL = 30f; // Update GPS every 30 seconds

    // Properties expected by ARControl
    private List<POI> groupPOIs = new List<POI>(); // POIs for group navigation
    private bool showOnlySelectedPOI = false; // Flag to show only selected POI in AR scene
    private List<POI> currentGroupPOIs = null; // Current group POIs being displayed (null when not in group mode)

    private void Start()
    {
        // Find ARControl if not assigned
        if (arControl == null)
        {
            arControl = FindFirstObjectByType<ARControl>();
        }

        if (arControl == null)
        {
            Debug.LogError("POI3D: ARControl not found!");
            return;
        }

        // Enable compass and gyroscope for heading data
        Input.compass.enabled = true;
        Input.gyro.enabled = true;

        Debug.Log("POI3D: Initialized successfully");
    }

    private void Update()
    {
        // Gyroscope + Compass fusion for stable heading
        Quaternion gyroAttitude = Input.gyro.attitude;
        float gyroYaw = gyroAttitude.eulerAngles.y;

        // Fuse gyro and compass (only if compass accuracy is good)
        float fusedHeading;
        if (Input.compass.headingAccuracy <= 20f)
        {
            fusedHeading = Mathf.LerpAngle(gyroYaw, Input.compass.trueHeading, 0.3f);
        }
        else
        {
            fusedHeading = gyroYaw; // Fallback to gyro only when compass is inaccurate
        }

        // Smooth the fused heading
        smoothHeading = Mathf.LerpAngle(smoothHeading, fusedHeading, Time.deltaTime * 5f);

        if (isActive && poiInstances.Count > 0 && Camera.main != null)
        {
            // POIs are now truly static - no position updates based on GPS or AR movement
            // Only update text orientation and distance for user feedback
            UpdatePOITexts();
        }
    }

    public void Activate3DPOIs()
    {
        Debug.Log("POI3D: Activate3DPOIs() called - Truly Static POI Mode");

        if (isActive)
        {
            Debug.Log("POI3D: Already active, returning");
            return;
        }

        isActive = true;
        poisCreated = false;

        // Set fixed AR world origin that never moves
        if (arControl.arSessionOrigin != null)
        {
            arWorldOrigin = arControl.arSessionOrigin.transform.position;
            Debug.Log($"POI3D: Set fixed AR world origin at {arWorldOrigin}");
        }
        else
        {
            arWorldOrigin = Vector3.zero;
            Debug.LogWarning("POI3D: arSessionOrigin is null, using Vector3.zero as world origin");
        }

        Debug.Log("POI3D: Activating 3D POI display - POIs will be positioned once and remain static");

        // Check if poi3DPrefab is assigned
        if (poi3DPrefab == null)
        {
            Debug.LogError("POI3D: poi3DPrefab is not assigned! Please assign a 3D prefab in the Inspector.");
            Debug.LogError("POI3D: Look for a GameObject with POI3D script and assign 'Poi 3d Prefab' field.");
            return;
        }

        // Check ARControl reference
        if (arControl == null)
        {
            arControl = FindFirstObjectByType<ARControl>();
            if (arControl == null)
            {
                Debug.LogError("POI3D: arControl is not assigned and could not be found! Please assign ARControl reference in the Inspector.");
                return;
            }
            Debug.Log("POI3D: Found ARControl via FindFirstObjectByType");
        }

        Debug.Log($"POI3D: poi3DPrefab assigned: {poi3DPrefab.name}");
        Debug.Log($"POI3D: arControl cached POIs: {arControl.cachedPOIs?.Count ?? 0}");

        // Check if we have any POIs
        if (arControl.cachedPOIs == null || arControl.cachedPOIs.Count == 0)
        {
            Debug.LogWarning("POI3D: No POIs available from database! Creating test POIs for demonstration.");
            CreateTestPOIsForDemo();
        }

        // Set GPS origin for initial positioning only
        SetGPSOrigin();

        // POIs are now truly static - no AR fusion or GPS following
        Debug.Log("POI3D: POIs will be positioned based on GPS and remain static thereafter");

        // Start updating 3D POIs
        Debug.Log("POI3D: Starting Update3DPOIs coroutine - static positioning mode");
        StartCoroutine(Update3DPOIs());
    }

    public void Deactivate3DPOIs()
    {
        if (!isActive) return;

        isActive = false;
        Debug.Log("POI3D: Deactivating 3D POI display");

        // Destroy all POI instances
        foreach (GameObject poiInstance in poiInstances)
        {
            if (poiInstance != null)
            {
                Destroy(poiInstance);
            }
        }
        poiInstances.Clear();
        poiData.Clear();

        poisCreated = false;

        // Clear navigation data
        groupPOIs.Clear();

        StopAllCoroutines();
    }

    private IEnumerator Update3DPOIs()
    {
        // Create POIs initially
        if (!poisCreated)
        {
            CreateFloatingPOIs();
            poisCreated = true;
        }

        // Keep coroutine running and check for display mode changes
        while (isActive)
        {
            yield return null;
        }
    }

    // Method to update the display mode and refresh POIs
    public void UpdateDisplayMode(bool showOnlySelected)
    {
        if (!isActive)
        {
            Debug.LogWarning("POI3D: Cannot update display mode - POI3D is not active");
            return;
        }

        bool modeChanged = showOnlySelected != showOnlySelectedPOI;
        showOnlySelectedPOI = showOnlySelected;

        // Reset group POIs when switching to single POI mode
        if (showOnlySelected && currentGroupPOIs != null)
        {
            currentGroupPOIs = null;
            modeChanged = true;
        }

        if (modeChanged)
        {
            Debug.Log($"POI3D: Display mode changed to showOnlySelectedPOI: {showOnlySelectedPOI}");

            // Clear existing POIs
            foreach (GameObject poiInstance in poiInstances)
            {
                if (poiInstance != null)
                {
                    Destroy(poiInstance);
                }
            }
            poiInstances.Clear();
            poiData.Clear();

            // Recreate POIs with new display mode
            poisCreated = false;
            CreateFloatingPOIs();
            poisCreated = true;
        }
    }

    // Method to update group display mode
    public void UpdateGroupDisplayMode(List<POI> groupPOIs)
    {
        if (!isActive)
        {
            Debug.LogWarning("POI3D: Cannot update group display mode - POI3D is not active");
            return;
        }

        bool modeChanged = !AreListsEqual(currentGroupPOIs, groupPOIs);
        currentGroupPOIs = groupPOIs != null ? new List<POI>(groupPOIs) : null;
        showOnlySelectedPOI = false; // Disable single POI mode when in group mode

        if (modeChanged)
        {
            Debug.Log($"POI3D: Group display mode changed to show {currentGroupPOIs?.Count ?? 0} group POIs");

            // Clear existing POIs
            foreach (GameObject poiInstance in poiInstances)
            {
                if (poiInstance != null)
                {
                    Destroy(poiInstance);
                }
            }
            poiInstances.Clear();
            poiData.Clear();

            // Recreate POIs with new display mode
            poisCreated = false;
            CreateFloatingPOIs();
            poisCreated = true;
        }
    }

    // Helper method to compare POI lists
    private bool AreListsEqual(List<POI> list1, List<POI> list2)
    {
        if (list1 == null && list2 == null) return true;
        if (list1 == null || list2 == null) return false;
        if (list1.Count != list2.Count) return false;

        for (int i = 0; i < list1.Count; i++)
        {
            if (list1[i] != list2[i]) return false;
        }
        return true;
    }

    private void SetGPSOrigin()
    {
        Vector2 newOrigin;

        // Get current GPS position
        if (Input.location.status == LocationServiceStatus.Running &&
            Permission.HasUserAuthorizedPermission(Permission.FineLocation) &&
            Input.location.isEnabledByUser)
        {
            newOrigin = new Vector2(Input.location.lastData.latitude, Input.location.lastData.longitude);
        }
        else
        {
            // Fallback for testing
            newOrigin = new Vector2(37.7749f, -122.4194f);
        }

        // Only update origin if user has moved > 5 meters (prevents GPS jitter)
        if (!hasGPSOrigin || Vector2.Distance(prevGPSOrigin, newOrigin) > 5f)
        {
            prevGPSOrigin = userGPSOrigin; // Store previous before updating
            userGPSOrigin = newOrigin;
            hasGPSOrigin = true;
            Debug.Log($"POI3D: Updated GPS origin to ({userGPSOrigin.x}, {userGPSOrigin.y}) - moved {Vector2.Distance(prevGPSOrigin, newOrigin):F1}m");
        }
        else
        {
            Debug.Log($"POI3D: GPS origin unchanged - movement {Vector2.Distance(prevGPSOrigin, newOrigin):F1}m < 5m threshold");
        }
    }

    private void CreateTestPOIsForDemo()
    {
        Debug.Log("POI3D: Creating test POIs for demonstration");

        // Create test POIs around the map center
        arControl.cachedPOIs = new List<POI>();

        POI testPOI1 = new POI();
        testPOI1.label = "Demo POI 1";
        testPOI1.lat = arControl.playAreaCenter.x + 0.001f; // North of center
        testPOI1.lng = arControl.playAreaCenter.y;
        testPOI1.color = "#FF0000"; // Red
        testPOI1.mark_type = "marker";
        arControl.cachedPOIs.Add(testPOI1);

        POI testPOI2 = new POI();
        testPOI2.label = "Demo POI 2";
        testPOI2.lat = arControl.playAreaCenter.x;
        testPOI2.lng = arControl.playAreaCenter.y + 0.001f; // East of center
        testPOI2.color = "#00FF00"; // Green
        testPOI2.mark_type = "marker";
        arControl.cachedPOIs.Add(testPOI2);

        POI testPOI3 = new POI();
        testPOI3.label = "Demo POI 3";
        testPOI3.lat = arControl.playAreaCenter.x - 0.001f; // South of center
        testPOI3.lng = arControl.playAreaCenter.y;
        testPOI3.color = "#0000FF"; // Blue
        testPOI3.mark_type = "marker";
        arControl.cachedPOIs.Add(testPOI3);

        Debug.Log($"POI3D: Created {arControl.cachedPOIs.Count} test POIs for demonstration");
    }

    private void CreateFloatingPOIs()
    {
        Debug.Log("POI3D: CreateFloatingPOIs() called");

        if (arControl == null || arControl.cachedPOIs == null || arControl.cachedPOIs.Count == 0)
        {
            Debug.LogError("POI3D: No cached POIs available - cannot create floating POIs");
            return;
        }

        // Determine which POIs to show
        List<POI> poisToShow;
        if (currentGroupPOIs != null)
        {
            poisToShow = currentGroupPOIs; // Show group POIs when in group navigation
        }
        else if (showOnlySelectedPOI && arControl.selectedPOI != null)
        {
            poisToShow = new List<POI> { arControl.selectedPOI }; // Show only selected POI for single navigation
        }
        else
        {
            poisToShow = arControl.cachedPOIs; // Show all POIs
        }

        Debug.Log($"POI3D: Creating {poisToShow.Count} GPS-positioned POIs (showOnlySelectedPOI: {showOnlySelectedPOI})");

        for (int i = 0; i < poisToShow.Count; i++)
        {
            POI poi = poisToShow[i];
            if (poi == null)
            {
                Debug.LogWarning($"POI3D: POI at index {i} is null, skipping");
                continue;
            }

            Debug.Log($"POI3D: Processing POI {i}: {poi.label} at ({poi.lat}, {poi.lng})");

            Vector3 position;
            if (hasGPSOrigin)
            {
                // Position POI based on GPS coordinates relative to user
                position = CalculateGPSPosition(poi);
                Debug.Log($"POI3D: Positioning POI {poi.label} at GPS-based position {position}");

                // Debug coordinate calculation
                if (Time.frameCount % 600 == 0) // Log every 10 seconds for first POI
                {
                    float latDiff = poi.lat - userGPSOrigin.x;
                    float lngDiff = poi.lng - userGPSOrigin.y;
                    float east = lngDiff * 111000f * Mathf.Cos(userGPSOrigin.x * Mathf.Deg2Rad);
                    float north = latDiff * 111000f;
                    Debug.Log($"POI3D Coordinate Debug for {poi.label}: latDiff={latDiff:F6}, lngDiff={lngDiff:F6}, east={east:F2}, north={north:F2}, finalPos={position}");
                }
            }
            else
            {
                // Fallback to floating in front if no GPS
                position = Camera.main.transform.position +
                            Camera.main.transform.forward * 8f +
                            Camera.main.transform.right * (i - poisToShow.Count / 2f) * 3f;
                Debug.Log($"POI3D: Positioning POI {poi.label} at fallback position {position} (GPS fallback)");
            }

            Debug.Log($"POI3D: Instantiating poi3DPrefab at position {position}");
            GameObject poiInstance = Instantiate(poi3DPrefab, position, Quaternion.identity);
            poiInstances.Add(poiInstance);
            poiData[poiInstance] = poi;

            Debug.Log($"POI3D: ✅ SUCCESSFULLY CREATED POI: {poi.label} at world position {position}");
            Debug.Log($"POI3D: POI should now be visible in the AR camera view!");

            // Configure visual
            ConfigurePOIVisual(poiInstance, poi);

            // POI is now static - no rotation animation
            // Previously: StartCoroutine(AnimatePOI(poiInstance));
        }

        Debug.Log($"POI3D: Successfully created {poiInstances.Count} GPS-positioned POIs");
    }


    private Vector3 CalculateGPSPosition(POI poi)
    {
        const float METERS_PER_DEGREE = 111000f;

        float latDiff = poi.lat - userGPSOrigin.x;
        float lngDiff = poi.lng - userGPSOrigin.y;

        // Convert lat/lng difference to meters
        float east  = lngDiff * METERS_PER_DEGREE * Mathf.Cos(userGPSOrigin.x * Mathf.Deg2Rad);
        float north = latDiff * METERS_PER_DEGREE;

        // Create local ENU vector (East, North, Up)
        // POIs should be at ABSOLUTE GPS positions, not rotated by device heading
        // Flip coordinates if POIs appear on opposite side of heading
        Vector3 enu = new Vector3(-east, poi.height, -north);

        // Final world position relative to fixed AR world origin
        // No heading rotation - POIs stay at their true GPS locations
        return arWorldOrigin + enu;
    }


    private void ConfigurePOIVisual(GameObject poiInstance, POI poi)
    {
        // Set color
        Renderer renderer = poiInstance.GetComponent<Renderer>();
        if (renderer != null && !string.IsNullOrEmpty(poi.color))
        {
            if (ColorUtility.TryParseHtmlString(poi.color, out Color color))
            {
                renderer.material.color = color;
            }
        }

        // Calculate distance and set text
        TMPro.TextMeshPro textComponent = poiInstance.GetComponentInChildren<TMPro.TextMeshPro>();
        if (textComponent != null)
        {
            float distance = CalculateDistance(poi);
            textComponent.text = $"{poi.label}\n{distance:F1}m";
        }
    }

    private float CalculateDistance(POI poi)
    {
        // Use current GPS position if available, otherwise use initial origin
        Vector2 currentUserPos = userGPSOrigin;
        if (Input.location.status == LocationServiceStatus.Running &&
            Permission.HasUserAuthorizedPermission(Permission.FineLocation) &&
            Input.location.isEnabledByUser)
        {
            currentUserPos = new Vector2(Input.location.lastData.latitude, Input.location.lastData.longitude);
        }

        // Calculate distance using Haversine formula
        float dLat = (poi.lat - currentUserPos.x) * Mathf.Deg2Rad;
        float dLng = (poi.lng - currentUserPos.y) * Mathf.Deg2Rad;

        float a = Mathf.Sin(dLat / 2) * Mathf.Sin(dLat / 2) +
                  Mathf.Cos(currentUserPos.x * Mathf.Deg2Rad) * Mathf.Cos(poi.lat * Mathf.Deg2Rad) *
                  Mathf.Sin(dLng / 2) * Mathf.Sin(dLng / 2);

        float c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1 - a));
        float distance = 6371000 * c; // Earth radius in meters

        return distance;
    }


    private void UpdatePOIPositionsWithARFussion()
    {
        if (!arFusionInitialized || Camera.main == null) return;

        // Calculate camera movement since initial placement
        Vector3 currentCameraPosition = Camera.main.transform.position;
        Vector3 cameraMovement = currentCameraPosition - initialARCameraPosition;

        // Update POI positions to follow camera movement (smooth AR tracking)
        foreach (var kvp in poiData)
        {
            GameObject poiInstance = kvp.Key;
            POI poi = kvp.Value;

            if (poiInstance == null) continue;

            // Calculate original GPS-based position
            Vector3 originalGPSPosition = CalculateGPSPosition(poi);

            // Apply camera movement to maintain relative positioning
            Vector3 newPosition = originalGPSPosition + cameraMovement;

            // Smoothly move POI to new position
            poiInstance.transform.position = Vector3.Lerp(
                poiInstance.transform.position,
                newPosition,
                Time.deltaTime * 5f // Smooth interpolation
            );
        }

        // Check if it's time for GPS update to correct any drift
        if (Time.time - lastGPSUpdateTime >= GPS_UPDATE_INTERVAL)
        {
            Debug.Log("POI3D: GPS update interval reached - correcting AR fusion positions");
            CorrectPositionsWithGPS();
            lastGPSUpdateTime = Time.time;
        }
    }

    private void CorrectPositionsWithGPS()
    {
        // Update GPS origin and reposition POIs based on new GPS data
        SetGPSOrigin();

        // Reset AR fusion baseline
        if (Camera.main != null)
        {
            initialARCameraPosition = Camera.main.transform.position;
        }

        // Reposition all POIs based on updated GPS
        foreach (var kvp in poiData)
        {
            GameObject poiInstance = kvp.Key;
            POI poi = kvp.Value;

            if (poiInstance == null) continue;

            Vector3 gpsPosition = CalculateGPSPosition(poi);
            poiInstance.transform.position = gpsPosition;

            Debug.Log($"POI3D: GPS correction - Moved {poi.label} to {gpsPosition}");
        }
    }

    private void UpdatePOITexts()
    {
        foreach (var kvp in poiData)
        {
            GameObject poiInstance = kvp.Key;
            POI poi = kvp.Value;

            if (poiInstance == null) continue;

            // Update text with current distance and make it face camera
            TMPro.TextMeshPro textComponent = poiInstance.GetComponentInChildren<TMPro.TextMeshPro>();
            if (textComponent != null && Camera.main != null)
            {
                float distance = CalculateDistance(poi);
                textComponent.text = $"{poi.label}\n{distance:F1}m";
                textComponent.transform.LookAt(Camera.main.transform);
                textComponent.transform.Rotate(0, 180, 0);
            }
        }
    }

    private IEnumerator AnimatePOI(GameObject poiInstance)
    {
        while (poiInstance != null && isActive)
        {
            poiInstance.transform.Rotate(Vector3.up, 30f * 0.1f);
            yield return new WaitForSeconds(0.1f);
        }
    }

    // Methods expected by ARControl
    public void StartGroupNavigation(string groupName, List<POI> groupPOIs)
    {
        Debug.Log($"POI3D: StartGroupNavigation called for '{groupName}' with {groupPOIs?.Count ?? 0} POIs");
        this.groupPOIs = groupPOIs ?? new List<POI>();
        this.currentGroupPOIs = groupPOIs != null ? new List<POI>(groupPOIs) : null;
        Debug.Log($"POI3D: Stored {this.groupPOIs.Count} POIs for group navigation");
    }

    public void UpdateOriginGPS(float lat, float lng)
    {
        Debug.Log($"POI3D: UpdateOriginGPS called with lat={lat}, lng={lng} - POIs are now truly static");

        // Update GPS origin for distance calculations only
        userGPSOrigin = new Vector2(lat, lng);
        hasGPSOrigin = true;

        // POIs remain static - no repositioning when GPS updates
        // This avoids POIs following the user's current GPS position
    }
}