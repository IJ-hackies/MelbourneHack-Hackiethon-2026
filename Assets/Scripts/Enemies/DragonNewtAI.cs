using System.Collections;
using UnityEngine;

// DragonNewt AI — extends MeleeChaseAI.
// Chases the player normally. When the attack triggers the dragon leaps (plays the attack clip),
// scaling up at the apex to sell the jump, then landing at frame 5/7 where it stamps a ground
// crack and fires a ring of shockwave projectiles.
//
// Jump scale is driven by an AnimationCurve mapped to the clip's normalizedTime so it stays
// in sync even if jumpAnimSpeed is adjusted. DamageHitFrame and AttackAnimDuration are
// recalculated in Start() to match the slowed playback speed.
public class DragonNewtAI : MeleeChaseAI
{
    [Header("Jump Animation")]
    // Playback speed of the attack clip (< 1 = slower). Walk clips always play at 1x.
    [SerializeField] private float jumpAnimSpeed = 0.65f;
    // Scale arc over the clip's normalizedTime (0→1).
    // Default: lifts off, peaks at ~1.45x at mid-air, returns to 1 just before landing (t≈0.71).
    [SerializeField] private AnimationCurve jumpScaleCurve = new AnimationCurve(
        new Keyframe(0.00f, 1.00f, 0f,  3f),
        new Keyframe(0.43f, 1.20f, 0f,  0f),
        new Keyframe(0.71f, 1.00f, -3f, 0f),
        new Keyframe(1.00f, 1.00f, 0f,  0f));

    [Header("Shockwave Ring")]
    [SerializeField] private int   shockwaveCount    = 8;
    [SerializeField] private float shockwaveSpeed    = 5f;
    [SerializeField] private float shockwaveLifetime = 1.2f;
    [SerializeField] private Color shockwaveColorA   = new Color(0.90f, 0.55f, 0.10f, 1f);
    [SerializeField] private Color shockwaveColorB   = new Color(0.45f, 0.20f, 0.05f, 1f);

    [Header("Attack SFX")]
    [SerializeField] private AudioClip attackSfxClip;
    [SerializeField, Range(0f, 1f)] private float attackSfxVolume = 1f;

    [Header("Ground Crack")]
    [SerializeField] private float crackDuration = 0.9f;
    [SerializeField] private Color crackColorA   = new Color(0.18f, 0.12f, 0.06f, 1f);
    [SerializeField] private Color crackColorB   = new Color(0.30f, 0.20f, 0.10f, 1f);

    public int   ShockwaveCount    { get => shockwaveCount;    set => shockwaveCount    = Mathf.Max(1, value); }
    public float ShockwaveSpeed    { get => shockwaveSpeed;    set => shockwaveSpeed    = Mathf.Max(0.1f, value); }
    public float ShockwaveLifetime { get => shockwaveLifetime; set => shockwaveLifetime = Mathf.Max(0.1f, value); }
    public Color ShockwaveColorA   { get => shockwaveColorA;   set => shockwaveColorA   = value; }
    public Color ShockwaveColorB   { get => shockwaveColorB;   set => shockwaveColorB   = value; }

    // Attack clip: 7 frames @ 10 fps. Landing is at frame 5 (normalizedTime = 5/7).
    private const float ClipFrames   = 7f;
    private const float ClipFps      = 10f;
    private const float LandingFrame = 5f;
    private const float BaseScale    = 5f;

    // Pre-hash all 8 directional attack state names for O(1) lookup each frame.
    private static readonly int[] AttackHashes;
    static DragonNewtAI()
    {
        string[] dirs = { "north", "north_east", "east", "south_east", "south", "south_west", "west", "north_west" };
        AttackHashes = System.Array.ConvertAll(dirs, d => Animator.StringToHash($"dn_attack_{d}"));
    }

    protected override void Start()
    {
        base.Start();
        WalkPrefix   = "dn_walk";
        AttackPrefix = "dn_attack";

        // Recalculate timing so MeleeChaseAI's wall-clock checks stay in sync with slowed playback.
        // The slowed clip lasts (ClipFrames/ClipFps) / jumpAnimSpeed seconds.
        // Landing at normalizedTime = LandingFrame/ClipFrames → wall-clock = that fraction × slowed duration.
        float slowedDuration = (ClipFrames / ClipFps) / jumpAnimSpeed;
        AttackAnimDuration = slowedDuration + 0.05f;                       // small buffer after last frame
        DamageHitFrame     = (LandingFrame / ClipFrames) * slowedDuration; // fires shockwave at landing

        StartCoroutine(JumpScaleLoop());
    }

    // Called at DamageHitFrame — the landing moment of the jump.
    protected override void Attack()
    {
        SFXManager.Instance?.PlayAtPosition(attackSfxClip, attackSfxVolume, transform.position);
        SpawnGroundCrack();
        ShockwaveProjectile.SpawnRing(
            transform.position,
            shockwaveCount, shockwaveSpeed, shockwaveLifetime,
            AttackDamage, shockwaveColorA, shockwaveColorB,
            playerHealth, playerHitEffect);
    }

    // Each frame: if the attack clip is playing, slow it down and drive scale from the curve.
    // Otherwise restore speed and scale. Exits cleanly on death.
    private IEnumerator JumpScaleLoop()
    {
        while (!health.IsDead)
        {
            if (animator.enabled && IsPlayingAttackClip())
            {
                animator.speed = jumpAnimSpeed;
                float t = Mathf.Clamp01(animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
                float s = jumpScaleCurve.Evaluate(t) * BaseScale;
                transform.localScale = new Vector3(s, s, 1f);
            }
            else
            {
                animator.speed       = 1f;
                transform.localScale = new Vector3(BaseScale, BaseScale, 1f);
            }
            yield return null;
        }
        transform.localScale = new Vector3(BaseScale, BaseScale, 1f);
    }

    private bool IsPlayingAttackClip()
    {
        int hash = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        foreach (int h in AttackHashes)
            if (hash == h) return true;
        return false;
    }

    // Brief burst of dark particles radiating outward — subtle ground crack impression.
    private void SpawnGroundCrack()
    {
        var go = new GameObject("FX_GroundCrack");
        go.transform.position = transform.position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Shader urp      = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallback = Shader.Find("Sprites/Default");
        go.GetComponent<ParticleSystemRenderer>().material = new Material(urp != null ? urp : fallback);

        var main = ps.main;
        main.loop            = false;
        main.startLifetime   = crackDuration;
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
        main.startColor      = new ParticleSystem.MinMaxGradient(crackColorA, crackColorB);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.maxParticles    = 60;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 50) });

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.1f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(crackColorA, 0f), new GradientColorKey(crackColorB, 1f) },
            new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
        Destroy(go, crackDuration + 0.1f);
    }
}
