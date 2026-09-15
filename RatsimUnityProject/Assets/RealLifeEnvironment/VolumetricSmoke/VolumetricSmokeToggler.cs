using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

public class VolumetricSmokeToggler : MonoBehaviour
{
    public PostProcessVolume postProcessVolume;
    [Tooltip("Leave empty to auto-find on the same GameObject")]

    private bool isEnabled = true;

    void Start()
    {
        if (postProcessVolume == null)
            postProcessVolume = Object.FindAnyObjectByType<PostProcessVolume>();

        if (postProcessVolume != null && postProcessVolume.profile.TryGetSettings(out VolumetricSmokeEffect effect))
        {
            isEnabled = effect.active;
        }
    }

    void Update()
    {
        // Toggle when Page Down is pressed
        if (Input.GetKeyDown(KeyCode.PageDown))
        {
            isEnabled = !isEnabled;
            

                
            if (postProcessVolume != null && postProcessVolume.profile.TryGetSettings(out VolumetricSmokeEffect effect))
            {
                effect.active = isEnabled;
            }
        }
    }
}
