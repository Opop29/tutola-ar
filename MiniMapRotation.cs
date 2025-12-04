using UnityEngine;

public class MiniMapRotation : MonoBehaviour
{
    [Header("Mini Map References")]
    [Tooltip("Reference to the mini map GameObject instance (will be created by ARControl)")]
    public GameObject miniMapInstance;
    
    [Header("Rotation Settings")]
    [Tooltip("Smooth rotation speed multiplier")]
    public float rotationSpeed = 3f;

    [Tooltip("Enable/disable compass-based rotation")]
    public bool useCompassRotation = true;

    [Tooltip("Enable/disable gyroscope fusion for stability")]
    public bool useGyroscopeFusion = true;

    [Tooltip("Gyroscope influence in fusion (0-1, higher = more gyro)")]
    public float gyroscopeFusionWeight = 0.3f;

    [Tooltip("Maximum allowed heading change per second (degrees)")]
    public float maxHeadingChangeRate = 90f;

    [Tooltip("Enable/disable smooth interpolation")]
    public bool smoothRotation = true;

    [Header("Advanced Heading Settings")]
    [Tooltip("Enable Kalman-like filtering for sensor fusion")]
    public bool useAdvancedFiltering = true;

    [Tooltip("Process noise for Kalman filter (higher = more responsive but noisier)")]
    public float processNoise = 0.1f;

    [Tooltip("Measurement noise for Kalman filter (higher = less responsive but smoother)")]
    public float measurementNoise = 1.0f;

    [Tooltip("Enable GPS course integration when moving")]
    public bool useGPSCourse = true;

    [Tooltip("Minimum speed for GPS course to be reliable (m/s)")]
    public float minGPSSpeed = 0.5f;

    [Tooltip("Automatic recalibration interval (seconds, 0 = disabled)")]
    public float autoRecalibrationInterval = 300f; // 5 minutes

    [Tooltip("Sensor quality threshold (lower = more strict)")]
    public float sensorQualityThreshold = 0.7f;

    [Header("Initial Heading Setup")]
    [Tooltip("Capture initial heading when AR starts (map faces user's current direction)")]
    public bool useInitialHeadingCapture = true;

    [Tooltip("Time to wait for compass stabilization before capturing initial heading (seconds)")]
    public float initialHeadingWarmupTime = 2.0f;

    [Tooltip("Minimum sensor quality required for initial heading capture")]
    public float initialHeadingQualityThreshold = 0.8f;

    [Tooltip("Debug logging frequency (0 = disabled)")]
    public float debugLogInterval = 5f; // Log every 5 seconds

    [Header("Fallback Settings")]
    [Tooltip("Use gyroscope when compass is unavailable")]
    public bool useGyroscopeFallback = false;

    [Tooltip("Fallback rotation speed when using gyroscope")]
    public float gyroscopeRotationSpeed = 2f;
    
    // Private variables
    private Quaternion targetMiniMapRotation;
    private float lastDebugLogTime = 0f;
    private bool isInitialized = false;

    // Compass and gyroscope references
    private float compassHeading;
    private float gyroscopeHeading;
    private float fusedHeading;
    private float previousFusedHeading;
    private float lastUpdateTime;

    // Advanced filtering variables
    private float kalmanEstimate; // Current heading estimate
    private float kalmanError; // Estimate uncertainty
    private float lastRecalibrationTime;
    private float sensorQualityScore;
    private Vector3 lastGPSPosition;
    private float lastGPSTime;
    private float gpsCourseHeading;
    private bool hasValidGPSCourse;

    // Heading history for outlier detection
    private float[] headingHistory;
    private int historyIndex;
    private const int HISTORY_SIZE = 10;

    // Initial heading capture
    private bool hasCapturedInitialHeading;
    private float initialHeadingOffset;
    private float initialHeadingCaptureStartTime;
    private bool isCapturingInitialHeading;
    private float adjustedHeading; // For debug logging
    
