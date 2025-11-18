using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using System;
using System.Linq;
using System.Globalization;

public class SupabaseClient : MonoBehaviour
{
    // 🔧 Replace with your Supabase project details
    private const string SUPABASE_URL = "https://xyyftberwxgynoholndm.supabase.co";  // e.g. https://abcd1234.supabase.co
    private const string SUPABASE_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Inh5eWZ0YmVyd3hneW5vaG9sbmRtIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTczMDQ2NDYsImV4cCI6MjA3Mjg4MDY0Nn0.B9TCQeLOjkLJ9KVn3vjUiHJDURZO4bJvOnzHvifVJ5c";  // find in your Supabase project's API settings
    private const string TABLE_NAME = "ar_pois"; // replace with your table name

    public ScrollRect scrollView; // The ScrollRect component of the ScrollView
    public Transform content; // The Content transform of the ScrollView
    public GameObject poiDisplayPrefab; // The poidesplay prefab

    // Make POIs accessible to other scripts for search functionality
    public List<POI> AllPOIs { get; private set; } = new List<POI>();

    void Start()
    {
        // SupabaseClient is ready but not fetching data automatically
        Debug.Log("SupabaseClient initialized - ready for manual operations");

        // Fetch and display POIs automatically
        StartCoroutine(FetchAndDisplayPOIs());
    }

    // Public method to refresh POIs (can be called from other scripts)
    public void RefreshPOIs()
    {
        StartCoroutine(FetchAndDisplayPOIs());
    }

    public IEnumerator FetchPOIs(System.Action<List<POI>> onSuccess, System.Action<string> onError)
    {
        // Supabase REST API endpoint for your table
        string url = $"{SUPABASE_URL}/rest/v1/{TABLE_NAME}?select=*";

        Debug.Log($"Fetching POIs from: {url}");

        UnityWebRequest request = UnityWebRequest.Get(url);

        // Required headers
        request.SetRequestHeader("apikey", SUPABASE_KEY);
        request.SetRequestHeader("Authorization", $"Bearer {SUPABASE_KEY}");
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
                    onSuccess?.Invoke(new List<POI>());
                    yield break;
                }

