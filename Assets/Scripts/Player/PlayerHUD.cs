using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Screen-space HUD: wooden board (top-left), heart with health, status effect queue.
//
// Setup on the Player prefab:
//   1. Add this component.
//   2. Assign Board, Heart, Burn, Poison, Bleed, Slow prefabs from Prefabs/Icons/ & Prefabs/UI/.
//   3. Create a TMP font asset from Assets/Fonts/alagard.ttf via
//      Window > TextMeshPro > Font Asset Creator, then assign it here.
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(PlayerStatusEffects))]
public class PlayerHUD : MonoBehaviour
{
    [Header("Prefabs (sprites are extracted at runtime)")]
    [SerializeField] private GameObject boardPrefab;
    [SerializeField] private GameObject heartIconPrefab;
    [SerializeField] private GameObject burnIconPrefab;
    [SerializeField] private GameObject poisonIconPrefab;
    [SerializeField] private GameObject bleedIconPrefab;
    [SerializeField] private GameObject slowIconPrefab;

    [Header("Font — assign TMP font asset generated from Assets/Fonts/alagard.ttf")]
    [SerializeField] private TMP_FontAsset alagardFont;

    // ── Layout (at 1920×1080 reference resolution) ────────────────────────────
    private const float BoardWidth  = 300f;
    private const float BoardHeight = 218f;
    private const float FrameInset  = 18f;
    private const float HeartSize   = 110f;
    private const float IconSize    = 58f;
    private const float IconSpacing = 61f; // icon width + gap

    // ── Runtime references ────────────────────────────────────────────────────
    private Health              health;
    private PlayerStatusEffects status;
    private GameObject          canvasGO;

    // ── UI nodes ──────────────────────────────────────────────────────────────
    private RectTransform heartRT;
    private TMP_Text      healthText;
    private RectTransform statusContainer;

    // ── Heartbeat ─────────────────────────────────────────────────────────────
    private float beatPhase;
    private float fastBeatTimer;
    private float healthFlashTimer;

    // ── Status slots ──────────────────────────────────────────────────────────
    private enum EffectType { Burn, Poison, Bleed, Slow }

    private class StatusSlot
    {
        public EffectType    type;
        public RectTransform rt;
        public TMP_Text      timerText;
        public float         targetX;
        public float         currentX;
        public float         lastRemaining;
        public float         flashTimer;
    }

    private readonly List<StatusSlot> slots = new List<StatusSlot>();

