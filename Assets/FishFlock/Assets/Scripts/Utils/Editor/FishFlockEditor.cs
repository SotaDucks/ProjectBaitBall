using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEditor;
using System.Reflection;
using System;
using System.Linq;
using Object = UnityEngine.Object;

namespace FishFlock
{
    static class FishFlockEditorUtil
    {
        public static readonly string[] _dontIncludeMe = new string[] { "m_Script" };
        public static GUIStyle label_style = new GUIStyle(EditorStyles.boldLabel);
    }

    [CustomEditor(typeof(FishFlockController))]
    public class FishFlockV2Editor : Editor
    {
        public Dictionary<string,string> targetFollowingGroup2 = new Dictionary<string, string>()
        {
            { "followTarget", "" },
            { "target", ""}
        };

        public Dictionary<string,string> settingsGroup = new Dictionary<string,string>()
        {
            { "swimmingAreaWidth", "The width limit of the swimming area where the group can swim." },
            { "swimmingAreaHeight", "The height limit of the swimming area where the group can swim." },
            { "swimmingAreaDepth", "The depth limit of the swimming area where the group can swim." },

            { "FishMovementAxis", "The movement axis of the fishes. This must be set before play mode to work properly." },
            { "debugDraw", "Draw the gizmos or debug lines on the scene view." },

            { "renderingMode", "" },
            { "computationMode", "" }
        };

        public Dictionary<string,string> flockingGroup = new Dictionary<string,string>()
        {
            { "fishesCount", "" },
            { "speed", "Min/Max speed to be applied on the fish direction vector." },
            { "rotation", "Min/Max turn speed when rotating the fish to it's direction vector." },

            { "neighbourDistance", "Desired distance between neighbours." },
            { "spawnRadius", "Spawn Radius of the fishes." },
            { "scale", "" },

            { "cohesionScale", "" },
        };

        public Dictionary<string,string> targetFollowingGroup = new Dictionary<string,string>()
        {
            { "followTarget", "Follow the specified target or not?" },
            { "target", "The transform target that the group will follow." },
        };

        public Dictionary<string,string> randomTargetPointsFollowingGroup = new Dictionary<string,string>()
        {
            { "targetPointsAmount", "Min/Max target points that will randomly be generated to follow if not following a target." },
            { "recalculatePoints", "Recalculate points after the group reaches the last one." },
            { "groupAreaSpeed", "The speed in which the group area will move." },
        };

        public Dictionary<string,string> gfxGroup = new Dictionary<string,string>()
        {
            { "computeShader", "" },
            { "gfxProfile", "" }
        };

        public Dictionary<string,string> collisionAvoidanceGroup = new Dictionary<string,string>()
        {
            { "force", "Avoidance force when checking collisions with the boxes." },
            { "colliders", "Colliders that the fishes will try to avoid." },
            { "updateAtRuntime", "Updates and recalculate the colliders bounds. If disabled it will only get the collider position/rotation/scale at the beggining of the application." },
        };

        static bool foldoutGroupItems;
        static bool foldoutGfxProfile;

        Dictionary<string, SerializedProperty> properties = new Dictionary<string, SerializedProperty>();

        private void OnEnable()
        {
            Type osType = typeof(FishFlockController);
            FieldInfo[] osFields = osType.GetFields();
            for(int i = 0; i < osFields.Length; i++)
            {
                FieldInfo fieldInfo = osFields[i];     
                properties.Add(fieldInfo.Name, serializedObject.FindProperty(fieldInfo.Name));
            }
        }

