using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Events;
using System;
using UnityEngine.Networking;
using System.Globalization;

public class ui_controller : MonoBehaviour
{
    public Transform content; // The Content transform of the ScrollView
    public GameObject poiDisplayPrefab; // The poidesplay prefab

    // Search functionality
    public TMP_InputField searchInputField;
    public GameObject searchScrollView; // The hidden ScrollView for search results
    public Transform searchContent; // The Content transform of the search ScrollView
    public GameObject searchPoiContentPrefab; // The prefab for search results
    public Button cancelSearchButton;

    // Group content
    public Transform groupContent; // The Content transform for group filtering

    // Information Panel
    public GameObject infoPanel;
    public TMP_Text infoLabelText;
    public TMP_Text infoMarkTypeText;
    public TMP_Text infoCoordinateText;
    public TMP_Text infoDateText;
    public Button infoBackButton;
    public Button navigationButton; // Navigation button to open AR for POI
    public UnityEngine.UI.Image infoMapImage;

    private List<POI> allPOIs = new List<POI>(); // Store all POIs for search
    private POI currentPOI; // Store the currently displayed POI in info panel
    private List<POI> currentGroupPOIs; // Store current group POIs for navigation

    private const string SUPABASE_URL = "https://xyyftberwxgynoholndm.supabase.co";
    private const string SUPABASE_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Inh5eWZ0YmVyd3hneW5vaG9sbmRtIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTczMDQ2NDYsImV4cCI6MjA3Mjg4MDY0Nn0.B9TCQeLOjkLJ9KVn3vjUiHJDURZO4bJvOnzHvifVJ5c";
    private const string TABLE_NAME = "ar_pois";


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        FetchAndDisplayPOIs();
        // Setup search functionality
        SetupSearchFunctionality();
        // Setup info panel functionality
        SetupInfoPanelFunctionality();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void FetchAndDisplayPOIs()
    {
        StartCoroutine(FetchPOIs(
            onSuccess: (List<POI> pois) =>
            {
                allPOIs = pois; // Store all POIs for search functionality
                Debug.Log($"Fetched {allPOIs.Count} POIs from Supabase for search functionality");

                // Filter active POIs
                List<POI> activePOIs = FilterActivePOIs(pois);
                Debug.Log($"Active POIs: {activePOIs.Count}");

                // Update allPOIs to only include active POIs for search functionality
                allPOIs = activePOIs;

                // Separate POIs: grouped vs non-grouped
                List<POI> groupedPOIs = activePOIs.Where(poi => poi != null && !string.IsNullOrEmpty(poi.group_name)).ToList();
                List<POI> nonGroupedPOIs = activePOIs.Where(poi => poi != null && string.IsNullOrEmpty(poi.group_name)).ToList();

                Debug.Log($"Grouped POIs: {groupedPOIs.Count}, Non-grouped POIs: {nonGroupedPOIs.Count}");

                // Display non-grouped POIs in main content
                DisplayPOIs(nonGroupedPOIs, content);
                Debug.Log($"Displayed {nonGroupedPOIs.Count} non-grouped POIs in main content");

                // Display grouped POIs in group content
                if (groupedPOIs.Count > 0)
                {
                    DisplayPOIsWithGroups();
                }
            },
            onError: (string error) =>
            {
                Debug.LogError("Failed to fetch POIs: " + error);
            }
        ));
    }

