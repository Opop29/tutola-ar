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
    public SupabaseClient supabaseClient; // Assign SupabaseClient in Inspector
    public POI3D poi3DHandler; // Assign POI3D script for 3D POI management
    // public GeospatialController geospatialController; // Assign GeospatialController for Earth Anchors (requires ARCore Extensions)
    public SinglePOI singlePOI; // Assign SinglePOI script for single POI navigation

    [Header("Map Settings")]
    public string mapboxToken = "pk.eyJ1Ijoib3BvcDI5IiwiYSI6ImNtZm8za3Q1NjAxcTEyanF4ZjZraWowdjEifQ.jNxrXsiX7Davmhjmp4ihWw"; // Mapbox token

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

    private bool isUpdatingCoordinates = false;
    private GameObject miniMapInstance;
    private Texture2D miniMapTexture;
    private bool isUpdatingMap = false;
    private float lastMapUpdateTime = 0f;
    private float lastARUpdateTime = 0f;
    private float lastCoordinateUpdateTime = 0f;
    public List<POI> cachedPOIs = new List<POI>();
    private List<GameObject> arMarkerInstances = new List<GameObject>();
    private const float ARRIVAL_DISTANCE_THRESHOLD = 10f; // Distance in meters to consider arrived at POI
    private Quaternion targetMiniMapRotation; // For smooth rotation
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

        // Start compass recalibration routine
        StartCoroutine(RecalibrateCompass());

        // Start location service with proper initialization
        StartLocationService();

        // Cache POIs from Supabase directly
        StartCoroutine(FetchPOIsFromSupabase());

        // if (geospatialController == null)
        // {
        //     geospatialController = FindObjectOfType<GeospatialController>();
        // }

        if (singlePOI == null)
        {
            singlePOI = FindFirstObjectByType<SinglePOI>();
        }
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
        mockPOI1.lat = 37.7749f + 0.0001f; // Very close north
        mockPOI1.lng = -122.4194f;
        mockPOI1.color = "#FF0000"; // Red
        mockPOI1.mark_type = "marker";
        cachedPOIs.Add(mockPOI1);

        POI mockPOI2 = new POI();
        mockPOI2.label = "Test POI 2";
        mockPOI2.lat = 37.7749f;
        mockPOI2.lng = -122.4194f + 0.0001f; // Very close east
        mockPOI2.color = "#00FF00"; // Green
        mockPOI2.mark_type = "marker";
        cachedPOIs.Add(mockPOI2);

        POI mockPOI3 = new POI();
        mockPOI3.label = "Test POI 3";
        mockPOI3.lat = 37.7749f - 0.0001f; // Very close south
        mockPOI3.lng = -122.4194f;
        mockPOI3.color = "#0000FF"; // Blue
        mockPOI3.mark_type = "marker";
        cachedPOIs.Add(mockPOI3);

        Debug.Log($"Created {cachedPOIs.Count} mock POIs for testing");
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

        // Set single POI mode
        if (poi3DHandler != null)
        {
            poi3DHandler.singlePOIMode = true;
            poi3DHandler.selectedPOI = selectedPOI;
            Debug.Log("Single POI mode enabled on POI3D");
        }
        else
        {
            Debug.LogError("POI3D handler not assigned in ARControl!");
        }

        // Start single POI navigation
        if (singlePOI != null)
        {
            singlePOI.StartSinglePOINavigation(selectedPOI);
            Debug.Log("Single POI navigation started");
        }
        else
        {
            Debug.LogError("SinglePOI component not assigned in ARControl!");
        }

        OpenAR(); // Open AR mode with navigation to selected POI
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
        Debug.Log($"Set selected POI to first in group: {selectedPOI.label}");

        // Set single POI mode to false for group navigation
        if (poi3DHandler != null)
        {
            poi3DHandler.singlePOIMode = false;
            poi3DHandler.selectedPOI = null; // Clear single POI selection
            Debug.Log("Disabled single POI mode for group navigation");
        }

        // Start group navigation - pass all POIs in the group to POI3D
        if (poi3DHandler != null)
        {
            poi3DHandler.StartGroupNavigation(groupName, groupPOIs);
            Debug.Log($"Started group navigation for '{groupName}' with {groupPOIs.Count} POIs");
        }

        // Start single POI navigation for the first POI (for compatibility)
        if (singlePOI != null)
        {
            singlePOI.StartSinglePOINavigation(selectedPOI);
            Debug.Log("Started single POI navigation for first POI in group");
        }

        OpenAR(); // Open AR mode with group navigation
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

        // Destroy mini-map
        if (miniMapInstance != null)
        {
            Destroy(miniMapInstance);
            miniMapInstance = null;
        }

        // Deactivate 3D POI display
        if (poi3DHandler != null)
        {
            poi3DHandler.Deactivate3DPOIs();
            poi3DHandler.singlePOIMode = false; // Reset single POI mode
        }

        // Stop single POI navigation
        if (singlePOI != null)
        {
            singlePOI.StopSinglePOINavigation();
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
        // Update mini-map orientation based on device bearing (Map Bearing Mode)
        if (miniMapInstance != null)
        {
            // Use device heading (bearing) for map rotation - direction of travel points up
            float deviceBearing = Input.compass.trueHeading;

            // Set target rotation for bearing-based map orientation
            targetMiniMapRotation = Quaternion.Euler(0f, -deviceBearing, 0f);

            // Smoothly interpolate to target rotation
            miniMapInstance.transform.localRotation = Quaternion.Slerp(
                miniMapInstance.transform.localRotation,
                targetMiniMapRotation,
                Time.deltaTime * 5f // Smooth interpolation speed
            );

            // Debug: Log map bearing mode (only occasionally to avoid spam)
            if (Time.frameCount % 300 == 0) // Every ~5 seconds at 60fps
            {
                Debug.Log($"Mini-map bearing mode - Device heading: {deviceBearing:F1}°, Map rotation: {miniMapInstance.transform.localRotation.eulerAngles.y:F1}°");
            }
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

            // Update POI origin GPS for static positioning as user moves
            if (poi3DHandler != null)
            {
                poi3DHandler.UpdateOriginGPS(smoothedLat, smoothedLng);
            }

            arCoordinateText.text = $"{smoothedLat:F7}, {smoothedLng:F7}";

            // Check if arrived at selected POI using smoothed coordinates
            CheckArrivalAtPOI(smoothedLat, smoothedLng);

            // Update map less frequently (every 8 seconds for better performance)
            if (!isUpdatingMap && Time.time - lastMapUpdateTime > 8f)
            {
                lastMapUpdateTime = Time.time;
                StartCoroutine(LoadMiniMapTexture());
            }

            // AR markers removed - no longer updating AR markers
        }
        else if (Input.location.status == LocationServiceStatus.Initializing)
        {
            arCoordinateText.text = "Initializing GPS...";
        }
        else if (Input.location.status == LocationServiceStatus.Failed)
        {
            arCoordinateText.text = "GPS Failed - Check Permissions";
        }
        else
        {
            arCoordinateText.text = "GPS Not Available";
        }
    }

    private IEnumerator LoadMiniMapTexture()
    {
        isUpdatingMap = true;

        float latitude, longitude;

        // Use smoothed GPS location if available, otherwise use a default location
        if (Input.location.status == LocationServiceStatus.Running &&
            Permission.HasUserAuthorizedPermission(Permission.FineLocation) &&
            Input.location.isEnabledByUser)
        {
            latitude = smoothedLat;
            longitude = smoothedLng;
            Debug.Log($"Using smoothed GPS location: ({latitude}, {longitude})");
        }
        else
        {
            // Default location (fallback when GPS is not available) - San Francisco for testing
            latitude = 37.7749f;
            longitude = -122.4194f;
            Debug.Log($"Using default test location: ({latitude}, {longitude}) - GPS status: {Input.location.status}");
        }

        // Build markers string for user location and POIs
        string markers = $"pin-l-circle+ffff00({longitude},{latitude})"; // User marker (yellow circle to represent user, will be rotated)

        // Add two blue markers near user marker when GPS is not available
        if (Input.location.status != LocationServiceStatus.Running ||
            !Permission.HasUserAuthorizedPermission(Permission.FineLocation) ||
            !Input.location.isEnabledByUser)
        {
            markers += $",pin-s-marker+0000ff({longitude + 0.001f},{latitude})"; // Blue marker to the east
            markers += $",pin-s-marker+0000ff({longitude},{latitude + 0.001f})"; // Blue marker to the north
        }

        Debug.Log($"Building map markers. User location: ({latitude}, {longitude}), cached POIs: {cachedPOIs.Count}");

        // Add POI markers - only selected POI in single POI mode, all POIs otherwise
        int poiCount = 0;
        foreach (POI poi in cachedPOIs)
        {
            if (poi != null && !string.IsNullOrEmpty(poi.color))
            {
                // In single POI mode, only show the selected POI
                if (poi3DHandler != null && poi3DHandler.singlePOIMode && poi != selectedPOI) continue;

                Debug.Log($"Processing POI for map: {poi.label} at ({poi.lat}, {poi.lng}) color: {poi.color}");

                // Clean color string (remove # if present)
                string colorCode = poi.color.Replace("#", "");

                // Highlight selected POI for navigation with different color
                string markerColor = (selectedPOI != null && poi == selectedPOI) ? "0000ff" : colorCode; // Blue for navigation target

                // Convert lat/lng to the correct order for Mapbox (lng,lat)
                markers += $",pin-s-marker+{markerColor}({poi.lng},{poi.lat})";

                poiCount++;
                Debug.Log($"Added POI marker {poiCount} to map: {poi.label} with color {markerColor} at ({poi.lng},{poi.lat})");
            }
            else
            {
                Debug.LogWarning($"Skipping invalid POI: {poi?.label ?? "null"} - color: {poi?.color ?? "null"}");
            }
        }

        Debug.Log($"Final markers string: {markers}");

        // Add navigation path line if we have a selected POI and GPS is available
        if (selectedPOI != null && Input.location.status == LocationServiceStatus.Running &&
            Permission.HasUserAuthorizedPermission(Permission.FineLocation) &&
            Input.location.isEnabledByUser)
        {
            // Create a path line from user to POI (simplified as a series of points)
            markers += $",path-5+0000ff-0.5({longitude},{latitude};{selectedPOI.lng},{selectedPOI.lat})";
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
            markers += $",path-10+00ff00({longitude},{latitude};{leftLng},{leftLat}),path-10+00ff00({longitude},{latitude};{rightLng},{rightLat})";
        }

        string mapUrl = $"https://api.mapbox.com/styles/v1/mapbox/streets-v11/static/{markers}/{longitude},{latitude},{mapZoomLevel},0,0/256x256@2x?access_token={mapboxToken}";

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

        // Haversine formula for distance calculation
        float dLat = (poiLat - userLat) * Mathf.Deg2Rad;
        float dLng = (poiLng - userLng) * Mathf.Deg2Rad;

        float a = Mathf.Sin(dLat / 2) * Mathf.Sin(dLat / 2) +
                  Mathf.Cos(userLat * Mathf.Deg2Rad) * Mathf.Cos(poiLat * Mathf.Deg2Rad) *
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
            centerLat = 37.7749f;
            centerLng = -122.4194f;
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

        // Send request
        yield return request.SendWebRequest();

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
                Debug.LogError($"JSON parsing error: {e.Message}");
                CreateMockPOIsForTesting();
            }
        }
        else
        {
            string errorMsg = $"❌ Error fetching data: {request.error}";
            if (request.downloadHandler != null && !string.IsNullOrEmpty(request.downloadHandler.text))
            {
                errorMsg += $" Response: {request.downloadHandler.text}";
            }
            Debug.LogError(errorMsg);
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
