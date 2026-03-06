using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

/// <summary>
/// This script handles Speech to Text using OpenAI Whisper API
/// </summary>
public class Transcribe : MonoBehaviour
{
    [Header("Model Settings")]
    private string apiKey;
    [SerializeField] private string model = "gpt-4o-transcribe";
    [SerializeField] private bool translate = false;        // If true, the audio will be translated to English

    private const int CLIP_LENGTH = 20;
    private const int CLIP_FREQUENCY = 16000;

    [Space]
    [Header("Audio trim settings")]
    [SerializeField, Range(0.0f, 0.1f)] private float silenceThreshold = 0.02f; // Threshold to consider as silence
    [SerializeField, Range(0.0f, 1.0f)] private float minSilenceLength = 1.5f; // Minimum length of silence to trim, in seconds

    private AudioClip audioClip;
    [Space]
    [Header("Device")]
    /// <summary>
    /// Set your prefered device name here to auto select microphone, if unsure, test run for once and check the log of all device names
    /// </summary>
    [SerializeField] private string[] preferredDeviceNames;
    private string deviceName;
    private bool isRecording = false;
    // Recording should be disabled soon as the transcription starts and only be re-enabled when the agent finishes speaking or upon error
    [SerializeField]
    private bool canRecord = true;

    [Space]
    public LLMDialogueManager dialogueManager;

    public enum LanguageMode
    {
        AutoDetect,
        Chinese,
        Korean,
        Japanese,
        English,
    }
    [Space]
    [Header("Language Selection")]
    public LanguageMode languageMode = LanguageMode.English;

    /// <summary>
    /// Match LanguageMode to language code, supports some of ISO-639-1 and ISO-639-3 codes.
    /// For more details, see: https://platform.openai.com/docs/guides/speech-to-text, the new documentation is for gpt-4o-transcribe but it still originates from Whisper-1
    /// To add a language, first add it to the LanguageMode enum above, then add the corresponding code to the dictionary below.
    /// Test if the language code is supported first before deploying!
    /// If the input language is unsure, use AutoDetect.
    /// </summary>
    private static readonly Dictionary<LanguageMode, string> LanguageCodes = new()
    {
        { LanguageMode.AutoDetect, null },
        { LanguageMode.Chinese, "zh" },
        { LanguageMode.Korean, "ko" },
        { LanguageMode.Japanese, "ja" },
        { LanguageMode.English, "en" }
    };
    private string GetLanguageCode(LanguageMode mode) => LanguageCodes.TryGetValue(mode, out var code) ? code : null;

    private Coroutine stopButtonCoroutine;
    private bool isUploading = false;

    void Start()
    {
        apiKey = APIKeys.APIKey;

        //Initialize device
        StartCoroutine(InitializeMicrophone());

        // Pre-warm microphone to avoid initial lag
        StartCoroutine(PreWarmMicrophone());

        // Subscribe to TextToSpeech audio playback events to stop recording when the agent is speaking, the current structure does not support interrupting
        TextToSpeech.OnAudioPlayback += HandleAudioPlayback;

        LLMDialogueManager.OnGenerationFailed += HandleGenerationResult;
    }

    void OnDestroy()
    {
        TextToSpeech.OnAudioPlayback -= HandleAudioPlayback;
        LLMDialogueManager.OnGenerationFailed -= HandleGenerationResult;
    }

    private void HandleAudioPlayback(bool isPlaying)
    {
        canRecord = !isPlaying;
    }

    private void HandleGenerationResult(bool isComplete)
    {
        canRecord = !isComplete;
    }

