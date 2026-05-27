// (Elvis L) Note: Heavily inspired from https://timaksu.com/post/126337219047/spruce-up-your-custom-unity-inspectors-with-a and https://www.blog.radiator.debacle.us/2016/06/working-with-custom-objectpreviews-and.html

using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using FishFlock;
namespace FishFlock
{
	[CustomPreview(typeof(FlockGfxProfile))]
	public class FlockGfxProfileRenderPreview : ObjectPreview
	{
		PreviewRenderUtility m_PreviewUtility;
		FlockGfxProfile profile;

		static Vector2 previewDir = new Vector2(-180f, 0f);
		static float previewCamDistance = -10.5f;

		public override bool HasPreviewGUI() { return true; }

		private void Init()
		{
			if (this.m_PreviewUtility == null)
			{
				this.m_PreviewUtility = new PreviewRenderUtility();
				this.m_PreviewUtility.cameraFieldOfView = 27f;
			}

			profile = target as FlockGfxProfile;
		}

		public override void OnPreviewGUI(Rect r, GUIStyle background)
		{

			// if this is happening, you have bigger problems
			if (!ShaderUtil.hardwareSupportsRectRenderTexture)
			{
				if (Event.current.type == EventType.Repaint)
				{
					EditorGUI.DropShadowLabel(new Rect(r.x, r.y, r.width, 40f), "Mesh preview requires\nrender texture support");
				}
				return;
			}

			Init();
			previewDir = Drag2D(previewDir, r);
			if (Event.current.type != EventType.Repaint)
			{
				return;
			}

			if (profile.mesh == null)
			{
				EditorGUI.DropShadowLabel(new Rect(r.x, r.y, r.width, 40f), "Mesh Required");

			}
			else if (profile.material == null)
			{
				EditorGUI.DropShadowLabel(new Rect(r.x, r.y, r.width, 40f), "Material Required");
			}
			else
			{
				m_PreviewUtility.BeginPreview(r, background); 

				RenderMeshPreview(profile.mesh, m_PreviewUtility, profile.material, previewDir, -1);

				Texture image = m_PreviewUtility.EndPreview(); 
				GUI.DrawTexture(r, image, ScaleMode.StretchToFill, false);
				//EditorGUI.DropShadowLabel(new Rect(r.x, r.y, r.width, 40f), target.name);
			}

			OnDestroy();
		}

		void RenderMeshPreview(Mesh mesh, PreviewRenderUtility previewUtility, Material material, Vector2 direction, int meshSubset)
		{
			Bounds bounds = mesh.bounds;
			float magnitude = bounds.extents.magnitude;
			float distance = 30f * magnitude;

			// setup the ObjectPreview's camera
			previewUtility.camera.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
			previewUtility.camera.clearFlags = CameraClearFlags.Color;
			previewUtility.camera.transform.position = new Vector3(0f, 0.0f, previewCamDistance);
			previewUtility.camera.transform.rotation = Quaternion.identity;
			previewUtility.camera.nearClipPlane = 0.3f;
			previewUtility.camera.farClipPlane = distance + magnitude * 1.1f;

			Quaternion quaternion = Quaternion.Euler(-direction.y, direction.x, 0f);
			Vector3 pos = quaternion * -bounds.center;

			pos.z += bounds.size.z;

			bool fog = RenderSettings.fog;
			Unsupported.SetRenderSettingsUseFogNoDirty(false);

			// submesh support
			int subMeshCount = mesh.subMeshCount;
			if (meshSubset < 0 || meshSubset >= subMeshCount)
			{
				for (int i = 0; i < subMeshCount; i++)
				{
					previewUtility.DrawMesh(mesh, pos, quaternion, material, i);
				}
			}
			else
			{
				previewUtility.DrawMesh(mesh, pos, quaternion, material, meshSubset);
			}

			previewUtility.camera.Render();

			Unsupported.SetRenderSettingsUseFogNoDirty(fog);
		}

		public override void OnPreviewSettings()
		{

		}

		void OnDestroy()
		{
			if (this.m_PreviewUtility != null)
			{
				this.m_PreviewUtility.Cleanup();
				this.m_PreviewUtility = null;
			}
		}

		// from http://timaksu.com/post/126337219047/spruce-up-your-custom-unity-inspectors-with-a
		public static Vector2 Drag2D(Vector2 scrollPosition, Rect position)
		{
			int controlID = GUIUtility.GetControlID("Slider".GetHashCode(), FocusType.Passive);
			Event current = Event.current;
			switch (current.GetTypeForControl(controlID))
			{
				case EventType.MouseDown:
					if (position.Contains(current.mousePosition) && position.width > 50f)
					{
						GUIUtility.hotControl = controlID;
						current.Use();
						EditorGUIUtility.SetWantsMouseJumping(1);
					}
					break;
				case EventType.MouseUp:
					if (GUIUtility.hotControl == controlID)
					{
						GUIUtility.hotControl = 0;
					}
					EditorGUIUtility.SetWantsMouseJumping(0);
					break;
				case EventType.MouseDrag:
					if (GUIUtility.hotControl == controlID)
					{
						scrollPosition -= current.delta * (float)((!current.shift) ? 1 : 3) / Mathf.Min(position.width, position.height) * 140f;
						scrollPosition.y = Mathf.Clamp(scrollPosition.y, -90f, 90f);
						current.Use();
						GUI.changed = true;
					}
					break;
                case EventType.ScrollWheel:
                    if (current.isScrollWheel)
                    {
                        previewCamDistance -= current.delta.y;
						previewCamDistance = Mathf.Clamp(previewCamDistance, -20.5f, 5f);


						current.Use();
                        GUI.changed = true;
                    }
                    break;
            }
			return scrollPosition;
		}

	}
}