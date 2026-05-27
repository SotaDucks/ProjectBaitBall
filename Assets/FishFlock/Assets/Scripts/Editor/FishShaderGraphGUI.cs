using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class FishShaderGraphGUI : ShaderGUI
{

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        // base.OnGUI(materialEditor, properties);

        bool emissionOn = false;
        foreach(var prop in properties)
        {
            if (prop.displayName.Contains("unity_")) continue;

            if (prop.name.Contains("_EnableEmission"))
            {
                EditorGUILayout.BeginHorizontal();
                emissionOn = prop.floatValue == 1f;
            }

            if (prop.name.Contains("_EmissionMap") || prop.name.Contains("_EmissionColor"))
                EditorGUI.BeginDisabledGroup(!emissionOn);

            materialEditor.ShaderProperty(prop, prop.displayName);

            if (prop.name.Contains("_EmissionMap") || prop.name.Contains("_EmissionColor"))
                EditorGUI.EndDisabledGroup();

            if (prop.name.Contains("_EnableEmission"))
                EditorGUILayout.EndHorizontal();
        }
    }
}