        public override void OnInspectorGUI()
        {
            FishFlockController controller = (FishFlockController)target;

            serializedObject.Update();
            
            EditorGUILayout.Space();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            FishFlockEditorUtil.label_style.fontSize = 20;
            GUILayout.Label("Fish Flock Controller", FishFlockEditorUtil.label_style);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();

            GUILayout.Space(10);

            //DrawPropertiesExcluding(serializedObject, FishFlockEditorUtil._dontIncludeMe);

            DrawGroup("Settings", settingsGroup);
            DrawGroup("Flocking", flockingGroup);
            DrawGroup("Target Following", targetFollowingGroup);
            DrawGroup("Random Target Points Following", randomTargetPointsFollowingGroup);
            //DrawGroup("Gfx", gfxGroup);

            EditorGUI.BeginChangeCheck();
            DrawArea("Gfx", () =>
            {
                var props = properties.Where(it => gfxGroup.ContainsKey(it.Key));
                for (int i = 0; i < gfxGroup.Count; i++)
                {
                    var key = props.ElementAt(i).Key;
                    var serializedProp = props.ElementAt(i).Value;
                    var tooltip = "";
                    gfxGroup.TryGetValue(props.ElementAt(i).Key, out tooltip);

                    
                    if (key == "gfxProfile" && controller.gfxProfile != null)
                    {
                        var editor = Editor.CreateEditor(controller.gfxProfile);
                        if (editor != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                           // foldoutGfxProfile = EditorGUILayout.Foldout(foldoutGfxProfile, "Gfx Profile");
                            EditorGUILayout.PropertyField(serializedProp, new GUIContent(serializedProp.displayName, tooltip));
                            if(GUILayout.Button(!foldoutGfxProfile ? "Show Profile" : "Hide Profile", EditorStyles.helpBox, GUILayout.Width(Screen.width * 0.15f)))
                            {
                                foldoutGfxProfile = !foldoutGfxProfile;
                            }
                            EditorGUILayout.EndHorizontal();

                            if (foldoutGfxProfile)
                            {
                                EditorGUILayout.BeginVertical(GUI.skin.box);
                                editor.OnInspectorGUI();
                                EditorGUILayout.EndVertical();
                            }
                        }
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(serializedProp, new GUIContent(serializedProp.displayName, tooltip));
                    }
                }

                
            });

            if (EditorGUI.EndChangeCheck())
            {

            }


            //DrawGroup("Collision Avoidance", collisionAvoidanceGroup);
            DrawArea("Collision Avoidance", () => 
            {
                Event evt = Event.current;
       
                EditorGUI.BeginChangeCheck();
 
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lookAheadSteps"), new GUIContent("Look Ahead Steps", 
                    "How much to look ahead for colliders."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("force"), new GUIContent("Force", "Avoidance force when checking collisions with the boxes."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("colliderSizeOffset"), new GUIContent("Collider Size Offset", 
                    "An offset value that is added to the total size of the collider."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("updateAtRuntime"), new GUIContent("Update at Runtime", "Updates and recalculate the colliders bounds. If disabled it will only get the collider position/rotation/scale at the beggining of the application."));
                if(EditorGUI.EndChangeCheck()) 
                {
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                }


                GUILayout.Space(5);
                DrawList("Colliders", "colliders", ref foldoutGroupItems, () => 
                {
                    
                });

                Rect drop_area = GUILayoutUtility.GetLastRect();

                switch (evt.type) {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!drop_area.Contains (evt.mousePosition))
                        return;
                    
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                
                    if (evt.type == EventType.DragPerform) 
                    {
                        DragAndDrop.AcceptDrag ();

                        bool addedItem = false;
                        
                        foreach (Object dragged_object in DragAndDrop.objectReferences) 
                        {             
                            if(dragged_object.GetType() == typeof(GameObject))
                            {   
                                Collider[] colls = (dragged_object as GameObject).GetComponentsInChildren<Collider>();
                                foreach(var coll in colls)
                                {              
                                    ArrayUtility.Add(ref controller.colliders, coll);       
                                    addedItem = true;                
                                }
                            }
                            else if(dragged_object.GetType() == typeof(Collider))
                            {
                                ArrayUtility.Add(ref controller.colliders, dragged_object as Collider);       
                                addedItem = true;    
                            }
                        }
                        if(addedItem)
                        {

                        }
                    }
                    break;
                }
            });   

            serializedObject.ApplyModifiedProperties();
        }

        void DrawGroup(string title, Dictionary<string, string> fields, UnityAction onBeforeDrawField = null, UnityAction onAfterDrawField = null, UnityAction onAfterDrawFields = null, 
                    UnityAction onChangeIsMade = null)
        {
            EditorGUI.BeginChangeCheck();

            DrawArea(title, () => 
            {
                var props = properties.Where(it => fields.ContainsKey(it.Key));
                for(int i = 0; i < fields.Count; i++)
                {
                    onBeforeDrawField?.Invoke();
                    var serializedProp = props.ElementAt(i).Value;
                    var tooltip = "";
                    fields.TryGetValue(props.ElementAt(i).Key, out tooltip);

                    EditorGUILayout.PropertyField(serializedProp, 
                    new GUIContent(serializedProp.displayName, tooltip));
                    onAfterDrawField?.Invoke();
                }

                onAfterDrawFields?.Invoke();
            });

            if(EditorGUI.EndChangeCheck())
                onChangeIsMade?.Invoke();
        }

        void DrawArea(string title, UnityAction onDrawArea = null)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(title);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            onDrawArea?.Invoke();

            EditorGUILayout.EndVertical();
        }

        List<int> indexesToRemove = new List<int>();
        void DrawList(string listTitle, string propertyName, ref bool foldoutFlag, UnityAction onRemoveItem = null)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            foldoutFlag = EditorGUILayout.BeginFoldoutHeaderGroup(foldoutFlag, listTitle);
            EditorGUILayout.EndHorizontal();
            indexesToRemove.Clear();
            
            var modPathsSO = serializedObject.FindProperty(propertyName);
            if(foldoutFlag)
            {
                EditorGUILayout.BeginVertical();

                if(modPathsSO.arraySize > 0)
                {
                    for(int i = 0; i < modPathsSO.arraySize; i++)
                    {
                        SerializedProperty modPathItemSO = modPathsSO.GetArrayElementAtIndex(i);

                        EditorGUILayout.BeginHorizontal();
                        // GUILayout.Space(12);
                        EditorGUILayout.LabelField(i + ":", GUILayout.Width(12));
                        
                        EditorGUILayout.PropertyField(modPathItemSO, new GUIContent(""));

                        Color prevCol = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.85f, 0.2f, 0.2f, 1f);
                        if(GUILayout.Button("X", GUILayout.Width(27)))
                        {
                            indexesToRemove.Add(i);
                        }
                        GUI.backgroundColor = prevCol;
                        EditorGUILayout.EndHorizontal();
                    }
                }
                else 
                {
                    GUILayout.Label("Drag and Drop prefabs here");
                }

                GUILayout.Space(10);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            var indexesRemoved = indexesToRemove.Count;
            foreach(var indexRemove in indexesToRemove)
            {
                modPathsSO.DeleteArrayElementAtIndex(indexRemove);
                modPathsSO.DeleteArrayElementAtIndex(indexRemove);
            }

            if(indexesRemoved > 0)
            {
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
                onRemoveItem?.Invoke();

            }
        }
    }
}
