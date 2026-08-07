using UnityEngine;
using UnityEngine.InputSystem;

public class MouseDragTwist2DActuator : MonoBehaviour
{
    RoslikeTCPServer conn;
    public float maxLinearVelocity = 1f;
    public float maxAngularVelocity = .5f;

    public float maxLinearAcceleration = .5f;
    public float maxAngularAcceleration = .2f;

    public string velCmdTopic = "/cmd_vel";
    public string accelCmdTopic = "/cmd_accel";

    public float accelModeLinearDrag = 0.2f;
    public float accelModeAngularDrag = 0.2f;
    public bool verbose = false;

    // --- Faults ---
    [Header("Faults")]
    public float steeringBias = 0f;
    public bool blockLeftTurn = false;
    public bool blockRightTurn = false;

    // --- Human control ---
    [Header("Human Control")]
    public string humanControlTopic = "/enable_human_control";
    public bool humanControlEnabled = false;

    public enum HumanControlMode { Velocity, Acceleration }
    public HumanControlMode humanControlMode = HumanControlMode.Velocity;

    [Header("FPS Mouse Control")]
    [Tooltip("Sensitivity for mouse movement turning")]
    public float mouseTurnSensitivity = 0.5f; 
    [Tooltip("Multiplier for max linear velocity per scroll tick (e.g., 1.2 multiplies by 1.2 when scrolling up, divides by 1.2 when scrolling down)")]
    public float scrollMultiplier = 1.2f; 
    [Range(0f, 1f)]
    [Tooltip("Smoothing factor for mouse turning. 0 is instant, approaching 1 is very smooth/delayed.")]
    public float mouseTurnSmoothness = 0f;
    [Tooltip("If true, small mouse movements are smoothed, but large movements instantly bypass the smoothing.")]
    public bool adaptiveSmoothing = true;
    [Tooltip("How quickly smoothing is disabled during fast movements. Higher values make large movements snap instantly.")]
    public float adaptiveSmoothnessFalloff = 0.05f;
    [Tooltip("Lock the cursor to the screen center when enabled (standard for FPS)")]
    public bool lockCursor = true;

    [Header("Teleport")]
    public float teleportUnitsForward = 5f;
    [Tooltip("Duration (in seconds) of the ease-in phase")]
    public float teleportEaseInAmount = 0.1f;
    [Tooltip("Duration (in seconds) of the ease-out phase")]
    public float teleportEaseOutAmount = 0.1f;
    [Tooltip("Fraction of the total distance to move during the ease-in/out phases")]
    public float teleportEaseDistanceFraction = 0.1f;

    private InputSystem_Actions _inputActions;
    private Vector2 _accumulatedMouseDelta;
    private float _smoothedTurnInput = 0f;
    private enum TeleportState { None, EaseIn, Jump, EaseOut }
    private TeleportState _teleportState = TeleportState.None;
    private float _teleportElapsed = 0f;
    private float _teleportPreviousT = 0f;
    private Vector3 _teleportVelocityToClear = Vector3.zero;

    void Start()
    {
        conn = RoslikeTCPServer.GetInstance();
        conn.Subscribe<TwistMessage>(velCmdTopic, OnVelTwistMessage);
        conn.Subscribe<TwistMessage>(accelCmdTopic, OnAccelTwistMessage);
        conn.Subscribe<BoolMessage>(humanControlTopic, OnHumanControlToggle);
        conn.RegisterTimerDiscrete(OnPhysicsTick, 1);

        _inputActions = new InputSystem_Actions();
    }

