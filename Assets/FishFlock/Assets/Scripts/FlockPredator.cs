using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FishFlock
{
    public class FlockPredator : MonoBehaviour
    {
        public float fleeRadius = 10f;
        public bool drawGizmos = true;

        private void OnDrawGizmos()
        {

            var c = Color.blue;
            c.a = 0.2f;
            Gizmos.color = c;
            Gizmos.DrawWireSphere(transform.position, fleeRadius);
        }
    }
}