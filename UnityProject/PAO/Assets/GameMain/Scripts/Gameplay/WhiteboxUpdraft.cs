using System.Collections.Generic;
using UnityEngine;

namespace PAO
{
    /// <summary>Environmental vertical current used by the Map3.5 whitebox.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WhiteboxUpdraft : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float m_LiftSpeed = 3.4f;
        private readonly Dictionary<PlayerController, float> m_PreviousLiftSpeeds = new Dictionary<PlayerController, float>();
        private readonly HashSet<PlayerController> m_PreviousLiftStates = new HashSet<PlayerController>();

        public void Configure(float liftSpeed) => m_LiftSpeed = Mathf.Max(0.1f, liftSpeed);

        private void OnTriggerStay(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;
            if (!m_PreviousLiftSpeeds.ContainsKey(player))
            {
                m_PreviousLiftSpeeds[player] = player.LiftSpeed;
                if (player.IsLifted) m_PreviousLiftStates.Add(player);
            }
            player.IsLifted = true;
            player.LiftSpeed = m_LiftSpeed;
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || !m_PreviousLiftSpeeds.TryGetValue(player, out float previousSpeed)) return;
            player.LiftSpeed = previousSpeed;
            player.IsLifted = m_PreviousLiftStates.Contains(player);
            m_PreviousLiftSpeeds.Remove(player);
            m_PreviousLiftStates.Remove(player);
        }

        private void OnDisable()
        {
            foreach (KeyValuePair<PlayerController, float> entry in m_PreviousLiftSpeeds)
            {
                if (entry.Key == null) continue;
                entry.Key.LiftSpeed = entry.Value;
                entry.Key.IsLifted = m_PreviousLiftStates.Contains(entry.Key);
            }
            m_PreviousLiftSpeeds.Clear();
            m_PreviousLiftStates.Clear();
        }
    }
}
