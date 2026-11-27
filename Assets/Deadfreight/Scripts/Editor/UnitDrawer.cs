// Scripts/Editor/UnitDrawer.cs
using UnityEditor;
using UnityEngine;
using Deadfreight.Core;

namespace Deadfreight.EditorTools
{
    [CustomPropertyDrawer(typeof(UnitAttribute))]
    public sealed class UnitDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.PropertyField(position, property, label);

            var unit = new GUIContent(((UnitAttribute)attribute).Unit);
            var style = EditorStyles.miniLabel;
            float width = style.CalcSize(unit).x + 4f;
            var unitRect = new Rect(position.xMax - width, position.y, width, position.height);

            using (new EditorGUI.DisabledScope(true))
                EditorGUI.LabelField(unitRect, unit, style);
        }
    }
}
