using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to the same child GameObject as PlayerAnimator (the sprite child).
/// Spawns ghost after-images and wind streaks whenever the player dashes.
/// </summary>
public class DashEffect : MonoBehaviour
{
    // After-image
    private const float AfterImageInterval = 0.03f;
    private const float AfterImageLifetime = 0.2f;
    private static readonly Color AfterImageTint = new Color(0.55f, 0.75f, 1f, 0.55f);

    // Wind lines
    private const int   WindLineCount    = 5;
    private const float WindLineLifetime = 0.15f;
    private const float WindLineSpeed    = 7f;
    private const float WindLineLength   = 0.55f;
    private const float WindLineWidth    = 0.055f;
    private static readonly Color WindLineTint = new Color(0.8f, 0.95f, 1f, 0.75f);

    // I-frame white flash
    private const float FlashInterval = 0.08f;

    // Sparks
    private const int   SparkCount       = 10;
    private const float SparkLifetime    = 0.18f;
    private const float SparkSpeedMin    = 2.5f;
    private const float SparkSpeedMax    = 6f;
    private const float SparkSize        = 0.07f;
    // Sparks fan out behind the player — bias is opposite to dash direction
    private const float SparkBackBias    = 0.6f; // 0 = full circle, 1 = strict cone behind
    private static readonly Color SparkColor = new Color(1f, 0.92f, 0.5f, 1f);

    private SpriteRenderer sr;
    private PlayerMovement movement;
    private bool  wasDashing;
    private bool  wasIFrame;
    private float afterImageTimer;
    private float flashTimer;
    private bool  flashWhite;
    private Color originalColor;

    // Shared 1×1 white sprite used for all wind lines
    private static Sprite pixelSprite;

    private void Awake()
    {
        sr       = GetComponent<SpriteRenderer>();
        movement = GetComponentInParent<PlayerMovement>();
        originalColor = sr.color;
    }

    private void Update()
    {
        bool dashing = movement.IsDashing;

        if (dashing && !wasDashing)
            OnDashStart();

        if (dashing)
        {
            afterImageTimer -= Time.deltaTime;
            if (afterImageTimer <= 0f)
            {
                SpawnAfterImage();
                afterImageTimer = AfterImageInterval;
            }
        }

        // White flash during i-frames
        bool iFrame = movement.IsIFrameActive;
        if (iFrame)
        {
            flashTimer -= Time.deltaTime;
            if (flashTimer <= 0f)
            {
                flashWhite = !flashWhite;
                sr.color = flashWhite ? Color.white : originalColor;
                flashTimer = FlashInterval;
            }
        }
        else if (wasIFrame)
        {
            sr.color = originalColor;
            flashWhite = false;
        }

        wasDashing = dashing;
        wasIFrame = iFrame;
    }

    private void OnDashStart()
    {
        afterImageTimer = 0f; // spawn one immediately
        flashTimer = 0f;
        flashWhite = false;
        SpawnWindLines();
        SpawnSparks();
    }

    // ── After-image ────────────────────────────────────────────────────────

    private void SpawnAfterImage()
    {
        if (sr.sprite == null) return;

        GameObject ghost = new GameObject("DashGhost");
        ghost.transform.SetPositionAndRotation(transform.position, transform.rotation);
        ghost.transform.localScale = transform.lossyScale;

        SpriteRenderer ghostSR  = ghost.AddComponent<SpriteRenderer>();
        ghostSR.sprite          = sr.sprite;
        ghostSR.sortingLayerID  = sr.sortingLayerID;
        ghostSR.sortingOrder    = sr.sortingOrder - 1;
        ghostSR.flipX           = sr.flipX;
        ghostSR.flipY           = sr.flipY;
        ghostSR.color           = AfterImageTint;

        StartCoroutine(FadeOut(ghostSR, ghost, AfterImageLifetime));
    }

    // ── Wind lines ─────────────────────────────────────────────────────────

