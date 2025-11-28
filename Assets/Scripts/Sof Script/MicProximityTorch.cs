using UnityEngine;
using System.Collections;
using System.Linq;

[RequireComponent(typeof(AudioSource))]
public class MicProximityTorch : MonoBehaviour
{
    [Header("Player Proximity")]
    [Tooltip("The player must be inside this torch's trigger to react.")]
    public string playerTag = "Player";

    [Header("Light / VFX")]
    public Light torchLight;
    public ParticleSystem flameFX;
    public Renderer flameRenderer;

    [Header("Brightness Mapping (same idea as your script)")]
    public float minIntensity = 0f;
    public float maxIntensity = 3.5f;
    public float riseLerp = 12f;
    public float fallLerp = 6f;

    [Header("Mic Sensitivity (dB)")]
    [Tooltip("Silence / background noise level")]
    public float dbFloor = -50f;
    [Tooltip("Full yelling / max brightness level")]
    public float dbCeil = -18f;

    [Header("Mic Settings")]
    [Tooltip("Matches part of the microphone name. For Quest PC Link use 'oculus'. Leave empty to use first device.")]
    public string preferredDeviceSubstring = "oculus";
    public int sampleRate = 22050;
    public int sampleCount = 512;

    [Header("Emission (if using mesh flame)")]
    [ColorUsage(true, true)] public Color emissionColor = new Color(1f, 0.5f, 0.1f);
    public float emissionBoost = 2.5f;

    [Header("Debug")]
    public bool showDebug = true;
    [Range(0, 1)] public float level01;

    private bool inZone = false;
    private AudioSource _src;
    private AudioClip _micClip;
    private string _deviceName = null;
    private float[] _buffer;
    private float _currentIntensity;
    private bool _micReady = false;
    private int _lastLevel = -1;

    void Awake()
    {
        if (torchLight)
        {
            _currentIntensity = minIntensity;
            torchLight.intensity = minIntensity;
        }

        _src = GetComponent<AudioSource>();
        _src.loop = true;
        _src.mute = true;
        _src.playOnAwake = false;

        _buffer = new float[Mathf.Max(64, sampleCount)];

        if (flameFX) { var em = flameFX.emission; em.rateOverTime = 0f; }
        SetEmission(0f);
    }

    void OnEnable() { StartCoroutine(StartMic()); }

    IEnumerator StartMic()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning($"{name}: No microphone devices found.");
            yield break;
        }

        if (!string.IsNullOrEmpty(preferredDeviceSubstring))
        {
            _deviceName = Microphone.devices.FirstOrDefault(d =>
                d.ToLower().Contains(preferredDeviceSubstring.ToLower()));
        }
        if (string.IsNullOrEmpty(_deviceName))
            _deviceName = Microphone.devices[0];

        if (showDebug) Debug.Log($"{name}: Using microphone: '{_deviceName}'");

        _micClip = Microphone.Start(_deviceName, true, 1, sampleRate);
        while (Microphone.GetPosition(_deviceName) <= 0) yield return null;

        _src.clip = _micClip;
        _src.Play();
        _micReady = true;
    }

    void OnDisable()
    {
        if (!string.IsNullOrEmpty(_deviceName) && Microphone.IsRecording(_deviceName))
            Microphone.End(_deviceName);
        _micReady = false;
    }

    void Update()
    {
        if (!_micReady || _micClip == null || torchLight == null || !inZone)
        {
            ApplyTarget(0f);
            return;
        }

        int pos = Microphone.GetPosition(_deviceName);
        if (pos <= 0) { ApplyTarget(0f); return; }

        int start = pos - sampleCount;
        if (start < 0) start += _micClip.samples;
        _micClip.GetData(_buffer, Mathf.Max(start, 0));

        double sum = 0;
        for (int i = 0; i < _buffer.Length; i++) sum += _buffer[i] * _buffer[i];
        float rms = Mathf.Sqrt((float)(sum / _buffer.Length) + 1e-12f);
        float db = 20f * Mathf.Log10(rms + 1e-12f);
        float t = Mathf.Clamp01(Mathf.InverseLerp(dbFloor, dbCeil, db));
        level01 = t;

        int level = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 10f, t)), 1, 10);
        if (showDebug && level != _lastLevel)
        {
            _lastLevel = level;
            Debug.Log($"{name}: Mic level {level}/10 (dB {db:F1}) device='{_deviceName}' inZone={inZone}");
        }

        float target = Mathf.Lerp(minIntensity, maxIntensity, t);
        ApplyTarget(target);
    }

    private void ApplyTarget(float targetIntensity)
    {
        float lerpSpeed = (targetIntensity > _currentIntensity ? riseLerp : fallLerp);
        _currentIntensity = Mathf.Lerp(_currentIntensity, targetIntensity, 1f - Mathf.Exp(-lerpSpeed * Time.deltaTime));

        torchLight.intensity = _currentIntensity;
        torchLight.enabled = _currentIntensity > 0.01f;

        if (flameFX)
        {
            var em = flameFX.emission;
            float rate = Mathf.Lerp(0f, 40f, Mathf.InverseLerp(0f, maxIntensity * 0.6f, _currentIntensity));
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
        if (other.CompareTag(playerTag))
        {
            inZone = true;
            if (showDebug) Debug.Log($"{name}: Player ENTERED torch zone.");
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            inZone = false;
            if (showDebug) Debug.Log($"{name}: Player LEFT torch zone.");
        }
    }

    void OnGUI()
    {
        if (!showDebug) return;
        if (!inZone) return;

        float h = 6f;
        float w = 200f * level01;
        GUI.color = Color.Lerp(Color.green, Color.red, level01);
        GUI.Box(new Rect(20, 20, w, h), GUIContent.none);
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 28, 260, 20), $"Mic {(int)(level01 * 100)}%  (inZone: {inZone})");
    }
}
