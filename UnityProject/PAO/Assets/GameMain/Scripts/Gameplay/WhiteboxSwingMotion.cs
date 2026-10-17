using UnityEngine;

namespace PAO
{
    /// <summary>Editor-authored pendulum motion for shrine-style whitebox decks and weights.</summary>
    public sealed class WhiteboxSwingMotion : MonoBehaviour
    {
        [SerializeField] private Vector3 m_LocalAxis = Vector3.right;
        [SerializeField] private float m_MaxAngle = 35f;
        [SerializeField] private float m_Period = 3f;
        [SerializeField] private Collider m_RiderCollider;
        [SerializeField] private Vector3 m_ChildOffset;

        private Quaternion m_InitialRotation;
        private Vector3 m_PreviousPosition;
        private float m_StartTime;

        public void Configure(Vector3 axis, float maxAngle, float period, Collider riderCollider, Vector3 childOffset)
        {
            m_LocalAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.right;
            m_MaxAngle = Mathf.Clamp(maxAngle, 0f, 70f);
            m_Period = Mathf.Max(0.5f, period);
            m_RiderCollider = riderCollider;
            m_ChildOffset = childOffset;
        }

        private void Awake()
        {
            m_InitialRotation = transform.localRotation;
            m_PreviousPosition = transform.position + transform.rotation * m_ChildOffset;
            m_StartTime = Time.time;
        }

        private void Update()
        {
            float phase = (Time.time - m_StartTime) * (Mathf.PI * 2f / m_Period);
            float angle = Mathf.Sin(phase) * m_MaxAngle;
            transform.localRotation = m_InitialRotation * Quaternion.AngleAxis(angle, m_LocalAxis);

            Vector3 currentPosition = transform.position + transform.rotation * m_ChildOffset;
            Vector3 delta = currentPosition - m_PreviousPosition;
            m_PreviousPosition = currentPosition;
            if (delta.sqrMagnitude > 0.0000001f && m_RiderCollider != null)
                CarryRiders(delta);
        }

        private void CarryRiders(Vector3 delta)
        {
            Bounds bounds = m_RiderCollider.bounds;
            Vector3 half = new Vector3(Mathf.Max(0.15f, bounds.extents.x * 0.9f), 0.3f, Mathf.Max(0.15f, bounds.extents.z * 0.9f));
            Vector3 center = new Vector3(bounds.center.x, bounds.max.y + 0.18f, bounds.center.z);
            Collider[] overlaps = Physics.OverlapBox(center, half, m_RiderCollider.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in overlaps)
            {
                PlayerController player = hit.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                CharacterController controller = player.GetComponent<CharacterController>();
                if (controller != null && controller.enabled) controller.Move(delta);
            }
        }
    }
}
