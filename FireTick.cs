using UnityEngine;
using static Automatons.Helper;

namespace Automatons
{
    public class FireTick : MonoBehaviour
    {
        private void Awake()
        {
            InvokeRepeating(nameof(Tick), 1, 2f);
        }

        private void Tick()
        {
            DoFirefighting();
        }
    }
}
