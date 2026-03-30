using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// In-game spell hotbar — 3 slots at bottom-center of screen.
/// Builds its own Canvas at runtime, reads from Grimoire.Instance.
///
/// Setup: Create empty GameObject, attach this script, assign sprites + font in Inspector.
/// </summary>
public class SpellHotbar : MonoBehaviour
{
    [Header("Sprites")]
    [SerializeField] private Sprite slotSprite;         // SpellSlot.png
    [SerializeField] private Sprite slotSelectedSprite; // SpellSlotSelected.png

    [Header("Font")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Layout (tweak in Inspector)")]
    [SerializeField] private float slotSize    = 80f;
    [SerializeField] private float slotGap     = 8f;
    private float bottomPad = 68f;
    [SerializeField] private float keyLabelY   = 8f;
    [SerializeField] private float nameFontSize = 12f;
    [SerializeField] private float keyFontSize  = 14f;

    private GameObject canvasGO;
    private SlotUI[] slots;

    // Dash cooldown indicator
    private Image          dashCooldownOverlay;
    private TMP_Text       dashKeyText;
    private TMP_Text       dashCenterLabel;
    private PlayerMovement cachedPlayer;


    private class SlotUI
    {
        public RectTransform root;
        public Image bg;
        public Image icon;
        public Image cooldownOverlay;
        public TMP_Text cooldownText;
        public TMP_Text nameText;
        public TMP_Text keyText;
    }

    private void Start()
    {
        BuildHotbar();
        if (Grimoire.Instance != null)
            Grimoire.Instance.OnLoadoutChanged += Refresh;
        SettingsData.OnBindingsChanged += RefreshKeyLabels;
        Refresh();
    }

    private void OnDestroy()
    {
        if (Grimoire.Instance != null)
            Grimoire.Instance.OnLoadoutChanged -= Refresh;
        SettingsData.OnBindingsChanged -= RefreshKeyLabels;
        if (canvasGO != null) Destroy(canvasGO);
    }

    private void Update()
    {
        if (PauseManager.IsPaused) return;
        UpdateCooldowns();
    }

    // ── Build ────────────────────────────────────────────────────────────────

    private void BuildHotbar()
    {
        canvasGO = new GameObject("SpellHotbar_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        float totalWidth = slotSize * Grimoire.LoadoutSize + slotGap * (Grimoire.LoadoutSize - 1);
        float startX = -totalWidth / 2f + slotSize / 2f;

        slots = new SlotUI[Grimoire.LoadoutSize];

        for (int i = 0; i < Grimoire.LoadoutSize; i++)
        {
            var slot = new SlotUI();

            // Root anchor: bottom-center
            slot.root = MakeRT($"Slot_{i}", canvasGO.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(startX + i * (slotSize + slotGap), bottomPad),
                new Vector2(slotSize, slotSize));

            // Background image (slot sprite)
            slot.bg = slot.root.gameObject.AddComponent<Image>();
            slot.bg.sprite = slotSprite;
            slot.bg.type = Image.Type.Simple;
            slot.bg.preserveAspect = true;

            // Spell icon overlay (centered inside slot, slightly inset)
            float iconInset = slotSize * 0.15f;
            float iconDim = slotSize - iconInset * 2f;
            var iconRT = MakeRT($"Icon_{i}", slot.root,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(iconDim, iconDim));
            slot.icon = iconRT.gameObject.AddComponent<Image>();
            slot.icon.preserveAspect = true;
            slot.icon.raycastTarget = false;
            slot.icon.enabled = false; // hidden until a spell with an icon is equipped

            // Cooldown overlay — radial sweep (clock-wipe style)
            var coolRT = MakeRT("Cooldown", slot.root,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            coolRT.offsetMin = Vector2.zero;
            coolRT.offsetMax = Vector2.zero;
            slot.cooldownOverlay = coolRT.gameObject.AddComponent<Image>();
            slot.cooldownOverlay.color = new Color(0f, 0f, 0f, 0.55f);
            slot.cooldownOverlay.raycastTarget = false;
            slot.cooldownOverlay.type = Image.Type.Filled;
            slot.cooldownOverlay.fillMethod = Image.FillMethod.Radial360;
            slot.cooldownOverlay.fillOrigin = 2; // top, sweeps clockwise
            slot.cooldownOverlay.fillClockwise = true;
            slot.cooldownOverlay.fillAmount = 0f;

            // Cooldown countdown text — centered, shown only while on cooldown
            slot.cooldownText = MakeText($"CooldownText_{i}", slot.root,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(slotSize, slotSize),
                "", nameFontSize + 2f, Color.white, TextAlignmentOptions.Center);
            slot.cooldownText.gameObject.SetActive(false);

            // Key label above slot — shows current binding from SettingsData
            slot.keyText = MakeText($"Key_{i}", slot.root,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, keyLabelY), new Vector2(slotSize, 20f),
                SettingsData.KeyLabel(GetSlotKey(i)), keyFontSize, Color.white, TextAlignmentOptions.Center);

            // Spell name below slot
            slot.nameText = MakeText($"Name_{i}", slot.root,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(slotSize + 20f, 20f),
                "", nameFontSize, Color.white, TextAlignmentOptions.Center);

            slots[i] = slot;
        }

        // ── Dash cooldown indicator — small slot to the right of spell slots ──
        BuildDashIndicator(canvasGO.transform, totalWidth, startX);
    }

    private void BuildDashIndicator(Transform parent, float totalWidth, float startX)
    {
        float dashSize = slotSize * 0.65f;
        float dashX = startX + Grimoire.LoadoutSize * (slotSize + slotGap) + slotGap + dashSize / 2f;

        var root = MakeRT("DashSlot", parent,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(dashX, bottomPad + (slotSize - dashSize) / 2f),
            new Vector2(dashSize, dashSize));

        // Background
        var bg = root.gameObject.AddComponent<Image>();
        bg.color = new Color(0.3f, 0.3f, 0.3f, 0.7f);
        if (slotSprite != null) { bg.sprite = slotSprite; bg.color = new Color(1f, 1f, 1f, 0.6f); }

        // Cooldown overlay — radial sweep (clock-wipe style)
        var coolRT = MakeRT("DashCooldown", root,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        coolRT.offsetMin = Vector2.zero;
        coolRT.offsetMax = Vector2.zero;
        dashCooldownOverlay = coolRT.gameObject.AddComponent<Image>();
        dashCooldownOverlay.color = new Color(0f, 0f, 0f, 0.55f);
        dashCooldownOverlay.raycastTarget = false;
        dashCooldownOverlay.type = Image.Type.Filled;
        dashCooldownOverlay.fillMethod = Image.FillMethod.Radial360;
        dashCooldownOverlay.fillOrigin = 2; // top, sweeps clockwise
        dashCooldownOverlay.fillClockwise = true;
        dashCooldownOverlay.fillAmount = 0f;

        // Center label — shows "DASH" when ready, remaining seconds when on cooldown
        dashCenterLabel = MakeText("DashLabel", root,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(dashSize, dashSize * 0.7f),
            "DASH", nameFontSize, Color.white, TextAlignmentOptions.Center);

        // Key label above slot
        dashKeyText = MakeText("DashKey", root,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
            new Vector2(0f, keyLabelY), new Vector2(dashSize, 20f),
            SettingsData.KeyLabel(SettingsData.Dash), keyFontSize, Color.white, TextAlignmentOptions.Center);
    }

    // ── Refresh on loadout change ────────────────────────────────────────────

    private void Refresh()
    {
        var grimoire = Grimoire.Instance;
        if (grimoire == null || slots == null) return;

        for (int i = 0; i < Grimoire.LoadoutSize; i++)
        {
            SpellData spell = grimoire.Loadout[i];
            bool isActive = i == grimoire.ActiveSlot;

            slots[i].bg.sprite = isActive ? slotSelectedSprite : slotSprite;
            slots[i].nameText.text = spell != null ? spell.spellName : "";
            slots[i].nameText.color = spell != null ? Color.white : new Color(1f, 1f, 1f, 0.4f);

            // Show spell icon if available
            if (spell != null && spell.icon != null)
            {
                slots[i].icon.sprite = spell.icon;
                slots[i].icon.enabled = true;
            }
            else
            {
                slots[i].icon.enabled = false;
            }

            // Dim empty slots
            slots[i].bg.color = spell != null ? Color.white : new Color(1f, 1f, 1f, 0.5f);

            // Key label highlight
            slots[i].keyText.color = isActive
                ? new Color(1f, 0.85f, 0.3f)  // gold
                : Color.white;
        }
    }

    // ── Cooldown animation ───────────────────────────────────────────────────

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

            float remaining = grimoire.GetCooldownRemaining(i);
            bool onCooldown = remaining > 0f;
            slots[i].cooldownOverlay.fillAmount = onCooldown ? remaining / spell.cooldown : 0f;
            slots[i].cooldownText.gameObject.SetActive(onCooldown);
            if (onCooldown)
                slots[i].cooldownText.text = remaining.ToString("F1");
        }

        // Dash cooldown
        if (dashCooldownOverlay != null)
        {
            if (cachedPlayer == null) cachedPlayer = FindAnyObjectByType<PlayerMovement>();
            float dashPct = cachedPlayer != null ? cachedPlayer.DashCooldownPct : 0f;
            bool dashOnCooldown = dashPct > 0f;
            dashCooldownOverlay.fillAmount = dashPct;
            if (dashCenterLabel != null)
            {
                dashCenterLabel.text = dashOnCooldown
                    ? cachedPlayer.DashCooldownRemaining.ToString("F1")
                    : "DASH";
                dashCenterLabel.color = dashOnCooldown ? Color.white : new Color(1f, 1f, 1f, 0.85f);
            }
        }

        // Also refresh active slot highlight (key press may not fire event if slot already active)
        int currentActive = grimoire.ActiveSlot;
        for (int i = 0; i < Grimoire.LoadoutSize; i++)
        {
            bool isActive = i == currentActive;
            if ((slots[i].bg.sprite == slotSelectedSprite) != isActive)
            {
                slots[i].bg.sprite = isActive ? slotSelectedSprite : slotSprite;
                slots[i].keyText.color = isActive
                    ? new Color(1f, 0.85f, 0.3f)
                    : Color.white;
            }
        }
    }

    // ── Key label refresh ────────────────────────────────────────────────────

    private void RefreshKeyLabels()
    {
        if (slots == null) return;
        for (int i = 0; i < Grimoire.LoadoutSize; i++)
            slots[i].keyText.text = SettingsData.KeyLabel(GetSlotKey(i));
        if (dashKeyText != null)
            dashKeyText.text = SettingsData.KeyLabel(SettingsData.Dash);
    }

    private static KeyCode GetSlotKey(int slotIndex) => slotIndex switch
    {
        0 => SettingsData.Slot1,
        1 => SettingsData.Slot2,
        2 => SettingsData.Slot3,
        _ => KeyCode.None,
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TMP_Text MakeText(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size,
        string text, float fontSize, Color color, TextAlignmentOptions align)
    {
        var rt = MakeRT(name, parent, anchorMin, anchorMax, pivot, pos, size);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = align;
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color = color;
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = new Color32(0, 0, 0, 200);
        tmp.raycastTarget = false;
        tmp.text = text;
        return tmp;
    }

    private static RectTransform MakeRT(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }
}
