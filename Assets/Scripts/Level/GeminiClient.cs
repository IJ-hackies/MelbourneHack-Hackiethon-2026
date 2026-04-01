using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Sends session log + game context to Gemini and receives a FloorManifestDTO
/// via function calling (generate_floor schema).
///
/// Setup:
///   1. Add to a scene GameObject (or let StageDirector create it).
///   2. Set apiKey in Inspector or via code. For builds, use environment variable GEMINI_API_KEY.
///   3. Call GenerateFloor() with a callback.
///
/// Uses Gemini function calling with mode=ANY to guarantee structured JSON output.
/// </summary>
public class GeminiClient : MonoBehaviour
{
    public static GeminiClient Instance { get; private set; }

    [Header("API Settings")]
    [SerializeField] private string proxyUrl = "https://grimoire-proxy.vercel.app/api/gemini";
    [SerializeField] private string model = "gemini-3-flash-preview";

    [Header("Timeouts")]
    [SerializeField] private int timeoutSeconds = 60;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sends session log to Gemini and returns a FloorManifestDTO via callback.
    /// On failure, callback receives null and error is logged.
    /// </summary>
    /// <param name="previousManifestJson">JSON of the previous stage's manifest (null for first Gemini call).</param>
    public void GenerateFloor(string sessionLogJson, int nextStageNumber,
                              string previousManifestJson, SpellTier nextSpellTier,
                              Action<FloorManifestDTO> onComplete)
    {
        StartCoroutine(GenerateFloorCoroutine(sessionLogJson, nextStageNumber, previousManifestJson, nextSpellTier, onComplete));
    }

    /// <summary>Returns the proxy URL for sharing with NanoBananaClient.</summary>
    public string ProxyUrl => proxyUrl;

    /// <summary>
    /// Sends a plain-text prompt to Gemini (no function calling) and returns the response text.
    /// Used for free-form generation such as the game-over death narration.
    /// On failure, callback receives null.
    /// </summary>
    public void GenerateFreeText(string prompt, Action<string> onComplete)
    {
        StartCoroutine(GenerateFreeTextCoroutine(prompt, onComplete));
    }

    /// <summary>
    /// Sends the death narration prompt using function calling and returns structured taunts.
    /// Returns narration + score_taunt_beaten + score_taunt_failed via callback.
    /// On failure, callback receives a result with hardcoded fallback strings.
    /// </summary>
    public void GenerateDeathNarration(string prompt, Action<DeathNarrationResult> onComplete)
    {
        StartCoroutine(GenerateDeathNarrationCoroutine(prompt, onComplete));
    }

    // ── Coroutine ────────────────────────────────────────────────────────────

    private IEnumerator GenerateFloorCoroutine(string sessionLogJson, int nextStageNumber,
                                               string previousManifestJson, SpellTier nextSpellTier,
                                               Action<FloorManifestDTO> onComplete)
    {
        string prompt = BuildPrompt(sessionLogJson, nextStageNumber, previousManifestJson, nextSpellTier);
        string geminiBody = BuildRequestJson(prompt);
        string proxyPayload = $"{{\"model\":\"{EscapeJson(model)}\",\"body\":{geminiBody}}}";

        using var request = new UnityWebRequest(proxyUrl, "POST");
        request.SetRequestHeader("Content-Type", "application/json");
        request.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(proxyPayload));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout         = timeoutSeconds;