    private void Start()
    {
        InitializeCompass();
        InitializeGyroscope();

        // Initialize heading variables
        fusedHeading = 0f;
        previousFusedHeading = 0f;
        lastUpdateTime = Time.time;

        // Initialize advanced filtering
        kalmanEstimate = 0f;
        kalmanError = 1.0f; // Initial uncertainty
        lastRecalibrationTime = Time.time;
        sensorQualityScore = 1.0f;

        // Initialize GPS tracking
        lastGPSPosition = Vector3.zero;
        lastGPSTime = Time.time;
        gpsCourseHeading = 0f;
        hasValidGPSCourse = false;

        // Initialize heading history
        headingHistory = new float[HISTORY_SIZE];
        for (int i = 0; i < HISTORY_SIZE; i++) {
            headingHistory[i] = 0f;
        }
        historyIndex = 0;

        // Initialize initial heading capture
        hasCapturedInitialHeading = false;
        initialHeadingOffset = 0f;
        initialHeadingCaptureStartTime = 0f;
        isCapturingInitialHeading = false;
        adjustedHeading = 0f;

        isInitialized = true;

        Debug.Log("MiniMapRotation: Script initialized successfully");
    }
    
    /// <summary>
    /// Initialize compass for heading detection
    /// </summary>
    private void InitializeCompass()
    {
        if (useCompassRotation)
        {
            try
            {
                // Enable compass and check if it works
                Input.compass.enabled = true;

                // Test if compass provides data
                float testHeading = Input.compass.trueHeading;
                if (!float.IsNaN(testHeading))
                {
                    compassHeading = testHeading;
                    Debug.Log($"MiniMapRotation: Compass initialized. Initial heading: {compassHeading:F1}°");
                }
                else
                {
                    Debug.LogWarning("MiniMapRotation: Compass available but no valid data");
                    useCompassRotation = false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"MiniMapRotation: Compass not supported - {e.Message}");
                useCompassRotation = false;
            }
        }
    }
    
    /// <summary>
    /// Initialize gyroscope for fusion or fallback rotation
    /// </summary>
    private void InitializeGyroscope()
    {
        if (useGyroscopeFusion || useGyroscopeFallback)
        {
            try
            {
                Input.gyro.enabled = true;
                // Test if gyroscope provides data
                if (Input.gyro.attitude != Quaternion.identity)
                {
                    Debug.Log("MiniMapRotation: Gyroscope initialized for fusion/fallback");
                }
                else
                {
                    Debug.LogWarning("MiniMapRotation: Gyroscope available but no valid data");
                    useGyroscopeFusion = false;
                    useGyroscopeFallback = false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"MiniMapRotation: Gyroscope not supported - {e.Message}");
                useGyroscopeFusion = false;
                useGyroscopeFallback = false;
            }
        }
    }
    
    private void Update()
    {
        // Update mini map rotation if instance exists
        if (miniMapInstance != null)
        {
            UpdateMiniMapRotation();
        }

        // Handle automatic recalibration
        if (autoRecalibrationInterval > 0 && Time.time - lastRecalibrationTime > autoRecalibrationInterval)
        {
            PerformAutomaticRecalibration();
            lastRecalibrationTime = Time.time;
        }

        // Debug logging
        if (debugLogInterval > 0 && Time.time - lastDebugLogTime > debugLogInterval)
        {
            LogDebugInfo();
            lastDebugLogTime = Time.time;
        }
    }

    /// <summary>
    /// Kalman-like filter for heading estimation
    /// </summary>
    private float ApplyKalmanFilter(float measurement)
    {
        // Prediction step
        float predictedEstimate = kalmanEstimate;
        float predictedError = kalmanError + processNoise;

        // Update step
        float kalmanGain = predictedError / (predictedError + measurementNoise);
        kalmanEstimate = predictedEstimate + kalmanGain * Mathf.DeltaAngle(predictedEstimate, measurement);
        kalmanError = (1 - kalmanGain) * predictedError;

        return kalmanEstimate;
    }

