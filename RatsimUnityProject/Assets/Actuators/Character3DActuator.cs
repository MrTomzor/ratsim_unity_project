using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class Character3DActuator : MonoBehaviour
{
    ZmqUnityServer conn;
    
    [Header("Movement")]
    public float walkSpeed = 3f;
    public float runSpeed = 6f;
    [Tooltip("How fast it speeds up. Use 1-2 for ice, 50+ for instant.")]
    public float acceleration = 1.5f;
    [Tooltip("How fast it slows down. Use 0.5-1 for ice, 50+ for instant. Larger value = more friction.")]
    public float deceleration = 1.0f;
    [Tooltip("How bouncy the agent is against walls (0 = dead stop, 1 = perfect bounce)")]
    public float wallBounciness = 0.8f;
    [Tooltip("How bouncy the agent is against the floor (0 = dead stop, 1 = perfect bounce)")]
    public float groundBounciness = 0.5f;
    [Tooltip("Minimum downward speed required to trigger a ground bounce")]
    public float minBounceVelocity = 2.0f;
    
    [Header("Jumping")]
    public float jumpForce = 5f;
    public float groundCheckDistance = 0.2f;
    public LayerMask groundLayer = ~0; // Default to everything

    [Header("ROS Topics")]
    public string velCmdTopic = "/cmd_vel";
    public string humanControlTopic = "/enable_human_control";

    [Header("Human Control")]
    public bool humanControlEnabled = false;
    
    [Header("FPS Mouse Control")]
    public float mouseTurnSensitivity = 0.5f; 
    public bool lockCursor = true;

    private InputSystem_Actions _inputActions;
    private Vector2 _accumulatedMouseDelta;
    private bool _jumpRequested = false;
    private Rigidbody _rb;
    private Collider _col;
    private float _rosAngularVelocityY = 0f;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        // Force freeze all rotations so physics collisions don't spin the agent
        _rb.constraints |= RigidbodyConstraints.FreezeRotation;

        // Apply a frictionless material so ground friction doesn't override our custom deceleration
        if (_col != null)
        {
            PhysicsMaterial zeroFriction = new PhysicsMaterial("ZeroFrictionAgent");
            zeroFriction.dynamicFriction = 0f;
            zeroFriction.staticFriction = 0f;
            zeroFriction.frictionCombine = PhysicsMaterialCombine.Minimum;
            zeroFriction.bounciness = 0f; 
            zeroFriction.bounceCombine = PhysicsMaterialCombine.Minimum;
            _col.material = zeroFriction;
        }

        conn = ZmqUnityServer.GetInstance();
        if (conn != null)
        {
            conn.Subscribe<TwistMessage>(velCmdTopic, OnVelTwistMessage);
            conn.Subscribe<BoolMessage>(humanControlTopic, OnHumanControlToggle);
            conn.RegisterTimerDiscrete(OnPhysicsTick, 1);
        }

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

        ApplyVelocity(msg);
    }

    private void ApplyVelocity(TwistMessage msg)
    {
        // linear_x = forward
        // linear_y = left/right strafe
        // linear_z = jump command (if > 0)
        
        Vector3 forward = transform.forward * msg.linear_x;
        Vector3 left = -transform.right * msg.linear_y;
        
        Vector3 targetVelocity = forward + left;
        
        // Preserve Y velocity for gravity/falling
        targetVelocity.y = _rb.linearVelocity.y;
        
        _rb.linearVelocity = targetVelocity;

        // angular_z is typically yaw in ROS. We store it to apply in FixedUpdate
        _rosAngularVelocityY = -msg.angular_z;
        
        if (msg.linear_z > 0 && IsGrounded())
        {
            _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, jumpForce, _rb.linearVelocity.z);
        }
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
            Debug.Log("Character3DActuator: human control ENABLED");
        }
        else
        {
            _inputActions.Disable();
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            
            // Stop movement but preserve gravity
            _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            _rb.angularVelocity = Vector3.zero;
            _rosAngularVelocityY = 0f;
            _accumulatedMouseDelta = Vector2.zero;
            Debug.Log("Character3DActuator: human control DISABLED");
        }
    }

    void FixedUpdate()
    {
        if (!humanControlEnabled && _rosAngularVelocityY != 0f)
        {
            // Apply continuous rotation for ROS commands
            _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0, _rosAngularVelocityY * Mathf.Rad2Deg * Time.fixedDeltaTime, 0));
        }
    }

    void Update()
    {
        if (!humanControlEnabled) return;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                _jumpRequested = true;
            }

            // T key to teleport 1 meter above terrain height
            if (Keyboard.current.tKey.wasPressedThisFrame)
            {
                float terrainY = RealLifeEnvironment.RealTerrainHeight.GetTriangulatedHeight(new Vector2(transform.position.x, transform.position.z));
                transform.position = new Vector3(transform.position.x, terrainY + 1f, transform.position.z);
                _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0, _rb.linearVelocity.z); // Reset vertical velocity
            }
        }

        if (Mouse.current != null)
        {
            _accumulatedMouseDelta += Mouse.current.delta.ReadValue();
        }
    }

    private void OnPhysicsTick(ZmqTimerEvent ev)
    {
        if (!humanControlEnabled) return;

        Vector2 keyboardMove = _inputActions.Player.Move.ReadValue<Vector2>();
        float rightInput = keyboardMove.x;
        float forwardInput = keyboardMove.y;
        
        bool isRunning = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        float currentSpeed = isRunning ? runSpeed : walkSpeed;

        Vector3 moveDirection = transform.forward * forwardInput + transform.right * rightInput;
        
        // Optional: normalize so diagonal movement isn't faster
        if (moveDirection.magnitude > 1f)
            moveDirection.Normalize();

        Vector3 targetVelocity = moveDirection * currentSpeed;

        Vector3 currentVelocity = _rb.linearVelocity;
        Vector3 currentHorizontal = new Vector3(currentVelocity.x, 0, currentVelocity.z);
        Vector3 targetHorizontal = new Vector3(targetVelocity.x, 0, targetVelocity.z);
        
        float accelRate = (targetHorizontal.magnitude > 0.1f) ? acceleration : deceleration;
        Vector3 newHorizontal = Vector3.Lerp(currentHorizontal, targetHorizontal, accelRate * conn.physicsStepTime);

        Vector3 finalVelocity = new Vector3(newHorizontal.x, currentVelocity.y, newHorizontal.z);

        // Apply jump
        if (_jumpRequested && IsGrounded())
        {
            finalVelocity.y = jumpForce;
        }
        _jumpRequested = false;

        _rb.linearVelocity = finalVelocity;

        // Calculate target turn input from accumulated mouse delta, then reset
        float targetTurnInput = _accumulatedMouseDelta.x * mouseTurnSensitivity;
        _accumulatedMouseDelta = Vector2.zero;

        // Apply angular velocity manually so it ignores physics collisions
        _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0, targetTurnInput, 0));
    }
    
    private bool IsGrounded()
    {
        if (_col != null)
        {
            // Raycast down from slightly inside the bottom of the collider bounds
            float buffer = 0.05f;
            Vector3 origin = new Vector3(_col.bounds.center.x, _col.bounds.min.y + buffer, _col.bounds.center.z);
            return Physics.Raycast(origin, Vector3.down, groundCheckDistance + buffer, groundLayer);
        }
        
        // Fallback if no collider is attached to this exact GameObject
        return Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, groundCheckDistance + 0.1f, groundLayer);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (wallBounciness <= 0f && groundBounciness <= 0f) return;

        bool bounced = false;
        foreach (ContactPoint contact in collision.contacts)
        {
            // Wall bounce: If the surface is mostly vertical
            if (Mathf.Abs(contact.normal.y) < 0.5f && wallBounciness > 0f)
            {
                // collision.relativeVelocity is (other_velocity - our_velocity)
                Vector3 incomingVelocity = -collision.relativeVelocity;
                
                // We only care about horizontal reflection
                Vector3 incomingHorizontal = new Vector3(incomingVelocity.x, 0, incomingVelocity.z);
                Vector3 normalHorizontal = new Vector3(contact.normal.x, 0, contact.normal.z).normalized;
                
                // Ensure we were actually moving into this normal
                if (Vector3.Dot(incomingHorizontal, normalHorizontal) < 0)
                {
                    Vector3 reflected = Vector3.Reflect(incomingHorizontal, normalHorizontal);
                    Vector3 bounceVelocity = reflected * wallBounciness;
                    
                    // Apply the new bounced velocity, preserving Y (gravity)
                    _rb.linearVelocity = new Vector3(bounceVelocity.x, _rb.linearVelocity.y, bounceVelocity.z);
                    bounced = true;
                }
            }
            // Ground bounce: If the surface is mostly horizontal and facing up
            else if (contact.normal.y >= 0.5f && groundBounciness > 0f)
            {
                Vector3 incomingVelocity = -collision.relativeVelocity;
                
                // If we were falling fast enough
                if (incomingVelocity.y < -minBounceVelocity)
                {
                    // Reflect the entire velocity vector across the sloped ground normal
                    Vector3 reflected = Vector3.Reflect(incomingVelocity, contact.normal);
                    _rb.linearVelocity = reflected * groundBounciness;
                    bounced = true;
                }
            }
            
            // Process only the primary impact
            if (bounced) break; 
        }
    }
}