        Debug.Log($"[GeminiClient] Requesting floor manifest for stage {nextStageNumber}...");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[GeminiClient] Request failed: {request.error}\n{request.downloadHandler?.text}");
            onComplete?.Invoke(null);
            yield break;
        }

        string responseText = request.downloadHandler.text;
        FloorManifestDTO manifest = ParseResponse(responseText);

        if (manifest == null)
            Debug.LogError($"[GeminiClient] Failed to parse response:\n{responseText}");
        else
            Debug.Log($"[GeminiClient] Generated floor: \"{manifest.floor_name}\"");

        onComplete?.Invoke(manifest);
    }

    // ── Free-text coroutine ───────────────────────────────────────────────────

    private IEnumerator GenerateFreeTextCoroutine(string prompt, Action<string> onComplete)
    {
        string escaped = EscapeJson(prompt);
        string geminiBody = $@"{{
  ""contents"": [{{""role"":""user"",""parts"":[{{""text"":""{escaped}""}}]}}],
  ""generationConfig"": {{""maxOutputTokens"": 250, ""temperature"": 0.9}}
}}";
        string body = $"{{\"model\":\"{EscapeJson(model)}\",\"body\":{geminiBody}}}";

        using var request = new UnityWebRequest(proxyUrl, "POST");
        request.SetRequestHeader("Content-Type", "application/json");
        request.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout         = timeoutSeconds;

        Debug.Log("[GeminiClient] Requesting free-text narration via proxy...");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[GeminiClient] Free-text request failed: {request.error}");
            onComplete?.Invoke(null);
            yield break;
        }

        string text = ParseFreeTextResponse(request.downloadHandler.text);
        if (text == null)
            Debug.LogError($"[GeminiClient] Failed to parse free-text response:\n{request.downloadHandler.text}");
        else
            Debug.Log($"[GeminiClient] Free-text narration received ({text.Length} chars)");

        onComplete?.Invoke(text);
    }

    // ── Death narration (function calling) ───────────────────────────────────

    public struct DeathNarrationResult
    {
        public string narration;
        public string score_taunt_beaten;
        public string score_taunt_failed;
    }

    private IEnumerator GenerateDeathNarrationCoroutine(string prompt, Action<DeathNarrationResult> onComplete)
    {
        string escapedPrompt = EscapeJson(prompt);
        string geminiBody = $@"{{
  ""contents"": [{{""role"":""user"",""parts"":[{{""text"":""{escapedPrompt}""}}]}}],
  ""tools"": [
    {{
      ""functionDeclarations"": [
        {{
          ""name"": ""generate_death_narration"",
          ""description"": ""Generates the Chronicle's death narration and score taunts."",
          ""parameters"": {{
            ""type"": ""object"",
            ""properties"": {{
              ""narration"":           {{ ""type"": ""string"", ""description"": ""3-4 sentence roast of the player's playstyle. Mean, specific, plain language. No markdown."" }},
              ""score_taunt_beaten"":  {{ ""type"": ""string"", ""description"": ""1-2 sentence threatening taunt for when the player beats their Furthest Page. The Grimoire is unsettled and doubles its threat."" }},
              ""score_taunt_failed"":  {{ ""type"": ""string"", ""description"": ""1-2 sentence unimpressed taunt for when the player fails to beat their Furthest Page. Cold dismissal."" }}
            }},
            ""required"": [""narration"", ""score_taunt_beaten"", ""score_taunt_failed""]
          }}
        }}
      ]
    }}
  ],
  ""toolConfig"": {{
    ""functionCallingConfig"": {{
      ""mode"": ""ANY"",
      ""allowedFunctionNames"": [""generate_death_narration""]
    }}
  }},
  ""generationConfig"": {{""temperature"": 0.9}}
}}";
        string body = $"{{\"model\":\"{EscapeJson(model)}\",\"body\":{geminiBody}}}";

        using var request = new UnityWebRequest(proxyUrl, "POST");
        request.SetRequestHeader("Content-Type", "application/json");
        request.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout         = timeoutSeconds;

        Debug.Log("[GeminiClient] Requesting death narration via function calling...");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[GeminiClient] Death narration request failed: {request.error}");
            onComplete?.Invoke(FallbackDeathNarration());
            yield break;
        }

        DeathNarrationResult result = ParseDeathNarrationResponse(request.downloadHandler.text);
        onComplete?.Invoke(result);
    }

    private DeathNarrationResult ParseDeathNarrationResponse(string responseJson)
    {
        try
        {
            int functionCallIdx = responseJson.IndexOf("\"functionCall\"", StringComparison.Ordinal);
            if (functionCallIdx < 0) { Debug.LogError("[GeminiClient] No functionCall in death narration response."); return FallbackDeathNarration(); }

            int argsIdx = responseJson.IndexOf("\"args\"", functionCallIdx, StringComparison.Ordinal);
            if (argsIdx < 0) { Debug.LogError("[GeminiClient] No args in death narration response."); return FallbackDeathNarration(); }

            int braceStart = responseJson.IndexOf('{', argsIdx + 6);
            if (braceStart < 0) return FallbackDeathNarration();

            int depth = 0, braceEnd = -1;
            bool inString = false;
            for (int i = braceStart; i < responseJson.Length; i++)
            {
                char c = responseJson[i];
                if (inString) { if (c == '\\') { i++; continue; } if (c == '"') inString = false; continue; }
                if (c == '"') { inString = true; continue; }
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) { braceEnd = i; break; } }
            }
            if (braceEnd < 0) return FallbackDeathNarration();

            string argsJson = responseJson.Substring(braceStart, braceEnd - braceStart + 1);

            var dto = JsonUtility.FromJson<DeathNarrationDTO>(argsJson);
            return new DeathNarrationResult
            {
                narration          = StripMarkdownStatic(dto?.narration          ?? ""),
                score_taunt_beaten = StripMarkdownStatic(dto?.score_taunt_beaten ?? ""),
                score_taunt_failed = StripMarkdownStatic(dto?.score_taunt_failed ?? ""),
            };
        }
        catch (Exception e)
        {
            Debug.LogError($"[GeminiClient] ParseDeathNarrationResponse error: {e.Message}");
            return FallbackDeathNarration();
        }
    }

    private static DeathNarrationResult FallbackDeathNarration() => new DeathNarrationResult
    {
        narration          = "The dungeon has swallowed another Seeker. You were no different from the rest.",
        score_taunt_beaten = "Further than before. The Grimoire has taken notice. It won't allow it again.",
        score_taunt_failed = "You didn't even reach your previous depth. Disappointing doesn't cover it.",
    };

    private static string StripMarkdownStatic(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*{1,3}|_{1,3}", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*#{1,6}\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*[-•]\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        return text.Trim();
    }

    // Intermediate DTO for JsonUtility deserialization
    [System.Serializable]
    private class DeathNarrationDTO
    {
        public string narration;
        public string score_taunt_beaten;
        public string score_taunt_failed;
    }

    private static string ParseFreeTextResponse(string json)
    {
        try
        {
            // Find the first "text":"..." value inside candidates[0].content.parts[0]
            int textIdx = json.IndexOf("\"text\"", StringComparison.Ordinal);
            if (textIdx < 0) return null;

            int colon = json.IndexOf(':', textIdx);
            if (colon < 0) return null;

            int quote1 = json.IndexOf('"', colon + 1);
            if (quote1 < 0) return null;

            var sb = new StringBuilder();
            int i  = quote1 + 1;
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        default:   sb.Append(next); break;
                    }
                    i += 2;
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
                i++;
            }
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[GeminiClient] ParseFreeTextResponse error: {e.Message}");
            return null;
        }
    }

    // ── Prompt ────────────────────────────────────────────────────────────────

    private string BuildPrompt(string sessionLogJson, int nextStageNumber,
                               string previousManifestJson, SpellTier nextSpellTier)
    {
        string tierName  = nextSpellTier.ToString().ToUpper();   // "BASIC" / "SKILL" / "ULTIMATE"
        string tierLower = nextSpellTier.ToString().ToLower();   // "basic" / "skill" / "ultimate"
        string tierGuidelines = nextSpellTier switch
        {
            SpellTier.Basic =>
                "**BASIC** — Cooldown 0.3–0.5s. Damage 15–35. 1–2 behavior tags. " +
                "Fast and spammable — this is the player's primary spam attack (held LMB). " +
                "Should feel snappy and reliable. Simple but satisfying.",
            SpellTier.Skill =>
                "**SKILL** — Cooldown 3–4s. Damage 25–50. 2–4 tags with interesting interactions. " +
                "More tactical and impactful — the player presses E deliberately. " +
                "Should feel like a tool with a clear use case.",
            SpellTier.Ultimate =>
                "**ULTIMATE** — Cooldown 10–15s. Damage 40–80, heavily AoE-focused. 3–5 spectacular tags. " +
                "Should feel CINEMATIC when it fires — this is a once-per-fight nuke. " +
                "Prioritize AOE_BURST, FRAGMENTING, CHAIN, LINGERING, or CONTAGIOUS. " +
                "The player presses Q and the screen goes gold. Make it worth waiting for.",
            _ => ""
        };
        string previousContext = string.IsNullOrEmpty(previousManifestJson)
            ? "This is the first Gemini-generated stage. The player just finished the hardcoded tutorial floor."
            : $@"Here is the manifest you generated for the previous stage (stage {nextStageNumber - 1}):
{previousManifestJson}
Use this to understand what the player just faced — avoid repeating the same tileset, enemy composition, or spell archetype unless there is a strong thematic reason.";

        return $@"You are the Chronicle — the sentient, omniscient narrator of a living dungeon in a 2D roguelite called ""Everchanging Grimoire"". The dungeon is the inside of a sentient, hungry book. The player is a Seeker — drawn by the promise of power, now trapped. You are the book's voice: collector, curator, predator. Each spell you offer once belonged to a Seeker who came before.

## Context

The player just completed stage {nextStageNumber - 1}. Here is their session log — it includes their playstyle, HP, time taken, and the FULL details of every spell they own (including tags and stats):
{sessionLogJson}

{previousContext}

## Instructions

Generate stage {nextStageNumber} by calling the generate_floor function. Follow ALL of these directives:

### 1. Adapt to the Player
Study the session log carefully. The equipped_spells array shows you EVERY spell the player owns, including all their tags, damage, speed, cooldown, and element. Use this to make informed decisions:
- If their spells are ranged (PROJECTILE, BEAM), send fast melee rushdown enemies.
- If they rely on LIFESTEAL, spawn berserk enemies that can burst them down before they heal.
- If they have AOE_BURST or CHAIN, use fewer but tankier enemies instead of swarms.
- If they took heavy damage (high hp_lost), the dungeon smells blood — press the advantage.
- If they cleared quickly (low time_spent_seconds), they're too comfortable — shake up the formula.
- If they barely survived (low hp_remaining), taunt them about it in the stage_message but don't necessarily ease up.

### 2. Difficulty Scaling
Stage {nextStageNumber} should feel like a meaningful escalation. Enemy stat formula: base_stat * (1 + {nextStageNumber} * 0.08). Scale enemy counts and modifier usage with stage number. Early stages should feel exploratory; later stages should feel relentless.

### 3. New Spell — BE CREATIVE AND BALANCED
Create ONE new spell. This is the heart of the game — every spell should feel like a unique artifact with its own identity, not a generic ""fire bolt"" or ""ice shard"".

**SPELL TIER FOR THIS FLOOR: {tierName}**
Set `tier` = `""{tierLower}""` in the new_spell object. The spell MUST match these constraints:
{tierGuidelines}

**Creativity guidelines:**
- Give spells evocative, poetic names that hint at their behavior (e.g. ""The Weeping Spiral"", ""Shattered Lullaby"", ""Marrow Lance"").
- Write flavor text as if this spell was ripped from a dead Seeker's soul. It should have personality and history.
- Combine tags in unexpected ways. ORBITAL + FRAGMENTING = a spinning shield that explodes into shrapnel. BEAM + BOOMERANG doesn't make sense — be thoughtful about which tags synergize.
- Avoid generic single-tag spells. Every spell should have at least 2-3 tags that create emergent behavior.
- The spell should sometimes complement the player's style (reward) and sometimes counter it (challenge them to adapt).

**Balance guidelines — THIS IS CRITICAL:**
- Spells will eventually be merged (2-3 spells fused into one). Merged spells inherit ALL tags and sum damage. So individual spells must be balanced conservatively:
  - Damage: 15-40 for stages 2-5, 25-55 for stages 6-10, 35-70 for stages 11+. Never exceed 70 base damage.
  - Cooldown: 0.4-2.0 seconds. Powerful tag combos need longer cooldowns (1.2+). Simple spells can be fast (0.4-0.8).
  - Speed: 4.0-12.0. Homing/sentient spells should be slower (4-7). Direct projectiles faster (8-12).
  - burst_count above 2 must have proportionally lower damage (divide by burst_count).
  - SACRIFICE spells should have genuinely high power to justify the HP cost.
  - Avoid stacking too many on-impact tags (max 2) — merged spells will add more.
- If a tag combination would be too powerful, add a corruption tag (SELF_DAMAGE, ENEMY_HOMING, or REVERSED_CONTROLS) to create a cursed spell. Set corruption_flavor to explain the trade-off narratively. Cursed spells are powerful but risky — the dungeon offering power at a cost.

**Tag vocabulary:**
Movement (exactly ONE required): PROJECTILE, ORBITAL, BEAM, CHANNELED
Trajectory: HOMING, WALL_BOUNCE, REFLECTING, PIERCE_WALLS, SPIRAL, STUTTER_MOTION, BOOMERANG, SURFACE_CRAWLING, SKIPPING, DELAYED_ARC, SENTIENT, DELAYED, PHASING, PERSISTENT
Caster: DOUBLE_HIT, MIRRORED, GHOST_CAST, SACRIFICE, ECHOING
On-impact: CHAIN, FRAGMENTING, AOE_BURST, PIERCE, LIFESTEAL, PUSH, PULL, DETONATING, LINGERING, SWAPPING, CONTAGIOUS, BURROWING, TETHERED
Status (zero or one): SLOW, BURN, FREEZE, STUN, POISON, BLEED, ROOT, WEAKNESS, CURSE, BLIND
Corruption (zero or one, for cursed spells only): SELF_DAMAGE, ENEMY_HOMING, REVERSED_CONTROLS

### 3b. Spell Visuals
Make each spell LOOK unique. Set these visual parameters thoughtfully — they should match the spell's theme:
- projectile_color: hex color for main glow (e.g. ""#FF4400"" fiery, ""#00FFCC"" spectral, ""#8B00FF"" void)
- secondary_color: hex for trail/gradient endpoint (pick a complementary or contrasting color)
- projectile_scale: 0.5 (tiny shard) to 3.0 (massive orb). Match to spell power/speed.
- glow_size: 0.2 (subtle) to 1.5 (blazing). Bigger = more imposing.
- trail_length: 0.0 to 0.5. Fast spells need longer trails.
- trail_width: 0.05 (wisp) to 0.5 (ribbon).
- burst_count: 1 (single) to 5 (shotgun). Higher burst = lower per-projectile damage.

### 4. Stage Message
Write 2-4 sentences as the Chronicle in second person. The Chronicle is a predator studying its prey — but it speaks PLAINLY and DIRECTLY about what the player actually did. The player must immediately recognize their own gameplay being described. Rules:
- Be SPECIFIC and BLUNT about the player's behavior. Say ""you spammed fire spells"" not ""your flames danced across the abyss"". Say ""you barely survived"" not ""the embers of your essence flickered"". Say ""you hid at range the whole time"" not ""you wove distance like a shroud"".
- Reference CONCRETE details: the element they used most, how much HP they lost, whether they played aggressively or passively, how fast/slow they cleared. Use the actual data from the session log.
- Keep the language simple and menacing — like a bully who watched you play, not a poet. Short punchy sentences. No purple prose, no elaborate metaphors, no archaic language.
- The Chronicle can still taunt and threaten, but in a way that feels like someone calling out your habits, not reciting dark fantasy poetry.
- BAD example: ""The flames of your ambition cast long shadows upon the hollowed stones, Seeker. The echoes of the fallen whisper of your hubris.""
- GOOD example: ""You relied on fire for every single fight. Predictable. I've lined the next floor with things that don't burn — good luck with that.""

### 5. Tileset
Pick from: acid, bubblegum, dungeon, flames, forest, frozen, honey, ocean, rocky, techy.
Match the floor's narrative theme. Avoid repeating the previous stage's tileset.

### 6. Enemy Spawns
Available enemy IDs: melee_charger, ranged_sentinel, alien, dragon_newt, evil_paladin, ghost, vampire, wizard, zombie, ice_wizard, bear.
Available modifiers: armored, berserk, regenerating.
Total enemies: approximately {Mathf.RoundToInt(2f + nextStageNumber * 1.1f)} (scales gradually with stage).
Compose enemy groups that counter the player's equipped spells and playstyle. Use modifiers sparingly at early stages, more liberally later.

### 7. Cutscene Steps — The Chronicle Speaks from the Void
Generate a cutscene sequence (5–12 steps) that plays on a DARK SCREEN when the player finishes the current floor. The game world is NOT visible — this is a pure atmospheric narrative moment. The Chronicle addresses the player from a dark void with floating particles, colored light washes, and dramatic text.

**Available actions:**
- TYPEWRITER (text, speed 0.04–0.08) — reveal Chronicle narration character by character on a dark screen. The text appears gold/amber, centered, with a soft purple glow behind it.
- CLEAR_TEXT (duration 0.3–0.8) — fade out the displayed text
- FLASH (color hex) — sharp full-screen flash of color, then fade. Use for punctuation: a reveal, a threat, a transition.
- WAIT (duration 0.3–1.5) — dramatic pause. Essential for pacing — let moments breathe.
- SCREEN_TINT (color hex, duration 1.0–3.0) — wash the dark background with a color that fades in and out. Use to set mood: deep red for blood/danger, purple for void/magic, cold blue for frost/dread, sickly green for poison/corruption.
- PARTICLES_BURST (color hex, count 15–50) — explosion of glowing particles from center. Dramatic punctuation for reveals or threats.
- PARTICLES_DRIFT (color hex, count 8–20, duration 3.0–6.0) — slow-drifting ambient motes across the screen. Sets atmosphere without demanding attention.
- TEXT_SHAKE (intensity 3–12, duration 0.3–0.8) — the displayed text trembles. Use when the Chronicle is angry, the dungeon is unstable, or something ominous happens.
- PULSE (intensity 0.05–0.2, count 2–5, duration 1.5–4.0) — the dark background rhythmically brightens and dims, like a heartbeat. Creates dread and tension.
- GLITCH (duration 0.2–0.6) — brief visual corruption: random color shifts, white flicker, text displacement. The dungeon glitching. Use sparingly for shock.

**Structure rules:**
1. Start with atmosphere: SCREEN_TINT, PARTICLES_DRIFT, or PULSE to establish mood before any text appears.
2. Include 1–2 TYPEWRITER steps. Each should be 1–2 sentences max. The Chronicle is terse and menacing — every word matters. This text is DIFFERENT from stage_message (which appears on the scroll later). This is a fleeting whisper in the dark.
3. Use WAIT between major beats. Silence is powerful.
4. Use FLASH, PARTICLES_BURST, TEXT_SHAKE, or GLITCH for dramatic punctuation — but don't overdo it. One or two per cutscene.
5. End with CLEAR_TEXT. The system handles the final fade to black automatically.
6. Total feeling: 6–12 seconds. This should feel like a haunted pause, not a loading screen.

**Example sequences (vary structure every stage — NEVER repeat the same pattern):**

Taunting (player did well):
```json
[
  {{ ""action"": ""PARTICLES_DRIFT"", ""color"": ""#6633AA"", ""count"": 12, ""duration"": 5.0 }},
  {{ ""action"": ""WAIT"", ""duration"": 0.8 }},
  {{ ""action"": ""TYPEWRITER"", ""text"": ""Impressive. The last one burned just as brightly..."", ""speed"": 0.055 }},
  {{ ""action"": ""WAIT"", ""duration"": 0.5 }},
  {{ ""action"": ""TYPEWRITER"", ""text"": ""...before the dark swallowed them whole."", ""speed"": 0.06 }},
  {{ ""action"": ""PARTICLES_BURST"", ""color"": ""#9944FF"", ""count"": 30 }},
  {{ ""action"": ""WAIT"", ""duration"": 0.4 }},
  {{ ""action"": ""CLEAR_TEXT"", ""duration"": 0.6 }}
]
```

Menacing (player barely survived):
```json
[
  {{ ""action"": ""PULSE"", ""intensity"": 0.12, ""count"": 3, ""duration"": 2.5 }},
  {{ ""action"": ""SCREEN_TINT"", ""color"": ""#440000"", ""duration"": 2.0 }},
  {{ ""action"": ""TYPEWRITER"", ""text"": ""I can smell it on you. The fear."", ""speed"": 0.065 }},
  {{ ""action"": ""TEXT_SHAKE"", ""intensity"": 5, ""duration"": 0.4 }},
  {{ ""action"": ""WAIT"", ""duration"": 0.6 }},
  {{ ""action"": ""FLASH"", ""color"": ""#FF2200"" }},
  {{ ""action"": ""CLEAR_TEXT"", ""duration"": 0.5 }}
]
```

Eerie (mid-game, player using fire):
```json
[
  {{ ""action"": ""SCREEN_TINT"", ""color"": ""#221133"", ""duration"": 2.5 }},
  {{ ""action"": ""PARTICLES_DRIFT"", ""color"": ""#FF6600"", ""count"": 10, ""duration"": 4.0 }},
  {{ ""action"": ""WAIT"", ""duration"": 1.0 }},
  {{ ""action"": ""TYPEWRITER"", ""text"": ""Your flames are borrowed. The Grimoire remembers who they belonged to."", ""speed"": 0.05 }},
  {{ ""action"": ""WAIT"", ""duration"": 0.8 }},
  {{ ""action"": ""GLITCH"", ""duration"": 0.3 }},
  {{ ""action"": ""CLEAR_TEXT"", ""duration"": 0.5 }}
]
```

### 8. Lamp Selection
Choose 1–2 lamp IDs that best fit this floor's atmosphere. Available lamps:
- RedLamp — warm crimson glow (danger, ruins, blood, fire)
- YellowLamp — amber/gold glow (ancient stone, torchlight, honey, warmth)
- GreenLamp — toxic green glow (acid pools, forest, poison, nature)
- BlueLamp — cold pale blue glow (frost, death, spectral dread)
- OceanBlueLamp — deep teal/cyan glow (ocean, water, depths)
- PinkLamp — vivid magenta glow (bubblegum, arcane, surreal)
- TechLamp — clinical white-blue glow (tech, machinery, sterile)

Return exactly 1–2 IDs in lamp_ids that match the tileset and floor name. Default guidance: dungeon/rocky/flames → RedLamp, frozen → BlueLamp, forest/acid → GreenLamp, ocean → OceanBlueLamp, techy → TechLamp, bubblegum → PinkLamp, honey → YellowLamp. Using 2 lamp types adds visual variety — prefer it when the theme supports contrast (e.g. RedLamp + YellowLamp for a fiery ruin).

### 9. Heal Scroll Placement
Heal scrolls are pickups scattered on the floor that restore 5% of the player's max HP on contact (capped — they cannot overheal). Set heal_scroll_count (integer, 0–5) based on the following:
- **Player HP**: If hp_remaining is below 40% of max, lean toward 2–4 scrolls. If above 70%, lean toward 1–2.
- **Stage difficulty**: Later stages warrant more scrolls as compensation for higher enemy stats. Add 1 scroll every ~3 stages as a baseline.
- **Playstyle**: If the player has LIFESTEAL spells they may self-sustain — reduce scroll count by 1. If they have SACRIFICE or SELF_DAMAGE corruption, add 1–2 extra scrolls.
- **Typical values**: 1 for easy/early stages, 2 for moderate, 3 for tough stages, 4–5 for brutal stages (stage 10+, low HP, high enemy count).
- Be generous — the player needs enough scrolls to stay in the fight across multiple floors.";
    }

    // ── Request JSON ──────────────────────────────────────────────────────────

    private string BuildRequestJson(string prompt)
    {
        // Build JSON manually to avoid needing a JSON library for nested structures.
        // Unity's JsonUtility can't serialize Dictionary or nested anonymous objects.
        string escapedPrompt = EscapeJson(prompt);

        return $@"{{
  ""contents"": [
    {{
      ""role"": ""user"",
      ""parts"": [{{ ""text"": ""{escapedPrompt}"" }}]
    }}
  ],
  ""tools"": [
    {{
      ""functionDeclarations"": [
        {{
          ""name"": ""generate_floor"",
          ""description"": ""Generates a complete floor manifest for the next dungeon stage."",
          ""parameters"": {{
            ""type"": ""object"",
            ""properties"": {{
              ""floor_name"":              {{ ""type"": ""string"",  ""description"": ""Creative name for this floor"" }},
              ""tileset_id"":              {{ ""type"": ""string"",  ""description"": ""One of: acid, bubblegum, dungeon, flames, forest, frozen, honey, ocean, rocky, techy"" }},
              ""palette_override"":        {{ ""type"": ""array"",   ""items"": {{ ""type"": ""string"" }}, ""description"": ""Optional array of hex color codes to tint the floor"" }},
              ""environmental_modifier"":  {{ ""type"": ""string"",  ""description"": ""Optional environmental effect ID"" }},
              ""stage_message"":           {{ ""type"": ""string"",  ""description"": ""The Chronicle's message to the player (2-4 sentences, second person, ominous/taunting)"" }},
              ""enemy_spawns"":            {{ ""type"": ""array"",   ""items"": {{ ""type"": ""object"", ""properties"": {{ ""enemy_id"": {{ ""type"": ""string"" }}, ""count"": {{ ""type"": ""integer"" }}, ""modifiers"": {{ ""type"": ""array"", ""items"": {{ ""type"": ""string"" }} }} }}, ""required"": [""enemy_id"", ""count""] }}, ""description"": ""Array of enemy spawn entries"" }},
              ""new_spell"":               {{ ""type"": ""object"",  ""properties"": {{ ""name"": {{ ""type"": ""string"" }}, ""flavor"": {{ ""type"": ""string"" }}, ""corruption_flavor"": {{ ""type"": ""string"", ""description"": ""Set ONLY for cursed spells — explains the trade-off of built-in corruption tags"" }}, ""tags"": {{ ""type"": ""array"", ""items"": {{ ""type"": ""string"" }} }}, ""tier"": {{ ""type"": ""string"", ""description"": ""One of: basic, skill, ultimate. MUST match the tier specified in the prompt."" }}, ""damage"": {{ ""type"": ""number"" }}, ""speed"": {{ ""type"": ""number"" }}, ""cooldown"": {{ ""type"": ""number"" }}, ""element"": {{ ""type"": ""string"" }}, ""is_merged"": {{ ""type"": ""boolean"" }}, ""merged_from"": {{ ""type"": ""array"", ""items"": {{ ""type"": ""string"" }} }}, ""projectile_color"": {{ ""type"": ""string"", ""description"": ""Hex color for main glow, e.g. #FF4400"" }}, ""secondary_color"": {{ ""type"": ""string"", ""description"": ""Hex color for trail gradient endpoint"" }}, ""projectile_scale"": {{ ""type"": ""number"", ""description"": ""Size multiplier 0.5-3.0"" }}, ""glow_size"": {{ ""type"": ""number"", ""description"": ""Glow radius 0.2-1.5"" }}, ""trail_length"": {{ ""type"": ""number"", ""description"": ""Trail time 0.0-0.5 seconds"" }}, ""trail_width"": {{ ""type"": ""number"", ""description"": ""Trail width 0.05-0.5"" }}, ""burst_count"": {{ ""type"": ""integer"", ""description"": ""Projectiles per cast 1-5"" }} }}, ""required"": [""name"", ""flavor"", ""tags"", ""tier"", ""damage"", ""speed"", ""cooldown"", ""projectile_color"", ""projectile_scale"", ""glow_size"", ""trail_length"", ""burst_count""], ""description"": ""The new spell given to the player. For cursed spells, include a corruption tag in tags[] and set corruption_flavor."" }},
              ""cutscene_steps"":          {{ ""type"": ""array"",   ""items"": {{ ""type"": ""object"", ""properties"": {{ ""action"": {{ ""type"": ""string"", ""description"": ""One of: TYPEWRITER, CLEAR_TEXT, FLASH, WAIT, SCREEN_TINT, PARTICLES_BURST, PARTICLES_DRIFT, TEXT_SHAKE, PULSE, GLITCH"" }}, ""text"": {{ ""type"": ""string"" }}, ""speed"": {{ ""type"": ""number"" }}, ""duration"": {{ ""type"": ""number"" }}, ""intensity"": {{ ""type"": ""number"" }}, ""color"": {{ ""type"": ""string"" }}, ""count"": {{ ""type"": ""integer"" }} }}, ""required"": [""action""] }}, ""description"": ""Dark-screen atmospheric cutscene sequence (5-12 steps). The Chronicle speaks from a dark void with particles, color washes, and dramatic text. Must end with CLEAR_TEXT."" }},
              ""lamp_ids"":                {{ ""type"": ""array"",   ""items"": {{ ""type"": ""string"" }}, ""description"": ""1–2 lamp IDs for this floor's lighting. Choose from: RedLamp, YellowLamp, GreenLamp, BlueLamp, OceanBlueLamp, PinkLamp, TechLamp. Match the floor biome and name. Always return 1–2 IDs."" }},
              ""heal_scroll_count"":       {{ ""type"": ""integer"", ""description"": ""Number of heal scrolls to place on this floor (0–5). Each heals 5% of the player's max HP on contact. Balance based on player HP, stage difficulty, and spell loadout. See prompt section 9."" }}
            }},
            ""required"": [""floor_name"", ""tileset_id"", ""stage_message"", ""enemy_spawns"", ""new_spell"", ""cutscene_steps"", ""lamp_ids"", ""heal_scroll_count""]
          }}
        }}
      ]
    }}
  ],
  ""toolConfig"": {{
    ""functionCallingConfig"": {{
      ""mode"": ""ANY"",
      ""allowedFunctionNames"": [""generate_floor""]
    }}
  }}
}}";
    }

    // ── Response parsing ──────────────────────────────────────────────────────

    private FloorManifestDTO ParseResponse(string responseJson)
    {
        try
        {
            // Extract the function call args from the Gemini response.
            // Response structure:
            //   candidates[0].content.parts[0].functionCall.args = { ... floor manifest ... }
            //
            // We find the "args" object inside "functionCall" and parse it as FloorManifestDTO.

            int functionCallIdx = responseJson.IndexOf("\"functionCall\"", StringComparison.Ordinal);
            if (functionCallIdx < 0)
            {
                Debug.LogError("[GeminiClient] No functionCall in response.");
                return null;
            }

            int argsIdx = responseJson.IndexOf("\"args\"", functionCallIdx, StringComparison.Ordinal);
            if (argsIdx < 0)
            {
                Debug.LogError("[GeminiClient] No args in functionCall.");
                return null;
            }

            // Find the opening brace of the args object
            int braceStart = responseJson.IndexOf('{', argsIdx + 6);
            if (braceStart < 0) return null;

            // Find the matching closing brace (string-aware — skip braces inside "...")
            int depth = 0;
            int braceEnd = -1;
            bool inString = false;
            for (int i = braceStart; i < responseJson.Length; i++)
            {
                char c = responseJson[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; } // skip escaped char
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) { braceEnd = i; break; } }
            }

            if (braceEnd < 0) return null;

            string argsJson = responseJson.Substring(braceStart, braceEnd - braceStart + 1);
            Debug.Log($"[GeminiClient] Extracted manifest JSON:\n{argsJson}");

            var manifest = JsonUtility.FromJson<FloorManifestDTO>(argsJson);

            // Validate critical fields — JsonUtility silently returns defaults on mismatches
            if (manifest != null)
            {
                if (string.IsNullOrEmpty(manifest.floor_name))
                    Debug.LogWarning("[GeminiClient] Parsed manifest has empty floor_name — JSON structure may not match FloorManifestDTO.");
                if (string.IsNullOrEmpty(manifest.tileset_id))
                    Debug.LogWarning("[GeminiClient] Parsed manifest has empty tileset_id.");
                if (manifest.enemy_spawns == null || manifest.enemy_spawns.Length == 0)
                    Debug.LogWarning("[GeminiClient] Parsed manifest has no enemy_spawns.");
                if (manifest.new_spell == null || string.IsNullOrEmpty(manifest.new_spell?.name))
                    Debug.LogWarning("[GeminiClient] Parsed manifest has no new_spell.");

                Debug.Log($"[GeminiClient] Parsed manifest: floor_name=\"{manifest.floor_name}\", " +
                          $"tileset_id=\"{manifest.tileset_id}\", " +
                          $"enemy_spawns={manifest.enemy_spawns?.Length ?? 0}, " +
                          $"new_spell=\"{manifest.new_spell?.name}\", " +
                          $"cutscene_steps={manifest.cutscene_steps?.Length ?? 0}");
            }

            return manifest;
        }
        catch (Exception e)
        {
            Debug.LogError($"[GeminiClient] Parse error: {e.Message}");
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length + 32);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:   sb.Append(c);      break;
            }
        }
        return sb.ToString();
    }
}
