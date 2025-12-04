using UnityEngine;

public class MiniMapRotationEditor : MonoBehaviour
{
    [Header("Editor Simulation Settings")]
    [Tooltip("Enable simulation mode for testing in Unity Editor")]
    public bool enableEditorSimulation = false;
    
    [Tooltip("Rotation speed for simulation (degrees per second)")]
    public float simulationRotationSpeed = 30f;
    
    [Tooltip("Show simulation status in Play mode")]
    public bool showPlayModeStatus = true;
    
    [Header("Keyboard Controls (Play Mode)")]
    [Tooltip("Use keyboard arrow keys to rotate map in Editor play mode")]
    public bool enableKeyboardControls = true;
    
    [Tooltip("Keyboard rotation speed")]
    public float keyboardRotationSpeed = 90f;
    
    private MiniMapRotation miniMapRotation;
    private float currentSimulatedHeading = 0f;
    private Vector3 lastFrameRotation = Vector3.zero;
    
    private void OnEnable()
    {
        miniMapRotation = GetComponent<MiniMapRotation>();
        
        #if UNITY_EDITOR
        if (enableEditorSimulation)
        {
            Debug.Log("🎮 MiniMapRotation Editor Simulation: ENABLED");
            Debug.Log("📝 Controls: Arrow keys to rotate, Space to reset");
        }
        #endif
    }
    
    private void OnDisable()
    {
        #if UNITY_EDITOR
        Debug.Log("🎮 MiniMapRotation Editor Simulation: DISABLED");
        #endif
    }
    
    private void Update()
    {
        #if UNITY_EDITOR
        HandleEditorSimulation();
        #endif
    }
    
    #if UNITY_EDITOR
    private void HandleEditorSimulation()
    {
        // Only run in Editor when explicitly enabled
        if (!Application.isPlaying && enableEditorSimulation)
        {
            // Auto-rotation in Scene view when not in play mode
            currentSimulatedHeading += simulationRotationSpeed * Time.deltaTime;
            if (currentSimulatedHeading >= 360f) currentSimulatedHeading -= 360f;
            
            ApplySimulatedRotation(currentSimulatedHeading);
        }
        else if (Application.isPlaying && enableEditorSimulation && enableKeyboardControls)
        {
            // Keyboard controls in Play mode
            HandleKeyboardRotation();
        }
    }
    
    private void HandleKeyboardRotation()
    {
        float rotationInput = 0f;
        
        // Arrow key controls
        if (Input.GetKey(KeyCode.LeftArrow))
            rotationInput -= keyboardRotationSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.RightArrow))
            rotationInput += keyboardRotationSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.UpArrow))
            rotationInput += keyboardRotationSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.DownArrow))
            rotationInput -= keyboardRotationSpeed * Time.deltaTime;
        
        // Space bar to reset
        if (Input.GetKeyDown(KeyCode.Space))
        {
            currentSimulatedHeading = 0f;
            rotationInput = 0f;
        }
        
        if (rotationInput != 0f)
        {
            currentSimulatedHeading += rotationInput;
            if (currentSimulatedHeading < 0f) currentSimulatedHeading += 360f;
            if (currentSimulatedHeading >= 360f) currentSimulatedHeading -= 360f;
            
            ApplySimulatedRotation(currentSimulatedHeading);
        }
    }
    
    private void ApplySimulatedRotation(float heading)
    {
        if (miniMapRotation != null && miniMapRotation.miniMapInstance != null)
        {
            // Override the rotation for simulation
            Quaternion targetRotation = Quaternion.Euler(0f, -heading, 0f);
            
            // Apply directly for immediate feedback in Editor
            miniMapRotation.miniMapInstance.transform.localRotation = targetRotation;
            
            lastFrameRotation = miniMapRotation.miniMapInstance.transform.localRotation.eulerAngles;
        }
    }
    #endif
    
    private void OnGUI()
    {
        #if UNITY_EDITOR
        if (showPlayModeStatus && enableEditorSimulation && Application.isPlaying)
        {
            // Display simulation status in Play mode
            GUILayout.BeginArea(new Rect(10, 10, 300, 120), GUI.skin.box);
            
            GUIStyle boldStyle = new GUIStyle(GUI.skin.label);
            boldStyle.fontStyle = FontStyle.Bold;
            
            GUILayout.Label("🧭 MiniMap Rotation Editor Simulation", boldStyle);
            GUILayout.Label($"🎯 Current Heading: {currentSimulatedHeading:F1}°");
            GUILayout.Label($"🔄 Map Rotation: {lastFrameRotation.y:F1}°");
            GUILayout.Label("🎮 Controls: Arrow Keys to Rotate");
            GUILayout.Label("🔄 Spacebar: Reset Rotation");
            GUILayout.EndArea();
        }
        #endif
    }
}