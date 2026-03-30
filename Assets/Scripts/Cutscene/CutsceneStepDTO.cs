using System;

/// <summary>
/// One step in a Gemini-generated cutscene sequence.
/// Deserialized from the cutscene_steps array in the Floor Manifest.
///
/// Each step has an action type and a set of optional parameters.
/// The CutscenePlayer interprets these at runtime.
/// </summary>
[Serializable]
public class CutsceneStepDTO
{
    /// <summary>
    /// Action type. One of:
    ///   TYPEWRITER               — reveal text character by character (text, speed)
    ///   CLEAR_TEXT               — fade out any visible text (duration)
    ///   FLASH                    — quick colored flash (color)
    ///   WAIT                     — pause for duration seconds
    ///   SCREEN_TINT              — shift the background overlay color (color, duration)
    ///   PARTICLES_BURST          — explosion of particles from center (color, count)
    ///   PARTICLES_DRIFT          — ambient drifting motes (color, count, duration)
    ///   TEXT_SHAKE               — shake the displayed text briefly (intensity, duration)
    ///   PULSE                    — rhythmic brightness pulse on the overlay (intensity, count, duration)
    ///   GLITCH                   — brief visual glitch/flicker (duration)
    /// </summary>
    public string action;

    // ── Parameters (optional — only the relevant ones are set per action) ────

    public string text;            // TYPEWRITER: the text to display
    public float  speed;           // TYPEWRITER: seconds per character (0.04–0.08 typical)
    public float  duration;        // most actions: how long the effect lasts
    public float  intensity;       // TEXT_SHAKE, PULSE: strength 0–1
    public string color;           // FLASH, SCREEN_TINT, PARTICLES: hex color
    public int    count;           // PARTICLES_BURST/DRIFT: particle count; PULSE: number of pulses
}