    private Sprite spriteBurn, spritePoison, spriteBleed, spriteSlow;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        health = GetComponent<Health>();
        status = GetComponent<PlayerStatusEffects>();
    }

    private void Start()
    {
        BuildHUD();
        health.OnDamaged.AddListener(_ => TriggerDamageFlash());
    }

    private void OnDestroy()
    {
        if (canvasGO != null) Destroy(canvasGO);
    }

    private void TriggerDamageFlash()
    {
        fastBeatTimer    = 2f;
        healthFlashTimer = 0.45f;
    }

    // ── Canvas construction ───────────────────────────────────────────────────

    private void BuildHUD()
    {
        canvasGO = new GameObject("PlayerHUD_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Board background
        var boardRT = MakeRT("Board", canvasGO.transform,
            anchorMin: Vector2.up, anchorMax: Vector2.up, pivot: Vector2.up,
            pos: new Vector2(10f, -10f), size: new Vector2(BoardWidth, BoardHeight));

        var boardImg    = boardRT.gameObject.AddComponent<Image>();
        boardImg.sprite = ExtractSprite(boardPrefab);
        boardImg.type   = Image.Type.Simple;

        // Heart icon
        heartRT = MakeRT("Heart", boardRT,
            anchorMin: new Vector2(0.5f, 1f), anchorMax: new Vector2(0.5f, 1f),
            pivot: new Vector2(0.5f, 1f),
            pos: new Vector2(0f, -(FrameInset + 5f)),
            size: new Vector2(HeartSize, HeartSize));

        var heartImg    = heartRT.gameObject.AddComponent<Image>();
        heartImg.sprite = ExtractSprite(heartIconPrefab);
        heartImg.preserveAspect = true;

        // Health number
        var healthTextRT = MakeRT("HealthText", heartRT,
            anchorMin: Vector2.zero, anchorMax: Vector2.one,
            pivot: new Vector2(0.5f, 0.5f),
            pos: Vector2.zero, size: Vector2.zero);
        healthTextRT.offsetMin = healthTextRT.offsetMax = Vector2.zero;

        healthText              = healthTextRT.gameObject.AddComponent<TextMeshProUGUI>();
        healthText.font         = alagardFont;
        healthText.fontSize     = 28f;
        healthText.fontStyle    = FontStyles.Bold;
        healthText.alignment    = TextAlignmentOptions.Center;
        healthText.enableWordWrapping = false;
        healthText.overflowMode = TextOverflowModes.Overflow;
        healthText.color        = Color.white;
        healthText.outlineWidth = 0.3f;
        healthText.outlineColor = new Color32(0, 0, 0, 210);

        // Status icon row — bottom strip inside frame
        statusContainer = MakeRT("StatusRow", boardRT,
            anchorMin: Vector2.zero, anchorMax: Vector2.zero,
            pivot: Vector2.zero,
            pos: new Vector2(FrameInset, FrameInset + 4f),
            size: new Vector2(BoardWidth - FrameInset * 2f, IconSize));

        // Cache effect sprites
        spriteBurn   = ExtractSprite(burnIconPrefab);
        spritePoison = ExtractSprite(poisonIconPrefab);
        spriteBleed  = ExtractSprite(bleedIconPrefab);
        spriteSlow   = ExtractSprite(slowIconPrefab);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (health == null || status == null) return;
        UpdateHeartbeat();
        UpdateHealthText();
        SyncStatusSlots();
        AnimateSlots();
    }

    // ── Heartbeat animation ───────────────────────────────────────────────────

    private void UpdateHeartbeat()
    {
        if (fastBeatTimer > 0f) fastBeatTimer -= Time.deltaTime;

        float freq      = fastBeatTimer > 0f ? 2.6f : 1.1f;
        float amplitude = fastBeatTimer > 0f ? 0.09f : 0.05f;

        beatPhase += freq * Mathf.PI * 2f * Time.deltaTime;
        float pulse = 1f + amplitude * Mathf.Max(0f, Mathf.Sin(beatPhase));
        heartRT.localScale = new Vector3(pulse, pulse, 1f);
    }

    // ── Health text ───────────────────────────────────────────────────────────

    private void UpdateHealthText()
    {
        healthText.text = Mathf.CeilToInt(health.Current).ToString();

        if (healthFlashTimer > 0f)
        {
            healthFlashTimer -= Time.deltaTime;
            float t = healthFlashTimer / 0.45f;
            healthText.color = Color.Lerp(Color.white, new Color(1f, 0.12f, 0.12f), t);
        }
        else
        {
            healthText.color = Color.white;
        }
    }

    // ── Status slot sync ──────────────────────────────────────────────────────

    private void SyncStatusSlots()
    {
        SyncSlot(EffectType.Burn,   status.IsBurning,  status.BurnRemaining);
        SyncSlot(EffectType.Poison, status.IsPoisoned, status.PoisonRemaining);
        SyncSlot(EffectType.Bleed,  status.IsBleeding, status.BleedRemaining);

        // Slow: timed gives countdown, zone gives no countdown (pass -1)
        bool  showSlow      = status.IsZoneSlowed || status.IsTimedSlowed;
        float slowRemaining = status.IsTimedSlowed ? status.TimedSlowRemaining : -1f;
        SyncSlot(EffectType.Slow, showSlow, slowRemaining);
    }

    private void SyncSlot(EffectType type, bool isActive, float remaining)
    {
        StatusSlot slot = FindSlot(type);

        if (!isActive)
        {
            if (slot != null) RemoveSlot(slot);
            return;
        }

        if (slot == null)
            slot = AddSlot(type);

        // Detect timer refresh (remaining jumped up = reapplied)
        if (remaining > 0f && slot.lastRemaining > 0f && remaining > slot.lastRemaining + 0.5f)
            slot.flashTimer = 0.4f;

        slot.lastRemaining = remaining;

        // Timer text
        if (remaining < 0f)
        {
            slot.timerText.text  = "";
        }
        else
        {
            int secs = Mathf.CeilToInt(Mathf.Max(0f, remaining));
            slot.timerText.text = secs > 0 ? secs.ToString() : "";
        }

        // Flash colour
        if (slot.flashTimer > 0f)
        {
            slot.flashTimer -= Time.deltaTime;
            float t = slot.flashTimer / 0.4f;
            slot.timerText.color = Color.Lerp(Color.white, new Color(1f, 0.12f, 0.12f), t);
        }
        else
        {
            slot.timerText.color = Color.white;
        }
    }

    // ── Slot lifecycle ────────────────────────────────────────────────────────

    private StatusSlot FindSlot(EffectType type)
    {
        foreach (var s in slots) if (s.type == type) return s;
        return null;
    }

    private StatusSlot AddSlot(EffectType type)
    {
        float startX = slots.Count * IconSpacing;

        var rt = MakeRT($"Slot_{type}", statusContainer,
            anchorMin: Vector2.zero, anchorMax: Vector2.zero,
            pivot: Vector2.zero,
            pos: new Vector2(startX, 0f),
            size: new Vector2(IconSize, IconSize));

        var img    = rt.gameObject.AddComponent<Image>();
        img.sprite = SpriteFor(type);
        img.preserveAspect = true;

        var timerRT = MakeRT("Timer", rt,
            anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0.5f, 0.5f),
            pos: new Vector2(TextOffsetXFor(type), 0f), size: new Vector2(IconSize, IconSize));

        var tmp          = timerRT.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font         = alagardFont;
        tmp.fontSize     = 26f;
        tmp.fontStyle    = FontStyles.Bold;
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color        = Color.white;
        tmp.outlineWidth = 0.35f;
        tmp.outlineColor = new Color32(0, 0, 0, 220);

        var slot = new StatusSlot
        {
            type      = type,
            rt        = rt,
            timerText = tmp,
            targetX   = startX,
            currentX  = startX,
        };
        slots.Add(slot);
        RecalcTargets();
        return slot;
    }

    private void RemoveSlot(StatusSlot slot)
    {
        if (slot.rt != null) Destroy(slot.rt.gameObject);
        slots.Remove(slot);
        RecalcTargets();
    }

    private void RecalcTargets()
    {
        for (int i = 0; i < slots.Count; i++)
            slots[i].targetX = i * IconSpacing;
    }

    private void AnimateSlots()
    {
        foreach (var slot in slots)
        {
            slot.currentX = slot.targetX;
            slot.rt.anchoredPosition = new Vector2(slot.currentX, 0f);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Horizontal nudge (negative = left) to visually centre the number on each sprite.
    private static float TextOffsetXFor(EffectType type) => type switch
    {
        EffectType.Bleed  => -9f,
        EffectType.Poison => -9f,
        EffectType.Burn   => -5f,
        _                 => 0f,
    };

    private Sprite SpriteFor(EffectType type) => type switch
    {
        EffectType.Burn   => spriteBurn,
        EffectType.Poison => spritePoison,
        EffectType.Bleed  => spriteBleed,
        EffectType.Slow   => spriteSlow,
        _                 => null,
    };

    private static Sprite ExtractSprite(GameObject prefab) =>
        prefab != null ? prefab.GetComponent<SpriteRenderer>()?.sprite : null;

    private static RectTransform MakeRT(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt        = go.AddComponent<RectTransform>();
        rt.anchorMin  = anchorMin;
        rt.anchorMax  = anchorMax;
        rt.pivot      = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta  = size;
        return rt;
    }
}
