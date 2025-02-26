using OpenAI;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Threading;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class TransformData
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;
}

[System.Serializable]
public class GameObjectData
{
    public string Name;
    public TransformData Transform;
    public Vector3 Forward;
    public List<string> Components;
}

[System.Serializable]
public class SceneData
{
    public List<GameObjectData> GameObjects;
}

[RequireComponent(typeof(AudioSource))]
public class LLMAssistant : MonoBehaviour
{
    [SerializeField] private Button recordButton;
    [SerializeField] private Text message;
    [SerializeField] private Dropdown dropdown;
    private AudioSource audioSource;

    private readonly string fileName = "output.wav";
    MeshRenderer meshRenderer;
    private Color dftColor = new Color(0.2f, 0.6f, 0.6f, 1.0f); // RGBA values for hologram blue
    private Color speakingColor = new Color(0.8f, 0.5f, 0.6f, 1.0f); // RGBA values for hologram blue

    private AudioClip clip;
    private bool isRecording;
    public UnityEvent onAudioStop;
    private bool wasPlaying;
    private OpenAIApi openai = new OpenAIApi("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");
    private CancellationTokenSource token = new CancellationTokenSource();
    int micIndex = 1;
    // a list of deivces
    private List<string> mics = new List<string>();

    private void Start()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.material.color = dftColor;
        audioSource = GetComponent<AudioSource>();
        foreach (var device in Microphone.devices)
        {
            // dropdown.options.Add(new Dropdown.OptionData(device));
            mics.Add(device);
        }
        // recordButton.onClick.AddListener(StartRecording);
        // dropdown.onValueChanged.AddListener(ChangeMicrophone);

        var index = PlayerPrefs.GetInt("user-mic-device-index");
        // dropdown.SetValueWithoutNotify(index);

        // Initialize the wasPlaying flag
        wasPlaying = audioSource.isPlaying;