    /// <summary>
    /// Wait for the device list to load and then select the mic for input 
    /// </summary>
    IEnumerator InitializeMicrophone()
    {
        // Wait for one frame to ensure Microphone.devices is populated
        yield return null;

        // Select microphone device
        try
        {
            if (Microphone.devices.Length > 0)
            {
                Debug.Log("-> Microphones: " + Microphone.devices.Length);
                deviceName = Microphone.devices[0];

                if (Microphone.devices.Length > 1)
                {
                    bool foundPreferredDevice = false;
                    for (int i = 0; i < Microphone.devices.Length; i++)
                    {
                        Debug.Log(Microphone.devices[i]);
                        string device = Microphone.devices[i].ToUpper();
                        foreach (var preferredName in preferredDeviceNames)
                        {
                            if (device.Contains(preferredName.ToUpper()))
                            {
                                deviceName = Microphone.devices[i];
                                foundPreferredDevice = true;
                                break;
                            }
                        }
                    }
                    if (!foundPreferredDevice)
                    {
                        deviceName = Microphone.devices[0];
                    }
                }
            }
            else
            {
                Debug.LogError("-> No Microphone found! :(");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize microphone: {e.Message}");
        }
    }

    IEnumerator PreWarmMicrophone()
    {
        // Wait for deviceName to be initialized
        while (string.IsNullOrEmpty(deviceName))
        {
            yield return null;
        }
        if (Microphone.devices.Length > 0)
        {
            var preClip = Microphone.Start(deviceName, false, 1, CLIP_FREQUENCY);
            // Wait until the microphone has started recording
            while (!(Microphone.GetPosition(deviceName) > 0)) { yield return null; }
            Microphone.End(deviceName); // Then stop recording immediately
            Debug.Log("Microphone pre-warmed.");
        }
    }

    void Update()
    {
        // Stop recording when the sample limit was reached
        if (isRecording)
        {
            if (Microphone.GetPosition(deviceName) >= audioClip.samples)
            {
                StopRecording();
            }
        }
    }

    // Public method to call when start/stop command is given
    public void OnStartButtonPressed()
    {
        Debug.Log("record button pressed");
        if (stopButtonCoroutine != null)
        {
            StopCoroutine(stopButtonCoroutine);
            Debug.Log("Stop button pressed again within 1 second, resuming recording.");
        }
        else
        {
            if (canRecord)
                StartRecording();
            else
                Debug.LogWarning("Cannot start recording now");
        } 
    }

    public void OnStopButtonPressed()
    {
        stopButtonCoroutine = StartCoroutine(HandleStopButtonPress());
    }

    private IEnumerator HandleStopButtonPress()
    {
        Debug.Log("Stop button pressed, waiting for 0.5 second to confirm.");
        yield return new WaitForSeconds(0.5f);
        StopRecording();
        stopButtonCoroutine = null;
    }

    private void StartRecording()
    {
        Debug.Log("-> StartRecording() - " + deviceName);
        audioClip = Microphone.Start(deviceName, false, CLIP_LENGTH, CLIP_FREQUENCY);
        isRecording = true;
        canRecord = false;
    }

    private void StopRecording()
    {
        if (!isRecording)
        {
            Debug.LogWarning("StopRecording called while not recording. Ignored.");
            return;
        }

        Debug.Log("-> StopRecording() - " + PrintAudioClipDetail(audioClip));
        Microphone.End(deviceName);
        audioClip.name = "Recording";
        isRecording = false;
        TrimSilence();

        if (audioClip.channels > 1)
        {
            //The recording is stereo, we want to feed a mono audioClip to the Whisper model.
            ConvertToMono();
        }

        // Check clip length to ditch too short recordings
        if (audioClip.length < 1.0f)
        {
            Debug.LogWarning("Audio is too short to upload");
            canRecord = true;
            return;
        }

        if (isUploading)
        {
            Debug.LogWarning("Upload already in progress, skipping this recording.");
            canRecord = true;
            return;
        }

        Debug.Log("Recording finished, uploading...");
        StartCoroutine(UploadRecording());
    }

    public void ConvertToMono()
    {
        int channels = audioClip.channels;
        int samples = audioClip.samples;

        float[] stereoData = new float[samples * channels];
        audioClip.GetData(stereoData, 0);

        float[] monoData = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float sum = 0f;

            // Sum all the channel values for this sample
            for (int j = 0; j < channels; j++)
            {
                sum += stereoData[i * channels + j];
            }

            // Average the sum to get the mono sample value
            monoData[i] = sum / channels;
        }

        // Create a new AudioClip in mono and set the data
        AudioClip monoClip = AudioClip.Create(audioClip.name + "_Mono", samples, 1, audioClip.frequency, false);
        monoClip.SetData(monoData, 0);
        audioClip = monoClip;
        //Debug.Log("-> ConvertToMono() - " + PrintAudioClipDetail(audioClip));
    }