    private void SpawnWindLines()
    {
        Vector2 dashDir = movement.MoveInput != Vector2.zero ? movement.MoveInput : movement.LastDirection;
        dashDir.Normalize();

        Vector2 perp = new Vector2(-dashDir.y, dashDir.x);
        float angle  = Mathf.Atan2(dashDir.y, dashDir.x) * Mathf.Rad2Deg;

        if (pixelSprite == null)
            pixelSprite = MakePixelSprite();

        for (int i = 0; i < WindLineCount; i++)
        {
            // Spread lines evenly across a 0.8-unit perpendicular band
            float t           = WindLineCount > 1 ? (i / (float)(WindLineCount - 1)) - 0.5f : 0f;
            Vector3 spawnPos  = transform.position + (Vector3)(perp * t * 0.8f);

            GameObject line       = new GameObject("DashWindLine");
            line.transform.position = spawnPos;
            line.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            line.transform.localScale = new Vector3(WindLineLength, WindLineWidth, 1f);

            SpriteRenderer lineSR  = line.AddComponent<SpriteRenderer>();
            lineSR.sprite          = pixelSprite;
            lineSR.sortingLayerID  = sr.sortingLayerID;
            lineSR.sortingOrder    = sr.sortingOrder - 2;
            lineSR.color           = WindLineTint;

            StartCoroutine(MoveFadeOut(lineSR, line, dashDir, WindLineLifetime));
        }
    }

    // ── Sparks ─────────────────────────────────────────────────────────────

    private void SpawnSparks()
    {
        Vector2 dashDir = movement.MoveInput != Vector2.zero ? movement.MoveInput : movement.LastDirection;
        dashDir.Normalize();
        Vector2 behind = -dashDir;

        if (pixelSprite == null)
            pixelSprite = MakePixelSprite();

        for (int i = 0; i < SparkCount; i++)
        {
            // Random direction biased toward behind the player
            float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector2 randomDir = new Vector2(Mathf.Cos(randomAngle), Mathf.Sin(randomAngle));
            Vector2 sparkDir  = Vector2.Lerp(randomDir, behind, SparkBackBias).normalized;
            float   speed     = Random.Range(SparkSpeedMin, SparkSpeedMax);

            GameObject spark      = new GameObject("DashSpark");
            spark.transform.position   = transform.position;
            spark.transform.localScale = Vector3.one * SparkSize;

            SpriteRenderer sparkSR  = spark.AddComponent<SpriteRenderer>();
            sparkSR.sprite          = pixelSprite;
            sparkSR.sortingLayerID  = sr.sortingLayerID;
            sparkSR.sortingOrder    = sr.sortingOrder + 1;
            sparkSR.color           = SparkColor;

            StartCoroutine(SparkFly(sparkSR, spark, sparkDir, speed, SparkLifetime));
        }
    }

    private static IEnumerator SparkFly(SpriteRenderer target, GameObject obj, Vector2 dir, float speed, float lifetime)
    {
        float elapsed = 0f;
        Color c       = target.color;
        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / lifetime;
            // Decelerate over lifetime
            obj.transform.position += (Vector3)(dir * speed * (1f - t) * Time.deltaTime);
            c.a = Mathf.Lerp(1f, 0f, t * t);
            target.color = c;
            yield return null;
        }
        Destroy(obj);
    }

    // ── Coroutines ─────────────────────────────────────────────────────────

    private static IEnumerator FadeOut(SpriteRenderer target, GameObject obj, float lifetime)
    {
        float elapsed = 0f;
        Color c       = target.color;
        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(AfterImageTint.a, 0f, elapsed / lifetime);
            target.color = c;
            yield return null;
        }
        Destroy(obj);
    }

    private static IEnumerator MoveFadeOut(SpriteRenderer target, GameObject obj, Vector2 dir, float lifetime)
    {
        float elapsed = 0f;
        Color c       = target.color;
        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / lifetime;
            obj.transform.position += (Vector3)(dir * WindLineSpeed * Time.deltaTime);
            c.a = Mathf.Lerp(WindLineTint.a, 0f, t);
            target.color = c;
            yield return null;
        }
        Destroy(obj);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Sprite MakePixelSprite()
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }
}