    void OnDestroy()
    {
        _inputActions?.Disable();
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void OnVelTwistMessage(TwistMessage msg)
    {
        if (humanControlEnabled) return; 

        if (verbose)
            Debug.Log($"Received TwistMessage: linear_x={msg.linear_x}, linear_y={msg.linear_y}, angular_z={msg.angular_z}");
        ApplyVelocity(msg);
    }

    public void OnAccelTwistMessage(TwistMessage msg)
    {
        if (humanControlEnabled) return;

        if (verbose)
            Debug.Log($"Received AccelTwistMessage: linear_x={msg.linear_x}, linear_y={msg.linear_y}, angular_z={msg.angular_z}");
        ApplyAcceleration(msg);
    }

    private float ApplyAngularFaults(float angularZ, float linearX)
    {
        if (Mathf.Abs(linearX) > 1e-6f) angularZ += steeringBias;
        if (blockLeftTurn && angularZ > 0f) angularZ = 0f;
        if (blockRightTurn && angularZ < 0f) angularZ = 0f;
        return angularZ;
    }

    private void ApplyVelocity(TwistMessage msg)
    {
        float angularZ = ApplyAngularFaults(msg.angular_z, msg.linear_x);
        Vector3 forward = transform.forward * msg.linear_x;
        Vector3 left = -transform.right * msg.linear_y;
        Vector3 rotationRad = new Vector3(0, -angularZ, 0);

        var rb = GetComponent<Rigidbody>();
        rb.linearVelocity = forward + left;
        rb.angularVelocity = rotationRad;
    }

    private void ApplyAcceleration(TwistMessage msg)
    {
        float angularZ = ApplyAngularFaults(msg.angular_z, msg.linear_x);
        Vector3 forward = transform.forward * msg.linear_x;
        Vector3 left = -transform.right * msg.linear_y;
        Vector3 rotationRad = new Vector3(0, -angularZ, 0);

        var rb = GetComponent<Rigidbody>();

        float dt = conn.physicsStepTime;
        rb.linearVelocity += (forward + left) * dt;
        rb.angularVelocity += rotationRad * dt;

        rb.linearVelocity *= (1.0f - accelModeLinearDrag);
        rb.angularVelocity *= (1.0f - accelModeAngularDrag);
    }

    private void OnHumanControlToggle(BoolMessage msg)
    {
        humanControlEnabled = msg.data;
        if (humanControlEnabled)
        {
            _inputActions.Enable();
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            Debug.Log("MouseDragTwist2DActuator: human control ENABLED");
        }
        else
        {
            _inputActions.Disable();
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            
            var rb = GetComponent<Rigidbody>();
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            _accumulatedMouseDelta = Vector2.zero;
            _smoothedTurnInput = 0f;
            _teleportState = TeleportState.None;
            _teleportVelocityToClear = Vector3.zero;
            Debug.Log("MouseDragTwist2DActuator: human control DISABLED");
        }
    }

    void Update()
    {
        if (!humanControlEnabled) return;

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && _teleportState == TeleportState.None)
        {
            _teleportState = teleportEaseInAmount > 0f ? TeleportState.EaseIn : TeleportState.Jump;
            _teleportElapsed = 0f;
            _teleportPreviousT = 0f;
        }

        if (Mouse.current != null)
        {
            // Accumulate mouse movement between physics ticks
            _accumulatedMouseDelta += Mouse.current.delta.ReadValue();

            float scrollY = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.01f)
            {
                if (scrollY > 0)
                {
                    maxLinearVelocity *= scrollMultiplier;
                }
                else
                {
                    maxLinearVelocity /= scrollMultiplier;
                }
                maxLinearVelocity = Mathf.Max(0.01f, maxLinearVelocity);
            }
        }
    }

    private void OnPhysicsTick(TimerEvent ev)
    {
        if (!humanControlEnabled) return;

        var rb = GetComponent<Rigidbody>();
        if (_teleportVelocityToClear != Vector3.zero)
        {
            rb.linearVelocity -= _teleportVelocityToClear;
            _teleportVelocityToClear = Vector3.zero;
        }

        Vector2 keyboardMove = _inputActions.Player.Move.ReadValue<Vector2>();
        float forwardInput = keyboardMove.y;
        
        float teleportVelocityBonus = 0f;
        if (_teleportState != TeleportState.None)
        {
            float dt = conn.physicsStepTime;
            float easeDist = teleportUnitsForward * teleportEaseDistanceFraction;
            float easeInActualDist = teleportEaseInAmount > 0f ? easeDist : 0f;
            float easeOutActualDist = teleportEaseOutAmount > 0f ? easeDist : 0f;
            float jumpDist = teleportUnitsForward - easeInActualDist - easeOutActualDist;

            if (_teleportState == TeleportState.EaseIn)
            {
                _teleportElapsed += dt;
                float t = Mathf.Clamp01(_teleportElapsed / teleportEaseInAmount);
                float easeT = t * t; 
                float deltaDist = (easeT - _teleportPreviousT) * easeInActualDist;
                teleportVelocityBonus = deltaDist / dt;
                _teleportPreviousT = easeT;

                if (t >= 1f) _teleportState = TeleportState.Jump;
            }
            else if (_teleportState == TeleportState.Jump)
            {
                teleportVelocityBonus = jumpDist / dt;
                if (teleportEaseOutAmount > 0f)
                {
                    _teleportState = TeleportState.EaseOut;
                    _teleportElapsed = 0f;
                    _teleportPreviousT = 0f;
                }
                else _teleportState = TeleportState.None;
            }
            else if (_teleportState == TeleportState.EaseOut)
            {
                _teleportElapsed += dt;
                float t = Mathf.Clamp01(_teleportElapsed / teleportEaseOutAmount);
                float easeT = t * (2f - t); 
                float deltaDist = (easeT - _teleportPreviousT) * easeOutActualDist;
                teleportVelocityBonus = deltaDist / dt;
                _teleportPreviousT = easeT;

                if (t >= 1f) _teleportState = TeleportState.None;
            }
        }

        // Calculate target turn input from accumulated mouse delta, then reset
        float targetTurnInput = _accumulatedMouseDelta.x * mouseTurnSensitivity;
        _accumulatedMouseDelta = Vector2.zero;

        // Apply smoothing
        if (mouseTurnSmoothness > 0f)
        {
            float effectiveSmoothness = mouseTurnSmoothness;

            if (adaptiveSmoothing)
            {
                // The larger the difference between current and target, the less smoothing we apply.
                float diff = Mathf.Abs(targetTurnInput - _smoothedTurnInput);
                effectiveSmoothness *= Mathf.Exp(-diff * adaptiveSmoothnessFalloff);
            }

            _smoothedTurnInput = Mathf.Lerp(_smoothedTurnInput, targetTurnInput, 1f - effectiveSmoothness);
        }
        else
        {
            _smoothedTurnInput = targetTurnInput;
        }

        float turnInput = _smoothedTurnInput;

        TwistMessage msg = new TwistMessage();
        
        if (humanControlMode == HumanControlMode.Velocity)
        {
            msg.linear_x = forwardInput * maxLinearVelocity + teleportVelocityBonus;
            // Apply the turn input instantly as angular velocity. 
            // We scale by physics rate so a specific mouse movement distance 
            // corresponds to a specific rotation amount regardless of tick rate.
            float physicsRate = 1.0f / conn.physicsStepTime; 
            msg.angular_z = -turnInput * physicsRate;
            
            ApplyVelocity(msg);
        }
        else
        {
            msg.linear_x = forwardInput * maxLinearAcceleration;
            msg.angular_z = -turnInput * maxAngularAcceleration;
            ApplyAcceleration(msg);
            
            if (teleportVelocityBonus != 0f)
            {
                _teleportVelocityToClear = transform.forward * teleportVelocityBonus;
                rb.linearVelocity += _teleportVelocityToClear;
            }
        }
    }
}
