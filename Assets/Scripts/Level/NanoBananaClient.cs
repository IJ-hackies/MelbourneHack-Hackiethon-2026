using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Generates pixel-art spell icons via Google Gemini image generation ("Nano Banana").
/// Uses the same API key and endpoint as GeminiClient, but with responseModalities: ["IMAGE"].
///
/// Setup: Add to a scene GameObject (or let StageDirector find it).
///        API key is read from GeminiClient or GEMINI_API_KEY env var.
/// </summary>
public class NanoBananaClient : MonoBehaviour
{
    public static NanoBananaClient Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private string model = "gemini-2.0-flash";
    [SerializeField] private int timeoutSeconds = 30;
    [SerializeField] private int iconSize = 64;

    private string apiKey;

    private string Endpoint =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Share API key with GeminiClient (which may have it from Inspector or env var)
        var gemini = FindAnyObjectByType<GeminiClient>();
        if (gemini != null && gemini.HasApiKey)
            apiKey = gemini.ApiKey;

        if (string.IsNullOrEmpty(apiKey))
            apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
    }

    /// <summary>
    /// Generates a pixel-art icon for a spell. Returns a Sprite via callback, or null on failure.
    /// </summary>
    public void GenerateIcon(SpellData spell, Action<Sprite> onComplete)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogWarning("[NanoBanana] No API key — skipping icon generation.");
            onComplete?.Invoke(null);
            return;
        }

        StartCoroutine(GenerateIconCoroutine(spell, onComplete));
    }

    private IEnumerator GenerateIconCoroutine(SpellData spell, Action<Sprite> onComplete)
    {
        string prompt = BuildPrompt(spell);
        string requestBody = BuildRequestJson(prompt);

        using var request = new UnityWebRequest(Endpoint, "POST");
        request.SetRequestHeader("x-goog-api-key", apiKey);
        request.SetRequestHeader("Content-Type", "application/json");
        request.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout         = timeoutSeconds;

        Debug.Log($"[NanoBanana] Generating icon for \"{spell.spellName}\"...");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[NanoBanana] Request failed: {request.error}\n{request.downloadHandler?.text}");
            onComplete?.Invoke(null);
            yield break;
        }

        string responseText = request.downloadHandler.text;
        Sprite sprite = ParseImageResponse(responseText, spell.spellName);

        if (sprite != null)
            Debug.Log($"[NanoBanana] Icon generated for \"{spell.spellName}\"");
        else
            Debug.LogWarning($"[NanoBanana] Failed to parse icon for \"{spell.spellName}\"");

        onComplete?.Invoke(sprite);
    }

    private string BuildPrompt(SpellData spell)
    {
        string tags = "";
        if (spell.tags != null)
            foreach (var t in spell.tags) tags += t + ", ";
        tags = tags.TrimEnd(',', ' ');

        string element = !string.IsNullOrEmpty(spell.element) ? spell.element : "arcane";
        string color = !string.IsNullOrEmpty(spell.projectileColor) ? spell.projectileColor : "#9966FF";

        return $"Generate a {iconSize}x{iconSize} pixel art spell icon for a 2D roguelite game. " +
               $"Spell name: \"{spell.spellName}\". Element: {element}. " +
               $"Behavior tags: {tags}. Primary color: {color}. " +
               $"Style: 16-bit retro pixel art, single centered spell icon, solid black background (#000000). " +
               $"The icon should be a simple, recognizable symbol that represents the spell's theme. " +
               $"No text, no border, no UI frame — just the spell icon on pure black.";
    }

    private string BuildRequestJson(string prompt)
    {
        string escapedPrompt = EscapeJson(prompt);

        return $@"{{
  ""contents"": [
    {{
      ""role"": ""user"",
      ""parts"": [{{ ""text"": ""{escapedPrompt}"" }}]
    }}
  ],
  ""generationConfig"": {{
    ""responseModalities"": [""TEXT"", ""IMAGE""]
  }}
}}";
    }

    private Sprite ParseImageResponse(string responseJson, string spellName)
    {
        try
        {
            // Look for inline_data block with base64 image
            int inlineIdx = responseJson.IndexOf("\"inline_data\"", StringComparison.Ordinal);
            if (inlineIdx < 0)
            {
                Debug.LogError("[NanoBanana] No inline_data in response.");
                return null;
            }

            // Find the "data" field within inline_data
            int dataIdx = responseJson.IndexOf("\"data\"", inlineIdx, StringComparison.Ordinal);
            if (dataIdx < 0)
            {
                Debug.LogError("[NanoBanana] No data field in inline_data.");
                return null;
            }

            // Extract the base64 string value
            int colonIdx = responseJson.IndexOf(':', dataIdx + 6);
            if (colonIdx < 0) return null;

            int quoteStart = responseJson.IndexOf('"', colonIdx + 1);
            if (quoteStart < 0) return null;

            int quoteEnd = responseJson.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) return null;

            string base64 = responseJson.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);

            // Decode base64 to texture
            byte[] imageBytes = Convert.FromBase64String(base64);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point; // pixel art — no smoothing
            if (!texture.LoadImage(imageBytes))
            {
                Debug.LogError("[NanoBanana] Failed to load image from bytes.");
                return null;
            }
            texture.filterMode = FilterMode.Point;

            // Create sprite from texture
            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f
            );
            sprite.name = $"SpellIcon_{spellName}";

            return sprite;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NanoBanana] Parse error: {e.Message}");
            return null;
        }
    }

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
