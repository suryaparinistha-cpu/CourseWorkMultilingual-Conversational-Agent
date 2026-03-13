using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using UnityEngine.Networking;
using System.Text;

/// <summary>
/// This script handles the communication with the LLM model,
/// It also works as a central hub for:
///     Controling character states
///     Calling Text-to-Speech methods
///     Reenabling transcription recording upon errors or when the response is complete
/// </summary>
public class LLMDialogueManager : MonoBehaviour
{
    private string apiKey;
    private string apiUrl = "https://api.openai.com/v1/chat/completions";

    [Space]
    [Header("Model Settings")]
    // List of available models may change, check https://platform.openai.com/docs/api-reference/models?lang=curl to get the latest list
    [SerializeField] private string chosenModel = "gpt-4o";
    // Maximum number of messages to keep in the conversation history
    [SerializeField] private int conversationLimit = 10;
    [SerializeField] private int tokenLimit = 400;

    // List conatining the conversation history
    private List<Message> conversationHistory = new List<Message>();

    [Space]
    [Header("Dialogue Settings")]
    // Define the JSON schema for structured response
    [SerializeField] private string jsonSchema = @"{
        ""name"": ""message_response"",
        ""schema"": {
            ""type"": ""object"",
            ""properties"": {
                ""content"": {
                    ""type"": ""string"",
                    ""description"": ""The content of the response message.""
                },
                ""emotion"": {
                    ""type"": ""string"",
                    ""description"": ""The emotion conveyed by the response."",
                    ""enum"": [
                        ""happy"",
                        ""sad"",
                        ""angry"",
                        ""surprised"",
                        ""confused"",
                        ""neutral""
                    ]
                }
            },
            ""required"": [
                ""content"",
                ""emotion""
            ],
            ""additionalProperties"": false
        },
        ""strict"": true
    }";
    [SerializeField] private TextAsset systemPromptAsset; // Write system promt in a txt file and assign it in the inspector
    [SerializeField] private AgentLanguage agentLanguage = AgentLanguage.English;
    private string systemPrompt;

    private bool canGenerateDialogue = true;

    public delegate void GenerationFailedHandler(bool isComplete);
    public static event GenerationFailedHandler OnGenerationFailed;

    [Space]
    [Header("Avatar Settings")]
    [SerializeField] private Animator animator;
    public bool PlayEmotionAnimations = true;

    public enum Emotion
    {
        neutral,
        happy,
        sad,
        angry,
        surprised,
        confused
    }

    public enum AgentLanguage
    {
        English,
        Spanish,
        French,
        German,
        Italian,
        Portuguese,
        Hindi,
        Japanese,
        Korean,
        ChineseSimplified,
        Catalan
    }

    string emotionCache;

    private void Start()
    {
        // Load Api Key
        apiKey = APIKeys.APIKey;

        // Check if a system prompt is assigned
        if (systemPromptAsset != null)
        {
            systemPrompt = BuildSystemPrompt();
            // Add system prompt to the conversation history
            conversationHistory.Add(new Message { role = "developer", content = systemPrompt });
        }
        else
            Debug.LogError("Please assign a system prompt TextAsset in the Inspector!");
        // Disable dialogue generation when audio is playing.
        // The OnAudioPlayback is a static event so nomatter which instance of TextToSpeech triggers it, this will be notified
        // The event will be triggered either when playback starts or ends with a boolean parameter
        TextToSpeech.OnAudioPlayback += HandleAudioPlayback;
    }

    void OnDestroy()
    {
        TextToSpeech.OnAudioPlayback -= HandleAudioPlayback;
    }

    // When a playback is in progress, disable dialogue generation
    private void HandleAudioPlayback(bool isPlaying)
    {
        canGenerateDialogue = !isPlaying;
    }

    //Generate dialogue
    public void GenerateDialogue(string userMessage)
    {
        if (!canGenerateDialogue) return;   // Just for safety

        // Add user message to the conversation history
        conversationHistory.Add(new Message { role = "user", content = userMessage });

        // Manage conversation history length
        TrimConversationHistory();

        StartCoroutine(SendRequest(userMessage));
    }

    // Cut conversation history to save tokens
    private void TrimConversationHistory()
    {
        if (conversationHistory.Count > conversationLimit * 2 + 1)  // Each user-assistant pair counts as 2 messages, plus 1 for the system prompt
        {
            conversationHistory.RemoveRange(1, 2);
        }
    }

    private IEnumerator SendRequest(string userMessage)
    {
        // First cionstruct the output structure
        LLMRequest requestData = new LLMRequest
        {
            model = chosenModel,
            messages = conversationHistory.ToArray(),
            max_tokens = tokenLimit,
            response_format = new Dictionary<string, object>
            {
                { "type", "json_schema" }, // Type could be "text", "json_schema" and "json_object"
                { "json_schema", JsonConvert.DeserializeObject<object>(jsonSchema) } // Must use JsonConverter as the JsonUtility does not support deserializing to object
            }
        };

        string jsonPayload = JsonConvert.SerializeObject(requestData, Formatting.Indented);

        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST")) //ËäÈ»UnityEngine.NetworkingÒÑ¾­°üº¬ÁËUnityWebRequest£¬µ«ÕâÀïÊ¹ÓÃusingÓï¾äÀ´¹ÜÀíÆäÉúÃüÖÜÆÚ£¡£¡£¡
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            // Check if the generation failed
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to send request: " + request.error);
                RestoreRecording(); // Reenable transcription
                yield break;
            }
            else
            {
                Debug.Log("Response: " + request.downloadHandler.text);

                // Deserialize the response
                LLMResponse llmResponse = JsonUtility.FromJson<LLMResponse>(request.downloadHandler.text);

                // If the response format is invalid...
                if (llmResponse == null || llmResponse.choices == null || llmResponse.choices.Length == 0)
                {
                    Debug.LogError("Invalid GPT response format");
                    RestoreRecording();
                    yield break;
                }

                string llmReply = llmResponse.choices[0].message?.content;
                if (string.IsNullOrEmpty(llmReply))
                {
                    Debug.LogError("Empty or null response from GPT");
                    RestoreRecording();
                    yield break;
                }

                // Process the raw NPC reply to extract the JSON part
                llmReply = ProcessNPCReply(llmReply);
                ActualResponse actualResponse = JsonUtility.FromJson<ActualResponse>(llmReply);

                if (actualResponse == null)
                {
                    Debug.LogError("Failed to parse actual response");
                    RestoreRecording();
                    yield break;
                }

                string npcReply = actualResponse.content;
                string emotion = actualResponse.emotion;

                // ¸üÐÂ¶Ô»°ÀúÊ·
                conversationHistory.Add(new Message { role = "assistant", content = npcReply });

                HandleRespond(npcReply, emotion);
            }
        }
    }

    // Log the result and sent to TTS
    void HandleRespond(string message, string emotion)
    {
        Debug.Log("NPC: " + message + "emotion: " + emotion);
        TextToSpeech tts = FindFirstObjectByType<TextToSpeech>();
        if (tts != null)
        {
            tts.CreateSpeech(message, emotion);
        }
        else
        {
            Debug.LogError("TTS not found in the scene.");
        }
    }

    private void RestoreRecording()
    {
        OnGenerationFailed?.Invoke(true); // Reenable transcription
    }

    private string ProcessNPCReply(string rawNpcReply)
    {
        int jsonStart = rawNpcReply.IndexOf('{');
        int jsonEnd = rawNpcReply.LastIndexOf('}');
        return rawNpcReply.Substring(jsonStart, jsonEnd - jsonStart + 1);
    }

    private string BuildSystemPrompt()
    {
        return $"You always speak in {GetLanguagePromptLabel(agentLanguage)}. {systemPromptAsset.text}";
    }

    private string GetLanguagePromptLabel(AgentLanguage language)
    {
        switch (language)
        {
            case AgentLanguage.English:
                return "English";
            case AgentLanguage.Spanish:
                return "Spanish";
            case AgentLanguage.French:
                return "French";
            case AgentLanguage.German:
                return "German";
            case AgentLanguage.Italian:
                return "Italian";
            case AgentLanguage.Portuguese:
                return "Portuguese";
            case AgentLanguage.Hindi:
                return "Hindi";
            case AgentLanguage.Japanese:
                return "Japanese";
            case AgentLanguage.Korean:
                return "Korean";
            case AgentLanguage.ChineseSimplified:
                return "Simplified Chinese";
            case AgentLanguage.Catalan:
                return "Catalan";
            default:
                return language.ToString();
        }
    }

    // Call this method to clear the conversation history when needed
    public void ClearConversation()
    {
        conversationHistory.Clear();
        systemPrompt = BuildSystemPrompt();
        conversationHistory.Add(new Message { role = "developer", content = systemPrompt });
    }

    public void HandleEmotion(string emotion)
    {
        if (!PlayEmotionAnimations)
        {
            emotionCache = Emotion.neutral.ToString();
            return;
        }
        Emotion detectedEmotion = ParseEmotion(emotion);
        emotionCache = detectedEmotion.ToString();
    }

    private Emotion ParseEmotion(string emotion)
    {
        if (System.Enum.TryParse(emotion, true, out Emotion result))
        {
            return result;
        }
        return Emotion.neutral;
    }

    public void StartTalkingAnimation()
    {
        animator.SetBool(emotionCache, true);
    }

    public void StopTalkingAnimation()
    {
        animator.SetBool(emotionCache, false);
    }
}

// Classes for the components

[System.Serializable]
public class LLMRequest
{
    public string model; // Model name
    public Message[] messages;
    public int max_tokens;
    public object response_format; // JSON schema for structured response
}

// A class for both outgoing and incomming message
[System.Serializable]
public class Message
{
    public string role;
    public string content;
}

[System.Serializable]
public class LLMResponse
{
    public Choice[] choices;
}

[System.Serializable]
public class Choice
{
    public Message message;
}

[System.Serializable]
public class ActualResponse
{
    public string content; // Reply text
    public string emotion; // Perdicted emotion
}
