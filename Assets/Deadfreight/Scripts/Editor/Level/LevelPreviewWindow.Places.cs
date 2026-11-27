// Scripts/Editor/Level/LevelPreviewWindow.Places.cs
using UnityEditor;
using UnityEngine;
using Deadfreight.Level;

namespace Deadfreight.EditorTools
{
    // The playable area, where the road comes in, and the places, drawn over the map. The window itself is in
    // LevelPreviewWindow.cs.
    public sealed partial class LevelPreviewWindow
    {
        const float BorderDim = 0.55f;      // how much the border's cells are darkened

        static readonly Color StartColor = new(0.3f, 0.9f, 0.3f);
        static readonly Color SiteColor = new(0.95f, 0.3f, 0.3f);
        static readonly Color FarmsteadColor = new(0.85f, 0.85f, 0.8f);
        static readonly Color HamletColor = new(0.75f, 0.5f, 1f);

        [SerializeField] bool showPlaces = true;
        [SerializeField] bool showPlaceLabels = true;

        static Color PlaceColor(PlaceKind kind) => kind switch
        {
            PlaceKind.Start => StartColor,
            PlaceKind.Site => SiteColor,
            PlaceKind.Hamlet => HamletColor,
            _ => FarmsteadColor,
        };

        /// <summary>Darker outside the playable area, so the border reads as not-for-play.</summary>
        Color DimBorder(int i, Color c) =>
            plan.Entry != null && !plan.InPlayArea(plan.CellAt(i)) ? c * BorderDim : c;

        void DrawPlaces()
        {
            if (plan.Entry == null || !showPlaces)
                return;

            DrawPlayArea();
            DrawRoadIn();

            var label = new GUIStyle(EditorStyles.miniBoldLabel);
            label.normal.textColor = Color.white;
            foreach (var place in plan.Places)
            {
                var at = ToScreen(place.Cell);
                float radius = place.Kind is PlaceKind.Start or PlaceKind.Site ? 6f : 4f;
                Handles.color = Color.black;
                Handles.DrawSolidDisc(at, Vector3.forward, radius + 1.5f);
                Handles.color = PlaceColor(place.Kind);
                Handles.DrawSolidDisc(at, Vector3.forward, radius);
                if (showPlaceLabels)
                    GUI.Label(new Rect(at.x + radius + 3f, at.y - 8f, 120f, 16f), place.Kind.ToString(), label);
            }
        }

        /// <summary>A thin outline round the playable area.</summary>
        void DrawPlayArea()
        {
            var area = plan.PlayArea;
            var rect = new Rect(
                mapRect.x + area.xMin * mapScale,
                mapRect.y + (plan.CellsY - area.yMax) * mapScale,     // the plan's y runs north, the screen's down
                area.width * mapScale,
                area.height * mapScale);
            Handles.DrawSolidRectangleWithOutline(rect, Color.clear, new Color(1f, 1f, 1f, 0.6f));
        }

        /// <summary>
        /// Where the road comes in: an arrow at the map edge pointing inland. The road itself is
        /// the start of the main road, drawn with the roads.
        /// </summary>
        void DrawRoadIn()
        {
            Vector3 edge = ToScreen(plan.Entry.Cell), start = ToScreen(plan.Start.Cell);
            var inward = (start - edge).normalized;
            var across = new Vector3(-inward.y, inward.x, 0f);
            var tip = edge + inward * 14f;
            Handles.color = StartColor;
            Handles.DrawAAConvexPolygon(tip, edge + across * 7f, edge - across * 7f);
        }

        /// <summary>What the panel says about the anchors.</summary>
        void DrawPlaceSummary()
        {
            if (plan.Entry == null)
                return;

            int farmsteads = 0;
            foreach (var p in plan.Places)
                if (p.Kind == PlaceKind.Farmstead) farmsteads++;
            float siteKm = Vector2Int.Distance(plan.Start.Cell, plan.Site.Cell) * plan.CellSize / 1000f;
            float playKm = plan.PlayArea.width * plan.CellSize / 1000f;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Places", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Road in from the {plan.Entry.Edge} edge · playable {playKm:0.#} km across", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Site {siteKm:0.0} km from the start (straight line) · {farmsteads} farmsteads", EditorStyles.miniLabel);
        }

        /// <summary>The place standing on a cell, if any.</summary>
        Place PlaceAt(Vector2Int cell)
        {
            foreach (var p in plan.Places)
                if (p.Cell == cell) return p;
            return null;
        }
    }
}
