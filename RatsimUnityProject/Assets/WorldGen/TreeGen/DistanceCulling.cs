using UnityEngine;

public class DistanceCulling : MonoBehaviour
{
    [Tooltip("The target to measure distance to. For example, the Main Camera or the Player.")]
    public Transform target;
    
    [Tooltip("The maximum distance before the object and its children are culled (hidden).")]
    public float maxDistance = 50f;

    [Tooltip("How frequently to check the distance (in seconds) to save performance. 0 means every frame.")]
    public float checkInterval = 0f;

    private Renderer[] _renderers;
    private float _timeSinceLastCheck = 0f;
    private float _maxDistanceSqr;
    private bool _isCurrentlyVisible = true;
    private bool _hasInitializedState = false;

    private void Start()
    {
        // Get all renderers on this object and all its children (including inactive ones)
        _renderers = GetComponentsInChildren<Renderer>(true);
        _maxDistanceSqr = maxDistance * maxDistance;
        
        // If target is not assigned, try to default to the Main Camera
        if (target == null && Camera.main != null)
        {
            target = Camera.main.transform;
        }
    }

    private void Update()
    {
        if (target == null) return;

        _timeSinceLastCheck += Time.deltaTime;
        
        if (_timeSinceLastCheck >= checkInterval || !_hasInitializedState)
        {
            _timeSinceLastCheck = 0f;
            
            // Using sqrMagnitude is much more performant than Vector3.Distance
            float sqrDistance = (transform.position - target.position).sqrMagnitude;
            
            bool shouldBeVisible = sqrDistance <= _maxDistanceSqr;
            
            if (!_hasInitializedState || _isCurrentlyVisible != shouldBeVisible)
            {
                _isCurrentlyVisible = shouldBeVisible;
                _hasInitializedState = true;
                
                foreach (var r in _renderers)
                {
                    if (r != null && r.enabled != shouldBeVisible)
                    {
                        r.enabled = shouldBeVisible;
                    }
                }
            }
        }
    }

    // Recalculate sqr magnitude if max distance is changed in the inspector while the game is running
    private void OnValidate()
    {
        _maxDistanceSqr = maxDistance * maxDistance;
    }
}
