using UnityEngine;

/// <summary>
/// Capsule-style NPC that wanders with a deterministic seeded RNG. Driven by the
/// RoslikeTCPServer discrete timer so motion is lockstep with physics.
///
/// Behaviour per tick:
///   1. If avoidant and an agent is within avoidanceDistance, set velocity radially
///      away from the agent (overrides the walk/pause state machine).
///   2. Otherwise step the walk/pause cycle. During walk, apply the stored speed along
///      the currently sampled direction; during pause, zero horizontal velocity.
///   3. When a walk leg finishes, sample a new direction for the next leg. If bounded
///      and close to the radius edge, directions are weighted toward the boundary centre.
///
/// The Rigidbody keeps gravity enabled so the capsule stays glued to uneven terrain;
/// rotation around X/Z is frozen so it never falls over. Horizontal velocity is set
/// directly each tick (vertical velocity is preserved so gravity still works).
/// </summary>
public class ChaoticWalker : MonoBehaviour {

    public enum Mode { Default, Avoidant, Aggressive }

    // ── Config (populated by ChaoticWalkersLoader on spawn) ──
    public float speed;
    public Mode mode;
    public float reactionRadius;
    public float reactionVelocity;
    public int walkMinSteps;
    public int walkMaxSteps;
    public int pauseMinSteps;
    public int pauseMaxSteps;
    public bool bounded;
    public Vector2 boundCenter;
    public float boundRadius;
    public float inwardBiasStrength;

    // ── Runtime state ──
    private System.Random _rng;
    private Rigidbody _rb;
    private Transform _agentTf;

    private enum Phase { Walking, Paused }
    private Phase _phase;
    private int _phaseStepsLeft;
    private Vector2 _dir; // xz-plane unit vector

    /// <summary>Call once right after Instantiate, before the first tick.</summary>
    public void Init(int seed) {
        _rng = new System.Random(seed);
        _rb = GetComponent<Rigidbody>();
        if (_rb != null) {
            _rb.constraints = RigidbodyConstraints.FreezeRotationX |
                              RigidbodyConstraints.FreezeRotationZ;
        }
        StartWalkPhase();

        RoslikeTCPServer.GetInstance().RegisterTimerDiscrete((ev) => Tick(), 1);
    }

    private void ResolveAgent() {
        if (_agentTf != null) return;
        var agentObj = WorldLoadingController.instance != null
            ? WorldLoadingController.instance.agentObject : null;
        if (agentObj != null) _agentTf = agentObj.transform;
    }

    private void Tick() {
        if (this == null || !gameObject.activeInHierarchy) return;
        ResolveAgent();

        // 1. Reaction override (avoidant flees, aggressive chases)
        if (mode != Mode.Default && _agentTf != null) {
            Vector3 d3 = transform.position - _agentTf.position;
            Vector2 d = new Vector2(d3.x, d3.z);
            float dist = d.magnitude;
            if (dist > 1e-4f && dist < reactionRadius) {
                Vector2 unit = d / dist;
                Vector2 reactDir = (mode == Mode.Avoidant) ? unit : -unit;
                ApplyHorizontalVelocity(reactDir * reactionVelocity);
                return;
            }
        }

        // 2. Step the phase state machine
        _phaseStepsLeft--;
        if (_phaseStepsLeft <= 0) {
            if (_phase == Phase.Walking) StartPausePhase();
            else StartWalkPhase();
        }

        // 3. Apply velocity for current phase
        if (_phase == Phase.Walking)
            ApplyHorizontalVelocity(_dir * speed);
        else
            ApplyHorizontalVelocity(Vector2.zero);
    }

    private void StartWalkPhase() {
        _phase = Phase.Walking;
        _phaseStepsLeft = RandRange(walkMinSteps, walkMaxSteps);
        _dir = SampleDirection();
    }

    private void StartPausePhase() {
        _phase = Phase.Paused;
        _phaseStepsLeft = RandRange(pauseMinSteps, pauseMaxSteps);
    }

    /// <summary>
    /// Uniform random unit 2D direction, optionally rotated toward the bound centre
    /// with strength that grows as the walker approaches the bound radius.
    /// </summary>
    private Vector2 SampleDirection() {
        float angle = (float)(_rng.NextDouble() * 2.0 * Mathf.PI);
        Vector2 d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        if (!bounded || boundRadius <= 0f) return d;

        Vector2 pos = new Vector2(transform.position.x, transform.position.z);
        Vector2 toCenter = boundCenter - pos;
        float r = toCenter.magnitude;
        if (r < 1e-4f) return d;
        Vector2 inward = toCenter / r;

        // Weight: 0 near centre, grows to 1 at/past bound. inwardBiasStrength scales it.
        float t = Mathf.Clamp01(r / boundRadius);
        float w = Mathf.Clamp01(t * t * inwardBiasStrength);

        // Hard redirect if the next step would leave the circle.
        if (r >= boundRadius) w = 1f;

        Vector2 mixed = Vector2.Lerp(d, inward, w);
        float m = mixed.magnitude;
        return m > 1e-4f ? mixed / m : inward;
    }

    private void ApplyHorizontalVelocity(Vector2 vxz) {
        if (_rb == null) return;
        Vector3 v = _rb.linearVelocity;
        v.x = vxz.x;
        v.z = vxz.y;
        _rb.linearVelocity = v;
    }

    private int RandRange(int minInclusive, int maxInclusive) {
        if (maxInclusive <= minInclusive) return Mathf.Max(1, minInclusive);
        return _rng.Next(minInclusive, maxInclusive + 1);
    }
}
