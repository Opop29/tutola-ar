using UnityEngine;
using UnityEngine.SceneManagement;

public class FixXRSerialization : MonoBehaviour
{
    [ContextMenu("Fix TrackedPoseDriver Components")]
    public void FixTrackedPoseDrivers()
    {
        Debug.Log("🔧 Starting XR Serialization Fix...");

        // Find all GameObjects in the current scene
        GameObject[] allObjects = SceneManager.GetActiveScene().GetRootGameObjects();

        int fixedCount = 0;

        foreach (GameObject obj in allObjects)
        {
            // Find all components in this object and its children
            Component[] components = obj.GetComponentsInChildren<Component>(true);

            foreach (Component comp in components)
            {
                if (comp != null && comp.GetType().Name.Contains("TrackedPoseDriver"))
                {
                    Debug.Log($"📍 Found TrackedPoseDriver on: {comp.gameObject.name}");

                    // Remove the corrupted component
                    DestroyImmediate(comp);
                    Debug.Log($"🗑️ Removed corrupted TrackedPoseDriver from: {comp.gameObject.name}");

                    // Add a new clean TrackedPoseDriver
                    // Note: We can't add XR components programmatically in edit mode
                    // This will need to be done manually in the inspector

                    fixedCount++;
                }
            }
        }

        if (fixedCount > 0)
        {
            Debug.Log($"✅ Fixed {fixedCount} TrackedPoseDriver components");
            Debug.Log("⚠️ IMPORTANT: You now need to manually add new TrackedPoseDriver components to the affected GameObjects");
        }
        else
        {
            Debug.Log("ℹ️ No TrackedPoseDriver components found to fix");
        }
    }

    [ContextMenu("List All XR Components")]
    public void ListXRComponents()
    {
        Debug.Log("🔍 Listing all XR-related components...");

        GameObject[] allObjects = SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (GameObject obj in allObjects)
        {
            Component[] components = obj.GetComponentsInChildren<Component>(true);

            foreach (Component comp in components)
            {
                if (comp != null && (comp.GetType().Name.Contains("XR") ||
                                   comp.GetType().Name.Contains("TrackedPose") ||
                                   comp.GetType().Name.Contains("AR")))
                {
                    Debug.Log($"📍 XR Component: {comp.GetType().Name} on {comp.gameObject.name}");
                }
            }
        }
    }
}