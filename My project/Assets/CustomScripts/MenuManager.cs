using UnityEngine;

public class MenuManager : MonoBehaviour
{
    public GameObject mainMenuCanvas;
    public GameObject selectSongCanvas;

    public void ShowSelectSongMenu()
    {
        mainMenuCanvas.SetActive(false);
        selectSongCanvas.SetActive(true);
    }

    public void GoBackToMainMenu()
    {
        selectSongCanvas.SetActive(false);
        mainMenuCanvas.SetActive(true);
    }
}
