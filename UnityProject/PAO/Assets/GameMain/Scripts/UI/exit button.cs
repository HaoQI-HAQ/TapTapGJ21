using UnityEngine;

public class QuitGameBtn : MonoBehaviour
{
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

    public void QuitGame()
    {
        if (clickSound != null)
        {
            audioSource.PlayOneShot(clickSound);
        }

#if UNITY_EDITOR
        Debug.Log("编辑器模式：退出游戏，打包后生效");
#else
        Application.Quit();
#endif
    }
}
