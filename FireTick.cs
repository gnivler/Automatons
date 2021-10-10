using System;
using UnityEngine;
using static Automatons.Helper;

namespace Automatons
{
    public class FireTick : MonoBehaviour
    {
        private void Awake()
        {
            InvokeRepeating(nameof(Tick), 1, 1f);
        }

        private void Tick()
        {
            ProcessBurningObjects();
        }
    }
}