    /// <summary>
    /// Calculate GPS course heading when moving
    /// </summary>
    private void UpdateGPSCourse()
    {
        if (!useGPSCourse || !Input.location.isEnabledByUser || Input.location.status != LocationServiceStatus.Running)
        {
            hasValidGPSCourse = false;
            return;
        }

        Vector3 currentPosition = new Vector3(
            Input.location.lastData.latitude,
            0,
            Input.location.lastData.longitude
        );

        float currentTime = Time.time;
        float timeDelta = currentTime - lastGPSTime;

        if (timeDelta > 0.5f && lastGPSPosition != Vector3.zero) // At least 0.5 seconds between updates
        {
            Vector3 displacement = currentPosition - lastGPSPosition;
            float distance = displacement.magnitude;

            // Convert lat/lng displacement to approximate meters (rough approximation)
            // 1 degree latitude ≈ 111km, 1 degree longitude ≈ 111km * cos(lat)
            float latDistance = displacement.x * 111000f; // meters
            float lngDistance = displacement.z * 111000f * Mathf.Cos(currentPosition.x * Mathf.Deg2Rad);

            Vector3 worldDisplacement = new Vector3(lngDistance, 0, latDistance);
            float speed = worldDisplacement.magnitude / timeDelta;

            if (speed >= minGPSSpeed)
            {
                gpsCourseHeading = Mathf.Atan2(worldDisplacement.x, worldDisplacement.z) * Mathf.Rad2Deg;
                gpsCourseHeading = (gpsCourseHeading + 360f) % 360f; // Normalize to 0-360
                hasValidGPSCourse = true;
                Debug.Log($"GPS Course: {gpsCourseHeading:F1}° at speed {speed:F2} m/s");
            }
            else
            {
                hasValidGPSCourse = false;
            }
        }

        lastGPSPosition = currentPosition;
        lastGPSTime = currentTime;
    }

    /// <summary>
    /// Monitor sensor quality and reliability
    /// </summary>
    private float CalculateSensorQuality()
    {
        float quality = 1.0f;

        // Compass quality based on consistency
        if (useCompassRotation && Input.compass.enabled)
        {
            float currentHeading = Input.compass.trueHeading;
            if (!float.IsNaN(currentHeading))
            {
                // Add to history for consistency check
                headingHistory[historyIndex] = currentHeading;
                historyIndex = (historyIndex + 1) % HISTORY_SIZE;

                // Calculate variance in recent readings
                float sum = 0f;
                float sumSquares = 0f;
                int validCount = 0;

                for (int i = 0; i < HISTORY_SIZE; i++)
                {
                    if (headingHistory[i] != 0f)
                    {
                        sum += headingHistory[i];
                        sumSquares += headingHistory[i] * headingHistory[i];
                        validCount++;
                    }
                }

                if (validCount > 1)
                {
                    float mean = sum / validCount;
                    float variance = (sumSquares / validCount) - (mean * mean);
                    float stdDev = Mathf.Sqrt(variance);

                    // Lower quality if high variance (inconsistent readings)
                    quality *= Mathf.Clamp01(1.0f - (stdDev / 45f)); // 45° std dev = 0 quality
                }
            }
            else
            {
                quality *= 0.5f; // Penalize invalid readings
            }
        }

        // Gyro quality (simpler - just check if available)
        if (useGyroscopeFusion && Input.gyro.enabled)
        {
            if (Input.gyro.attitude == Quaternion.identity)
            {
                quality *= 0.8f; // Slight penalty for no gyro data
            }
        }

        sensorQualityScore = quality;
        return quality;
    }

    /// <summary>
    /// Perform automatic recalibration of sensors
    /// </summary>
    private void PerformAutomaticRecalibration()
    {
        Debug.Log("MiniMapRotation: Performing automatic recalibration");

        // Reset Kalman filter
        kalmanEstimate = fusedHeading;
        kalmanError = 1.0f;

        // Recalibrate compass
        if (useCompassRotation)
        {
            Input.compass.enabled = false;
            // Small delay
            StartCoroutine(DelayedCompassRecalibration());
        }

        // Reset GPS tracking
        lastGPSPosition = Vector3.zero;
        hasValidGPSCourse = false;

        Debug.Log("MiniMapRotation: Automatic recalibration completed");
    }

