using UnityEngine;

namespace PAO
{
    /// <summary>
    /// Simple repeatable whitebox lift. Moves between its authored start point
    /// and one endpoint, then returns; also carries a player standing on top.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WhiteboxPathPlatform : MonoBehaviour
    {
        [SerializeField] private Vector3 m_LocalAxis = Vector3.right;
        [SerializeField, Min(0.1f)] private float m_TravelDistance = 8f;
        [SerializeField, Min(0.1f)] private float m_Speed = 1.5f;

        private Vector3 m_StartLocalPosition;
        private Vector3 m_PreviousPosition;
        private float m_StartTime;
        private Collider m_PlatformCollider;

        public void Configure(Vector3 localAxis, float travelDistance, float speed)
        {
            m_LocalAxis = localAxis.sqrMagnitude > 0.0001f ? localAxis.normalized : Vector3.right;
            m_TravelDistance = Mathf.Max(0.1f, travelDistance);
            m_Speed = Mathf.Max(0.1f, speed);
        }

        private void Awake()
        {
            m_StartLocalPosition = transform.localPosition;
            m_PreviousPosition = transform.position;
            m_StartTime = Time.time;
            m_PlatformCollider = GetComponent<Collider>();
        }

        private void Update()
        {
            if (m_PlatformCollider == null)
                return;

            float distance = Mathf.PingPong((Time.time - m_StartTime) * m_Speed, m_TravelDistance);
            transform.localPosition = m_StartLocalPosition + m_LocalAxis * distance;

            Vector3 currentPosition = transform.position;
            Vector3 delta = currentPosition - m_PreviousPosition;
            m_PreviousPosition = currentPosition;
            if (delta.sqrMagnitude > 0.0000001f)
                CarryPlayers(delta);
        }

        private void CarryPlayers(Vector3 delta)
        {
            Bounds bounds = m_PlatformCollider.bounds;
            Vector3 halfExtents = new Vector3(
                Mathf.Max(0.15f, bounds.extents.x * 0.92f),
                0.28f,
                Mathf.Max(0.15f, bounds.extents.z * 0.92f));
            Vector3 probeCenter = new Vector3(
                bounds.center.x,
                bounds.max.y + halfExtents.y - 0.08f,
                bounds.center.z);

            Collider[] overlaps = Physics.OverlapBox(
                probeCenter,
                halfExtents,
                transform.rotation,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < overlaps.Length; i++)
            {
                PlayerController player = overlaps[i].GetComponentInParent<PlayerController>();
                if (player == null)
                    continue;

                CharacterController controller = player.GetComponent<CharacterController>();
                if (controller != null && controller.enabled)
                    controller.Move(delta);
            }
        }
    }
}
