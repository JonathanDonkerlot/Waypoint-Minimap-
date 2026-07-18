using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using MelonLoader;
public static class WaypointManager
{
    private class Waypoint
    {
        public Vector3 Position;
        public Color Color;
        public GameObject Beam;
        public GameObject Label;
        public TextMesh Text;
    }

    private static readonly List<Waypoint> waypoints = new List<Waypoint>();

    private static bool loaded = false;

    private static readonly string SaveFile =
        Path.Combine(
            Directory.GetCurrentDirectory(),
            "MelonLoader",
            "Waypoints.txt");

    public static void Init()
    {
        EnsureSaveFile();
        LoadWaypoints();
    }

    public static void Update()
    {
        if (!loaded && Camera.main != null)
        {
            loaded = true;

            foreach (Waypoint wp in waypoints)
            {
                TextMesh text;
                wp.Beam = CreateBeam(wp.Position, wp.Color);
                wp.Label = CreateLabel(wp.Position, wp.Color, out text);
                wp.Text = text;
            }

            MelonLogger.Msg($"[Waypoint] Spawned {waypoints.Count} saved waypoint(s).");
        }

        UpdateWaypointLabels();
    }

    public static void PlaceWaypoint()
    {
        Transform player = GetPlayerTransform();

        if (player == null)
        {
            MelonLogger.Warning("[Waypoint] Player transform not found.");
            return;
        }

        Vector3 pos = player.position + player.forward * 2f;

        Color color = Color.HSVToRGB(
            UnityEngine.Random.value,
            1f,
            1f);

        TextMesh text;

        Waypoint wp = new Waypoint
        {
            Position = pos,
            Color = color,
            Beam = CreateBeam(pos, color),
            Label = CreateLabel(pos, color, out text),
            Text = text
        };

        waypoints.Add(wp);

        SaveWaypoints();

        MelonLogger.Msg($"[Waypoint] Created waypoint #{waypoints.Count}");
    }

    public static void DeleteLastWaypoint()
    {
        if (waypoints.Count == 0)
            return;

        Waypoint wp = waypoints[waypoints.Count - 1];

        if (wp.Beam != null)
            GameObject.Destroy(wp.Beam);
        if (wp.Label != null)
            GameObject.Destroy(wp.Label);

        waypoints.RemoveAt(waypoints.Count - 1);

        SaveWaypoints();

        MelonLogger.Msg("[Waypoint] Deleted last waypoint.");
    }

    public static void ClearWaypoints()
    {
        foreach (Waypoint wp in waypoints)
        {
            if (wp.Beam != null)
                GameObject.Destroy(wp.Beam);
            if (wp.Label != null)
                GameObject.Destroy(wp.Label);
        }

        waypoints.Clear();

        SaveWaypoints();

        MelonLogger.Msg("[Waypoint] Cleared all waypoints.");
    }

    private static readonly List<(Vector3 Position, Color Color)> displayWaypoints = new();
    public static List<(Vector3 Position, Color Color)> GetWaypointsForDisplay()
    {
        displayWaypoints.Clear();

        foreach (Waypoint wp in waypoints)
        {
            displayWaypoints.Add((wp.Position, wp.Color));
        }

        return displayWaypoints;
    }

    private static Transform GetPlayerTransform()
    {
        if (Camera.main != null)
            return Camera.main.transform;

        return null;
    }

    private static GameObject CreateBeam(Vector3 position, Color color)
    {
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);

        beam.name = $"Waypoint_{waypoints.Count + 1}";

        Collider col = beam.GetComponent<Collider>();
        if (col != null)
            GameObject.Destroy(col);

        beam.transform.position = position + Vector3.up * 20f;
        beam.transform.localScale = new Vector3(0.3f, 20f, 0.3f);

        Renderer renderer = beam.GetComponent<Renderer>();

        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = color;

            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 3f);

            renderer.material = mat;
        }

        return beam;
    }
    private static GameObject CreateLabel(Vector3 position, Color color, out TextMesh textMesh)
    {
        GameObject labelObject = new GameObject("WaypointLabel");
        labelObject.transform.position = position + Vector3.up * 31f;
        labelObject.transform.localScale = Vector3.one * 3f;

        textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.text = "0m";
        textMesh.characterSize = 0.3f;
        textMesh.fontSize = 48;
        textMesh.color = color;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;

        return labelObject;
    }
    private static void UpdateWaypointLabels()
    {
        if (Camera.main == null) return;
        Transform cam = Camera.main.transform;

        foreach (Waypoint wp in waypoints)
        {
            if (wp.Label == null) continue;

            Transform labelTransform = wp.Label.transform;

            labelTransform.LookAt(cam);
            labelTransform.Rotate(0f, 180f, 0f);

            if (wp.Text != null)
            {
                float distance = Vector3.Distance(cam.position, wp.Position);

                string newText = $"{distance:F0}m";

                if (wp.Text.text != newText)
                    wp.Text.text = newText;
            }
        }
    }

    private static void EnsureSaveFile()
    {
        string folder = Path.GetDirectoryName(SaveFile);

        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        if (!File.Exists(SaveFile))
            File.WriteAllText(SaveFile, "");
    }

    private static void SaveWaypoints()
    {
        using (StreamWriter writer = new StreamWriter(SaveFile, false))
        {
            foreach (Waypoint wp in waypoints)
            {
                writer.WriteLine(string.Join(",",
                    wp.Position.x.ToString(CultureInfo.InvariantCulture),
                    wp.Position.y.ToString(CultureInfo.InvariantCulture),
                    wp.Position.z.ToString(CultureInfo.InvariantCulture),
                    wp.Color.r.ToString(CultureInfo.InvariantCulture),
                    wp.Color.g.ToString(CultureInfo.InvariantCulture),
                    wp.Color.b.ToString(CultureInfo.InvariantCulture)
                ));
            }
        }
    }

    private static void LoadWaypoints()
    {
        waypoints.Clear();

        string[] lines = File.ReadAllLines(SaveFile);

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] parts = line.Split(',');

            if (parts.Length != 6)
                continue;

            Vector3 pos = new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));

            Color color = new Color(
                float.Parse(parts[3], CultureInfo.InvariantCulture),
                float.Parse(parts[4], CultureInfo.InvariantCulture),
                float.Parse(parts[5], CultureInfo.InvariantCulture)
            );

            waypoints.Add(new Waypoint
            {
                Position = pos,
                Color = color,
                Beam = null
            });
        }

        MelonLogger.Msg($"[Waypoint] Loaded {waypoints.Count} waypoint(s) from disk.");
    }

}