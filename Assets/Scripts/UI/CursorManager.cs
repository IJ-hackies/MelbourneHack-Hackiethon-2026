using UnityEngine;

public class CursorManager : MonoBehaviour
{
    [SerializeField] Texture2D cursorTexture;
    [SerializeField] Vector2 hotspot = Vector2.zero;

    void Start()
    {
        if (cursorTexture != null)
            Cursor.SetCursor(cursorTexture, hotspot, CursorMode.Auto);
    }
}
