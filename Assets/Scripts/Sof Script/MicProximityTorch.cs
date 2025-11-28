using UnityEngine;

public class MicProximityTorch : MonoBehaviour
{
    [Header("Player Proximity")]
    [Tooltip("The Tag used on the player root object.")]
    public string playerTag = "Player";

    [Tooltip("Player must be inside this torch's trigger zone for it to react.")]
    public bool requireProximity = true;

    [Tooltip("Radius of the auto-generated trigger sphere around this torch.")]
    [Min(0.1f)]
    public float triggerRadius = 2f;

    [Header("Light / VFX")]
    public Light torchLight;
    public ParticleSystem flameFX;
    public Renderer flameRenderer;

    [Header("Brightness Mapping (from global mic level 0..1)")]
    public float minIntensity = 0f;
    public float maxIntensity = 3.5f;
    public float riseLerp = 12f;
    public float fallLerp = 6f;

    [Header("Emission (if using mesh flame)")]
    [ColorUsage(true, true)] public Color emissionColor = new Color(1f, 0.5f, 0.1f);
    public float emissionBoost = 2.5f;

    [Header("Debug")]
    public bool showDebug = true;
    [Range(0, 1)] public float lastLevel01;

    private bool inZone = false;
    private float _currentIntensity;
    private SphereCollider _triggerCollider;

    void Awake()
    {
        if (torchLight)
        {
            _currentIntensity = minIntensity;
            torchLight.intensity = minIntensity;
        }

        if (flameFX)
        {
            var em = flameFX.emission;
            em.rateOverTime = 0f;
        }

        SetEmission(0f);
        SetupTriggerCollider();
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            SetupTriggerCollider();
        }
        else
        {
            if (_triggerCollider != null)
                _triggerCollider.radius = triggerRadius;
        }
    }

    private void SetupTriggerCollider()
    {
        _triggerCollider = GetComponent<SphereCollider>();
        if (requireProximity)
        {
            if (_triggerCollider == null)
                _triggerCollider = gameObject.AddComponent<SphereCollider>();

            _triggerCollider.isTrigger = true;
            _triggerCollider.radius = triggerRadius;
            _triggerCollider.center = Vector3.zero;
        }
        else
        {
            // If we don't require proximity, we don't strictly need a trigger
            if (_triggerCollider != null)
            {
                // you can keep it or disable isTrigger; here we just leave it
            }
        }
    }

    void Update()
    {
        var mic = GlobalMicLevel.Instance;
        if (mic == null || !mic.MicReady || torchLight == null)
        {
            ApplyTarget(0f);
            return;
        }

        if (requireProximity && !inZone)
        {
            ApplyTarget(0f);
            return;
        }

        float t = mic.level01;   // 0..1 loudness from global mic
        lastLevel01 = t;

        float target = Mathf.Lerp(minIntensity, maxIntensity, t);
        ApplyTarget(target);
    }

    private void ApplyTarget(float targetIntensity)
    {
        float lerpSpeed = (targetIntensity > _currentIntensity ? riseLerp : fallLerp);
        _currentIntensity = Mathf.Lerp(_currentIntensity, targetIntensity,
            1f - Mathf.Exp(-lerpSpeed * Time.deltaTime));

        if (torchLight)
        {
            torchLight.intensity = _currentIntensity;
            torchLight.enabled = _currentIntensity > 0.01f;
        }

        if (flameFX)
        {
            var em = flameFX.emission;
            float rate = Mathf.Lerp(0f, 40f,
                Mathf.InverseLerp(0f, maxIntensity * 0.6f, _currentIntensity));
            em.rateOverTime = rate;

            if (_currentIntensity > 0.05f && !flameFX.isPlaying) flameFX.Play();
            if (_currentIntensity <= 0.01f && flameFX.isPlaying) flameFX.Stop();
        }

        float n = Mathf.InverseLerp(0f, maxIntensity * 0.6f, _currentIntensity);
        SetEmission(n);
    }

    private void SetEmission(float normalized)
    {
        if (!flameRenderer) return;

        var m = flameRenderer.material;
        if (!m.IsKeywordEnabled("_EMISSION")) m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emissionColor * (normalized * emissionBoost));
    }

    void OnTriggerEnter(Collider other)
    {
        if (!requireProximity) return;
        if (other.CompareTag(playerTag))
        {
            inZone = true;
            if (showDebug) Debug.Log($"{name}: Player ENTERED torch zone.");
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!requireProximity) return;
        if (other.CompareTag(playerTag))
        {
            inZone = false;
            if (showDebug) Debug.Log($"{name}: Player LEFT torch zone.");
        }
    }

    void OnGUI()
    {
        if (!showDebug) return;

        if (requireProximity && !inZone) return;

        float h = 6f;
        float w = 200f * lastLevel01;
        GUI.color = Color.Lerp(Color.green, Color.red, lastLevel01);
        GUI.Box(new Rect(20, 20, w, h), GUIContent.none);
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 28, 320, 20),
            $"Torch '{name}' Mic {(int)(lastLevel01 * 100)}%  (inZone: {inZone})");
    }
}
