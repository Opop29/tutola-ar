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
    private bool hasGPSOrigin = false;

    // GPS + AR Fusion properties
    private Vector3 initialARCameraPosition; // AR camera position when POIs were placed
    private bool arFusionInitialized = false; // Whether AR fusion has been set up
    private float lastGPSUpdateTime = 0f; // Track when GPS was last updated
    private const float GPS_UPDATE_INTERVAL = 30f; // Update GPS every 30 seconds

    // Properties expected by ARControl
    public bool singlePOIMode = false;
    public POI selectedPOI = null;

    private void Start()
    {
        // Find ARControl if not assigned
        if (arControl == null)
        {
            arControl = FindObjectOfType<ARControl>();
        }

        if (arControl == null)
        {
            Debug.LogError("POI3D: ARControl not found!");
            return;
        }

        Debug.Log("POI3D: Initialized successfully");
    }

    private void Update()
    {
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
        Debug.Log("POI3D: Activating 3D POI display - POIs will be positioned once and remain static");

        // Check if poi3DPrefab is assigned
        if (poi3DPrefab == null)
        {
            Debug.LogError("POI3D: poi3DPrefab is not assigned!");
            return;
        }

        // Check ARControl reference
        if (arControl == null)
        {
            Debug.LogError("POI3D: arControl is not assigned!");
            return;
        }

        Debug.Log($"POI3D: poi3DPrefab assigned: {poi3DPrefab.name}");
        Debug.Log($"POI3D: arControl cached POIs: {arControl.cachedPOIs?.Count ?? 0}");

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

        StopAllCoroutines();
    }

    private IEnumerator Update3DPOIs()
    {
        // Only create POIs once - they remain static after initial placement
        if (!poisCreated)
        {
            CreateFloatingPOIs();
            poisCreated = true;
        }

        // Keep coroutine running but don't update positions
        while (isActive)
        {
            yield return null;
        }
    }

    private void SetGPSOrigin()
    {
        // Set user's current GPS position as origin for POI positioning
        if (Input.location.status == LocationServiceStatus.Running &&
            Permission.HasUserAuthorizedPermission(Permission.FineLocation) &&
            Input.location.isEnabledByUser)
        {
            userGPSOrigin = new Vector2(Input.location.lastData.latitude, Input.location.lastData.longitude);
            hasGPSOrigin = true;
            Debug.Log($"POI3D: Set GPS origin at ({userGPSOrigin.x}, {userGPSOrigin.y})");
        }
        else
        {
            // Fallback for testing
            userGPSOrigin = new Vector2(37.7749f, -122.4194f);
            hasGPSOrigin = true;
            Debug.Log($"POI3D: Using fallback GPS origin at ({userGPSOrigin.x}, {userGPSOrigin.y})");
        }
    }

    private void CreateFloatingPOIs()
    {
        if (arControl == null || arControl.cachedPOIs == null || arControl.cachedPOIs.Count == 0)
        {
            Debug.Log("POI3D: No cached POIs available");
            return;
        }

        Debug.Log($"POI3D: Creating {arControl.cachedPOIs.Count} GPS-positioned POIs");

        for (int i = 0; i < arControl.cachedPOIs.Count; i++)
        {
            POI poi = arControl.cachedPOIs[i];
            if (poi == null) continue;

            Vector3 position;
            if (hasGPSOrigin)
            {
                // Position POI based on GPS coordinates relative to user
                position = CalculateGPSPosition(poi);
                Debug.Log($"POI3D: Positioning POI {poi.label} at GPS-based position {position}");
            }
            else
            {
                // Fallback to floating in front if no GPS
                position = Camera.main.transform.position +
                          Camera.main.transform.forward * 8f +
                          Camera.main.transform.right * (i - arControl.cachedPOIs.Count / 2f) * 3f;
                Debug.Log($"POI3D: Positioning POI {poi.label} at fallback position {position}");
            }

            GameObject poiInstance = Instantiate(poi3DPrefab, position, Quaternion.identity);
            poiInstances.Add(poiInstance);
            poiData[poiInstance] = poi;

            // Configure visual
            ConfigurePOIVisual(poiInstance, poi);

            // POI is now static - no rotation animation
            // Previously: StartCoroutine(AnimatePOI(poiInstance));
        }

        Debug.Log($"POI3D: Created {poiInstances.Count} GPS-positioned POIs");
    }

    private Vector3 CalculateGPSPosition(POI poi)
    {
        // Convert GPS difference to world position
        // Simple approximation: 1 degree lat/lng ≈ 111km
        const float METERS_PER_DEGREE = 111000f;

        float latDiff = poi.lat - userGPSOrigin.x;
        float lngDiff = poi.lng - userGPSOrigin.y;

        // Convert to meters (approximate)
        float east = lngDiff * METERS_PER_DEGREE * Mathf.Cos(userGPSOrigin.x * Mathf.Deg2Rad);
        float north = latDiff * METERS_PER_DEGREE;

        // Position relative to camera (user's position)
        Vector3 cameraPos = Camera.main.transform.position;
        return new Vector3(cameraPos.x + east, cameraPos.y + poi.height, cameraPos.z + north);
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
        // For now, just log - group navigation not implemented in simplified version
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