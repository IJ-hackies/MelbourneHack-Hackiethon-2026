using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Dash")]
    [SerializeField] private float     dashDistance   = 3f;
    [SerializeField] private float     dashDuration   = 0.12f;
    [SerializeField] private float     dashCooldown   = 1.5f;
    [SerializeField] private float     iFrameDuration = 0.5f;
    [SerializeField] private AudioClip dashClip;
    [SerializeField] [Range(0f, 1f)] private float dashAudioVolume = 1f;

    [Header("Dash Speed Boost")]
    [SerializeField] private float     dashBoostDuration  = 0.8f;
    [SerializeField] private float     dashBoostMultiplier = 1.4f;

    private Rigidbody2D rb;
    private Vector2 moveInput;

    // Last non-zero direction for idle facing
    private Vector2 lastDirection = Vector2.down;

    // Multiplied into movement speed — status effects (slow, etc.) set this below 1.
    private float speedMultiplier = 1f;
    public float SpeedMultiplier { get => speedMultiplier; set => speedMultiplier = Mathf.Max(0f, value); }

    // Dash state
    private bool  isDashing;
    private bool  isIFrameActive;
    private float dashCooldownTimer;
    private Health playerHealth;
    private Coroutine boostCoroutine;

    public bool  IsDashing              => isDashing;
    public bool  IsIFrameActive         => isIFrameActive;
    public float DashCooldownPct       => dashCooldown > 0f ? Mathf.Clamp01(dashCooldownTimer / dashCooldown) : 0f;
    public float DashCooldownRemaining => Mathf.Max(0f, dashCooldownTimer);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerHealth = GetComponent<Health>();
    }

    private void Update()
    {
        moveInput.x = (Input.GetKey(SettingsData.MoveRight) ? 1f : 0f)
                    - (Input.GetKey(SettingsData.MoveLeft)  ? 1f : 0f);
        moveInput.y = (Input.GetKey(SettingsData.MoveUp)    ? 1f : 0f)
                    - (Input.GetKey(SettingsData.MoveDown)  ? 1f : 0f);
        moveInput.Normalize();

        if (moveInput != Vector2.zero)
            lastDirection = moveInput;

        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;

        if (Input.GetKeyDown(SettingsData.Dash) && !isDashing && dashCooldownTimer <= 0f)
            StartCoroutine(DashRoutine());
    }

    private void FixedUpdate()
    {
        if (!isDashing)
            rb.MovePosition(rb.position + moveInput * moveSpeed * speedMultiplier * Time.fixedDeltaTime);
    }

    private IEnumerator DashRoutine()
    {
        isDashing = true;
        isIFrameActive = true;
        dashCooldownTimer = dashCooldown;

        if (dashClip != null)
            AudioSource.PlayClipAtPoint(dashClip, transform.position, dashAudioVolume);

        // Use current move direction, or last facing if standing still
        Vector2 dashDir = moveInput != Vector2.zero ? moveInput : lastDirection;
        float dashSpeed = dashDistance / dashDuration;

        // Enable i-frames
        if (playerHealth != null)
            playerHealth.IsInvulnerable = true;

        // Start speed boost
        if (boostCoroutine != null)
            StopCoroutine(boostCoroutine);
        boostCoroutine = StartCoroutine(SpeedBoostRoutine());

        float elapsed = 0f;
        while (elapsed < dashDuration)
        {
            float dt = Time.fixedDeltaTime;
            rb.MovePosition(rb.position + dashDir * dashSpeed * dt);
            elapsed += dt;
            yield return new WaitForFixedUpdate();
        }

        isDashing = false;

        // Keep i-frames for the full duration
        float remaining = iFrameDuration - dashDuration;
        if (remaining > 0f)
            yield return new WaitForSeconds(remaining);

        if (playerHealth != null)
            playerHealth.IsInvulnerable = false;
        isIFrameActive = false;
    }

    private IEnumerator SpeedBoostRoutine()
    {
        speedMultiplier *= dashBoostMultiplier;
        yield return new WaitForSeconds(dashBoostDuration);
        speedMultiplier /= dashBoostMultiplier;
        boostCoroutine = null;
    }

    // Called by PlayerAnimator once animations are set up
    public Vector2 MoveInput => moveInput;
    public Vector2 LastDirection => lastDirection;
    public bool IsMoving => moveInput != Vector2.zero;
}