                // Parse JSON array directly
                POI[] pois = JsonUtility.FromJson<POIList>("{\"pois\":" + json + "}").pois;
                List<POI> poiList = new List<POI>(pois);
                Debug.Log($"Successfully parsed {poiList.Count} POIs");
                onSuccess?.Invoke(poiList);
            }
            catch (Exception e)
            {
                Debug.LogError($"JSON parsing error: {e.Message}");
                onError?.Invoke("Failed to parse JSON: " + e.Message);
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
            onError?.Invoke(errorMsg);
        }
    }

    private IEnumerator FetchAndDisplayPOIs()
    {
        Debug.Log("Starting POI fetch from Supabase...");

        yield return StartCoroutine(FetchPOIs(
            onSuccess: (List<POI> pois) =>
            {
                // Store all POIs for search functionality
                AllPOIs = pois;
                Debug.Log($"Fetched {AllPOIs.Count} POIs from Supabase for search functionality");

                // Display all POIs (including outdated ones)
                DisplayPOIs(pois);
            },
            onError: (string error) =>
            {
                Debug.LogError("Failed to fetch POIs: " + error);
                // Create mock POIs for testing if fetch fails
                CreateMockPOIsForTesting();
            }
        ));
    }

    private void CreateMockPOIsForTesting()
    {
        AllPOIs = new List<POI>();

        // Create mock POIs very close to default location for testing
        POI mockPOI1 = new POI();
        mockPOI1.label = "Test POI 1";
        mockPOI1.lat = 37.7749f + 0.0001f; // Very close north
        mockPOI1.lng = -122.4194f;
        mockPOI1.color = "#FF0000"; // Red
        mockPOI1.mark_type = "marker";
        AllPOIs.Add(mockPOI1);

        POI mockPOI2 = new POI();
        mockPOI2.label = "Test POI 2";
        mockPOI2.lat = 37.7749f;
        mockPOI2.lng = -122.4194f + 0.0001f; // Very close east
        mockPOI2.color = "#00FF00"; // Green
        mockPOI2.mark_type = "marker";
        AllPOIs.Add(mockPOI2);

        POI mockPOI3 = new POI();
        mockPOI3.label = "Test POI 3";
        mockPOI3.lat = 37.7749f - 0.0001f; // Very close south
        mockPOI3.lng = -122.4194f;
        mockPOI3.color = "#0000FF"; // Blue
        mockPOI3.mark_type = "marker";
        AllPOIs.Add(mockPOI3);

        Debug.Log($"Created {AllPOIs.Count} mock POIs for testing due to fetch failure");

        // Display mock POIs
        DisplayPOIs(AllPOIs);
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

    private void DisplayPOIs(List<POI> pois)
    {
        // Clear existing items
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }

        // Instantiate and populate prefabs
        foreach (POI poi in pois)
        {
            GameObject poiItem = Instantiate(poiDisplayPrefab, content);
            PopulatePOIItem(poiItem, poi);
        }

        // Adjust Content size to fit all items
        AdjustContentSize(pois.Count);

        // Configure ScrollRect for clamped scrolling
        ConfigureScrollRect();

        // Force canvas update to ensure proper scrolling
        Canvas.ForceUpdateCanvases();
    }

    private void AdjustContentSize(int itemCount)
    {
        if (itemCount == 0) return;

        // Get the VerticalLayoutGroup component
        VerticalLayoutGroup layoutGroup = content.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup == null) return;

        // Calculate total height based on actual instantiated items
        float totalHeight = 0f;
        float spacing = layoutGroup.spacing;
        float paddingTop = layoutGroup.padding.top;
        float paddingBottom = layoutGroup.padding.bottom;

        // Sum up the actual heights of all child items
        for (int i = 0; i < content.childCount; i++)
        {
            RectTransform childRect = content.GetChild(i).GetComponent<RectTransform>();
            if (childRect != null)
            {
                totalHeight += childRect.rect.height;
                if (i < content.childCount - 1) // Add spacing between items
                {
                    totalHeight += spacing;
                }
            }
        }

        // Add padding
        totalHeight += paddingTop + paddingBottom;

        // Set the Content size
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, totalHeight);

        // Force layout rebuild to ensure proper scrolling
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

        // Also rebuild parent layouts
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect.parent.GetComponent<RectTransform>());
    }

    private void ConfigureScrollRect()
    {
        if (scrollView == null) return;

        // Set movement type to Clamped to prevent scrolling beyond content bounds
        scrollView.movementType = ScrollRect.MovementType.Clamped;

        // Ensure vertical scrolling is enabled
        scrollView.vertical = true;
        scrollView.horizontal = false;

        // Set scroll sensitivity for smooth scrolling
        scrollView.scrollSensitivity = 10f;

        // Enable inertia for natural feel
        scrollView.inertia = true;
        scrollView.decelerationRate = 0.135f;

        // Reset scroll position to top
        scrollView.verticalNormalizedPosition = 1f;

        // Ensure the ScrollRect viewport is properly sized
        if (scrollView.viewport != null)
        {
            // The viewport should be the size of the ScrollView itself
            RectTransform viewportRect = scrollView.viewport.GetComponent<RectTransform>();
            RectTransform scrollRect = scrollView.GetComponent<RectTransform>();

            // Match viewport size to ScrollRect size
            viewportRect.sizeDelta = scrollRect.sizeDelta;
        }
    }

    private void PopulatePOIItem(GameObject poiItem, POI poi)
    {
        // Find the text components
        TMP_Text labelText = poiItem.transform.Find("label").GetComponent<TMP_Text>();
        TMP_Text coordinateText = poiItem.transform.Find("coordinate").GetComponent<TMP_Text>();
        TMP_Text markTypeText = poiItem.transform.Find("Mark-type").GetComponent<TMP_Text>();
        Image colorImage = poiItem.transform.Find("color").GetComponent<Image>();

        // Set texts
        labelText.text = poi.label ?? "Unnamed POI";
        coordinateText.text = $"{poi.lat:F6}, {poi.lng:F6}";
        markTypeText.text = poi.mark_type ?? "marker";

        // Set color
        if (!string.IsNullOrEmpty(poi.color) && ColorUtility.TryParseHtmlString(poi.color, out Color color))
        {
            colorImage.color = color;
        }
        else
        {
            colorImage.color = Color.gray; // Default color if parsing fails
        }
    }
}