        // Start the coroutine to monitor the AudioSource
        StartCoroutine(MonitorAudioSource());
        onAudioStop.AddListener(() =>
        {
            meshRenderer.material.color = dftColor;
        });
    }

    public void incrMicIndex()
    {
        micIndex++;
        if (micIndex >= Microphone.devices.Length)
        {
            micIndex = 0;
        }
        // dropdown.SetValueWithoutNotify(micIndex);
        ChangeMicrophone(micIndex);
    }

    private void ChangeMicrophone(int index)
    {
        PlayerPrefs.SetInt("user-mic-device-index", index);
    }

    [ContextMenu("StartRecording")]
    public void StartRecording()
    {
        if (isRecording)
        {
            EndRecording();
            return;
        }
        isRecording = true;
        // recordButton.enabled = false;
        // change the color of the cube
        meshRenderer.material.color = new Color(0.0f, 1.0f, 1.0f, 0.8f); // RGBA values for hologram blue

        var index = PlayerPrefs.GetInt("user-mic-device-index");
        clip = Microphone.Start(mics[index], false, 10, 44100);
    }


    List<ChatMessage> msg = new List<ChatMessage>() {
                new ChatMessage()
                {
                    Role = "system",
                    Content = @"You are an eligent, smart, speech assistant in Unity.
This is a MR environment. The user is wearing a headset.
The user's camera is attached at a gameObject called `CenterEyeAnchor` (it shows the position, the forward, of the user)
You are attached in a Cube named `GPTAssistant`.
Remember, the x (Left and right), y (Up and down), z (Forward and back) axis in Unity.
The user will talk to you and you will respond the user using text, which will be converted to audio. So plz keep your responses short and sweet. Remember, your response should be no more than 50 words.
Whenever the user ask you something, the prompt will also provide the context of the Unity scene. So you can use that context to provide more relevant responses. Remember, it is a MR system, the user is asking question from a first person's perspective."
                },
        };

    [ContextMenu("EndRecording")]
    public async void EndRecording()
    {
        isRecording = false;
        // change the color of the cube back to the default
        meshRenderer.material.color = dftColor;

        // message.text = "Transcripting...";

        Microphone.End(null);
        byte[] data = SaveWav.Save(fileName, clip);

        var req = new CreateAudioTranscriptionsRequest
        {
            FileData = new FileData() { Data = data, Name = "audio.wav" },
            // File = Application.persistentDataPath + "/" + fileName,
            Model = "whisper-1",
            Language = "en"
        };
        var res = await openai.CreateAudioTranscription(req);
        if (res.Text.Trim() == "")
        {
            message.text = "Sorry, I didn't get that. Can you please repeat?";
            return;
        }

        msg.Add(new ChatMessage()
        {
            Role = "user",
            Content = $"Unity Scene:{LLMAssistant.SerializeScene()}\n\nUser's prompt:{res.Text}"
        }
        );

        var completionResponse = await openai.CreateChatCompletion(new CreateChatCompletionRequest()
        {
            Model = "gpt-4o-mini",
            Messages = msg
        });

        if (completionResponse.Choices != null && completionResponse.Choices.Count > 0)
        {
            var message = completionResponse.Choices[0].Message;
            msg.Add(new ChatMessage()
            {
                Role = "assistant",
                Content = message.Content.Trim()
            });

            // foreach (var m in msg)
            // {
            //     Debug.Log($"Role: {m.Role}\nContent: {m.Content}");
            // }
            TextToSpeech(message.Content.Trim());
        }
    }

    private void HandleResponse(List<CreateChatCompletionResponse> responses)
    {
        message.text = string.Join("", responses.Select(r => r.Choices[0].Delta.Content));
    }

    private async void TextToSpeech(string text)
    {
        // message.text = text;
        var request = new CreateTextToSpeechRequest
        {
            Input = text,
            Model = "tts-1",
            Voice = "nova"
        };
        var response = await openai.CreateTextToSpeech(request);

        if (response.AudioClip)
        {
            if (audioSource.isPlaying)
            {
                audioSource.Stop(); // Stop any currently playing audio
            }
            Debug.Log("Playing audio");
            audioSource.PlayOneShot(response.AudioClip);
            meshRenderer.material.color = speakingColor;
        }
    }

    private void OnDestroy()
    {
        token.Cancel();
    }

    private IEnumerator MonitorAudioSource()
    {
        while (true)
        {
            // Check if the audio was playing and has now stopped
            if (wasPlaying && !audioSource.isPlaying)
            {
                // Invoke the onAudioStop event
                onAudioStop.Invoke();
            }

            // Update the wasPlaying flag
            wasPlaying = audioSource.isPlaying;

            // Wait for a short period before checking again
            yield return new WaitForSeconds(0.1f);
        }
    }

    public static string SerializeScene()
    {
        SceneData sceneData = new SceneData();
        sceneData.GameObjects = new List<GameObjectData>();

        foreach (GameObject obj in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            CollectGameObjectData(obj, sceneData.GameObjects);
        }

        return JsonUtility.ToJson(sceneData, true);
    }

    private static void CollectGameObjectData(GameObject obj, List<GameObjectData> sceneData)
    {
        GameObjectData data = new GameObjectData
        {
            Name = obj.name,
            Transform = new TransformData
            {
                Position = obj.transform.position,
                Rotation = obj.transform.rotation,
                Scale = obj.transform.localScale
            },
            Components = new List<string>()
        };

        foreach (Component component in obj.GetComponents<Component>())
        {
            data.Components.Add(component.GetType().Name);
        }

        // Check if the object has MeshRenderer and BoxCollider components
        bool hasMeshRenderer = obj.GetComponent<MeshRenderer>() != null;
        bool hasBoxCollider = obj.GetComponent<BoxCollider>() != null;

        if (hasMeshRenderer && hasBoxCollider)
        {
            sceneData.Add(data);
        }
        else if (obj.name == "CenterEyeAnchor")
        {
            // get camera component
            var camera = obj.GetComponent<Camera>();
            // get the camera's forward vector
            var forward = camera.transform.forward;
            data.Forward = forward;
            sceneData.Add(data);
        }
        foreach (Transform child in obj.transform)
        {
            CollectGameObjectData(child.gameObject, sceneData);
        }
    }
}
