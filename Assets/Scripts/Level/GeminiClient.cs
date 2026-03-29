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
    [SerializeField] private string apiKey;
    [SerializeField] private string model = "gemini-2.5-flash-preview-05-20";

    [Header("Timeouts")]
    [SerializeField] private int timeoutSeconds = 60;

    private string Endpoint =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (string.IsNullOrEmpty(apiKey))
            apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sends session log to Gemini and returns a FloorManifestDTO via callback.
    /// On failure, callback receives null and error is logged.
    /// </summary>
    public void GenerateFloor(string sessionLogJson, int nextStageNumber,
                              Action<FloorManifestDTO> onComplete)
    {
        StartCoroutine(GenerateFloorCoroutine(sessionLogJson, nextStageNumber, onComplete));
    }

    /// <summary>Returns true if an API key is configured.</summary>
    public bool HasApiKey => !string.IsNullOrEmpty(apiKey);

    /// <summary>Returns the configured API key (for sharing with NanoBananaClient).</summary>
    public string ApiKey => apiKey;

    // ── Coroutine ────────────────────────────────────────────────────────────

    private IEnumerator GenerateFloorCoroutine(string sessionLogJson, int nextStageNumber,
                                               Action<FloorManifestDTO> onComplete)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[GeminiClient] No API key set. Set GEMINI_API_KEY env var or assign in Inspector.");
            onComplete?.Invoke(null);
            yield break;
        }

        string prompt = BuildPrompt(sessionLogJson, nextStageNumber);
        string requestBody = BuildRequestJson(prompt);

        using var request = new UnityWebRequest(Endpoint, "POST");
        request.SetRequestHeader("x-goog-api-key", apiKey);
        request.SetRequestHeader("Content-Type", "application/json");
        request.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestBody));
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

    // ── Prompt ────────────────────────────────────────────────────────────────

    private string BuildPrompt(string sessionLogJson, int nextStageNumber)
    {
        return $@"You are the Chronicle — the sentient, omniscient narrator of an ever-changing dungeon in a 2D roguelite game called ""Everchanging Grimoire"".

The player just completed stage {nextStageNumber - 1}. Here is their session log:
{sessionLogJson}

Generate the next floor (stage {nextStageNumber}) by calling the generate_floor function. Your goal:

1. **Adapt to the player**: Counter their dominant strategy. If they use fire, spawn fire-immune enemies. If they're aggressive, use ranged enemies and open arenas. If passive, use fast rushdown enemies.

2. **Difficulty scaling**: Stage {nextStageNumber} should be harder than the last. Scale enemy counts and use modifiers. Formula guide: base_stat * (1 + {nextStageNumber} * 0.08).

3. **New spell**: Create ONE new spell with a creative name and flavor text. Compose behavior from these tags (pick 2-5):
   Movement (exactly ONE): PROJECTILE, ORBITAL, BEAM, CHANNELED
   Trajectory: HOMING, WALL_BOUNCE, REFLECTING, PIERCE_WALLS, SPIRAL, STUTTER_MOTION, BOOMERANG, SURFACE_CRAWLING, SKIPPING, DELAYED_ARC, SENTIENT, DELAYED, PHASING, PERSISTENT
   Caster: DOUBLE_HIT, MIRRORED, GHOST_CAST, SACRIFICE, ECHOING
   On-impact: CHAIN, FRAGMENTING, AOE_BURST, PIERCE, LIFESTEAL, PUSH, PULL, DETONATING, LINGERING, SWAPPING, CONTAGIOUS, BURROWING, TETHERED
   Status (zero or one): SLOW, BURN, FREEZE, STUN, POISON, BLEED, ROOT, WEAKNESS, CURSE, BLIND
   Corruption (maximum 1): SELF_DAMAGE, ENEMY_HOMING, REVERSED_CONTROLS

   The spell should be thematically interesting and counter or complement the player's style.
   If creating a cursed spell, set corruption_flavor to explain the trade-off.

3b. **Spell visuals**: Make each spell LOOK unique and powerful. Set these visual parameters:
   - projectile_color: hex color for the main glow (e.g. ""#FF4400"" for fiery orange, ""#00FFCC"" for spectral cyan)
   - secondary_color: hex color for trail/gradient endpoint (different from primary for richer look)
   - projectile_scale: 0.5 (tiny shard) to 3.0 (massive orb). Scale up for powerful or slow spells.
   - glow_size: 0.2 (subtle) to 1.5 (blazing aura). Bigger = more imposing.
   - trail_length: 0.0 (no trail) to 0.5 (long streaking tail). Fast spells should have trails.
   - trail_width: 0.05 (thin wisp) to 0.5 (thick ribbon).
   - burst_count: 1 (single shot) to 5 (shotgun spread). More = wider spread pattern.
   Be creative! A poison spell might be small, bright green with a long thin trail. A void cannon might be huge, dark purple with a wide short trail and burst_count 1. A frost barrage might be small icy blue with burst_count 4.

4. **Spell corruption**: Optionally corrupt 0-2 of the player's existing spells by adding/removing tags. Use this to weaken their strongest tools. Only corrupt spells listed in spells_used.

5. **stage_message**: Write 2-4 sentences in second person as the Chronicle. Ominous, taunting, or poetic — never neutral. Reference the player's combat_style, primary_element, and most_damage_taken_from.

6. **Tileset**: Pick from: acid, bubblegum, dungeon, flames, forest, frozen, honey, ocean, rocky, techy. Match the floor's theme.

7. **Enemy spawns**: Use these enemy IDs: melee_charger, ranged_sentinel, alien, dragon_newt, evil_paladin, ghost, vampire, wizard. Modifiers: armored, berserk, regenerating. Total enemies should be {4 + nextStageNumber} to {6 + nextStageNumber * 2} (capped at 30).";
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
              ""new_spell"":               {{ ""type"": ""object"",  ""properties"": {{ ""name"": {{ ""type"": ""string"" }}, ""flavor"": {{ ""type"": ""string"" }}, ""corruption_flavor"": {{ ""type"": ""string"" }}, ""tags"": {{ ""type"": ""array"", ""items"": {{ ""type"": ""string"" }} }}, ""damage"": {{ ""type"": ""number"" }}, ""speed"": {{ ""type"": ""number"" }}, ""cooldown"": {{ ""type"": ""number"" }}, ""element"": {{ ""type"": ""string"" }}, ""is_merged"": {{ ""type"": ""boolean"" }}, ""merged_from"": {{ ""type"": ""array"", ""items"": {{ ""type"": ""string"" }} }}, ""projectile_color"": {{ ""type"": ""string"", ""description"": ""Hex color for main glow, e.g. #FF4400"" }}, ""secondary_color"": {{ ""type"": ""string"", ""description"": ""Hex color for trail gradient endpoint"" }}, ""projectile_scale"": {{ ""type"": ""number"", ""description"": ""Size multiplier 0.5-3.0"" }}, ""glow_size"": {{ ""type"": ""number"", ""description"": ""Glow radius 0.2-1.5"" }}, ""trail_length"": {{ ""type"": ""number"", ""description"": ""Trail time 0.0-0.5 seconds"" }}, ""trail_width"": {{ ""type"": ""number"", ""description"": ""Trail width 0.05-0.5"" }}, ""burst_count"": {{ ""type"": ""integer"", ""description"": ""Projectiles per cast 1-5"" }} }}, ""required"": [""name"", ""flavor"", ""tags"", ""damage"", ""speed"", ""cooldown"", ""projectile_color"", ""projectile_scale"", ""glow_size"", ""trail_length"", ""burst_count""], ""description"": ""The new spell given to the player"" }},
              ""corrupted_spells"":        {{ ""type"": ""array"",   ""items"": {{ ""type"": ""object"", ""properties"": {{ ""spell_name"": {{ ""type"": ""string"" }}, ""added_tags"": {{ ""type"": ""array"", ""items"": {{ ""type"": ""string"" }} }}, ""removed_tags"": {{ ""type"": ""array"", ""items"": {{ ""type"": ""string"" }} }}, ""new_flavor"": {{ ""type"": ""string"" }} }}, ""required"": [""spell_name""] }}, ""description"": ""Existing spells to corrupt"" }}
            }},
            ""required"": [""floor_name"", ""tileset_id"", ""stage_message"", ""enemy_spawns"", ""new_spell""]
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

            // Find the matching closing brace
            int depth = 0;
            int braceEnd = -1;
            for (int i = braceStart; i < responseJson.Length; i++)
            {
                if (responseJson[i] == '{') depth++;
                else if (responseJson[i] == '}') { depth--; if (depth == 0) { braceEnd = i; break; } }
            }

            if (braceEnd < 0) return null;

            string argsJson = responseJson.Substring(braceStart, braceEnd - braceStart + 1);
            Debug.Log($"[GeminiClient] Extracted manifest JSON:\n{argsJson}");

            return JsonUtility.FromJson<FloorManifestDTO>(argsJson);
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
