using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.Networking;

public class StartButtonLoader : MonoBehaviour
{
    [Header("UI References")]
    public Button startButton;
    public Slider loadingBar;
    public TMP_Text advisoryText;
    public TMP_Text percentageText;
    public GameObject noInternetPanel;
    public TMP_Text noInternetText;
    public Button retryButton;
    public GameObject completionPanel;

    [Header("Loading Settings")]
    public float loadingSpeed = 0.25f; // Adjust for slower/faster loading

    private Coroutine internetMonitorCoroutine;

    private void Start()
    {
        // Ensure CanvasGroups are present for smooth transitions
        if (!loadingBar.gameObject.GetComponent<CanvasGroup>()) loadingBar.gameObject.AddComponent<CanvasGroup>();
        if (!advisoryText.gameObject.GetComponent<CanvasGroup>()) advisoryText.gameObject.AddComponent<CanvasGroup>();
        if (!percentageText.gameObject.GetComponent<CanvasGroup>()) percentageText.gameObject.AddComponent<CanvasGroup>();
        if (!completionPanel.GetComponent<CanvasGroup>()) completionPanel.AddComponent<CanvasGroup>();

        // Set initial alphas
        loadingBar.GetComponent<CanvasGroup>().alpha = 1f;
        advisoryText.GetComponent<CanvasGroup>().alpha = 1f;
        percentageText.GetComponent<CanvasGroup>().alpha = 1f;
        completionPanel.GetComponent<CanvasGroup>().alpha = 0f;

        // Hide loading elements at start
        loadingBar.gameObject.SetActive(false);
        advisoryText.gameObject.SetActive(false);
        percentageText.gameObject.SetActive(false);
        noInternetPanel.SetActive(false);
        completionPanel.SetActive(false);

        // Add button listeners
        startButton.onClick.AddListener(OnStartButtonClick);
        retryButton.onClick.AddListener(OnRetryButtonClick);
    }

    private void OnStartButtonClick()
    {
        // Hide start button when clicked
        startButton.gameObject.SetActive(false);

        // Show loading elements and ensure alphas are set
        loadingBar.value = 0;
        loadingBar.gameObject.SetActive(true);
        loadingBar.GetComponent<CanvasGroup>().alpha = 1f;
        advisoryText.gameObject.SetActive(true);
        advisoryText.GetComponent<CanvasGroup>().alpha = 1f;
        percentageText.gameObject.SetActive(true);
        percentageText.GetComponent<CanvasGroup>().alpha = 1f;

        // Start loading sequence
        StartCoroutine(LoadingSequence());
    }

    private void OnRetryButtonClick()
    {
        // Hide no internet panel
        noInternetPanel.SetActive(false);

        // Stop internet monitoring if it's running
        if (internetMonitorCoroutine != null)
        {
            StopCoroutine(internetMonitorCoroutine);
            internetMonitorCoroutine = null;
        }

        // Show loading elements and reset alphas
        loadingBar.value = 0;
        loadingBar.gameObject.SetActive(true);
        loadingBar.GetComponent<CanvasGroup>().alpha = 1f;
        advisoryText.gameObject.SetActive(true);
        advisoryText.GetComponent<CanvasGroup>().alpha = 1f;
        percentageText.gameObject.SetActive(true);
        percentageText.GetComponent<CanvasGroup>().alpha = 1f;

        // Restart loading sequence
        StartCoroutine(LoadingSequence());
    }