    private System.Collections.IEnumerator DelayedCompassRecalibration()
    {
        yield return new WaitForSeconds(0.1f);
        Input.compass.enabled = true;
    }

    /// <summary>
    /// Detect and reject heading outliers
    /// </summary>
    private bool IsHeadingOutlier(float heading)
    {
        if (historyIndex < 3) return false; // Need some history

        float sum = 0f;
        int count = 0;

        for (int i = 0; i < HISTORY_SIZE; i++)
        {
            if (headingHistory[i] != 0f)
            {
                sum += headingHistory[i];
                count++;
            }
        }

        if (count == 0) return false;

        float mean = sum / count;
        float deviation = Mathf.DeltaAngle(mean, heading);

        // Consider outlier if deviation > 60 degrees from mean
        return Mathf.Abs(deviation) > 60f;
    }

    /// <summary>
    /// Update the mini map rotation based on device heading with advanced determination methods
    /// </summary>
    private void UpdateMiniMapRotation()
    {
        float currentTime = Time.time;
        float deltaTime = currentTime - lastUpdateTime;
        lastUpdateTime = currentTime;

        // Update GPS course for potential integration
        UpdateGPSCourse();

        // Monitor sensor quality
        float currentSensorQuality = CalculateSensorQuality();

        float rawHeading = 0f;
        bool headingValid = false;

        // Get compass heading
        if (useCompassRotation && Input.compass.enabled)
        {
            try
            {
                float compassValue = Input.compass.trueHeading;
                if (!float.IsNaN(compassValue) && compassValue >= 0f && compassValue <= 360f)
                {
                    compassHeading = compassValue;
                    rawHeading = compassValue;
                    headingValid = true;
                }
                else
                {
                    throw new System.Exception("Invalid compass data");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"MiniMapRotation: Compass error - {e.Message}");
                useCompassRotation = false;
            }
        }

        // Get gyroscope heading for fusion
        float gyroHeading = 0f;
        if (useGyroscopeFusion && Input.gyro.enabled)
        {
            gyroHeading = GetGyroscopeHeading();
        }

        // Multi-source heading determination
        float determinedHeading = 0f;

        if (headingValid)
        {
            // Start with compass-gyro fusion
            if (useGyroscopeFusion && Input.gyro.enabled)
            {
                determinedHeading = Mathf.LerpAngle(gyroHeading, compassHeading, 1f - gyroscopeFusionWeight);
            }
            else
            {
                determinedHeading = compassHeading;
            }

            // Integrate GPS course if available and sensor quality is good
            if (useGPSCourse && hasValidGPSCourse && currentSensorQuality > sensorQualityThreshold)
            {
                // Weight GPS course based on sensor quality
                float gpsWeight = Mathf.Clamp01((currentSensorQuality - sensorQualityThreshold) / (1f - sensorQualityThreshold));
                determinedHeading = Mathf.LerpAngle(determinedHeading, gpsCourseHeading, gpsWeight * 0.3f);
                Debug.Log($"Integrated GPS course: {gpsCourseHeading:F1}° with weight {gpsWeight:F2}");
            }
        }
        else if (useGyroscopeFallback && Input.gyro.enabled)
        {
            // Use gyroscope as fallback
            determinedHeading = gyroHeading;
            headingValid = true;

            if (useCompassRotation) // Only log if compass was expected but not available
            {
                Debug.Log($"Compass unavailable, using gyroscope fallback: {determinedHeading:F1}°");
            }
        }

        if (!headingValid)
        {
            // No rotation source available
            Debug.LogWarning("MiniMapRotation: No rotation source available (compass/gyroscope)");
            return;
        }

        // Outlier detection
        if (IsHeadingOutlier(determinedHeading))
        {
            Debug.LogWarning($"Rejected outlier heading: {determinedHeading:F1}°");
            return; // Skip this update
        }

        // Apply Kalman filtering if enabled
        if (useAdvancedFiltering)
        {
            fusedHeading = ApplyKalmanFilter(determinedHeading);
        }
        else
        {
            fusedHeading = determinedHeading;
        }

        // Validate heading change to prevent jumps
        float headingChange = Mathf.DeltaAngle(previousFusedHeading, fusedHeading);
        float maxChange = maxHeadingChangeRate * deltaTime;

        if (Mathf.Abs(headingChange) > maxChange)
        {
            // Limit the change to prevent jumping
            fusedHeading = previousFusedHeading + Mathf.Clamp(headingChange, -maxChange, maxChange);
            Debug.Log($"Limited heading change from {headingChange:F1}° to {Mathf.Clamp(headingChange, -maxChange, maxChange):F1}°");
        }

        previousFusedHeading = fusedHeading;

        // Check for initial heading capture
        CheckInitialHeadingCapture(fusedHeading, currentSensorQuality);

        // Apply initial heading offset if captured
        adjustedHeading = fusedHeading;
        if (hasCapturedInitialHeading)
        {
            // The initial heading becomes the new "north" for the map
            // So we adjust all subsequent headings relative to the initial heading
            adjustedHeading = Mathf.DeltaAngle(initialHeadingOffset, fusedHeading);
        }

        // Calculate target rotation
        // Note: Negative heading rotates the map opposite to device rotation for proper navigation alignment
        targetMiniMapRotation = Quaternion.Euler(0f, -adjustedHeading, 0f);

        // Apply rotation with or without smoothing
        if (smoothRotation)
        {
            miniMapInstance.transform.localRotation = Quaternion.Slerp(
                miniMapInstance.transform.localRotation,
                targetMiniMapRotation,
                Time.deltaTime * rotationSpeed
            );
        }
        else
        {
            miniMapInstance.transform.localRotation = targetMiniMapRotation;
        }
    }
    
