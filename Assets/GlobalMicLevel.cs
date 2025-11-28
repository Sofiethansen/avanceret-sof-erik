using UnityEngine;
using System.Collections;
using System.Linq;

[RequireComponent(typeof(AudioSource))]
public class GlobalMicLevel : MonoBehaviour
{
    public static GlobalMicLevel Instance { get; private set; }

    [Header("Mic Device")]
    [Tooltip("Matches part of the microphone name. For Quest PC Link use 'oculus'. Leave empty to use first device.")]
    public string preferredDeviceSubstring = "oculus";

    [Header("Mic Sensitivity (dB)")]
    [Tooltip("Silence / background noise level")]
    public float dbFloor = -50f;
    [Tooltip("Full yelling / max brightness level")]
    public float dbCeil = -18f;

    [Header("Mic Settings")]
    public int sampleRate = 22050;
    public int sampleCount = 512;

    [Header("Debug")]
    public bool showDebug = true;
    [Range(0, 1)] public float level01;
    public float currentDb;

    private AudioSource _src;
    private AudioClip _micClip;
    private string _deviceName = null;
    private float[] _buffer;
    private bool _micReady = false;

    public bool MicReady => _micReady;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("GlobalMicLevel: duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _src = GetComponent<AudioSource>();
        _src.loop = true;
        _src.mute = true;
        _src.playOnAwake = false;

        _buffer = new float[Mathf.Max(64, sampleCount)];
    }

    void OnEnable()
    {
        StartCoroutine(StartMic());
    }

    void OnDisable()
    {
        if (!string.IsNullOrEmpty(_deviceName) && Microphone.IsRecording(_deviceName))
            Microphone.End(_deviceName);

        _micReady = false;
    }

    IEnumerator StartMic()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("GlobalMicLevel: No microphone devices found.");
            yield break;
        }

        if (!string.IsNullOrEmpty(preferredDeviceSubstring))
        {
            _deviceName = Microphone.devices.FirstOrDefault(d =>
                d.ToLower().Contains(preferredDeviceSubstring.ToLower()));
        }

        if (string.IsNullOrEmpty(_deviceName))
            _deviceName = Microphone.devices[0];

        if (showDebug)
            Debug.Log($"GlobalMicLevel: Using microphone: '{_deviceName}'");

        _micClip = Microphone.Start(_deviceName, true, 1, sampleRate);
        while (Microphone.GetPosition(_deviceName) <= 0) yield return null;

        _src.clip = _micClip;
        _src.Play();
        _micReady = true;

        if (showDebug)
            Debug.Log("GlobalMicLevel: Mic ready.");
    }

    void Update()
    {
        if (!_micReady || _micClip == null)
        {
            level01 = 0f;
            return;
        }

        int pos = Microphone.GetPosition(_deviceName);
        if (pos <= 0)
        {
            level01 = 0f;
            return;
        }

        int start = pos - sampleCount;
        if (start < 0) start += _micClip.samples;
        _micClip.GetData(_buffer, Mathf.Max(start, 0));

        double sum = 0;
        for (int i = 0; i < _buffer.Length; i++)
            sum += _buffer[i] * _buffer[i];

        float rms = Mathf.Sqrt((float)(sum / _buffer.Length) + 1e-12f);
        float db = 20f * Mathf.Log10(rms + 1e-12f);
        currentDb = db;

        level01 = Mathf.Clamp01(Mathf.InverseLerp(dbFloor, dbCeil, db));
    }

    void OnGUI()
    {
        if (!showDebug) return;

        float h = 6f;
        float w = 200f * level01;
        GUI.color = Color.Lerp(Color.green, Color.red, level01);
        GUI.Box(new Rect(20, 40, w, h), GUIContent.none);
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 48, 260, 20),
            $"Global Mic: {(int)(level01 * 100)}%  (dB {currentDb:F1})");
    }
}
