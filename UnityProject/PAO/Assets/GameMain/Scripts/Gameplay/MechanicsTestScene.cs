using UnityEngine;

namespace PAO
{
    /// <summary>Scene opt-in for the pause menu's mechanics testing tools.</summary>
    public sealed class MechanicsTestScene : MonoBehaviour
    {
        [SerializeField] private Vector3 m_SpawnPosition = new Vector3(.55f, .59f, -3.37f);

        public void ReturnToSpawn(PlayerController player)
        {
            if (player == null) return;
            BubbleRideInteractor ride = player.GetComponent<BubbleRideInteractor>();
            if (ride != null) ride.EndInteraction();
            player.Teleport(m_SpawnPosition, Quaternion.identity);
        }
    }
}