    /// <summary>
    /// Calculate heading from gyroscope data
    /// </summary>
    private float GetGyroscopeHeading()
    {
        if (!Input.gyro.enabled)
            return 0f;
            
        // Get device rotation from gyroscope attitude
        Quaternion gyroRotation = Input.gyro.attitude;
        
        // Convert to Euler angles and extract heading
        Vector3 euler = gyroRotation.eulerAngles;
        gyroscopeHeading = euler.y;
        
        return gyroscopeHeading;
    }
    
    /// <summary>
    /// Set the mini map instance reference
    /// Called by ARControl when mini map is created
    /// </summary>
    /// <param name="mapInstance">The mini map GameObject instance</param>
    public void SetMiniMapInstance(GameObject mapInstance)
    {
        miniMapInstance = mapInstance;

        // Start initial heading capture if enabled
        if (useInitialHeadingCapture && !hasCapturedInitialHeading)
        {
            StartInitialHeadingCapture();
        }

        Debug.Log("MiniMapRotation: Mini map instance assigned");
    }

    /// <summary>
    /// Start capturing the initial heading for map orientation
    /// </summary>
    private void StartInitialHeadingCapture()
    {
        isCapturingInitialHeading = true;
        initialHeadingCaptureStartTime = Time.time;
        Debug.Log($"MiniMapRotation: Starting initial heading capture (warmup: {initialHeadingWarmupTime}s)");
    }

    /// <summary>
    /// Capture the initial heading and set map orientation
    /// </summary>
    private void CaptureInitialHeading(float heading)
    {
        initialHeadingOffset = heading;
        hasCapturedInitialHeading = true;
        isCapturingInitialHeading = false;

        // Set initial map rotation to face the user's current heading
        if (miniMapInstance != null)
        {
            // For initial setup, we want the map to face the direction the user is heading
            // So we rotate the map so that the user's heading direction appears at the top
            miniMapInstance.transform.localRotation = Quaternion.Euler(0f, -heading, 0f);
        }

        Debug.Log($"MiniMapRotation: Initial heading captured: {heading:F1}° - Map now faces user's heading direction");
    }

