using UnityEngine;
using TMPro;
using UnityEngine.Android;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Globalization;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARControl : MonoBehaviour
{
    [Header("References")]
    public GameObject arSessionOrigin;  // Assign XR Origin in Inspector
    public GameObject arSession;        // Assign AR Session in Inspector
    public GameObject mainUICanvas;     // Assign the main UI Canvas in Inspector
    public TMP_Text arCoordinateText;   // Assign TMP Text for AR coordinates display
    public GameObject miniMapPlanePrefab; // Assign 3D plane prefab for mini-map
    public GameObject userMarkerPrefab; // Assign 3D prefab for user marker on mini-map
    public GameObject userHeadingIndicatorPrefab; // Assign 3D prefab for user heading indicator
    public SupabaseClient supabaseClient; // Assign SupabaseClient in Inspector
    public POI3D poi3DHandler; // Assign POI3D script for 3D POI management
    // public GeospatialController geospatialController; // Assign GeospatialController for Earth Anchors (requires ARCore Extensions)
    public MiniMapRotation miniMapRotation; // Assign MiniMapRotation script for mini map rotation control

    [Header("Map Settings")]
    public string mapboxToken = "pk.eyJ1Ijoib3BvcDI5IiwiYSI6ImNtZm8za3Q1NjAxcTEyanF4ZjZraWowdjEifQ.jNxrXsiX7Davmhjmp4ihWw"; // Mapbox token

    [Header("Limited Play Area Settings")]
    [Tooltip("Enable geographic boundaries for user navigation")]
    public bool enablePlayAreaLimits = true;
    
    [Tooltip("Center coordinates of the allowed play area")]
    public Vector2 playAreaCenter = new Vector2(8.360118854454575f, 124.86808673329348f);
    
    [Tooltip("Radius of the allowed play area in meters")]
    public float playAreaRadiusMeters = 5000f; // 5km radius
    
    [Tooltip("Show warning when user is near play area boundary")]
    public bool showPlayAreaWarnings = true;
    
    [Tooltip("Distance from boundary to start showing warnings (meters)")]
    public float playAreaWarningDistance = 1000f; // 1km from boundary

    [Header("Supabase Settings")]
    public string supabaseUrl = "https://xyyftberwxgynoholndm.supabase.co";
    public string supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Inh5eWZ0YmVyd3hneW5vaG9sbmRtIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTczMDQ2NDYsImV4cCI6MjA3Mjg4MDY0Nn0.B9TCQeLOjkLJ9KVn3vjUiHJDURZO4bJvOnzHvifVJ5c";
    public string tableName = "ar_pois";

    public float miniMapScale = 0.15f; // Adjustable scale for mini-map size
    public float mapZoomLevel = 14f; // Adjustable zoom level for map (lower = more zoomed out)

    [Header("Navigation")]
    public POI selectedPOI; // POI selected from information panel for navigation
    public float maxPOIDisplayDistance = 1000f; // Maximum distance in meters to display POIs
    public float maxARDisplayDistance = 50f; // Maximum distance in meters to display AR objects
    private bool showOnlySelectedPOI = false; // Flag to show only selected POI on mini map
    private List<POI> currentGroupPOIs = null; // POIs for current group navigation (null when not in group mode)

    [Header("POI Marker Settings")]
    [Tooltip("Scale factor for spreading out POI markers on the mini-map")]
    public float poiMarkerSpread = 1.0f;
    [Tooltip("Scale factor for POI marker size (relative to user marker)")]
    public float poiMarkerScale = 0.7f;

    [Header("Heading Indicator Settings")]
    [Tooltip("Calibration offset for heading indicator (degrees)")]
    public float headingIndicatorOffset = 0f;

    // Private variables to track previous values for real-time updates
    private float previousPoiMarkerSpread = 1.0f;
    private float previousPoiMarkerScale = 0.7f;

    private bool isUpdatingCoordinates = false;
    private GameObject miniMapInstance;
    private GameObject userMarkerInstance;
    private GameObject userHeadingIndicatorInstance;
    private List<GameObject> poiMarkerInstances = new List<GameObject>();

    // Public accessor for mini-map instance
    public GameObject MiniMapInstance => miniMapInstance;
    private Texture2D miniMapTexture;
    private bool isUpdatingMap = false;
    private float lastMapUpdateTime = 0f;
    private float lastARUpdateTime = 0f;
    private float lastCoordinateUpdateTime = 0f;
    public List<POI> cachedPOIs = new List<POI>();
    private List<GameObject> arMarkerInstances = new List<GameObject>();
    private const float ARRIVAL_DISTANCE_THRESHOLD = 10f; // Distance in meters to consider arrived at POI
    private List<float> recentLats = new List<float>(); // For GPS smoothing
    private List<float> recentLngs = new List<float>(); // For GPS smoothing
    private const int SMOOTHING_SAMPLES = 10; // Number of samples for GPS smoothing
    private float smoothedLat; // Smoothed latitude
    private float smoothedLng; // Smoothed longitude

    private void Start()
    {
        // Configure ARPlaneManager for horizontal plane detection only
        ConfigureARPlaneManager();

        // Request GPS permission if not already granted
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
        }

        // Enable gyroscope and compass
        Input.gyro.enabled = true;
        Input.compass.enabled = true;

        // Start compass recalibration routine (periodic to prevent drift)
        StartCoroutine(RecalibrateCompass());

        // Start location service with proper initialization
        StartLocationService();

        // Cache POIs from Supabase directly
        StartCoroutine(FetchPOIsFromSupabase());

        // if (geospatialController == null)
        // {
        //     geospatialController = FindObjectOfType<GeospatialController>();
        // }


        // Auto-assign MiniMapRotation if not already assigned
        if (miniMapRotation == null)
        {
            miniMapRotation = GetComponent<MiniMapRotation>();
            if (miniMapRotation == null)
            {
                miniMapRotation = gameObject.AddComponent<MiniMapRotation>();
                Debug.Log("ARControl: Auto-added MiniMapRotation component to this GameObject");
            }
        }

        // Initialize previous values for real-time updates
        previousPoiMarkerSpread = poiMarkerSpread;
        previousPoiMarkerScale = poiMarkerScale;
    }

    private void ConfigureARPlaneManager()
    {
        // Find ARPlaneManager component in the AR Session Origin
        ARPlaneManager planeManager = null;
        
        if (arSessionOrigin != null)
        {
            planeManager = arSessionOrigin.GetComponent<ARPlaneManager>();
        }
        
        // If not found on arSessionOrigin, search in the scene
        if (planeManager == null)
        {
            planeManager = FindFirstObjectByType<ARPlaneManager>();
        }
        
        if (planeManager != null)
        {
            // Set detection mode to horizontal planes only
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            Debug.Log("ARPlaneManager configured for horizontal plane detection only");
        }
        else
        {
            Debug.LogWarning("ARPlaneManager not found - plane detection mode could not be configured");
        }
    }

    private void CreateMockPOIsForTesting()
    {
        cachedPOIs = new List<POI>();

        // Create mock POIs very close to default location for testing
        POI mockPOI1 = new POI();
        mockPOI1.label = "Test POI 1";
        mockPOI1.lat = 8.360058053962094f + 0.0001f; // Very close north
        mockPOI1.lng = 124.86811131469575f;
        mockPOI1.color = "#FF0000"; // Red
        mockPOI1.mark_type = "marker";
        cachedPOIs.Add(mockPOI1);

        POI mockPOI2 = new POI();
        mockPOI2.label = "Test POI 2";
        mockPOI2.lat = 8.360058053962094f;
        mockPOI2.lng = 124.86811131469575f + 0.0001f; // Very close east
        mockPOI2.color = "#00FF00"; // Green
        mockPOI2.mark_type = "marker";
        cachedPOIs.Add(mockPOI2);

        POI mockPOI3 = new POI();
        mockPOI3.label = "Test POI 3";
        mockPOI3.lat = 8.360058053962094f - 0.0001f; // Very close south
        mockPOI3.lng = 124.86811131469575f;
        mockPOI3.color = "#0000FF"; // Blue
        mockPOI3.mark_type = "marker";
        cachedPOIs.Add(mockPOI3);

        Debug.Log($"✅ Created {cachedPOIs.Count} mock POIs for testing - app will work normally with sample data");
        Debug.Log("💡 Tip: Check your Supabase project settings and API keys if you need real POI data");
    }

    // Function to call when button is clicked
    public void OpenAR()
    {
        // Hide main UI canvas
        if (mainUICanvas != null)
            mainUICanvas.SetActive(false);

        // Enable AR objects
        if (arSessionOrigin != null)
            arSessionOrigin.SetActive(true);
        if (arSession != null)
            arSession.SetActive(true);

        // Create and position mini-map
        if (miniMapPlanePrefab != null && miniMapInstance == null)
        {
            miniMapInstance = Instantiate(miniMapPlanePrefab);
            miniMapInstance.transform.SetParent(Camera.main.transform);
            miniMapInstance.transform.localPosition = new Vector3(0f, -0.4f, 1f); // Center bottom position for wider view
            miniMapInstance.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            // Don't override the prefab's scale - use the prefab's original scale
            // The miniMapScale variable is now just for reference in the inspector
            // miniMapInstance.transform.localScale = new Vector3(miniMapScale, miniMapScale, miniMapScale);


            // Start loading map texture
            StartCoroutine(LoadMiniMapTexture());

            // Ensure mini-map has collider for mouse interaction
            if (miniMapInstance.GetComponent<Collider>() == null)
            {
                BoxCollider collider = miniMapInstance.AddComponent<BoxCollider>();
                collider.size = new Vector3(1f, 0.1f, 1f); // Cover the mini-map area
                Debug.Log("ARControl: Added collider to mini-map for mouse interaction");
            }

            // Create 3D user marker
            if (userMarkerPrefab != null)
            {
                userMarkerInstance = Instantiate(userMarkerPrefab);
                userMarkerInstance.transform.SetParent(miniMapInstance.transform);
                userMarkerInstance.transform.localPosition = new Vector3(0f, 2.190881f, 0.03f); // Start at center of mini-map with slight Z offset
                userMarkerInstance.transform.localRotation = Quaternion.identity;
                userMarkerInstance.transform.localScale = new Vector3(userMarkerInstance.transform.localScale.x, 2.190881f, userMarkerInstance.transform.localScale.z); // Set Y scale to 2.190881
                Debug.Log("ARControl: Created 3D user marker on mini-map");

                // Create user heading indicator as child of user marker
                if (userHeadingIndicatorPrefab != null)
                {
                    userHeadingIndicatorInstance = Instantiate(userHeadingIndicatorPrefab);
                    userHeadingIndicatorInstance.transform.SetParent(userMarkerInstance.transform);
                    userHeadingIndicatorInstance.transform.localPosition = Vector3.zero;
                    userHeadingIndicatorInstance.transform.localRotation = Quaternion.identity;
                    userHeadingIndicatorInstance.transform.localScale = Vector3.one;
                    Debug.Log("ARControl: Created user heading indicator on mini-map");
                }
                else
                {
                    Debug.LogWarning("ARControl: User heading indicator prefab not assigned!");
                }
            }
            else
            {
                Debug.LogWarning("ARControl: User marker prefab not assigned!");
            }

            // Create 3D POI markers
            CreatePOIMarkers();

            // Connect mini map to rotation script
            if (miniMapRotation != null)
            {
                miniMapRotation.SetMiniMapInstance(miniMapInstance);
                Debug.Log("MiniMapRotation: Connected to mini map instance");
            }
            else
            {
                Debug.LogWarning("ARControl: MiniMapRotation script not assigned! Please drag MiniMapRotation.cs component to ARControl game object.");
            }
        }

        // Start updating coordinates
        StartUpdatingCoordinates();

        // Activate 3D POI display
        Debug.Log($"ARControl: Activating 3D POI display - poi3DHandler is {(poi3DHandler != null ? "assigned" : "null")}");
        if (poi3DHandler == null)
        {
            poi3DHandler = FindFirstObjectByType<POI3D>();
            Debug.Log($"ARControl: Found POI3D via FindFirstObjectByType: {poi3DHandler != null}");
        }

        if (poi3DHandler != null)
        {
            Debug.Log("ARControl: Calling poi3DHandler.Activate3DPOIs()");
            poi3DHandler.Activate3DPOIs();
            Debug.Log("ARControl: 3D POI display activated");
        }
        else
        {
            Debug.LogWarning("ARControl: POI3D handler not found - 3D POIs will not be displayed");
        }


        // Initialize update timers
        lastMapUpdateTime = Time.time;
        lastARUpdateTime = Time.time;
        lastCoordinateUpdateTime = Time.time;

        // Adjust camera settings for better AR visibility
        if (Camera.main != null)
        {
            Camera.main.farClipPlane = 1000f; // Increase far clip plane to prevent frustum culling of distant POIs
            Camera.main.nearClipPlane = 0.1f; // Adjust near clip for better close object rendering
        }

        // Disable VSync for better performance on mobile
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 30; // Limit to 30 FPS for mobile performance
    }

    // Function to call from information panel navigation button
    public void NavigateToPOI(POI poi)
    {
        Debug.Log($"NavigateToPOI called with POI: {poi?.label ?? "null"}");

        // Find the POI in cached data to ensure we have the latest data
        POI cachedPOI = cachedPOIs.Find(p => p != null && p.label == poi.label &&
            Mathf.Approximately(p.lat, poi.lat) && Mathf.Approximately(p.lng, poi.lng));

        if (cachedPOI != null)
        {
            selectedPOI = cachedPOI;
            Debug.Log($"Selected POI from cached data: {selectedPOI.label} at ({selectedPOI.lat}, {selectedPOI.lng}) with color {selectedPOI.color}");
        }
        else
        {
            selectedPOI = poi;
            Debug.LogWarning($"POI not found in cached data, using provided POI: {selectedPOI.label}");
        }

        // Enable selected POI only view on mini map
        showOnlySelectedPOI = true;
        Debug.Log($"Mini map set to show only selected POI: {selectedPOI.label}");

        // Start navigation
        OpenAR(); // Open AR mode with navigation to selected POI

        // Update 3D POI display mode after AR is opened
        if (poi3DHandler != null)
        {
            poi3DHandler.UpdateDisplayMode(true);
        }

        // Update 3D POI markers on mini-map
        UpdatePOIMarkerPositions();
    }

    // Function to navigate to POI by label (useful for external calls)
    public void NavigateToPOIByLabel(string poiLabel)
    {
        POI foundPOI = cachedPOIs.Find(p => p != null && p.label == poiLabel);

        if (foundPOI != null)
        {
            NavigateToPOI(foundPOI);
        }
        else
        {
            Debug.LogError($"POI with label '{poiLabel}' not found in cached data");
        }
    }

    // Function to toggle showing only selected POI on mini map
    public void ShowSelectedPOI()
    {
        if (selectedPOI == null)
        {
            Debug.LogWarning("No POI selected for navigation - cannot show selected POI only");
            return;
        }

        showOnlySelectedPOI = true;
        Debug.Log($"Mini map now showing only selected POI: {selectedPOI.label}");

        // Refresh mini map to show only selected POI
        if (miniMapInstance != null)
        {
            StartCoroutine(LoadMiniMapTexture());
        }

        // Update 3D POI markers on mini-map
        UpdatePOIMarkerPositions();

        // Update 3D POI display mode
        if (poi3DHandler != null)
        {
            poi3DHandler.UpdateDisplayMode(true);
        }
    }

    // Function to show all POIs on mini map (view all)
    public void ShowAllPOIs()
    {
        showOnlySelectedPOI = false;
        currentGroupPOIs = null;
        Debug.Log("Mini map now showing all POIs");

        // Refresh mini map to show all POIs
        if (miniMapInstance != null)
        {
            StartCoroutine(LoadMiniMapTexture());
        }

        // Update 3D POI markers on mini-map
        UpdatePOIMarkerPositions();

        // Update 3D POI display mode
        if (poi3DHandler != null)
        {
            poi3DHandler.UpdateDisplayMode(false);
            poi3DHandler.UpdateGroupDisplayMode(null);
        }
    }

    // Function to retry fetching POIs from Supabase (useful if connection was temporarily down)
    public void RetryFetchPOIs()
    {
        Debug.Log("Retrying to fetch POIs from Supabase...");
        StartCoroutine(FetchPOIsFromSupabase());
    }


    // Function to navigate to an entire group of POIs
    public void NavigateToGroup(string groupName, List<POI> groupPOIs)
    {
        Debug.Log($"NavigateToGroup called for '{groupName}' with {groupPOIs?.Count ?? 0} POIs");

        if (string.IsNullOrEmpty(groupName) || groupPOIs == null || groupPOIs.Count == 0)
        {
            Debug.LogError("Invalid group navigation parameters");
            return;
        }

        // Set the first POI in the group as the selected POI for navigation
        selectedPOI = groupPOIs[0];
        currentGroupPOIs = new List<POI>(groupPOIs); // Store group POIs for display
        Debug.Log($"Set selected POI to first in group: {selectedPOI.label}");

        // Start group navigation - pass all POIs in the group to POI3D
        if (poi3DHandler != null)
        {
            poi3DHandler.StartGroupNavigation(groupName, groupPOIs);
            Debug.Log($"Started group navigation for '{groupName}' with {groupPOIs.Count} POIs");
        }

        OpenAR(); // Open AR mode with group navigation

        // Update 3D POI display mode for group after AR is opened
        if (poi3DHandler != null)
        {
            poi3DHandler.UpdateGroupDisplayMode(groupPOIs);
        }

        // Update 3D POI markers on mini-map
        UpdatePOIMarkerPositions();
    }

    // Function to call when back button is clicked
    public void CloseAR()
    {
        // Stop updating coordinates
        StopUpdatingCoordinates();

        // Disable AR objects
        if (arSessionOrigin != null)
            arSessionOrigin.SetActive(false);
        if (arSession != null)
            arSession.SetActive(false);

        // Destroy mini-map and user marker
        if (miniMapInstance != null)
        {
            // Disconnect from rotation script before destroying
            if (miniMapRotation != null)
            {
                miniMapRotation.SetMiniMapInstance(null);
            }

            Destroy(miniMapInstance);
            miniMapInstance = null;
            userMarkerInstance = null; // Will be destroyed with parent
        }

        // Clear POI markers
        ClearPOIMarkers();

        // Deactivate 3D POI display
        if (poi3DHandler != null)
        {
            poi3DHandler.Deactivate3DPOIs();
        }


        // Reset navigation state
        selectedPOI = null;
        showOnlySelectedPOI = false;
        currentGroupPOIs = null;

        // Reset 3D POI display mode
        if (poi3DHandler != null)
        {
            poi3DHandler.UpdateDisplayMode(false);
            poi3DHandler.UpdateGroupDisplayMode(null);
        }

        // Show main UI canvas
        if (mainUICanvas != null)
            mainUICanvas.SetActive(true);
    }

    private void StartUpdatingCoordinates()
    {
        if (!isUpdatingCoordinates)
        {
            isUpdatingCoordinates = true;
            InvokeRepeating("UpdateARCoordinates", 0f, 0.1f); // Update every 0.1 seconds for faster response
        }
    }

    private void StopUpdatingCoordinates()
    {
        if (isUpdatingCoordinates)
        {
            isUpdatingCoordinates = false;
            CancelInvoke("UpdateARCoordinates");
            if (arCoordinateText != null)
                arCoordinateText.text = "";
        }
    }

    private void Update()
    {
        // Mini-map rotation is now handled by MiniMapRotation script

        // Check for real-time updates to POI marker spread and scale
        if (miniMapInstance != null && poiMarkerInstances.Count > 0)
        {
            bool spreadChanged = !Mathf.Approximately(poiMarkerSpread, previousPoiMarkerSpread);
            bool scaleChanged = !Mathf.Approximately(poiMarkerScale, previousPoiMarkerScale);

            if (spreadChanged || scaleChanged)
            {
                RefreshPOIMarkerTransforms();
                previousPoiMarkerSpread = poiMarkerSpread;
                previousPoiMarkerScale = poiMarkerScale;
            }
        }
    }



    private void CreatePOIMarkers()
    {
        if (userMarkerPrefab == null || miniMapInstance == null) return;

        // Clear existing POI markers
        ClearPOIMarkers();

        // Determine which POIs to show
        List<POI> poisToShow;
        if (currentGroupPOIs != null)
        {
            poisToShow = currentGroupPOIs;
        }
        else if (showOnlySelectedPOI && selectedPOI != null)
        {
            poisToShow = new List<POI> { selectedPOI };
        }
        else
        {
            poisToShow = cachedPOIs;
        }

        // Create 3D markers for each POI
        foreach (POI poi in poisToShow)
        {
            if (poi != null)
            {
                GameObject poiMarker = Instantiate(userMarkerPrefab);
                poiMarker.transform.SetParent(miniMapInstance.transform);
                poiMarker.transform.localRotation = Quaternion.Euler(0f, 285f, 0f); // Rotate 285 degrees to face correct direction

                // Set scale relative to user marker
                poiMarker.transform.localScale = new Vector3(
                    poiMarker.transform.localScale.x * poiMarkerScale,
                    2.190881f * poiMarkerScale,
                    poiMarker.transform.localScale.z * poiMarkerScale
                );

                // Set color based on POI color
                SetMarkerColor(poiMarker, poi.color);

                // Position the marker (will be updated in UpdatePOIMarkerPositions)
                UpdatePOIMarkerPosition(poiMarker, poi);

                poiMarkerInstances.Add(poiMarker);
                Debug.Log($"Created 3D POI marker for {poi.label} with color {poi.color}");
            }
        }
    }

    private void UpdatePOIMarkerPositions()
    {
        if (miniMapInstance == null) return;

        // Determine which POIs to show
        List<POI> poisToShow;
        if (currentGroupPOIs != null)
        {
            poisToShow = currentGroupPOIs;
        }
        else if (showOnlySelectedPOI && selectedPOI != null)
        {
            poisToShow = new List<POI> { selectedPOI };
        }
        else
        {
            poisToShow = cachedPOIs;
        }

        // Update positions for existing markers or create new ones if needed
        for (int i = 0; i < poisToShow.Count && i < poiMarkerInstances.Count; i++)
        {
            POI poi = poisToShow[i];
            GameObject marker = poiMarkerInstances[i];
            if (poi != null && marker != null)
            {
                UpdatePOIMarkerPosition(marker, poi);
            }
        }

        // If we have more POIs than markers, create additional markers
        if (poisToShow.Count > poiMarkerInstances.Count)
        {
            for (int i = poiMarkerInstances.Count; i < poisToShow.Count; i++)
            {
                POI poi = poisToShow[i];
                if (poi != null && userMarkerPrefab != null)
                {
                    GameObject poiMarker = Instantiate(userMarkerPrefab);
                    poiMarker.transform.SetParent(miniMapInstance.transform);
                    poiMarker.transform.localRotation = Quaternion.Euler(0f, 285f, 0f); // Rotate 285 degrees to face correct direction
                    poiMarker.transform.localScale = new Vector3(
                        poiMarker.transform.localScale.x * poiMarkerScale,
                        2.190881f * poiMarkerScale,
                        poiMarker.transform.localScale.z * poiMarkerScale
                    );
                    // Set color based on POI color
                    SetMarkerColor(poiMarker, poi.color);
                    UpdatePOIMarkerPosition(poiMarker, poi);
                    poiMarkerInstances.Add(poiMarker);
                    Debug.Log($"Created additional 3D POI marker for {poi.label} with color {poi.color}");
                }
            }
        }
        // If we have fewer POIs than markers, hide extra markers
        else if (poisToShow.Count < poiMarkerInstances.Count)
        {
            for (int i = poisToShow.Count; i < poiMarkerInstances.Count; i++)
            {
                if (poiMarkerInstances[i] != null)
                {
                    poiMarkerInstances[i].SetActive(false);
                }
            }
        }
    }

    private void UpdatePOIMarkerPosition(GameObject marker, POI poi)
    {
        if (marker == null || poi == null || miniMapInstance == null) return;

        float mapCenterLat = playAreaCenter.x;
        float mapCenterLng = playAreaCenter.y;

        // Calculate pixel positions using Web Mercator projection (same as Mapbox)
        Vector2 poiPixel = LatLngToPixel(poi.lat, poi.lng, mapZoomLevel);
        Vector2 centerPixel = LatLngToPixel(mapCenterLat, mapCenterLng, mapZoomLevel);

        // Calculate offset in pixels from center (128x128 is center of 256x256 texture)
        float pixelOffsetX = poiPixel.x - centerPixel.x;
        float pixelOffsetY = poiPixel.y - centerPixel.y;

        // Convert pixel offsets to world coordinates (map plane is 1x1 units)
        // 256 pixels = 1 unit, so 1 pixel = 1/256 units
        // Invert coordinates to match map orientation
        float worldOffsetX = -pixelOffsetX / 256f;
        float worldOffsetY = pixelOffsetY / 256f;

        // Check if POI is within the visible map area (within ~0.5 units of center)
        bool withinMapBounds = Mathf.Abs(worldOffsetX) <= 0.5f && Mathf.Abs(worldOffsetY) <= 0.5f;

        if (withinMapBounds)
        {
            // Apply spread factor and position on mini-map plane (matching Mapbox coordinate system)
            Vector3 basePosition = new Vector3(worldOffsetX * poiMarkerSpread, 2.190881f, worldOffsetY * poiMarkerSpread);
            marker.transform.localPosition = basePosition;

            marker.SetActive(true);
        }
        else
        {
            // POI is outside map view, hide marker
            marker.SetActive(false);
        }
    }

    private Vector2 LatLngToPixel(float lat, float lng, float zoom)
    {
        // Web Mercator projection (same as Mapbox)
        float scale = 256f * Mathf.Pow(2f, zoom);

        float x = (lng + 180f) / 360f * scale;
        float y = (1f - Mathf.Log(Mathf.Tan(Mathf.PI / 4f + lat * Mathf.Deg2Rad / 2f)) / Mathf.PI) / 2f * scale;

        return new Vector2(x, y);
    }

    private void ClearPOIMarkers()
    {
        foreach (GameObject marker in poiMarkerInstances)
        {
            if (marker != null)
            {
                Destroy(marker);
            }
        }
        poiMarkerInstances.Clear();
    }

    private void RefreshPOIMarkerTransforms()
    {
        // Check if scale changed - if so, recreate markers with new scale
        if (!Mathf.Approximately(poiMarkerScale, previousPoiMarkerScale))
        {
            // Scale changed, need to recreate markers
            CreatePOIMarkers();
        }
        else
        {
            // Only spread changed, just update positions
            UpdatePOIMarkerPositions();
        }
    }

    private Color HexToColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Color.white;

        // Remove # if present
        hex = hex.Replace("#", "");

        // Handle different hex formats
        if (hex.Length == 6)
        {
            // RRGGBB format
            if (ColorUtility.TryParseHtmlString("#" + hex, out Color color))
            {
                return color;
            }
        }
        else if (hex.Length == 3)
        {
            // RGB format (expand to RRGGBB)
            string expandedHex = "" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
            if (ColorUtility.TryParseHtmlString("#" + expandedHex, out Color color))
            {
                return color;
            }
        }

        // Default to white if parsing fails
        return Color.white;
    }

    private void SetMarkerColor(GameObject marker, string hexColor)
    {
        if (marker == null) return;

        Color poiColor = HexToColor(hexColor);

        // Get all renderers in the marker and its children
        Renderer[] renderers = marker.GetComponentsInChildren<Renderer>();

        if (renderers.Length > 0)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    renderer.material.color = poiColor;
                }
            }
            Debug.Log($"Set POI marker color to {poiColor} (from {hexColor}) on {renderers.Length} renderers");
        }
        else
        {
            Debug.LogWarning("POI marker has no Renderer components to set color");
        }
    }

    private void UpdateARCoordinates()
    {
        if (arCoordinateText == null) return;

        // GPS + AR Fusion: More frequent GPS updates for better fusion accuracy
        if (Time.time - lastCoordinateUpdateTime < 1f) return;
        lastCoordinateUpdateTime = Time.time;

        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            arCoordinateText.text = "GPS Permission Required";
            return;
        }

        if (!Input.location.isEnabledByUser)
        {
            arCoordinateText.text = "Please Enable GPS";
            return;
        }

        // Check if location service is running
        if (Input.location.status == LocationServiceStatus.Running)
        {
            float latitude = Input.location.lastData.latitude;
            float longitude = Input.location.lastData.longitude;

            // Add to smoothing lists
            recentLats.Add(latitude);
            recentLngs.Add(longitude);
            if (recentLats.Count > SMOOTHING_SAMPLES)
            {
                recentLats.RemoveAt(0);
                recentLngs.RemoveAt(0);
            }

            // Calculate smoothed coordinates
            smoothedLat = recentLats.Count > 0 ? recentLats.Average() : latitude;
            smoothedLng = recentLngs.Count > 0 ? recentLngs.Average() : longitude;
        }

        // Update POI origin GPS for static positioning as user moves
        if (poi3DHandler != null)
        {
            poi3DHandler.UpdateOriginGPS(smoothedLat, smoothedLng);
        }

        arCoordinateText.text = $"{smoothedLat:F7}, {smoothedLng:F7}";

        // Check play area boundaries and show warnings if needed
        CheckPlayAreaBoundaries(smoothedLat, smoothedLng);

        // Check if arrived at selected POI using smoothed coordinates
        CheckArrivalAtPOI(smoothedLat, smoothedLng);

        // Update 3D user marker position
        UpdateUserMarkerPosition();

        // Update map less frequently (every 8 seconds for better performance)
        if (!isUpdatingMap && Time.time - lastMapUpdateTime > 8f)
        {
            lastMapUpdateTime = Time.time;
            StartCoroutine(LoadMiniMapTexture());
        }

        // AR markers removed - no longer updating AR markers
    }

    private IEnumerator LoadMiniMapTexture()
    {
        isUpdatingMap = true;

        float latitude, longitude;

        // Use fixed map center (play area center)
        float mapCenterLat = playAreaCenter.x;
        float mapCenterLng = playAreaCenter.y;
        latitude = mapCenterLat;
        longitude = mapCenterLng;
        Debug.Log($"Using fixed map center: ({latitude}, {longitude})");

        // Build markers string for POIs only (no user marker on texture)
        string markers = "";

        Debug.Log($"Building map markers. User location: ({latitude}, {longitude}), cached POIs: {cachedPOIs.Count}");

        // POI markers are now 3D objects - no longer adding 2D markers to map texture
        Debug.Log("3D POI markers replace 2D texture markers - skipping POI marker addition to map");

        Debug.Log($"Final markers string: {markers}");

        // Add navigation path line if we have a selected POI and GPS is available
        if (selectedPOI != null && Input.location.status == LocationServiceStatus.Running &&
            Permission.HasUserAuthorizedPermission(Permission.FineLocation) &&
            Input.location.isEnabledByUser)
        {
            // Create a path line from center to POI (since map is centered on play area)
            if (!string.IsNullOrEmpty(markers)) markers += ",";
            markers += $"path-5+0000ff-0.5({longitude},{latitude};{selectedPOI.lng},{selectedPOI.lat})";
        }

        // Add view indicator (flashlight beam) showing compass direction
        if (Camera.main != null)
        {
            float compassHeading = Input.compass.trueHeading; // Use compass heading for view indicator
            float halfFOV = 5f; // Narrow cone for heading-up mode (forward direction)
            float coneDistance = 0.01f; // Distance for the cone tip (in degrees, ~1km at equator)

            // Calculate bearing angles for the cone edges
            float leftBearing = compassHeading - halfFOV;
            float rightBearing = compassHeading + halfFOV;

            // Convert bearings to lat/lng offsets (flat earth approximation)
            float leftLat = latitude + coneDistance * Mathf.Cos(leftBearing * Mathf.Deg2Rad);
            float leftLng = longitude + coneDistance * Mathf.Sin(leftBearing * Mathf.Deg2Rad) / Mathf.Cos(latitude * Mathf.Deg2Rad);

            float rightLat = latitude + coneDistance * Mathf.Cos(rightBearing * Mathf.Deg2Rad);
            float rightLng = longitude + coneDistance * Mathf.Sin(rightBearing * Mathf.Deg2Rad) / Mathf.Cos(latitude * Mathf.Deg2Rad);

            Debug.Log($"View indicator: center ({longitude},{latitude}), left ({leftLng},{leftLat}), right ({rightLng},{rightLat})");

            // Add green lines for view cone edges
            if (!string.IsNullOrEmpty(markers)) markers += ",";
            markers += $"path-10+00ff00({longitude},{latitude};{leftLng},{leftLat}),path-10+00ff00({longitude},{latitude};{rightLng},{rightLat})";
        }

        string mapUrl;
        if (string.IsNullOrEmpty(markers))
        {
            // No markers, omit overlay parameter
            mapUrl = $"https://api.mapbox.com/styles/v1/mapbox/streets-v11/static/{longitude},{latitude},{mapZoomLevel},0,0/256x256@2x?access_token={mapboxToken}";
        }
        else
        {
            // Include markers in overlay
            mapUrl = $"https://api.mapbox.com/styles/v1/mapbox/streets-v11/static/{markers}/{longitude},{latitude},{mapZoomLevel},0,0/256x256@2x?access_token={mapboxToken}";
        }

        Debug.Log($"Generated map URL: {mapUrl}");
        Debug.Log($"Map URL length: {mapUrl.Length} characters");

        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(mapUrl))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                miniMapTexture = ((DownloadHandlerTexture)www.downloadHandler).texture;

                // Apply texture to mini-map plane
                if (miniMapInstance != null)
                {
                    Renderer renderer = miniMapInstance.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.material.mainTexture = miniMapTexture;
                        Debug.Log($"Successfully loaded mini-map texture! Texture size: {miniMapTexture.width}x{miniMapTexture.height}");
                        Debug.Log($"Markers included: {markers}");

                        // Update POI marker positions after map texture loads
                        UpdatePOIMarkerPositions();
                    }
                    else
                    {
                        Debug.LogError("Mini-map instance found but no Renderer component!");
                    }
                }
                else
                {
                    Debug.LogError("Mini-map instance is null when trying to apply texture!");
                }
            }
            else
            {
                Debug.LogError($"Failed to load mini-map: {www.error}");
                Debug.LogError($"Response code: {www.responseCode}");
                Debug.LogError($"Map URL was: {mapUrl}");

                // Try to get more error details
                if (www.downloadHandler != null)
                {
                    string errorText = www.downloadHandler.text;
                    if (!string.IsNullOrEmpty(errorText))
                    {
                        Debug.LogError($"Server response: {errorText}");
                    }
                }
            }
        }

        isUpdatingMap = false;
    }

    private void UpdateUserMarkerPosition()
    {
        if (userMarkerInstance == null || miniMapInstance == null) return;

        if (Input.location.status == LocationServiceStatus.Running &&
            Permission.HasUserAuthorizedPermission(Permission.FineLocation) &&
            Input.location.isEnabledByUser)
        {
            float mapCenterLat = playAreaCenter.x;
            float mapCenterLng = playAreaCenter.y;

            // Use raw GPS coordinates for more accurate positioning (less smoothing delay)
            float userLat = Input.location.lastData.latitude;
            float userLng = Input.location.lastData.longitude;

            // Calculate pixel positions using Web Mercator projection (same as Mapbox)
            Vector2 userPixel = LatLngToPixel(userLat, userLng, mapZoomLevel);
            Vector2 centerPixel = LatLngToPixel(mapCenterLat, mapCenterLng, mapZoomLevel);

            // Calculate offset in pixels from center
            float pixelOffsetX = userPixel.x - centerPixel.x;
            float pixelOffsetY = userPixel.y - centerPixel.y;

            // Convert pixel offsets to world coordinates (map plane is 1x1 units)
            // 256 pixels = 1 unit, so pixel offset / 256 gives world units
            // Invert X coordinate to match Unity's coordinate system
            float worldOffsetX = -pixelOffsetX / 256f;
            float worldOffsetY = pixelOffsetY / 256f;

            // Debug logging to verify coordinate conversion
            if (Time.frameCount % 300 == 0) // Log every 5 seconds
            {
                Debug.Log($"User Marker Position Debug:");
                Debug.Log($"  GPS: ({userLat:F7}, {userLng:F7})");
                Debug.Log($"  Map Center: ({mapCenterLat:F7}, {mapCenterLng:F7})");
                Debug.Log($"  Pixel Offset: ({pixelOffsetX:F2}, {pixelOffsetY:F2})");
                Debug.Log($"  World Offset: ({worldOffsetX:F3}, {worldOffsetY:F3})");
                Debug.Log($"  Distance from center: {Mathf.Sqrt(worldOffsetX * worldOffsetX + worldOffsetY * worldOffsetY):F3} units");
            }

            // Check if user is within the visible map area (within ~0.5 units of center)
            bool withinMapBounds = Mathf.Abs(worldOffsetX) <= 0.5f && Mathf.Abs(worldOffsetY) <= 0.5f;

            if (!withinMapBounds)
            {
                // User is outside map view, hide marker
                userMarkerInstance.SetActive(false);
                Debug.LogWarning($"User marker hidden - outside map bounds at ({worldOffsetX:F3}, {worldOffsetY:F3})");
                return;
            }

            userMarkerInstance.SetActive(true);

            // Position on mini-map plane (matching Mapbox coordinate system)
            userMarkerInstance.transform.localPosition = new Vector3(worldOffsetX, 2.190881f, worldOffsetY);

            // Update heading indicator rotation based on compass
            if (userHeadingIndicatorInstance != null)
            {
                float compassHeading = Input.compass.trueHeading;

                // Debug logging for heading calibration
                if (Time.frameCount % 300 == 0) // Log every 5 seconds
                {
                    Debug.Log($"Heading Indicator Debug: Compass={compassHeading:F1}°, Offset={headingIndicatorOffset:F1}°, Final Rotation={compassHeading + headingIndicatorOffset:F1}°");
                }

                // Rotate the heading indicator to show direction
                // Apply calibration offset for device-specific adjustments
                float finalHeading = compassHeading + headingIndicatorOffset;
                userHeadingIndicatorInstance.transform.localRotation = Quaternion.Euler(0f, finalHeading, 0f);
            }
        }
        else
        {
            // No GPS available, hide marker and heading indicator
            userMarkerInstance.SetActive(false);
            if (userHeadingIndicatorInstance != null)
            {
                userHeadingIndicatorInstance.SetActive(false);
            }
        }
    }


    private void OnEnable()
    {
        // Start location service if not already started
        StartLocationService();
    }

    private void StartLocationService()
    {
        Debug.Log("Starting location service...");

        // Check if location service is enabled on device
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogError("Location service is not enabled on device");
            return;
        }

        // Start location service with high accuracy
        Input.location.Start(0.5f, 0.5f); // Desired accuracy: 0.5 meter, update distance: 0.5 meter

        // Wait for initialization
        int maxWait = 20; // Wait up to 20 seconds for initialization
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            Debug.Log("Waiting for GPS initialization...");
            System.Threading.Thread.Sleep(1000); // Wait 1 second
            maxWait--;
        }

        if (Input.location.status == LocationServiceStatus.Failed)
        {
            Debug.LogError("Location service failed to initialize");
        }
        else if (Input.location.status == LocationServiceStatus.Running)
        {
            Debug.Log("Location service initialized successfully");
        }
        else
        {
            Debug.LogWarning("Location service status: " + Input.location.status);
        }
    }

    private void OnDisable()
    {
        // Stop location service
        Input.location.Stop();
        StopUpdatingCoordinates();
    }

    private IEnumerator RecalibrateCompassImmediately()
    {
        // Immediately recalibrate compass when AR opens
        Input.compass.enabled = false;
        yield return new WaitForSeconds(0.1f);
        Input.compass.enabled = true;
        Debug.Log("Compass recalibrated immediately on AR open");

        // Wait a moment for compass to stabilize
        yield return new WaitForSeconds(1.0f);
        Debug.Log("Compass stabilization complete");
    }

    private IEnumerator RecalibrateCompass()
    {
        while (true)
        {
            // Recalibrate compass every 5 minutes to prevent drift
            yield return new WaitForSeconds(300f);
            Input.compass.enabled = false;
            yield return new WaitForSeconds(0.1f);
            Input.compass.enabled = true;
            Debug.Log("Compass recalibrated");
        }
    }




    private float CalculateDistance(float poiLat, float poiLng)
    {
        if (Input.location.status != LocationServiceStatus.Running) return 0f;

        float userLat = Input.location.lastData.latitude;
        float userLng = Input.location.lastData.longitude;
        
        return CalculateDistanceBetweenCoordinates(userLat, userLng, poiLat, poiLng);
    }
    
    /// <summary>
    /// Calculate distance between any two coordinate pairs using Haversine formula
    /// </summary>
    private float CalculateDistanceBetweenCoordinates(float lat1, float lng1, float lat2, float lng2)
    {
        // Haversine formula for distance calculation
        float dLat = (lat2 - lat1) * Mathf.Deg2Rad;
        float dLng = (lng2 - lng1) * Mathf.Deg2Rad;

        float a = Mathf.Sin(dLat / 2) * Mathf.Sin(dLat / 2) +
                  Mathf.Cos(lat1 * Mathf.Deg2Rad) * Mathf.Cos(lat2 * Mathf.Deg2Rad) *
                  Mathf.Sin(dLng / 2) * Mathf.Sin(dLng / 2);

        float c = 2 * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1 - a));
        float distance = 6371000 * c; // Earth radius in meters

        return distance;
    }

    private Vector3 CalculateDirection(float poiLat, float poiLng)
    {
        if (Input.location.status != LocationServiceStatus.Running) return Vector3.forward;

        float userLat = Input.location.lastData.latitude;
        float userLng = Input.location.lastData.longitude;

        // Calculate bearing (direction)
        float dLng = (poiLng - userLng) * Mathf.Deg2Rad;

        float x = Mathf.Sin(dLng) * Mathf.Cos(poiLat * Mathf.Deg2Rad);
        float y = Mathf.Cos(userLat * Mathf.Deg2Rad) * Mathf.Sin(poiLat * Mathf.Deg2Rad) -
                  Mathf.Sin(userLat * Mathf.Deg2Rad) * Mathf.Cos(poiLat * Mathf.Deg2Rad) * Mathf.Cos(dLng);

        float bearing = Mathf.Atan2(x, y);

        // Convert bearing to direction vector
        return new Vector3(Mathf.Sin(bearing), 0f, Mathf.Cos(bearing));
    }


    private void CheckArrivalAtPOI(float userLat, float userLng)
    {
        if (selectedPOI == null) return;

        float distanceToPOI = CalculateDistance(selectedPOI.lat, selectedPOI.lng);

        if (distanceToPOI <= ARRIVAL_DISTANCE_THRESHOLD)
        {
            // User has arrived at the POI - log arrival with POI details from cached data
            Debug.Log($"🎯 Arrived at POI: {selectedPOI.label} (ID: {selectedPOI.id}, Color: {selectedPOI.color})");
        }
    }

    /// <summary>
    /// Check if user is within play area boundaries and show warnings if near limits
    /// </summary>
    private void CheckPlayAreaBoundaries(float userLat, float userLng)
    {
        if (!enablePlayAreaLimits) return;

        // Calculate distance from play area center
        float distanceFromCenter = CalculateDistanceBetweenCoordinates(playAreaCenter.x, playAreaCenter.y, userLat, userLng);
        
        // Check if user is outside play area
        if (distanceFromCenter > playAreaRadiusMeters)
        {
            Debug.LogWarning($"🚫 USER OUTSIDE PLAY AREA! Distance from center: {distanceFromCenter:F1}m (Limit: {playAreaRadiusMeters}m)");
            
            // You could implement additional actions here like:
            // - Disable certain AR features
            // - Show popup warning
            // - Redirect user back to center
        }
        // Show warning if user is near boundary
        else if (showPlayAreaWarnings && distanceFromCenter > (playAreaRadiusMeters - playAreaWarningDistance))
        {
            float distanceToBoundary = playAreaRadiusMeters - distanceFromCenter;
            Debug.LogWarning($"⚠️ Near Play Area Boundary! Distance to boundary: {distanceToBoundary:F1}m");
            
            // You could implement additional actions here like:
            // - Show UI warning
            // - Vibrate device
            // - Play warning sound
        }
        else
        {
            // User is safely within play area
            if (Time.frameCount % 300 == 0) // Log every ~5 seconds at 60fps
            {
                Debug.Log($"✅ Within Play Area - Distance from center: {distanceFromCenter:F1}m");
            }
        }
    }

    private List<POI> GetNearbyPOIs()
    {
        List<POI> nearbyPOIs = new List<POI>();

        if (Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogWarning("GPS not available - cannot filter nearby POIs");
            return cachedPOIs; // Return all POIs if GPS is not available
        }

        foreach (POI poi in cachedPOIs)
        {
            if (poi != null)
            {
                float distance = CalculateDistance(poi.lat, poi.lng);
                if (distance <= maxPOIDisplayDistance)
                {
                    nearbyPOIs.Add(poi);
                    Debug.Log($"POI {poi.label} is within range: {distance:F1}m");
                }
            }
        }

        Debug.Log($"Found {nearbyPOIs.Count} nearby POIs out of {cachedPOIs.Count} total POIs");
        return nearbyPOIs;
    }

    // Get POIs that are currently visible on the mini map
    public List<POI> GetVisibleMapPOIs()
    {
        List<POI> visiblePOIs = new List<POI>();

        // Use current map center (smoothed GPS if available, otherwise fallback)
        float centerLat, centerLng;
        if (Input.location.status == LocationServiceStatus.Running &&
            Permission.HasUserAuthorizedPermission(Permission.FineLocation) &&
            Input.location.isEnabledByUser)
        {
            centerLat = smoothedLat;
            centerLng = smoothedLng;
        }
        else
        {
            // Fallback location
            centerLat = 8.360058053962094f;
            centerLng = 124.86811131469575f;
        }

        Debug.Log($"Getting visible map POIs with center: ({centerLat}, {centerLng}), zoom: {mapZoomLevel}");

        foreach (POI poi in cachedPOIs)
        {
            if (poi != null && IsPOIWithinMapView(poi, centerLat, centerLng, mapZoomLevel))
            {
                visiblePOIs.Add(poi);
                Debug.Log($"POI {poi.label} is visible on map");
            }
        }

        Debug.Log($"Found {visiblePOIs.Count} POIs visible on mini map out of {cachedPOIs.Count} total");
        return visiblePOIs;
    }




    private bool IsPOIWithinMapView(POI poi, float centerLat, float centerLng, float zoomLevel)
    {
        // Calculate the approximate view bounds based on zoom level
        // Higher zoom level = smaller area visible
        // Mapbox tile size is 256x256 pixels, each tile covers approximately 360/2^zoom degrees
        float degreesPerTile = 360f / Mathf.Pow(2f, zoomLevel);
        // For a 256x256 map, we see about half a tile in each direction
        float latRange = degreesPerTile * 0.25f; // Quarter tile height for more conservative bounds
        float lngRange = degreesPerTile * 0.25f; // Quarter tile width

        // Check if POI is within the map bounds
        bool withinLatBounds = poi.lat >= (centerLat - latRange) && poi.lat <= (centerLat + latRange);
        bool withinLngBounds = poi.lng >= (centerLng - lngRange) && poi.lng <= (centerLng + lngRange);

        Debug.Log($"POI {poi.label} at ({poi.lat}, {poi.lng}) - Center: ({centerLat}, {centerLng}) - Ranges: lat±{latRange}, lng±{lngRange} - Within bounds: {withinLatBounds && withinLngBounds}");

        return withinLatBounds && withinLngBounds;
    }


    private IEnumerator FetchPOIsFromSupabase()
    {
        // Supabase REST API endpoint for your table
        string url = $"{supabaseUrl}/rest/v1/{tableName}?select=*";

        Debug.Log($"Fetching POIs from: {url}");

        UnityWebRequest request = UnityWebRequest.Get(url);

        // Required headers
        request.SetRequestHeader("apikey", supabaseKey);
        request.SetRequestHeader("Authorization", $"Bearer {supabaseKey}");
        request.SetRequestHeader("Content-Type", "application/json");

        // Set timeout for mobile devices
        request.timeout = 10; // 10 seconds timeout

        Debug.Log($"ARControl Supabase Request Details:");
        Debug.Log($"  URL: {url}");
        Debug.Log($"  Method: {request.method}");
        Debug.Log($"  Headers: apikey={supabaseKey.Substring(0, 20)}..., Authorization=Bearer {supabaseKey.Substring(0, 20)}...");
        Debug.Log($"  Timeout: {request.timeout}s");

        // Send request
        yield return request.SendWebRequest();

        Debug.Log($"ARControl Supabase Response Details:");
        Debug.Log($"  Response Code: {request.responseCode}");
        Debug.Log($"  Result: {request.result}");
        Debug.Log($"  Error: {request.error}");
        if (request.downloadHandler != null && !string.IsNullOrEmpty(request.downloadHandler.text))
        {
            Debug.Log($"  Response Body: {request.downloadHandler.text}");
        }

        // Check for errors
        if (request.result == UnityWebRequest.Result.Success)
        {
            string json = request.downloadHandler.text;
            Debug.Log("✅ Data fetched successfully!");
            Debug.Log($"Raw JSON response: {json}");

            try
            {
                // Handle empty response
                if (string.IsNullOrEmpty(json) || json.Trim() == "[]")
                {
                    Debug.Log("Empty POI data received, using mock data");
                    CreateMockPOIsForTesting();
                    yield break;
                }

                // Parse JSON array directly
                POI[] pois = JsonUtility.FromJson<POIList>("{\"pois\":" + json + "}").pois;
                cachedPOIs = new List<POI>(pois);
                Debug.Log($"Successfully parsed {cachedPOIs.Count} POIs");

                // Filter active POIs based on dates
                cachedPOIs = FilterActivePOIs(cachedPOIs);

                // Log details of each POI for debugging
                foreach (POI poi in cachedPOIs)
                {
                    if (poi != null)
                    {
                        Debug.Log($"POI: {poi.label} at ({poi.lat}, {poi.lng}) color: {poi.color} mark_type: {poi.mark_type}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to parse POI data: {e.Message} - using mock data instead");
                CreateMockPOIsForTesting();
            }
        }
        else
        {
            string errorMsg = $"Failed to fetch POI data from server: {request.error}";
            if (request.downloadHandler != null && !string.IsNullOrEmpty(request.downloadHandler.text))
            {
                errorMsg += $" Server response: {request.downloadHandler.text}";
            }
            Debug.LogWarning(errorMsg);
            Debug.Log("Using mock POI data for testing - app will continue to work normally");
            CreateMockPOIsForTesting();
        }
    }

    private List<POI> FilterActivePOIs(List<POI> pois)
    {
        DateTime currentDate = DateTime.Now.Date; // Get current date without time
        Debug.Log($"Current date for filtering: {currentDate.ToString("MM-dd-yyyy")}");

        return pois.Where(poi =>
        {
            // Include permanent POIs (null or empty dates)
            if (poi.dates == null || poi.dates.Length == 0)
            {
                Debug.Log($"Including permanent POI: {poi.label}");
                return true;
            }

            // Check if any of the POI's dates are not outdated (current or future dates)
            foreach (string dateStr in poi.dates)
            {
                Debug.Log($"Checking date '{dateStr}' for POI: {poi.label}");
                if (DateTime.TryParseExact(dateStr, "MM-dd-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime poiDate))
                {
                    Debug.Log($"Parsed date: {poiDate.ToString("MM-dd-yyyy")}, Current date: {currentDate.ToString("MM-dd-yyyy")}, Is future/current: {poiDate.Date >= currentDate}");
                    if (poiDate.Date >= currentDate)
                    {
                        Debug.Log($"Including dated POI: {poi.label} (has valid date: {dateStr})");
                        return true; // Include POI if it has at least one non-outdated date
                    }
                }
                else
                {
                    Debug.LogWarning($"Failed to parse date '{dateStr}' for POI: {poi.label}");
                }
            }

            Debug.Log($"Excluding outdated POI: {poi.label}");
            return false; // Exclude POI if all dates are outdated
        }).ToList();
    }


}