    private IEnumerator LoadingSequence()
    {
        string[] messages = new string[]
        {
            "Please wait...",
            "Fetching data...",
            "Checking internet connection...",
            "Preparing navigation..."
        };

        int messageIndex = 0;
        advisoryText.text = messages[messageIndex];

        float progress = 0f;

        while (progress < 1f)
        {
            progress += Time.deltaTime * loadingSpeed;
            loadingBar.value = progress;
            percentageText.text = Mathf.RoundToInt(progress * 100) + "%";

            // Fetch data at 78%
            if (progress >= 0.78f && progress < 0.79f)
            {
                bool dataFetched = false;
                yield return StartCoroutine(FetchData(result => dataFetched = result));
                if (dataFetched)
                {
                    // Data fetched successfully, continue
                }
                else
                {
                    // Fetch failed, show no internet panel and stop loading
                    noInternetPanel.SetActive(true);
                    noInternetText.text = "Failed to fetch data. Please check your connection and try again.";
                    yield break; // Stop the coroutine
                }
            }

            // Change message every 25% progress
            if (progress > (messageIndex + 1) * 0.25f && messageIndex < messages.Length - 1)
            {
                messageIndex++;
                advisoryText.text = messages[messageIndex];
            }

            yield return null;
        }

        loadingBar.value = 1f;
        percentageText.text = "100%";

        // Final internet check after loading completes
        bool finalInternetCheck = false;
        yield return StartCoroutine(CheckInternetConnection(result => finalInternetCheck = result));

        if (!finalInternetCheck)
        {
            // No internet detected even after loading, show no internet panel
            noInternetPanel.SetActive(true);
            noInternetText.text = "No internet connection detected. Please check your connection and try again.";
            yield break; // Stop the coroutine
        }

        advisoryText.text = "Loading complete! Starting navigation...";
        yield return new WaitForSeconds(1.5f);

        // Smoothly fade out loading UI
        yield return StartCoroutine(FadeOut(loadingBar.GetComponent<CanvasGroup>(), 0.5f));
        yield return StartCoroutine(FadeOut(advisoryText.GetComponent<CanvasGroup>(), 0.5f));
        yield return StartCoroutine(FadeOut(percentageText.GetComponent<CanvasGroup>(), 0.5f));

        // Hide loading UI
        loadingBar.gameObject.SetActive(false);
        advisoryText.gameObject.SetActive(false);
        percentageText.gameObject.SetActive(false);

        // Show and fade in completion panel
        completionPanel.SetActive(true);
        yield return StartCoroutine(FadeIn(completionPanel.GetComponent<CanvasGroup>(), 0.5f));

        // Start monitoring internet connection after completion
        internetMonitorCoroutine = StartCoroutine(MonitorInternetConnection());

        // TODO: Add your navigation start logic here
        Debug.Log("Navigation started!");
    }

    private IEnumerator CheckInternetConnection(System.Action<bool> callback)
    {
        UnityWebRequest request = UnityWebRequest.Get("https://www.google.com");
        yield return request.SendWebRequest();

        bool hasConnection = request.result == UnityWebRequest.Result.Success;
        if (hasConnection)
        {
            Debug.Log("Internet connection available");
        }
        else
        {
            Debug.Log("No internet connection");
        }

        callback(hasConnection);
    }

    private IEnumerator FetchData(System.Action<bool> callback)
    {
        Debug.Log("Refetching data...");

        // Supabase REST API endpoint for your table
        string url = "https://xyyftberwxgynoholndm.supabase.co/rest/v1/ar_pois?select=*";

        UnityWebRequest request = UnityWebRequest.Get(url);

        // Required headers
        request.SetRequestHeader("apikey", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Inh5eWZ0YmVyd3hneW5vaG9sbmRtIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTczMDQ2NDYsImV4cCI6MjA3Mjg4MDY0Nn0.B9TCQeLOjkLJ9KVn3vjUiHJDURZO4bJvOnzHvifVJ5c");
        request.SetRequestHeader("Authorization", "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Inh5eWZ0YmVyd3hneW5vaG9sbmRtIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTczMDQ2NDYsImV4cCI6MjA3Mjg4MDY0Nn0.B9TCQeLOjkLJ9KVn3vjUiHJDURZO4bJvOnzHvifVJ5c");
        request.SetRequestHeader("Content-Type", "application/json");

        // Send request
        yield return request.SendWebRequest();

        // Check for errors
        bool success = request.result == UnityWebRequest.Result.Success;
        if (success)
        {
            Debug.Log("✅ Data fetched successfully!");
            Debug.Log(request.downloadHandler.text);
        }
        else
        {
            Debug.LogError("❌ Failed to fetch data: " + request.error);
            Debug.LogError("Response: " + request.downloadHandler.text);
        }

        callback(success);
    }

    private IEnumerator MonitorInternetConnection()
    {
        while (true)
        {
            bool hasInternet = false;
            yield return StartCoroutine(CheckInternetConnection(result => hasInternet = result));

            if (!hasInternet)
            {
                // Smoothly fade out completion panel
                yield return StartCoroutine(FadeOut(completionPanel.GetComponent<CanvasGroup>(), 0.5f));

                // Hide completion panel and show no internet panel
                completionPanel.SetActive(false);
                noInternetPanel.SetActive(true);
                noInternetText.text = "No internet connection detected. Please check your connection and try again.";

                // Stop monitoring while waiting for user action
                yield break;
            }

            yield return new WaitForSeconds(5f); // Check every 5 seconds when connected
        }
    }

    private IEnumerator FadeOut(CanvasGroup canvasGroup, float duration)
    {
        float startAlpha = canvasGroup.alpha;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, time / duration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
    }

    private IEnumerator FadeIn(CanvasGroup canvasGroup, float duration)
    {
        float startAlpha = canvasGroup.alpha;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, time / duration);
            yield return null;
        }

        canvasGroup.alpha = 1f;
    }
}
