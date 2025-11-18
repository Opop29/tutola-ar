using UnityEngine;
using TMPro;
using UnityEngine.Android;
using UnityEngine.UI;
using System.Collections;

public class ARButtonController : MonoBehaviour
{
    public GameObject xrOrigin; // reference to XR Origin
    public GameObject canvasUI; // reference to Canvas UI
    public TMP_Text coordinateText; // reference to TMP text for coordinates
    public CanvasGroup canvasGroup; // reference to Canvas Group for fading
    public float fadeDuration = 0.5f; // duration of fade effect

    private void Start()
    {
        // Request GPS permission
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
        }

        // Start updating coordinates
        InvokeRepeating("UpdateCoordinates", 0f, 1f); // Update every second
    }

    public void ShowAR()
    {
        StartCoroutine(FadeOutAndShowAR());
    }

    public void HideAR()
    {
        StartCoroutine(FadeInAndHideAR());
    }

    private IEnumerator FadeOutAndShowAR()
    {
        // Fade out canvas
        if (canvasGroup != null)
        {
            float startAlpha = canvasGroup.alpha;
            float time = 0;

            while (time < fadeDuration)
            {
                time += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, time / fadeDuration);
                yield return null;
            }

            canvasGroup.alpha = 0f;
        }

        // Show XR Origin and hide Canvas UI
        if (xrOrigin != null)
        {
            xrOrigin.SetActive(true);
        }
        if (canvasUI != null)
        {
            canvasUI.SetActive(false);
        }
    }

    private IEnumerator FadeInAndHideAR()
    {
        // Hide XR Origin and show Canvas UI
        if (xrOrigin != null)
        {
            xrOrigin.SetActive(false);
        }
        if (canvasUI != null)
        {
            canvasUI.SetActive(true);
        }

        // Fade in canvas
        if (canvasGroup != null)
        {
            float startAlpha = canvasGroup.alpha;
            float time = 0;

            while (time < fadeDuration)
            {
                time += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, time / fadeDuration);
                yield return null;
            }

            canvasGroup.alpha = 1f;
        }
    }

    private void UpdateCoordinates()
    {
        if (coordinateText == null) return;

        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            coordinateText.text = "Please open GPS";
            return;
        }

        if (!Input.location.isEnabledByUser)
        {
            coordinateText.text = "Please open GPS";
            return;
        }

        // Check if location service is running
        if (Input.location.status == LocationServiceStatus.Running)
        {
            float latitude = Input.location.lastData.latitude;
            float longitude = Input.location.lastData.longitude;
            coordinateText.text = $"{latitude:F6}, {longitude:F6}";
        }
        else
        {
            coordinateText.text = "Please open GPS";
        }
    }

    private void OnEnable()
    {
        // Start location service
        if (!Input.location.isEnabledByUser)
        {
            Input.location.Start();
        }
    }

    private void OnDisable()
    {
        // Stop location service
        Input.location.Stop();
    }

    public void ExitApplication()
    {
        Application.Quit();
    }
}
