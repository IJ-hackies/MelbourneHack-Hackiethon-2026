using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// In-game spell hotbar — 3 slots in a quarter-circle arc from the bottom-left corner.
/// Slot 0 = Basic (LMB), Slot 1 = Skill 1 (E), Slot 2 = Skill 2 (Q).
/// A separate, larger Ultimate box sits tucked into the very bottom-left corner,
/// showing a fill gauge (water rising) that charges on enemy hits, not a cooldown.
/// When the gauge is full the box glows and pulses to signal readiness.
/// </summary>
public class SpellHotbar : MonoBehaviour
{
    public static SpellHotbar Instance { get; private set; }

    /// <summary>Get the RectTransform for a hotbar slot (0=Basic, 1=Skill1, 2=Skill2).</summary>
    public RectTransform GetSlotRT(int slot) => slots != null && slot >= 0 && slot < slots.Length ? slots[slot].root : null;
    /// <summary>The canvas GO for sorting order manipulation.</summary>
    public GameObject CanvasGO => canvasGO;

    [Header("Sprites")]
    [SerializeField] private Sprite slotSprite;

    [Header("Font")]
    [SerializeField] private TMP_FontAsset font;

    // Arc layout — consts so Inspector can never override
    private const float slotSize      = 96f;
    private const float arcRadius     = 220f;
    private const float arcStartAngle = 10f;
    private const float arcEndAngle   = 80f;
    private const float cornerPadX    = 22f;
    private const float cornerPadY    = 22f;
    private const float nameFontSize  = 10f;
    private const float keyFontSize   = 14f;

    // Ultimate box
    private const float ultSize    = 108f;
    private const float ultPosX    = cornerPadX + ultSize * 0.5f;
    private const float ultPosY    = cornerPadY + ultSize * 0.5f;

    private GameObject canvasGO;
    private SlotUI[] slots; // index = slot (0=Basic, 1=Skill1, 2=Skill2)

    // Dash cooldown indicator
    private RectTransform  _dashRT;
    private Image          dashCooldownOverlay;
    private TMP_Text       dashKeyText;
    private TMP_Text       dashCenterLabel;
    private PlayerMovement cachedPlayer;

    /// <summary>RectTransform of the dash indicator slot.</summary>
    public RectTransform DashRT => _dashRT;

    // Ultimate box
    private RectTransform    ultRoot;
    private WaterGaugeGraphic ultGauge;     // animated water-fill gauge
    private Image             ultGlowImg;   // pulsing glow border when ready
    private Image             ultIconImg;   // spell icon
    private TMP_Text      ultKeyText;
    private TMP_Text      ultNameText;
    private TMP_Text      ultCountText;

    // Placement mode
    private bool              _inPlacementMode;
    private System.Action<int> _placementCallback;
    private readonly System.Collections.Generic.List<GameObject> _placementGlowGOs = new();
    private readonly System.Collections.Generic.List<UnityEngine.EventSystems.EventTrigger> _placementTriggers = new();

    // Both skill slots are blue; basic is green
    private static readonly Color[] TierColors = {
        new Color(0.25f, 0.75f, 0.35f, 1f), // Slot 0 Basic:  green
        new Color(0.35f, 0.55f, 1.00f, 1f), // Slot 1 Skill1: blue
        new Color(0.35f, 0.55f, 1.00f, 1f), // Slot 2 Skill2: blue
    };
    private static readonly string[] TierNames = { "BASIC", "SKILL", "SKILL" };

