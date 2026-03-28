using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    private Rigidbody2D rb;
    private Vector2 moveInput;

    // Last non-zero direction for idle facing
    private Vector2 lastDirection = Vector2.down;

    // Multiplied into movement speed — status effects (slow, etc.) set this below 1.
    private float speedMultiplier = 1f;
    public float SpeedMultiplier { get => speedMultiplier; set => speedMultiplier = Mathf.Max(0f, value); }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        moveInput.x = Input.GetAxisRaw("Horizontal");
        moveInput.y = Input.GetAxisRaw("Vertical");
        moveInput.Normalize();

        if (moveInput != Vector2.zero)
            lastDirection = moveInput;
    }

    private void FixedUpdate()
    {
        rb.MovePosition(rb.position + moveInput * moveSpeed * speedMultiplier * Time.fixedDeltaTime);
    }

    // Called by PlayerAnimator once animations are set up
    public Vector2 MoveInput => moveInput;
    public Vector2 LastDirection => lastDirection;
    public bool IsMoving => moveInput != Vector2.zero;
}
