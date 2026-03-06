# Virtual Session With Multilingual Virtual Agent
This is a virtual icebreaker session with a multilingual conversational virtual agent aimed to reduce foreign language anxiety for non-native speakers by allowing them to speak casually using their mother tongue while listening the virtual agent's speech in the target language (English by default).

Talk to the agent like **using a walkie-talkie** - press and hold the talk button (A button on the Oculus right controller by default) to speak, release when you want to finish.

There are two scenes for VR and non-vr respectively. This can be chosen from the scene folder

# Setup instructions
Most of the scripts that require setting up were attached to the AIManager object in the scene. Additionally, the gaze model was attached to the avatar's root and the lip-sync was attached to its head.

## Setting up the API key
This project is based on OpenAI's online service, so an OpenAI API key is required.
For simplicity, the project keeps the API key with a local parameter instead of reading it through an environment variable.
*Some extra caution is advised to prevent key leak!*

You can purchase an API key from: https://platform.openai.com/
To start, go to Assets/Scripts/APIKeys.cs and replace the string with your own key.

### IMPORTANT SAFETY WARNING
API keys should ***Always be kept secret*** and never shared. To avoid accidentally committing to the online repo, as soon as you pull the repo, run the following command:
```
git update-index --assume-unchanged Assets/Scripts/APIKeys.cs
```
This will let git ignore the changes to the APIKeys script.

### API cost
Although the API usage isn't free, the cost of this project is quite low. As a reference, the development and 10 pilot sessions together only cost $0.28.

## Setting up the Transcribe service
First, locate the Transcribe component on the AIManager; there are various settings that might be worth playing with. The Model field shows the name of the model called for the transcription task. 'gpt-4o-transcribe' is fine. The trim settings control how slience were trimed from the recording.

### Choosing up the mic
In the *preferred device* list, type in a series of device names in order to compare with exsisting device list. All the names should be in **UPPER CASE**.

Alternatively, you can run the project once and search for "Mic" in the console to check the logged device name list.

### Choosing language & translation
Select a language in the *Language Mode* section to set a preferred transcription language. In a multilanguage FLA training scenario, this should match the language that the participant use during the session.
* Note that this does not strictly limit the resulting language; it only sets a bias to language recognition.
If the model confidently detects a different language, it will still output in that specific language. In practice, closely related languages like Portuguese and Spanish may be confused with wrong language mode, but drastically different languages like Chinese and German will not be much affected.

More language can be added through script, check the annotation for more information.

The *Translation* option determines whether the model will attempt to translate everything to English or transcribe with the detected language.

## Setting up the Dialogue Manager
The LLM Dialogue Manager component holds most of the options for the conversation with the LLM.

Under Model Settings, there are useful parameters to choose models, conversation histories and token limits. Check [https://platform.openai.com/docs/api-reference/chat](OpenAI API Reference) for a better understanding.

### System prompt & structured reply
The system prompt defines the default settings and knowledge of the LLM instance; it may contain information such as personalities, tasks, environment context, and even the agent's native language.
* The **default reply language** of the agent is defined in the system prompt.

To customise the system prompt, write it down in a .txt file and assign it to the *System Prompt Asset*.

The JSON schema allows the LLM to output in a predefined format. Read [https://platform.openai.com/docs/guides/structured-outputs?api-mode=chat](this) for a better explanation.

## Setting up the Text To Speech service
The *Selected Voice* parameter allows selecting a voice for the agent. This is useful when changing the avatar appearance for the agent, [https://platform.openai.com/docs/guides/text-to-speech#voice-options](here) to find the complete voice list and samples.

## Other settings and notes
* The emotion animations for the avatar were downloaded from Mixamo, and many of them were meant for more cartoon-style characters. During the pilot study, many participants complained that the animations were over-exaggerated. This can be switched off with the *Play Emotion Animation* option in the LLMDialogueManager; switching it off will result in the agent always speaking with the natural animation.
* The gaze and head model is not made for making quick turns; it's advised to limit the participants' movement range.
* The talk button for VR was bound using the *\[BuildingBlock\] Controller Buttons Mapper*, more controls can be added in the same way.
* The talk button for non-VR is space by default.
* It's common to experience a lag or slight freeze during the first transcription task of each session. This is due to Mic warming up.

## Research paper
My research paper can be found here: [https://dl.acm.org/doi/10.1145/3717511.3749308] 
