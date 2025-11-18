using UnityEngine;

public class SinglePOI : MonoBehaviour
{
    private POI targetPOI;
    private LineRenderer navigationLine;
    private bool isNavigating = false;

    private void Start()
    {
        // Create LineRenderer for navigation line
        navigationLine = gameObject.AddComponent<LineRenderer>();
        navigationLine.material = new Material(Shader.Find("Sprites/Default"));
        navigationLine.startColor = Color.blue;
        navigationLine.endColor = Color.blue;
        navigationLine.startWidth = 0.05f;
        navigationLine.endWidth = 0.05f;
        navigationLine.positionCount = 2;
        navigationLine.enabled = false;
    }

    private void Update()
    {
        if (isNavigating && targetPOI != null && Camera.main != null)
        {
            UpdateNavigationLine();
        }
    }

    public void StartSinglePOINavigation(POI poi)
    {
        if (poi == null)
        {
            Debug.LogError("SinglePOI: Cannot start navigation - POI is null");
            return;
        }

        targetPOI = poi;
        isNavigating = true;

        // Enable navigation line
        navigationLine.enabled = true;

        Debug.Log($"Starting single POI navigation to: {poi.label} at ({poi.lat}, {poi.lng})");
    }

    public void StopSinglePOINavigation()
    {
        isNavigating = false;
        targetPOI = null;

        // Disable navigation line
        if (navigationLine != null)
        {
            navigationLine.enabled = false;
        }

        Debug.Log("Stopping single POI navigation");
    }

    private void UpdateNavigationLine()
    {
        // Navigation line disabled since POI indicator was removed
        // The POI3D system now handles POI positioning and display
        if (navigationLine != null)
        {
            navigationLine.enabled = false;
        }
    }


    private void OnDestroy()
    {
        // No cleanup needed since POI indicator was removed
    }
}