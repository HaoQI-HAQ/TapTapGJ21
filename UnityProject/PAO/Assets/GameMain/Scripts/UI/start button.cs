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
        if (string.IsNullOrEmpty(nextSceneName) || !Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogError("[StartGameBtn] 目标场景未加入构建列表：" + nextSceneName, this);
            return;
        }

        if (clickSound != null)
        {
            audioSource.PlayOneShot(clickSound);
        }
        SceneManager.LoadScene(nextSceneName);
    }
}