    private class SlotUI
    {
        public RectTransform root;
        public Image         bg;
        public Image         icon;
        public Image         cooldownOverlay;
        public TMP_Text      cooldownText;
        public TMP_Text      nameText;
        public TMP_Text      keyText;
        public TMP_Text      tierLabel;
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        BuildHotbar();
        if (Grimoire.Instance != null)
            Grimoire.Instance.OnLoadoutChanged += Refresh;
        SettingsData.OnBindingsChanged += RefreshKeyLabels;
        if (UltimateAbility.Instance != null)
        {
            UltimateAbility.Instance.OnGaugeChanged += RefreshUltimateUI;
            UltimateAbility.Instance.OnBecameReady  += OnUltimateBecameReady;
        }
        Refresh();
        RefreshUltimateUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Grimoire.Instance != null)
            Grimoire.Instance.OnLoadoutChanged -= Refresh;
        SettingsData.OnBindingsChanged -= RefreshKeyLabels;
        if (UltimateAbility.Instance != null)
        {
            UltimateAbility.Instance.OnGaugeChanged -= RefreshUltimateUI;
            UltimateAbility.Instance.OnBecameReady  -= OnUltimateBecameReady;
        }
        if (canvasGO != null) Destroy(canvasGO);
    }

    private void Update()
    {
        if (_inPlacementMode)
        {
            UpdatePlacementGlows();
            return;
        }
        if (PauseManager.IsPaused) return;
        UpdateCooldowns();
        UpdateUltimateAnimation();
    }

    // ── Build ────────────────────────────────────────────────────────────────

    private void BuildHotbar()
    {
        canvasGO = new GameObject("SpellHotbar_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        slots = new SlotUI[Grimoire.LoadoutSize];
        var arcOrigin = new Vector2(cornerPadX, cornerPadY);

        for (int i = 0; i < Grimoire.LoadoutSize; i++)
        {
            float t        = Grimoire.LoadoutSize > 1 ? (float)i / (Grimoire.LoadoutSize - 1) : 0f;
            float angleDeg = Mathf.Lerp(arcStartAngle, arcEndAngle, t);
            float angleRad = angleDeg * Mathf.Deg2Rad;
            var   outDir   = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
            var   slotPos  = arcOrigin + outDir * arcRadius;

            var slot = new SlotUI();

            slot.root = MakeRT($"Slot_{i}", canvasGO.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                slotPos, new Vector2(slotSize, slotSize));

            slot.bg = slot.root.gameObject.AddComponent<Image>();
            slot.bg.sprite         = slotSprite;
            slot.bg.type           = Image.Type.Simple;
            slot.bg.preserveAspect = true;
            slot.bg.color          = new Color(TierColors[i].r, TierColors[i].g, TierColors[i].b, 0.65f);

            float iconDim = slotSize * 0.50f;
            var iconRT = MakeRT($"Icon_{i}", slot.root,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -4f), new Vector2(iconDim, iconDim));
            slot.icon = iconRT.gameObject.AddComponent<Image>();
            slot.icon.preserveAspect = true;
            slot.icon.raycastTarget  = false;
            slot.icon.enabled        = false;

            var coolRT = MakeRT("Cooldown", slot.root,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            coolRT.offsetMin = Vector2.zero;
            coolRT.offsetMax = Vector2.zero;
            slot.cooldownOverlay = coolRT.gameObject.AddComponent<Image>();
            slot.cooldownOverlay.color         = new Color(0f, 0f, 0f, 0.55f);
            slot.cooldownOverlay.raycastTarget  = false;
            slot.cooldownOverlay.type           = Image.Type.Filled;
            slot.cooldownOverlay.fillMethod     = Image.FillMethod.Radial360;
            slot.cooldownOverlay.fillOrigin     = 2;
            slot.cooldownOverlay.fillClockwise  = true;
            slot.cooldownOverlay.fillAmount     = 0f;

            slot.cooldownText = MakeText($"CooldownText_{i}", slot.root,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(slotSize, slotSize),
                "", nameFontSize + 3f, Color.white, TextAlignmentOptions.Center);
            slot.cooldownText.gameObject.SetActive(false);

            slot.keyText = MakeText($"Key_{i}", slot.root,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(4f, 0f), new Vector2(44f, 20f),
                SettingsData.KeyLabel(GetSlotKey(i)), keyFontSize, Color.white, TextAlignmentOptions.Left);

            slot.tierLabel = MakeText($"Tier_{i}", slot.root,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 3f), new Vector2(slotSize, 15f),
                TierNames[i], keyFontSize - 3f,
                new Color(TierColors[i].r, TierColors[i].g, TierColors[i].b, 0.9f),
                TextAlignmentOptions.Center);

            slot.nameText = MakeText($"Name_{i}", slot.root,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -2f), new Vector2(slotSize - 4f, 15f),
                "", nameFontSize, Color.white, TextAlignmentOptions.Center);

            slots[i] = slot;
        }

        float dashSize = slotSize * 0.65f;
        BuildDashIndicator(canvasGO.transform, dashSize);
        BuildUltimateBox(canvasGO.transform);
    }

    private void BuildDashIndicator(Transform parent, float dashSize)
    {
        const float BarHalfW   = 230f;
        const float BarCenterY = 44f;
        const float Gap        = 14f;
        float posX = BarHalfW + Gap + dashSize * 0.5f;

        _dashRT = MakeRT("DashSlot", parent,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
            new Vector2(posX, BarCenterY), new Vector2(dashSize, dashSize));

        var bg = _dashRT.gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.45f);
        if (slotSprite != null) { bg.sprite = slotSprite; bg.preserveAspect = true; }

        var coolRT = MakeRT("DashCooldown", _dashRT,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        coolRT.offsetMin = Vector2.zero;
        coolRT.offsetMax = Vector2.zero;
        dashCooldownOverlay              = coolRT.gameObject.AddComponent<Image>();
        dashCooldownOverlay.color        = new Color(0f, 0f, 0f, 0.55f);
        dashCooldownOverlay.raycastTarget = false;
        dashCooldownOverlay.type         = Image.Type.Filled;
        dashCooldownOverlay.fillMethod   = Image.FillMethod.Radial360;
        dashCooldownOverlay.fillOrigin   = 2;
        dashCooldownOverlay.fillClockwise = true;
        dashCooldownOverlay.fillAmount   = 0f;

        dashCenterLabel = MakeText("DashLabel", _dashRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(dashSize, dashSize * 0.8f),
            "DASH", nameFontSize, Color.white, TextAlignmentOptions.Center);

        dashKeyText = MakeText("DashKey", _dashRT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(dashSize * 0.5f + 8f, 0f), new Vector2(60f, 20f),
            SettingsData.KeyLabel(SettingsData.Dash), keyFontSize - 1f, Color.white, TextAlignmentOptions.Left);
    }

    private void BuildUltimateBox(Transform parent)
    {
        // Dark gold/purple background
        var bg2Color = new Color(0.22f, 0.08f, 0.32f, 0.85f);

        ultRoot = MakeRT("UltimateSlot", parent,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
            new Vector2(ultPosX, ultPosY), new Vector2(ultSize, ultSize));

        var bg = ultRoot.gameObject.AddComponent<Image>();
        bg.color = bg2Color;
        if (slotSprite != null) { bg.sprite = slotSprite; bg.type = Image.Type.Simple; bg.preserveAspect = true; }

        // Water gauge fill — animated wave surface, bottom-to-top
        var gaugeRT = MakeRT("GaugeFill", ultRoot,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        gaugeRT.offsetMin = new Vector2(4f, 4f);
        gaugeRT.offsetMax = new Vector2(-4f, -4f);
        ultGauge            = gaugeRT.gameObject.AddComponent<WaterGaugeGraphic>();
        ultGauge.color      = new Color(0.32f, 0.04f, 0.52f, 0.88f); // dark purple
        ultGauge.fillAmount = 0f;

        // Spell icon — centered, above the count text
        float ultIconDim = ultSize * 0.45f;
        var ultIconRT = MakeRT("UltIcon", ultRoot,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 4f), new Vector2(ultIconDim, ultIconDim));
        ultIconImg = ultIconRT.gameObject.AddComponent<Image>();
        ultIconImg.preserveAspect = true;
        ultIconImg.raycastTarget  = false;
        ultIconImg.enabled        = false;

        // Glow border — hidden until ready
        var glowRT = MakeRT("GlowBorder", ultRoot,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        glowRT.offsetMin = new Vector2(-6f, -6f);
        glowRT.offsetMax = new Vector2(6f, 6f);
        ultGlowImg = glowRT.gameObject.AddComponent<Image>();
        ultGlowImg.color         = new Color(1f, 0.8f, 0.2f, 0f);
        ultGlowImg.raycastTarget = false;
        if (slotSprite != null) { ultGlowImg.sprite = slotSprite; ultGlowImg.type = Image.Type.Simple; ultGlowImg.preserveAspect = true; }

        // "ULTIMATE" tier label at bottom
        MakeText("UltTierLabel", ultRoot,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 3f), new Vector2(ultSize, 14f),
            "ULTIMATE", keyFontSize - 3f, new Color(0.85f, 0.6f, 1f, 0.9f), TextAlignmentOptions.Center);

        // Spell name at top
        ultNameText = MakeText("UltName", ultRoot,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -2f), new Vector2(ultSize - 4f, 15f),
            "", nameFontSize, Color.white, TextAlignmentOptions.Center);

        // Hit counter in center
        ultCountText = MakeText("UltCount", ultRoot,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 4f), new Vector2(ultSize, 30f),
            "0 / 40", keyFontSize, Color.white, TextAlignmentOptions.Center);

        // Key label above the box
        ultKeyText = MakeText("UltKey", ultRoot,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(4f, 0f), new Vector2(44f, 20f),
            SettingsData.KeyLabel(SettingsData.SpellUltimate), keyFontSize, Color.white, TextAlignmentOptions.Left);
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    private void Refresh()
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null || slots == null) return;

        for (int i = 0; i < Grimoire.LoadoutSize; i++)
        {
            SpellData spell   = grimoire.Loadout[i];
            Color     tierCol = TierColors[i];

            slots[i].bg.color = spell != null
                ? new Color(tierCol.r, tierCol.g, tierCol.b, 0.75f)
                : new Color(tierCol.r, tierCol.g, tierCol.b, 0.25f);

            // No spell names on any hotbar slot
            slots[i].nameText.text = "";

            if (spell != null && spell.icon != null)
            {
                slots[i].icon.sprite  = spell.icon;
                slots[i].icon.enabled = true;
            }
            else
            {
                slots[i].icon.enabled = false;
            }
        }
    }

    private void RefreshUltimateUI()
    {
        var ult = UltimateAbility.Instance;
        if (ult == null || ultGauge == null) return;

        ultGauge.fillAmount = ult.GaugePct;
        ultCountText.text   = $"{ult.HitCount} / {ult.RequiredHits}";
        ultNameText.text    = "";

        // Icon
        if (ultIconImg != null)
        {
            var spell = ult.Spell;
            if (spell != null && spell.icon != null)
            {
                ultIconImg.sprite  = spell.icon;
                ultIconImg.enabled = true;
            }
            else
            {
                ultIconImg.enabled = false;
            }
        }

        // Gauge color: dark purple → bright gold as it fills
        float pct = ult.GaugePct;
        ultGauge.color = new Color(
            Mathf.Lerp(0.32f, 1.0f,  pct),
            Mathf.Lerp(0.04f, 0.78f, pct),
            Mathf.Lerp(0.52f, 0.08f, pct),
            Mathf.Lerp(0.88f, 0.95f, pct));
    }

    private void OnUltimateBecameReady()
    {
        if (ultGauge != null)
            ultGauge.color = new Color(1f, 0.85f, 0.15f, 0.95f);
    }

    // ── Cooldowns ────────────────────────────────────────────────────────────

    private void UpdateCooldowns()
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null || slots == null) return;

        for (int i = 0; i < Grimoire.LoadoutSize; i++)
        {
            SpellData spell = grimoire.Loadout[i];
            if (spell == null)
            {
                slots[i].cooldownOverlay.fillAmount = 0f;
                slots[i].cooldownText.gameObject.SetActive(false);
                continue;
            }

            float remaining  = grimoire.GetCooldownRemaining(i);
            bool  onCooldown = remaining > 0f;
            slots[i].cooldownOverlay.fillAmount = onCooldown ? remaining / spell.cooldown : 0f;
            slots[i].cooldownText.gameObject.SetActive(onCooldown);
            if (onCooldown)
                slots[i].cooldownText.text = remaining.ToString("F1");
        }

        // Dash cooldown
        if (dashCooldownOverlay != null)
        {
            if (cachedPlayer == null) cachedPlayer = FindAnyObjectByType<PlayerMovement>();
            float dashPct    = cachedPlayer != null ? cachedPlayer.DashCooldownPct : 0f;
            bool  onCooldown = dashPct > 0f;
            dashCooldownOverlay.fillAmount = dashPct;
            if (dashCenterLabel != null)
            {
                dashCenterLabel.text  = onCooldown
                    ? cachedPlayer.DashCooldownRemaining.ToString("F1")
                    : "DASH";
                dashCenterLabel.color = onCooldown ? Color.white : new Color(1f, 1f, 1f, 0.85f);
            }
        }
    }

    private void UpdateUltimateAnimation()
    {
        if (ultRoot == null) return;
        var ult = UltimateAbility.Instance;
        if (ult == null) return;

        if (ult.IsReady)
        {
            // Pulse scale
            float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.04f;
            ultRoot.localScale = new Vector3(pulse, pulse, 1f);

            // Glow border alpha pulse
            if (ultGlowImg != null)
            {
                float a = 0.5f + Mathf.Sin(Time.time * 5f) * 0.3f;
                ultGlowImg.color = new Color(1f, 0.85f, 0.2f, a);
            }
        }
        else
        {
            ultRoot.localScale = Vector3.one;
            if (ultGlowImg != null)
                ultGlowImg.color = new Color(1f, 0.85f, 0.2f, 0f);
        }
    }

    // ── Key labels ────────────────────────────────────────────────────────────

    private void RefreshKeyLabels()
    {
        if (slots == null) return;
        for (int i = 0; i < Grimoire.LoadoutSize; i++)
            slots[i].keyText.text = SettingsData.KeyLabel(GetSlotKey(i));
        if (dashKeyText != null)
            dashKeyText.text = SettingsData.KeyLabel(SettingsData.Dash);
        if (ultKeyText != null)
            ultKeyText.text = SettingsData.KeyLabel(SettingsData.SpellUltimate);
    }

    private static KeyCode GetSlotKey(int slotIndex) => slotIndex switch
    {
        0 => SettingsData.Attack,
        1 => SettingsData.SpellSkill,
        2 => SettingsData.SpellSkill2,
        _ => KeyCode.None,
    };

    // ── Placement mode ───────────────────────────────────────────────────────

    public void EnterPlacementMode(SpellData spell, System.Action<int> onSlotSelected)
    {
        _inPlacementMode   = true;
        _placementCallback = onSlotSelected;

        // Arc slots
        for (int i = 0; i < Grimoire.LoadoutSize; i++)
        {
            bool valid = (i == 0 && spell.tier == SpellTier.Basic)
                      || ((i == 1 || i == 2) && spell.tier == SpellTier.Skill);
            if (valid)
            {
                AddSlotGlow(slots[i].root, TierColors[i]);
                AddSlotClickTrigger(slots[i].root, i);
            }
            else
            {
                slots[i].bg.color = new Color(0.3f, 0.3f, 0.3f, 0.2f);
            }
        }

        // Ultimate box
        if (spell.tier == SpellTier.Ultimate)
        {
            AddSlotGlow(ultRoot, new Color(0.9f, 0.72f, 0.15f));
            AddSlotClickTrigger(ultRoot, -1);
        }
        else
        {
            var ultBg = ultRoot?.GetComponent<Image>();
            if (ultBg != null) ultBg.color = new Color(0.15f, 0.05f, 0.22f, 0.3f);
        }
    }

    public void ExitPlacementMode()
    {
        _inPlacementMode   = false;
        _placementCallback = null;
        foreach (var go in _placementGlowGOs)
            if (go != null) Destroy(go);
        _placementGlowGOs.Clear();
        foreach (var t in _placementTriggers)
            if (t != null) Destroy(t);
        _placementTriggers.Clear();

        // Restore ultimate bg
        var ultBg = ultRoot?.GetComponent<Image>();
        if (ultBg != null) ultBg.color = new Color(0.22f, 0.08f, 0.32f, 0.85f);
        Refresh();
    }

    private void AddSlotGlow(RectTransform parent, Color glowColor)
    {
        var go  = new GameObject("PlacementGlow");
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.one;
        rt.offsetMin  = new Vector2(-7f, -7f);
        rt.offsetMax  = new Vector2( 7f,  7f);
        var img       = go.AddComponent<Image>();
        img.color     = new Color(glowColor.r, glowColor.g, glowColor.b, 0.75f);
        img.raycastTarget = false;
        if (slotSprite != null) { img.sprite = slotSprite; img.type = Image.Type.Simple; img.preserveAspect = true; }
        _placementGlowGOs.Add(go);
    }

    private void AddSlotClickTrigger(RectTransform target, int slotIndex)
    {
        var trigger = target.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var entry   = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick };
        int captured = slotIndex;
        entry.callback.AddListener(_ => _placementCallback?.Invoke(captured));
        trigger.triggers.Add(entry);
        _placementTriggers.Add(trigger);
    }

    private void UpdatePlacementGlows()
    {
        float a = 0.45f + Mathf.Sin(Time.unscaledTime * 5f) * 0.3f;
        foreach (var go in _placementGlowGOs)
        {
            if (go == null) continue;
            var img = go.GetComponent<Image>();
            if (img != null) img.color = new Color(img.color.r, img.color.g, img.color.b, a);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TMP_Text MakeText(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size,
        string text, float fontSize, Color color, TextAlignmentOptions align)
    {
        var rt  = MakeRT(name, parent, anchorMin, anchorMax, pivot, pos, size);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font             = font;
        tmp.fontSize         = fontSize;
        tmp.fontStyle        = FontStyles.Bold;
        tmp.alignment        = align;
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.overflowMode     = TextOverflowModes.Overflow;
        tmp.color            = color;
        tmp.outlineWidth     = 0.25f;
        tmp.outlineColor     = new Color32(0, 0, 0, 200);
        tmp.raycastTarget    = false;
        tmp.text             = text;
        return tmp;
    }

    private static RectTransform MakeRT(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        return rt;
    }
}
