using UnityEngine;
using UnityEngine.SceneManagement;

public class StartGameBtn : MonoBehaviour
{
    public string nextSceneName;
    public AudioClip clickSound;
    private AudioSource audioSource;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    public void LoadGameScene()
    {
        if (clickSound != null)
        {
            audioSource.PlayOneShot(clickSound);
        }
        SceneManager.LoadScene(nextSceneName);
    }
}