    /// <summary>
    /// Trim leading and trailing silence from the audio clip
    /// </summary>
    private void TrimSilence()
    {
        if (!isRecording)
        {
            if (audioClip is null)
            {
                Debug.LogError("clip is NULL");
                return;
            }

            int channels = audioClip.channels;
            int frequency = audioClip.frequency;
            int samples = audioClip.samples;

            float[] audioData = new float[samples * channels];
            audioClip.GetData(audioData, 0);

            bool isSilent = false;
            float silenceStart = 0;
            var trimmedSamples = new List<float>();

            for (int i = 0; i < audioData.Length; i += channels)
            {
                // Simple volume estimation
                float volume = Mathf.Abs(audioData[i]);
                if (volume < silenceThreshold)
                {
                    if (!isSilent)
                    {
                        isSilent = true;
                        //frequency * channels = samples per second, this converts sample index to time in seconds
                        silenceStart = i / (float)(frequency * channels); 
                    }
                }
                else
                {
                    if (isSilent)
                    {
                        float silenceDuration = i / (float)(frequency * channels) - silenceStart;
                        if (silenceDuration < minSilenceLength)
                        {
                            // Add the silence back, as it's too short to be considered true silence
                            for (int j = (int)(silenceStart * frequency * channels); j < i; j++)
                            {
                                trimmedSamples.Add(audioData[j]);
                            }
                        }
                        isSilent = false;
                    }
                    else
                    {
                        trimmedSamples.Add(audioData[i]);
                    }
                }
            }

            if (trimmedSamples.Count > 0)
            {
                // Create a new AudioClip
                AudioClip trimmedClip = AudioClip.Create(audioClip.name + "_Trimmed", trimmedSamples.Count, channels, frequency, false);
                trimmedClip.SetData(trimmedSamples.ToArray(), 0);
                audioClip = trimmedClip; // Replace the old clip with the trimmed clip
                Debug.Log("-> TrimSilence() - " + PrintAudioClipDetail(audioClip));
            }
        }
    }

    private string PrintAudioClipDetail(AudioClip clip)
    {
        string details = "clip secs: " + audioClip.length + ", samp: " + audioClip.samples + ", chan: " + audioClip.channels + ", freq: " + audioClip.frequency;
        return details;
    }

    IEnumerator UploadRecording()
    {
        isUploading = true;
        string filePath = Path.Combine(Application.persistentDataPath, "recorded.wav");
        SaveWav(filePath, audioClip);

        byte[] audioData = File.ReadAllBytes(filePath);

        // This post will contain file and text fields, so we need to store it in a form
        WWWForm form = new WWWForm();

        // Setting up the form fields
        form.AddBinaryData("file", audioData, "recorded.wav", "audio/wav");
        form.AddField("model", model);

        string lang = GetLanguageCode(languageMode);
        if (!string.IsNullOrEmpty(lang))    // Do not add language field if set to auto-detect
        {
            form.AddField("language", lang);
        }

        string url = translate
        ? "https://api.openai.com/v1/audio/translations"
        : "https://api.openai.com/v1/audio/transcriptions";

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Transcribe success");
                string result = request.downloadHandler.text;
                Debug.Log("Returned Json��" + result);   // Display the JSON

                // The result is in JSON format, this is to parse it to extract the transcribed text
                // Details of FromJson https://docs.unity3d.com/ScriptReference/JsonUtility.FromJson.html
                WhisperResponse response = JsonUtility.FromJson<WhisperResponse>(result);

                // Trigger dialogue generation if the transcription is successful
                if (response != null && !string.IsNullOrEmpty(response.text))
                {
                    Debug.Log("Extracted text��" + response.text);   //Display the actual text
                    dialogueManager.GenerateDialogue(response.text);
                }
                else
                {
                    canRecord = true;
                    Debug.LogWarning("Text field is empty");
                }
            }
            else
            {
                canRecord = true;
                string responseBody = request.downloadHandler != null ? request.downloadHandler.text : "<no-body>";
                string requestId = request.GetResponseHeader("x-request-id");
                string limitReq = request.GetResponseHeader("x-ratelimit-limit-requests");
                string remainReq = request.GetResponseHeader("x-ratelimit-remaining-requests");
                string resetReq = request.GetResponseHeader("x-ratelimit-reset-requests");

                Debug.LogError(
                    "Transcribe failed " +
                    $"status={(int)request.responseCode} " +
                    $"error={request.error} " +
                    $"x-request-id={requestId} " +
                    $"rate-limit={limitReq} " +
                    $"remaining={remainReq} " +
                    $"reset={resetReq} " +
                    $"body={responseBody}"
                );
            }

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        isUploading = false;
        
    }

    /// <summary>
    /// Save AudioClip to WAV file in given filepath, using WavUtility script
    /// </summary>
    void SaveWav(string filepath, AudioClip clip)
    {
        if (clip != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filepath));
            WavUtility.Save(filepath, clip);
            Debug.Log("Saved as" + filepath);
        }
        else
        {
            Debug.LogError("No recording found");
        }
    }

    [System.Serializable]   // The FromJson method requires the class to be serializable
    private class WhisperResponse
    {
        public string text;
    }

    /// <summary>
    /// Public method to set whether recording is allowed
    /// </summary>
    public void SetCanRecord(bool value)
    {
        canRecord = value;
    }
}
