using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TestBoids.Boids.Editor
{
    [CustomEditor(typeof(FishSchoolManager))]
    public sealed class FishSchoolManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            FishSchoolManager manager = (FishSchoolManager)target;
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Stable Opening Bake", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bake runs the fish-school simulation in edit mode, saves the stable poses and velocities, then the manager can use that snapshot as the game's first-frame state.",
                MessageType.Info);
            EditorGUILayout.LabelField("Baked fish", manager.BakedInitialStateCount.ToString());

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Bake Stable Initial State"))
                {
                    Undo.RegisterFullObjectHierarchyUndo(manager.gameObject, "Bake Fish School Initial State");
                    manager.BakeStableInitialStateForEditor();
                    EditorUtility.SetDirty(manager);
                    EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                }

                if (GUILayout.Button("Clear Baked Initial State"))
                {
                    Undo.RecordObject(manager, "Clear Fish School Initial State");
                    manager.ClearBakedInitialStateForEditor();
                    EditorUtility.SetDirty(manager);
                    EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                }
            }
        }
    }
}