    /// <summary>
    /// Check if initial heading capture should be performed
    /// </summary>
    private void CheckInitialHeadingCapture(float currentHeading, float sensorQuality)
    {
        if (!isCapturingInitialHeading || hasCapturedInitialHeading)
            return;

        float elapsedTime = Time.time - initialHeadingCaptureStartTime;

        // Wait for warmup period and sufficient sensor quality
        if (elapsedTime >= initialHeadingWarmupTime && sensorQuality >= initialHeadingQualityThreshold)
        {
            CaptureInitialHeading(currentHeading);
        }
        else if (elapsedTime >= initialHeadingWarmupTime)
        {
            Debug.Log($"MiniMapRotation: Waiting for better sensor quality ({sensorQuality:F2} < {initialHeadingQualityThreshold:F2})");
        }
    }
    
    /// <summary>
    /// Enable or disable compass rotation
    /// </summary>
    /// <param name="enable">Whether to enable compass rotation</param>
    public void SetCompassRotation(bool enable)
    {
        useCompassRotation = enable;
        if (enable)
        {
            Input.compass.enabled = true;
        }
        Debug.Log($"MiniMapRotation: Compass rotation {(enable ? "enabled" : "disabled")}");
    }
    
    /// <summary>
    /// Enable or disable gyroscope fallback
    /// </summary>
    /// <param name="enable">Whether to enable gyroscope fallback</param>
    public void SetGyroscopeFallback(bool enable)
    {
        useGyroscopeFallback = enable;
        if (enable)
        {
            Input.gyro.enabled = true;
        }
        Debug.Log($"MiniMapRotation: Gyroscope fallback {(enable ? "enabled" : "disabled")}");
    }
    
    /// <summary>
    /// Set rotation speed
    /// </summary>
    /// <param name="speed">New rotation speed</param>
    public void SetRotationSpeed(float speed)
    {
        rotationSpeed = Mathf.Max(0.1f, speed);
        Debug.Log($"MiniMapRotation: Rotation speed set to {rotationSpeed}");
    }
    
    /// <summary>
    /// Debug logging of current status
    /// </summary>
    private void LogDebugInfo()
    {
        if (miniMapInstance == null)
        {
            Debug.Log("MiniMapRotation: No mini map instance assigned");
            return;
        }

        float compassValue = useCompassRotation && Input.compass.enabled ? Input.compass.trueHeading : 0f;
        float gyroValue = (useGyroscopeFusion || useGyroscopeFallback) && Input.gyro.enabled ? GetGyroscopeHeading() : 0f;

        string initialHeadingStatus = "Not Captured";
        if (isCapturingInitialHeading)
            initialHeadingStatus = $"Capturing ({Time.time - initialHeadingCaptureStartTime:F1}s/{initialHeadingWarmupTime:F1}s)";
        else if (hasCapturedInitialHeading)
            initialHeadingStatus = $"{initialHeadingOffset:F1}°";

        Debug.Log($"MiniMapRotation Status - " +
                  $"Compass: {compassValue:F1}°, " +
                  $"Gyro: {gyroValue:F1}°, " +
                  $"GPS Course: {(hasValidGPSCourse ? $"{gpsCourseHeading:F1}°" : "N/A")}, " +
                  $"Kalman: {kalmanEstimate:F1}°, " +
                  $"Final: {fusedHeading:F1}°, " +
                  $"Adjusted: {adjustedHeading:F1}°, " +
                  $"Map Rotation: {miniMapInstance.transform.localRotation.eulerAngles.y:F1}°, " +
                  $"Sensor Quality: {sensorQualityScore:F2}, " +
                  $"Initial Heading: {initialHeadingStatus}, " +
                  $"Compass: {(useCompassRotation ? "ON" : "OFF")}, " +
                  $"Gyro Fusion: {(useGyroscopeFusion ? "ON" : "OFF")}, " +
                  $"Advanced Filtering: {(useAdvancedFiltering ? "ON" : "OFF")}");
    }
    
    private void OnEnable()
    {
        // Re-initialize when script is enabled
        if (isInitialized)
        {
            InitializeCompass();
            InitializeGyroscope();
        }
    }

    private void OnDisable()
    {
        // Clean up when script is disabled
        if (useCompassRotation)
        {
            Input.compass.enabled = false;
        }

        if (useGyroscopeFusion || useGyroscopeFallback)
        {
            Input.gyro.enabled = false;
        }
    }
}