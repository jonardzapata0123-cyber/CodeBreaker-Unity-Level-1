using UnityEngine;

public class MouseMovement : MonoBehaviour
{
    public float mouseSensitivity = 100f;
    private Transform playerBody;
    private float xRotation = 0f;

    void Start()
    {
        // Kusa nitong kukunin ang Player parent object para hindi mo na i-drag sa Inspector
        playerBody = transform.parent;

        // I-lock ang cursor sa gitna ng screen
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        // Camera lang ang titingin pataas at pababa
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // Ang mismong Player parent ang iikot pakaliwa at pakanan
        if (playerBody != null)
        {
            playerBody.Rotate(Vector3.up * mouseX);
        }
    }
}