    private List<POI> FilterActivePOIs(List<POI> pois)
    {
        DateTime currentDate = DateTime.Now.Date; // Get current date without time
        Debug.Log($"Current date for filtering: {currentDate.ToString("yyyy-MM-dd")}");

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
                if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime poiDate))
                {
                    Debug.Log($"Parsed date: {poiDate.ToString("yyyy-MM-dd")}, Current date: {currentDate.ToString("yyyy-MM-dd")}, Is future/current: {poiDate.Date >= currentDate}");
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

    public IEnumerator FetchPOIs(System.Action<List<POI>> onSuccess, System.Action<string> onError)
    {
        // Supabase REST API endpoint for your table
        string url = $"{SUPABASE_URL}/rest/v1/{TABLE_NAME}?select=*";

        UnityWebRequest request = UnityWebRequest.Get(url);

        // Required headers
        request.SetRequestHeader("apikey", SUPABASE_KEY);
        request.SetRequestHeader("Authorization", $"Bearer {SUPABASE_KEY}");
        request.SetRequestHeader("Content-Type", "application/json");

        // Send request
        yield return request.SendWebRequest();

        // Check for errors
        if (request.result == UnityWebRequest.Result.Success)
        {
            string json = request.downloadHandler.text;
            Debug.Log("✅ Data fetched successfully!");
            Debug.Log(json);

            try
            {
                // Parse JSON array directly
                POI[] pois = JsonUtility.FromJson<POIList>("{\"pois\":" + json + "}").pois;
                List<POI> poiList = new List<POI>(pois);
                onSuccess?.Invoke(poiList);
            }
            catch (Exception e)
            {
                onError?.Invoke("Failed to parse JSON: " + e.Message);
            }
        }
        else
        {
            string errorMsg = "❌ Error fetching data: " + request.error + " Response: " + request.downloadHandler.text;
            Debug.LogError(errorMsg);
            onError?.Invoke(errorMsg);
        }
    }

    private void DisplayPOIs(List<POI> pois, Transform targetContent)
    {
        if (targetContent == null)
        {
            Debug.LogWarning("Target content is null, skipping display");
            return;
        }

        // Clear existing items
        foreach (Transform child in targetContent)
        {
            Destroy(child.gameObject);
        }

        // Instantiate and populate prefabs
        foreach (POI poi in pois)
        {
            GameObject poiItem = Instantiate(poiDisplayPrefab, targetContent);

            // Make the POI item elastic by adding LayoutElement
            LayoutElement layoutElement = poiItem.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = poiItem.AddComponent<LayoutElement>();
            }
            layoutElement.flexibleHeight = 1; // Allow flexible height

            PopulatePOIItem(poiItem, poi);
        }
    }

    private void PopulatePOIItem(GameObject poiItem, POI poi)
    {
        Debug.Log($"Populating POI item for: {poi?.label ?? "null POI"}");

        // Find the text components
        TMP_Text labelText = poiItem.transform.Find("label").GetComponent<TMP_Text>();
        TMP_Text coordinateText = poiItem.transform.Find("coordinate").GetComponent<TMP_Text>();
        TMP_Text markTypeText = poiItem.transform.Find("Mark-type").GetComponent<TMP_Text>();
        Image colorImage = poiItem.transform.Find("color").GetComponent<Image>();

        // Set texts
        if (labelText != null) labelText.text = poi.label;
        if (coordinateText != null) coordinateText.text = $"{poi.lat:F6}, {poi.lng:F6}";
        if (markTypeText != null) markTypeText.text = poi.mark_type;

        // Set color
        if (ColorUtility.TryParseHtmlString(poi.color, out Color color))
        {
            if (colorImage != null) colorImage.color = color;
        }
        else
        {
            if (colorImage != null) colorImage.color = Color.white;
        }

        // Find and setup the open button
        Button openButton = poiItem.transform.Find("open")?.GetComponent<Button>();
        if (openButton != null)
        {
            // Remove any existing listeners to avoid duplicates
            openButton.onClick.RemoveAllListeners();
            // Add click listener to show info panel
            openButton.onClick.AddListener(() =>
            {
                Debug.Log($"Open button clicked for POI: {poi?.label ?? "null"}");
                ShowInfoPanel(poi);
            });
            Debug.Log($"Open button listener added for POI: {poi.label}");
        }
        else
        {
            Debug.LogError("Open button not found on POI display prefab!");
        }
    }

    private void SetupSearchFunctionality()
    {
        if (searchInputField != null)
        {
            // Add listener for input field value changes
            searchInputField.onValueChanged.AddListener(OnSearchInputChanged);
        }

        // Add listener for cancel search button
        if (cancelSearchButton != null)
        {
            cancelSearchButton.onClick.AddListener(CancelSearch);
        }

        // Initially hide the search ScrollView
        if (searchScrollView != null)
        {
            searchScrollView.SetActive(false);
        }
    }

    private void OnSearchInputChanged(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            // Hide search results when input is empty
            if (searchScrollView != null)
            {
                searchScrollView.SetActive(false);
            }
        }
        else
        {
            // Show search results and filter POIs
            if (searchScrollView != null)
            {
                searchScrollView.SetActive(true);
            }

            // Only perform search if POIs are available
            if (allPOIs != null && allPOIs.Count > 0)
            {
                PerformSearch(searchText);
            }
            else
            {
                Debug.LogWarning("Cannot search - POIs not yet loaded from Supabase. Please wait for initial fetch to complete.");
            }
        }
    }

    private void PerformSearch(string searchText)
    {
        if (allPOIs == null || allPOIs.Count == 0)
        {
            Debug.LogWarning("No POIs available for search. Make sure POIs are fetched from Supabase first.");
            return;
        }

        // Use case-insensitive comparison
        string lowerSearchText = searchText.ToLowerInvariant();

        // Only search through non-grouped POIs for main content search
        List<POI> nonGroupedPOIs = allPOIs.Where(poi => poi != null && string.IsNullOrEmpty(poi.group_name)).ToList();

        // Filter POIs based on EXACT label matching - only show POIs where the label starts with the search text
        List<POI> filteredPOIs = nonGroupedPOIs.Where(poi =>
            poi.label.ToLowerInvariant().StartsWith(lowerSearchText)
        ).OrderBy(poi => poi.label.ToLowerInvariant()).ToList(); // Sort alphabetically

        Debug.Log($"Searching for '{searchText}' in {nonGroupedPOIs.Count} non-grouped POIs. Found {filteredPOIs.Count} matches that start with the search text.");
        foreach (POI poi in filteredPOIs)
        {
            Debug.Log($"Match: Label='{poi.label}', HasGroup='{!string.IsNullOrEmpty(poi.group_name)}'");
        }

        DisplaySearchResults(filteredPOIs);
    }

    private void DisplaySearchResults(List<POI> searchResults)
    {
        // Clear existing search results
        if (searchContent != null)
        {
            foreach (Transform child in searchContent)
            {
                Destroy(child.gameObject);
            }

            Debug.Log($"Instantiating {searchResults.Count} search result prefabs");

            // Instantiate and populate search result prefabs
            foreach (POI poi in searchResults)
            {
                if (searchPoiContentPrefab != null)
                {
                    GameObject searchItem = Instantiate(searchPoiContentPrefab, searchContent);
                    PopulateSearchItem(searchItem, poi);
                    Debug.Log($"Instantiated search item for POI: {poi.label}");
                }
                else
                {
                    Debug.LogError("searchPoiContentPrefab is not assigned!");
                }
            }

            // Force layout update to ensure proper display
            LayoutRebuilder.ForceRebuildLayoutImmediate(searchContent.GetComponent<RectTransform>());
            Canvas.ForceUpdateCanvases();

            Debug.Log($"Search results display completed. Content has {searchContent.childCount} children");
        }
        else
        {
            Debug.LogError("searchContent is not assigned!");
        }
    }

    private void PopulateSearchItem(GameObject searchItem, POI poi)
    {
        Debug.Log($"Populating search item for POI: {poi.label} with mark_type: {poi.mark_type}");

        // Find the button component
        Button button = searchItem.GetComponent<Button>();
        if (button != null)
        {
            // Remove any existing listeners to avoid duplicates
            button.onClick.RemoveAllListeners();
            // Add click listener to the button (you can customize what happens when clicked)
            button.onClick.AddListener(() => OnSearchResultClicked(poi));
        }
        else
        {
            Debug.LogWarning("No Button component found on search item prefab!");
        }

        // Find the text components within the button - try different naming variations
        TMP_Text labelText = searchItem.transform.Find("label")?.GetComponent<TMP_Text>();
        if (labelText == null)
        {
            // Try alternative names
            labelText = searchItem.transform.Find("Label")?.GetComponent<TMP_Text>();
            if (labelText == null)
            {
                Debug.LogWarning("Could not find 'label' TMP_Text component in search prefab!");
            }
        }

        TMP_Text markTypeText = searchItem.transform.Find("mark_type")?.GetComponent<TMP_Text>();
        if (markTypeText == null)
        {
            // Try alternative names
            markTypeText = searchItem.transform.Find("marktype")?.GetComponent<TMP_Text>();
            markTypeText = markTypeText ?? searchItem.transform.Find("MarkType")?.GetComponent<TMP_Text>();
            markTypeText = markTypeText ?? searchItem.transform.Find("Mark_Type")?.GetComponent<TMP_Text>();
            if (markTypeText == null)
            {
                Debug.LogWarning("Could not find 'mark_type' TMP_Text component in search prefab!");
            }
        }

        // Set texts
        if (labelText != null)
        {
            labelText.text = poi.label;
            Debug.Log($"Set label text to: {poi.label}");
        }

        if (markTypeText != null)
        {
            markTypeText.text = poi.mark_type;
            Debug.Log($"Set mark_type text to: {poi.mark_type}");
        }
    }

    private void OnSearchResultClicked(POI selectedPOI)
    {
        // Handle what happens when a search result is clicked
        Debug.Log($"Selected POI: {selectedPOI.label} - {selectedPOI.mark_type}");

        // Show information panel with POI details
        ShowInfoPanel(selectedPOI);
    }

    public void ShowInfoPanel(POI poi)
    {
        Debug.Log($"ShowInfoPanel called for POI: {(poi != null ? poi.label : "null")}");

        if (poi == null)
        {
            Debug.LogError("POI is null, cannot show info panel");
            return;
        }

        currentPOI = poi; // Store the current POI

        // Check if this is a group POI and store group information
        if (poi.mark_type == "group" && !string.IsNullOrEmpty(poi.group_name))
        {
            // This is a merged group POI - get all POIs in this group
            currentGroupPOIs = allPOIs.Where(p => p != null && p.group_name == poi.group_name).ToList();
            Debug.Log($"Group POI detected: {poi.group_name} with {currentGroupPOIs.Count} POIs");
        }
        else
        {
            currentGroupPOIs = null; // Not a group POI
        }

        if (infoPanel != null)
        {
            infoPanel.SetActive(true);
            Debug.Log("Info panel set to active");

            if (infoLabelText != null)
                infoLabelText.text = poi.label;

            if (infoMarkTypeText != null)
                infoMarkTypeText.text = poi.mark_type;

            if (infoCoordinateText != null)
                infoCoordinateText.text = $"{poi.lat:F6}, {poi.lng:F6}";

            if (infoDateText != null)
            {
                if (poi.dates != null && poi.dates.Length > 0)
                {
                    // Convert dates from MM-dd-yyyy to MM/dd/yyyy format for display
                    List<string> formattedDates = new List<string>();
                    foreach (string dateStr in poi.dates)
                    {
                        if (DateTime.TryParseExact(dateStr, "MM-dd-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                        {
                            formattedDates.Add(date.ToString("MM/dd/yyyy"));
                        }
                        else
                        {
                            formattedDates.Add(dateStr); // Keep original if parsing fails
                        }
                    }
                    infoDateText.text = string.Join(", ", formattedDates);
                }
                else
                {
                    infoDateText.text = "This POI is permanent";
                }
            }

            // Setup navigation button listener
            if (navigationButton != null)
            {
                navigationButton.onClick.RemoveAllListeners();
                navigationButton.onClick.AddListener(() => OpenARForPOI(poi));
            }


            // Load and display map with marker(s)
            if (infoMapImage != null)
            {
                StartCoroutine(LoadMapWithMarker(poi));
            }
        }
        else
        {
            Debug.LogError("Info panel is not assigned!");
        }
    }


    private void CancelSearch()
    {
        // Clear search input
        if (searchInputField != null)
        {
            searchInputField.text = "";
        }

        // Hide search ScrollView
        if (searchScrollView != null)
        {
            searchScrollView.SetActive(false);
        }
    }

    private void SetupInfoPanelFunctionality()
    {
        // Add listener for info back button
        if (infoBackButton != null)
        {
            infoBackButton.onClick.AddListener(OnInfoBackButtonClick);
        }


        // Initially hide the info panel
        if (infoPanel != null)
        {
            infoPanel.SetActive(false);
        }
    }

    // Method to display merged groups (one POI per group) in the groupContent
    public void DisplayPOIsWithGroups()
    {
        if (groupContent == null)
        {
            Debug.LogError("Group content is not assigned!");
            return;
        }

        if (allPOIs == null || allPOIs.Count == 0)
        {
            Debug.LogWarning("No POIs available to display in group");
            return;
        }

        // Clear existing content
        foreach (Transform child in groupContent)
        {
            Destroy(child.gameObject);
        }

        // Filter POIs that have group_name (belong to groups)
        List<POI> groupPOIs = allPOIs.Where(poi => poi != null && !string.IsNullOrEmpty(poi.group_name)).ToList();

        Debug.Log($"Found {groupPOIs.Count} POIs that belong to groups");

        // Group POIs by group_name and create one representative POI per group
        var groupedPOIs = groupPOIs.GroupBy(poi => poi.group_name);

        int displayedGroups = 0;
        foreach (var group in groupedPOIs.OrderBy(g => g.Key))
        {
            Debug.Log($"Group '{group.Key}': {group.Count()} POIs");

            // Create a merged POI representing the entire group
            POI mergedPOI = CreateMergedGroupPOI(group.Key, group.ToList());

            GameObject poiItem = Instantiate(poiDisplayPrefab, groupContent);

            // Make the POI item elastic by adding LayoutElement
            LayoutElement layoutElement = poiItem.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = poiItem.AddComponent<LayoutElement>();
            }
            layoutElement.flexibleHeight = 1; // Allow flexible height

            PopulateMergedPOIItem(poiItem, mergedPOI, group.ToList());

            displayedGroups++;
        }

        Debug.Log($"Successfully displayed {displayedGroups} merged groups in groupContent");
    }

    // Create a merged POI representing an entire group
    private POI CreateMergedGroupPOI(string groupName, List<POI> groupPOIs)
    {
        if (groupPOIs == null || groupPOIs.Count == 0)
        {
            return null;
        }

        // Use the first POI in the group as the base, but modify properties for the group
        POI mergedPOI = new POI
        {
            id = groupPOIs[0].id, // Use first POI's ID
            label = groupName, // Just the group name - keep it short
            mark_type = "group", // Special mark_type for groups
            color = groupPOIs[0].color, // Use first POI's color
            height = groupPOIs[0].height,
            group_name = groupName,
            group_index = 0,
            created_at = groupPOIs[0].created_at,
            updated_at = groupPOIs[0].updated_at
        };

        // Calculate center coordinates of all POIs in the group
        mergedPOI.lat = groupPOIs.Average(p => p.lat);
        mergedPOI.lng = groupPOIs.Average(p => p.lng);

        return mergedPOI;
    }

    // Populate POI item for merged groups
    private void PopulateMergedPOIItem(GameObject poiItem, POI mergedPOI, List<POI> groupPOIs)
    {
        // Find the text components
        TMP_Text labelText = poiItem.transform.Find("label").GetComponent<TMP_Text>();
        TMP_Text coordinateText = poiItem.transform.Find("coordinate").GetComponent<TMP_Text>();
        TMP_Text markTypeText = poiItem.transform.Find("Mark-type").GetComponent<TMP_Text>();
        Image colorImage = poiItem.transform.Find("color").GetComponent<Image>();

        // Set texts for merged POI
        if (labelText != null) labelText.text = mergedPOI.label;
        if (coordinateText != null) coordinateText.text = $"{mergedPOI.lat:F6}, {mergedPOI.lng:F6}";
        if (markTypeText != null) markTypeText.text = mergedPOI.mark_type;

        // Set color
        if (ColorUtility.TryParseHtmlString(mergedPOI.color, out Color color))
        {
            if (colorImage != null) colorImage.color = color;
        }
        else
        {
            if (colorImage != null) colorImage.color = Color.white;
        }

        // Find and setup the open button for group navigation
        Button openButton = poiItem.transform.Find("open").GetComponent<Button>();
        if (openButton != null)
        {
            // Remove any existing listeners to avoid duplicates
            openButton.onClick.RemoveAllListeners();
            // Add click listener to show info panel first (like individual POIs)
            openButton.onClick.AddListener(() =>
            {
                Debug.Log($"Group POI button clicked for: {mergedPOI.label}");
                ShowInfoPanel(mergedPOI);
            });
        }
        else
        {
            Debug.LogWarning("Open button not found on POI display prefab!");
        }
    }


    // Method to display POIs that belong to a specific group_name in the groupContent
    public void DisplayPOIsByGroupName(string groupName)
    {
        if (groupContent == null)
        {
            Debug.LogError("Group content is not assigned!");
            return;
        }

        if (allPOIs == null || allPOIs.Count == 0)
        {
            Debug.LogWarning("No POIs available to display in group");
            return;
        }

        // Clear existing content
        foreach (Transform child in groupContent)
        {
            Destroy(child.gameObject);
        }

        // Filter POIs by group_name
        List<POI> groupPOIs = allPOIs.Where(poi => poi != null && poi.group_name == groupName)
                                     .OrderBy(poi => poi.group_index)
                                     .ToList();

        Debug.Log($"Displaying {groupPOIs.Count} POIs for group_name '{groupName}' in groupContent");

        // Display filtered POIs
        foreach (POI poi in groupPOIs)
        {
            GameObject poiItem = Instantiate(poiDisplayPrefab, groupContent);

            // Make the POI item elastic by adding LayoutElement
            LayoutElement layoutElement = poiItem.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = poiItem.AddComponent<LayoutElement>();
            }
            layoutElement.flexibleHeight = 1; // Allow flexible height

            PopulatePOIItem(poiItem, poi);
        }

        Debug.Log($"Successfully displayed {groupPOIs.Count} POIs in groupContent for group_name '{groupName}'");
    }

    // Method to get all available group_names from POIs that have groups
    public List<string> GetAvailableGroups()
    {
        if (allPOIs == null || allPOIs.Count == 0)
        {
            return new List<string>();
        }

        return allPOIs
            .Where(poi => poi != null && !string.IsNullOrEmpty(poi.group_name))
            .Select(poi => poi.group_name)
            .Distinct()
            .OrderBy(group => group)
            .ToList();
    }

    // Method to get count of POIs in each group
    public Dictionary<string, int> GetGroupCounts()
    {
        if (allPOIs == null || allPOIs.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        return allPOIs
            .Where(poi => poi != null && !string.IsNullOrEmpty(poi.group_name))
            .GroupBy(poi => poi.group_name)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    // Navigate to an entire group of POIs
    public void NavigateToGroup(string groupName, List<POI> groupPOIs)
    {
        Debug.Log($"NavigateToGroup called for '{groupName}' with {groupPOIs?.Count ?? 0} POIs");

        if (string.IsNullOrEmpty(groupName) || groupPOIs == null || groupPOIs.Count == 0)
        {
            Debug.LogError("Invalid group navigation parameters");
            return;
        }

        // Find ARControl to handle group navigation
        ARControl arControl = FindFirstObjectByType<ARControl>();
        if (arControl != null)
        {
            // Start group navigation - pass all POIs in the group
            arControl.NavigateToGroup(groupName, groupPOIs);
            Debug.Log($"Started group navigation for '{groupName}' with {groupPOIs.Count} POIs");
        }
        else
        {
            Debug.LogError("ARControl not found for group navigation");
        }
    }

    private void OnInfoBackButtonClick()
    {
        HideInfoPanel();
    }

    private void HideInfoPanel()
    {
        if (infoPanel != null)
        {
            infoPanel.SetActive(false);
        }
    }

    private void OpenARForPOI(POI poi)
    {
        // Find the ARControl script in the scene (updated from AR_view_all)
        ARControl arControl = FindFirstObjectByType<ARControl>();
        if (arControl != null)
        {
            // Hide the info panel
            HideInfoPanel();

            // Check if this is a group POI
            if (poi.mark_type == "group" && currentGroupPOIs != null && currentGroupPOIs.Count > 0)
            {
                // Navigate to the entire group
                NavigateToGroup(poi.group_name, currentGroupPOIs);
                Debug.Log($"Opening AR navigation for group: {poi.group_name} with {currentGroupPOIs.Count} POIs");
            }
            else
            {
                // Open AR for the specific POI
                arControl.NavigateToPOI(poi);
                Debug.Log($"Opening AR navigation for POI: {poi.label}");
            }
        }
        else
        {
            Debug.LogError("ARControl script not found in the scene!");
        }
    }

    private IEnumerator LoadMapWithMarker(POI poi)
    {
        string mapUrl;
        string accessToken = "pk.eyJ1Ijoib3BvcDI5IiwiYSI6ImNtZm8za3Q1NjAxcTEyanF4ZjZraWowdjEifQ.jNxrXsiX7Davmhjmp4ihWw";

        // Check if this is a group POI
        if (poi.mark_type == "group" && currentGroupPOIs != null && currentGroupPOIs.Count > 0)
        {
            // Build markers for all POIs in the group
            List<string> markers = new List<string>();
            float centerLat = 0f;
            float centerLng = 0f;

            foreach (POI groupPOI in currentGroupPOIs)
            {
                // Convert hex color to Mapbox format
                string markerColor = groupPOI.color.Replace("#", "").ToLower();
                if (markerColor.Length == 3)
                {
                    markerColor = $"{markerColor[0]}{markerColor[0]}{markerColor[1]}{markerColor[1]}{markerColor[2]}{markerColor[2]}";
                }

                markers.Add($"pin-s-marker+{markerColor}({groupPOI.lng},{groupPOI.lat})");
                centerLat += groupPOI.lat;
                centerLng += groupPOI.lng;
            }

            // Calculate center of all group POIs
            centerLat /= currentGroupPOIs.Count;
            centerLng /= currentGroupPOIs.Count;

            // Join all markers
            string markersString = string.Join(",", markers);

            // Mapbox Static API URL with multiple markers
            mapUrl = $"https://api.mapbox.com/styles/v1/mapbox/streets-v11/static/{markersString}/{centerLng},{centerLat},14,0/400x300?access_token={accessToken}";

            Debug.Log($"Loading group map with {currentGroupPOIs.Count} markers for group: {poi.group_name}");
        }
        else
        {
            // Single POI marker (original logic)
            string markerColor = poi.color.Replace("#", "").ToLower();
            if (markerColor.Length == 3)
            {
                markerColor = $"{markerColor[0]}{markerColor[0]}{markerColor[1]}{markerColor[1]}{markerColor[2]}{markerColor[2]}";
            }

            // Mapbox Static API URL with single marker
            mapUrl = $"https://api.mapbox.com/styles/v1/mapbox/streets-v11/static/pin-s-marker+{markerColor}({poi.lng},{poi.lat})/{poi.lng},{poi.lat},15,0/400x300?access_token={accessToken}";

            Debug.Log($"Loading single POI map for: {poi.label}");
        }

        UnityWebRequest request = UnityWebRequestTexture.GetTexture(mapUrl);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Texture2D texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
            infoMapImage.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            Debug.Log("Mapbox map loaded successfully");
        }
        else
        {
            Debug.LogError("Failed to load map: " + request.error);
            // Optionally set a default/fallback image
            infoMapImage.sprite = null;
        }
    }
}
