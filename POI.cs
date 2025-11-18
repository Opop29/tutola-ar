using System;

[System.Serializable]
public class POI
{
    public int id; // Primary key
    public float lat;
    public float lng;
    public string label;
    public string mark_type;
    public string color; // e.g., "#ff0000" for red, "#00ff00" for green, etc.
    public int height;
    public string[] dates;
    public string group_name; // Group name for grouping POIs
    public int group_index; // Index within the group
    public string created_at;
    public string updated_at;

    // Computed properties for compatibility
    public float latitude => lat;
    public float longitude => lng;
}

[System.Serializable]
public class POIList
{
    public POI[] pois;
}