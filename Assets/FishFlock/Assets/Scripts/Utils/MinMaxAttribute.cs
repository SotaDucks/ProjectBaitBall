using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FishFlock.Utils
{
    public class MinMaxAttribute : PropertyAttribute
    {
        public float Min;        public float Max;

        public MinMaxAttribute(float min, float max)
        {
            Min = min;
            Max = max;
        }
    }
}