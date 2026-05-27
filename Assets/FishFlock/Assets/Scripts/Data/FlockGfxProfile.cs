using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishFlock
{
    [CreateAssetMenu(fileName = "New FlockGfxProfile", menuName = "Fish Flock/Create Flock Gfx Profile")]
    public class FlockGfxProfile : ScriptableObject
    {
        [Tooltip("The prefab that will be instantiated to manipulate the fishes. If you are using instancing this is not going to be used.")]
        public GameObject prefab;
        [Tooltip("Mesh to be drawn when using instancing.")]
        public Mesh mesh;
        [Tooltip("Instanced Material to be used to draw the fishes.")]
        public Material material;
        public ShadowCastingMode shadowCasting = ShadowCastingMode.On;
        public bool receiveShadows = true;
    }
}
