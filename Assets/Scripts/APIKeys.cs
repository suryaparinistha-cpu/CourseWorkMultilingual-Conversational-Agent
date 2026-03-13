using UnityEngine;

/// <summary>
/// This script holds OpenAI API keys for all the services
/// Do not forget to set your API key here
/// Keys are confidential information and should not be shared publicly, so do not commit this script to public repositories
/// Maybe just add this to .gitignore for good
/// For a safer approach, consider changing this part to using environment variables yourself
/// </summary>
public class APIKeys : MonoBehaviour
{
    public static APIKeys Instance { get; private set; }

    /// <summary>
    /// Don't forget to put in your OpenAI API Key
    /// </summary>

    public static string APIKey => Instance.apiKey;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
