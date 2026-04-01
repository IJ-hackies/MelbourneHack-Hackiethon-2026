using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reads the active spell's tags and dispatches the correct cast behavior.
/// Handles all caster-level concerns: MIRRORED, GHOST_CAST, SACRIFICE, ECHOING.
/// Per-projectile behavior is delegated to ProjectileHandler and its behavior components.
/// </summary>
public class SpellExecutor : MonoBehaviour
{
    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;

    [Header("Orbital")]
    [SerializeField] private GameObject orbitalPrefab;

    private Health    playerHealth;
    private Coroutine echoRoutine;

    private void Awake()
    {
        playerHealth = GetComponent<Health>();
    }

    private void Update()
    {
        if (PauseManager.IsPaused) return;

        // Basic — held (LMB), slot 0
        if (IsKeyHeld(SettingsData.Attack))
            TryCastSlot(0);

        // Skill 1 — pressed (E), slot 1
        if (Input.GetKeyDown(SettingsData.SpellSkill))
            TryCastSlot(1);

        // Skill 2 — pressed (Q), slot 2
        if (Input.GetKeyDown(SettingsData.SpellSkill2))
            TryCastSlot(2);

        // Ultimate — pressed (X), separate gauge-based system
        if (Input.GetKeyDown(SettingsData.SpellUltimate))
            UltimateAbility.Instance?.TryFire();
    }

    private static bool IsKeyHeld(KeyCode key)
    {
        if (key == KeyCode.Mouse0) return Input.GetMouseButton(0);
        if (key == KeyCode.Mouse1) return Input.GetMouseButton(1);
        if (key == KeyCode.Mouse2) return Input.GetMouseButton(2);
        return Input.GetKey(key);
    }

    private void TryCastSlot(int slot)
    {
        Grimoire grimoire = Grimoire.Instance;
        if (grimoire == null) return;

        SpellData spell = grimoire.Loadout[slot];
        if (spell == null) return;
        if (!grimoire.IsSlotReady(slot)) return;

        grimoire.RecordCast(slot);

        // Both skill slots (1 and 2) use the Skill multiplier
        SpellTier tier = spell.tier;
        float tierMult = tier switch
        {
            SpellTier.Skill    => Mathf.Max(1.8f, 1f + spell.cooldown * 0.4f),
            SpellTier.Ultimate => Mathf.Max(4.0f, 1f + spell.cooldown * 0.3f),
            _                  => 1f,
        };

        const float DamageMult = 0.8f;
        Vector2 aimTarget = GetAimWorldPosition();

        if (slot > 0) // any non-basic slot gets cast effects + AOE burst
        {
            SFXManager.Instance?.PlaySkillCast();
            StartCoroutine(SkillCastEffects(spell));
            StartCoroutine(SpawnTierAOE(aimTarget, spell.damage * tierMult * 0.6f * DamageMult,
                                        radius: 3.5f, ProjectileHandler.GetSpellColor(spell), duration: 0.5f));
        }

        ExecuteSpell(spell, tierMult);
    }

    private void ExecuteSpell(SpellData spell, float baseDamageMult = 1f)
    {
        SFXManager.Instance?.PlayPlayerShoot();

        Vector2 aimDir = GetAimDirection();
        if (spell.HasTag(SpellTag.REVERSED_CONTROLS)) aimDir = -aimDir;

        // SACRIFICE — 15% HP cost, 2× damage multiplier
        float dmgMult = baseDamageMult;
        if (spell.HasTag(SpellTag.SACRIFICE) && playerHealth != null)
        {
            float cost = playerHealth.Current * 0.15f;
            playerHealth.TakeDamage(cost);
            dmgMult *= 2f;
        }

        if (spell.isMerged && spell.mergedSourceSpells != null && spell.mergedSourceSpells.Length > 0)
        {
            // Fire each source spell's projectile simultaneously with reduced damage
            float perSpellMult = dmgMult * 0.5f;
            foreach (var src in spell.mergedSourceSpells)
            {
                if (src == null) continue;
                if (src.HasTag(SpellTag.BEAM))      HandleBeam(src, aimDir, perSpellMult);
                if (src.HasTag(SpellTag.ORBITAL))    HandleOrbital(src);
                if (src.HasTag(SpellTag.PROJECTILE) || src.HasTag(SpellTag.CHANNELED))
                {
                    HandleProjectile(src, aimDir, damageMultiplier: perSpellMult);
                    if (src.HasTag(SpellTag.DOUBLE_HIT))
                        HandleProjectile(src, Quaternion.Euler(0, 0, 15f) * (Vector3)aimDir, damageMultiplier: perSpellMult);
                }
            }
        }
        else
        {
            if      (spell.HasTag(SpellTag.BEAM))    HandleBeam(spell, aimDir);
            else if (spell.HasTag(SpellTag.ORBITAL)) HandleOrbital(spell);
            else if (spell.HasTag(SpellTag.PROJECTILE) || spell.HasTag(SpellTag.CHANNELED))
            {
                HandleProjectile(spell, aimDir, damageMultiplier: dmgMult);
                if (spell.HasTag(SpellTag.DOUBLE_HIT))
                    HandleProjectile(spell, Quaternion.Euler(0, 0, 15f) * (Vector3)aimDir, damageMultiplier: dmgMult);
            }
        }

        // MIRRORED — identical copy fired in the opposite direction
        if (spell.HasTag(SpellTag.MIRRORED))
        {
            if (spell.isMerged && spell.mergedSourceSpells != null)
            {
                foreach (var src in spell.mergedSourceSpells)
                    if (src != null && (src.HasTag(SpellTag.PROJECTILE) || src.HasTag(SpellTag.CHANNELED)))
                        HandleProjectile(src, -aimDir, damageMultiplier: dmgMult * 0.5f);
            }
            else
                HandleProjectile(spell, -aimDir, damageMultiplier: dmgMult);
        }

        // GHOST_CAST — invisible damage copy alongside the visible spell
        if (spell.HasTag(SpellTag.GHOST_CAST))
        {
            if (spell.isMerged && spell.mergedSourceSpells != null)
            {
                foreach (var src in spell.mergedSourceSpells)
                    if (src != null && (src.HasTag(SpellTag.PROJECTILE) || src.HasTag(SpellTag.CHANNELED)))
                        HandleProjectile(src, aimDir, isGhost: true, damageMultiplier: dmgMult * 0.5f);
            }
            else
                HandleProjectile(spell, aimDir, isGhost: true, damageMultiplier: dmgMult);
        }

        // ECHOING — re-casts automatically after 3s
        if (spell.HasTag(SpellTag.ECHOING))
        {
            if (echoRoutine != null) StopCoroutine(echoRoutine);
            echoRoutine = StartCoroutine(EchoCast(spell));
        }

        // SELF_DAMAGE corruption
        if (spell.HasTag(SpellTag.SELF_DAMAGE))
            playerHealth?.TakeDamage(spell.damage * 0.2f);

        Grimoire.Instance?.RecordSpellUsed(spell);
    }

    // ── Echo ─────────────────────────────────────────────────────────────────

    private IEnumerator EchoCast(SpellData spell)
    {
        yield return new WaitForSeconds(3f);
        // Re-cast silently (no cooldown check, no echo chain)
        Vector2 aimDir = GetAimDirection();
        HandleProjectile(spell, aimDir);
    }

    // ── Movement handlers ─────────────────────────────────────────────────────

    private void HandleProjectile(SpellData spell, Vector2 dir,
                                  bool isGhost = false, float damageMultiplier = 1f)
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning("[SpellExecutor] No projectilePrefab assigned.");
            return;
        }

        int count = Mathf.Clamp(spell.burstCount, 1, 5);
        float spreadAngle = count > 1 ? 30f : 0f; // total spread across all projectiles
        float step = count > 1 ? spreadAngle / (count - 1) : 0f;
        float startAngle = -spreadAngle / 2f;

        for (int i = 0; i < count; i++)
        {
            float angle = startAngle + step * i;
            Vector2 shotDir = count > 1
                ? (Vector2)(Quaternion.Euler(0, 0, angle) * (Vector3)dir)
                : dir;

            var proj    = Instantiate(projectilePrefab, transform.position, Quaternion.identity);
            var handler = proj.GetComponent<ProjectileHandler>();
            if (handler != null)
            {
                handler.sourcePrefab = projectilePrefab;
                handler.Init(spell, shotDir, isGhost: isGhost, damageMultiplier: damageMultiplier);
            }
        }
    }

    private const int MaxOrbitals = 3;

    private void HandleOrbital(SpellData spell)
    {
        int existing = 0;
        foreach (Transform child in transform)
            if (child.GetComponent<OrbitalMotion>() != null) existing++;

        if (existing >= MaxOrbitals) return;

        GameObject orb;
        if (orbitalPrefab != null)
        {
            orb = Instantiate(orbitalPrefab, transform.position, Quaternion.identity, transform);
        }
        else
        {
            // Create orbital dynamically when no prefab is assigned
            orb = new GameObject("Orbital_" + spell.spellName);
            orb.transform.SetParent(transform, false);
            int projLayer = projectilePrefab != null
                ? projectilePrefab.layer
                : LayerMask.NameToLayer("PlayerProjectile");
            if (projLayer >= 0) orb.layer = projLayer;

            var sr = orb.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCircleSprite();
            sr.sortingOrder = 5;

            var rb = orb.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var col = orb.AddComponent<CircleCollider2D>();
            col.radius = 0.3f;
            col.isTrigger = true;
        }

        var orbital = orb.GetComponent<OrbitalMotion>() ?? orb.AddComponent<OrbitalMotion>();
        orbital.Init(spell, existing);
    }

    private void HandleBeam(SpellData spell, Vector2 dir, float damageMultiplier = 1f)
    {
        const float BeamRange = 15f;
        int wallMask = 1 << 9; // Wall layer

        // Find where the beam ends (wall hit or max range)
        Vector2 origin = transform.position;
        RaycastHit2D wallHit = Physics2D.Raycast(origin, dir, BeamRange, wallMask);
        float beamDist = wallHit.collider != null ? wallHit.distance : BeamRange;
        Vector2 beamEnd = origin + dir * beamDist;

        float dmg = spell.damage * damageMultiplier * 0.8f; // global 20% reduction

        // Deal damage to enemies along the beam
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, beamDist, ProjectileHandler.EnemyMask);

        foreach (var hit in hits)
        {
            var h = hit.collider.GetComponent<Health>();
            if (h == null || h.IsDead) continue;

            h.TakeDamage(dmg);
            UltimateAbility.Instance?.RegisterHit();
            SessionLogger.Instance?.RecordDamageDealt(spell.element, dmg);

            if (spell.HasTag(SpellTag.LIFESTEAL))
                playerHealth?.Heal(dmg * 0.3f);

            ApplyBeamStatusEffects(spell, hit.collider.gameObject, hit.point);

            if (!spell.HasTag(SpellTag.PIERCE)) break;
        }

        // Spawn a visible beam line
        SpawnBeamVisual(origin, beamEnd, spell);
    }

    private void ApplyBeamStatusEffects(SpellData spell, GameObject enemyObj, Vector2 hitPoint)
    {
        var status = enemyObj.GetComponent<StatusEffectHandler>()
                  ?? enemyObj.AddComponent<StatusEffectHandler>();

        if (spell.HasTag(SpellTag.BURN))     status.ApplyBurn(spell.damage);
        if (spell.HasTag(SpellTag.FREEZE))   status.ApplyFreeze();
        if (spell.HasTag(SpellTag.SLOW))     status.ApplySlow();
        if (spell.HasTag(SpellTag.STUN))     status.ApplyStun();
        if (spell.HasTag(SpellTag.POISON))   status.ApplyPoison(spell.damage);
        if (spell.HasTag(SpellTag.BLEED))    status.ApplyBleed(spell.damage);
        if (spell.HasTag(SpellTag.ROOT))     status.ApplyRoot();
        if (spell.HasTag(SpellTag.WEAKNESS)) status.ApplyWeakness();
        if (spell.HasTag(SpellTag.CURSE))    status.ApplyCurse();
        if (spell.HasTag(SpellTag.BLIND))    status.ApplyBlind();

        if (spell.HasTag(SpellTag.PUSH))
        {
            var rb        = enemyObj.GetComponent<Rigidbody2D>();
            Vector2 away  = ((Vector2)enemyObj.transform.position - (Vector2)transform.position).normalized;
            rb?.AddForce(away * 8f, ForceMode2D.Impulse);
        }

        if (spell.HasTag(SpellTag.PULL))
        {
            var rb        = enemyObj.GetComponent<Rigidbody2D>();
            Vector2 toward = ((Vector2)transform.position - (Vector2)enemyObj.transform.position).normalized;
            rb?.AddForce(toward * 10f, ForceMode2D.Impulse);
        }

        if (spell.HasTag(SpellTag.AOE_BURST))
        {
            Collider2D[] aoeHits = Physics2D.OverlapCircleAll(hitPoint, 3f, ProjectileHandler.EnemyMask);
            foreach (var aoe in aoeHits)
            {
                var aoeH = aoe.GetComponent<Health>();
                if (aoeH != null && !aoeH.IsDead && aoe.gameObject != enemyObj)
                    aoeH.TakeDamage(spell.damage * 0.5f);
            }
        }
    }

    // ── Tier AOE + visual effects ─────────────────────────────────────────────

    private IEnumerator SkillCastEffects(SpellData spell)
    {
        Color spellCol = ProjectileHandler.GetSpellColor(spell);
        Vector2 origin = transform.position;

        // Screen flash
        var flashGO = new GameObject("SkillFlash");
        var cv = flashGO.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 997;
        var img = flashGO.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(spellCol.r, spellCol.g, spellCol.b, 0.5f);
        img.raycastTarget = false;

        // Radial particle burst from player (12 sparks)
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI * 2f / 12f;
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var part = new GameObject("SkillSpark");
            part.transform.position = origin;
            var sr = part.AddComponent<SpriteRenderer>();
            sr.sprite = GetPixelSprite();
            sr.sortingLayerName = "Entities";
            sr.sortingOrder = 301;
            sr.color = spellCol;
            part.transform.localScale = Vector3.one * 0.22f;
            StartCoroutine(DriftParticle(part, dir * 6f, spellCol, 0.28f));
        }

        // Inner fast ring from player
        StartCoroutine(SpawnSkillRing(origin, spellCol, 2.5f, 0.22f));

        float elapsed = 0f;
        while (elapsed < 0.22f)
        {
            elapsed += Time.unscaledDeltaTime;
            img.color = new Color(spellCol.r, spellCol.g, spellCol.b,
                Mathf.Lerp(0.5f, 0f, elapsed / 0.22f));
            yield return null;
        }
        if (flashGO != null) Destroy(flashGO);
    }

    private IEnumerator SpawnSkillRing(Vector2 center, Color color, float maxRadius, float duration)
    {
        var go = new GameObject("SkillRing");
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace    = true;
        lr.loop             = true;
        lr.material         = HitEffectSpawner.GetAdditiveParticleMaterial();
        lr.sortingLayerName = "Entities";
        lr.sortingOrder     = 302;

        const int segs = 32;
        lr.positionCount = segs;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float frac  = t / duration;
            float r     = maxRadius * frac;
            float alpha = Mathf.Lerp(1f, 0f, frac);
            float w     = Mathf.Lerp(0.18f, 0.03f, frac);

            Color c = new Color(color.r, color.g, color.b, alpha);
            lr.startColor = c; lr.endColor = c;
            lr.startWidth = w; lr.endWidth = w;

            for (int i = 0; i < segs; i++)
            {
                float ang = i * Mathf.PI * 2f / segs;
                lr.SetPosition(i, new Vector3(center.x + Mathf.Cos(ang) * r,
                                              center.y + Mathf.Sin(ang) * r, 0f));
            }
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    /// <summary>
    /// Expanding ring visual + instant AoE damage at <paramref name="center"/>.
    /// </summary>
    private IEnumerator SpawnTierAOE(Vector2 center, float damage, float radius, Color color, float duration)
    {
        // Damage all enemies in the radius
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius, ProjectileHandler.EnemyMask);
        foreach (var hit in hits)
        {
            var h = hit.GetComponent<Health>();
            if (h != null && !h.IsDead)
            {
                h.TakeDamage(damage);
                UltimateAbility.Instance?.RegisterHit();
                if (playerHealth != null) // lifesteal passthrough
                    playerHealth.Heal(damage * 0.05f);
            }
        }

        // Inner burst particles
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI * 2f / 8f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var part = new GameObject("AOEPart");
            part.transform.position = center;
            var sr = part.AddComponent<SpriteRenderer>();
            sr.sprite = GetPixelSprite();
            sr.sortingLayerName = "Entities";
            sr.sortingOrder = 299;
            sr.color = color;
            part.transform.localScale = Vector3.one * 0.18f;
            StartCoroutine(DriftParticle(part, dir * 4f, color, duration * 0.7f));
        }

        // Expanding ring
        var go = new GameObject("AOERing");
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace    = true;
        lr.loop             = true;
        lr.material         = HitEffectSpawner.GetAdditiveParticleMaterial();
        lr.sortingLayerName = "Entities";
        lr.sortingOrder     = 300;

        const int segs = 40;
        lr.positionCount = segs;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float frac  = t / duration;
            float r     = radius * frac;
            float alpha = Mathf.Lerp(1f, 0f, frac);
            float w     = Mathf.Lerp(0.22f, 0.04f, frac);

            Color c = new Color(color.r, color.g, color.b, alpha);
            lr.startColor = c; lr.endColor = c;
            lr.startWidth = w; lr.endWidth = w;

            for (int i = 0; i < segs; i++)
            {
                float ang = i * Mathf.PI * 2f / segs;
                lr.SetPosition(i, new Vector3(center.x + Mathf.Cos(ang) * r,
                                              center.y + Mathf.Sin(ang) * r, 0f));
            }
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private static IEnumerator DriftParticle(GameObject go, Vector2 velocity, Color color, float lifetime)
    {
        float t = 0f;
        var sr = go.GetComponent<SpriteRenderer>();
        while (t < lifetime && go != null)
        {
            t += Time.deltaTime;
            go.transform.position += (Vector3)(velocity * Time.deltaTime);
            if (sr != null) sr.color = new Color(color.r, color.g, color.b,
                Mathf.Lerp(1f, 0f, t / lifetime));
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    // ── Ultimate screen effects ───────────────────────────────────────────────

    private IEnumerator UltimateScreenEffects()
    {
        // Gold flash overlay
        var flashGO = new GameObject("UltimateFlash");
        var flashCanvas = flashGO.AddComponent<Canvas>();
        flashCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        flashCanvas.sortingOrder = 999;
        flashGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        var flashImg = flashGO.AddComponent<UnityEngine.UI.Image>();
        flashImg.color = new Color(1f, 0.85f, 0.2f, 0.65f);
        flashImg.raycastTarget = false;

        // Slow motion
        Time.timeScale = 0.12f;

        // Lightning strikes
        for (int i = 0; i < 4; i++)
            SpawnLightningStrike();

        // Fade flash over 0.2 real seconds
        float elapsed = 0f;
        const float flashDur = 0.2f;
        while (elapsed < flashDur)
        {
            elapsed += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(0.65f, 0f, elapsed / flashDur);
            if (flashImg != null) flashImg.color = new Color(1f, 0.85f, 0.2f, a);
            yield return null;
        }
        if (flashGO != null) Destroy(flashGO);

        // Hold slowmo for a beat, then restore
        yield return new WaitForSecondsRealtime(0.25f);
        if (!PauseManager.IsPaused)
            Time.timeScale = 1f;
    }

    private void SpawnLightningStrike()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Random horizontal position
        float normX = Random.Range(0.1f, 0.9f);
        Vector3 topScreen    = cam.ViewportToWorldPoint(new Vector3(normX, 1.1f, cam.nearClipPlane + 1f));
        Vector3 bottomScreen = cam.ViewportToWorldPoint(new Vector3(normX + Random.Range(-0.15f, 0.15f), -0.1f, cam.nearClipPlane + 1f));
        topScreen.z    = 0f;
        bottomScreen.z = 0f;

        var go = new GameObject("Lightning");
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace  = true;
        lr.positionCount  = 2;
        lr.SetPosition(0, topScreen);
        lr.SetPosition(1, bottomScreen);
        lr.startWidth     = 0.12f;
        lr.endWidth       = 0.05f;
        Color bolt = new Color(0.7f, 0.85f, 1f, 1f);
        lr.startColor     = bolt;
        lr.endColor       = bolt;
        lr.material       = HitEffectSpawner.GetAdditiveParticleMaterial();
        lr.sortingLayerName = "Entities";
        lr.sortingOrder   = 998;

        StartCoroutine(FadeLightning(lr, bolt));
    }

    private IEnumerator FadeLightning(LineRenderer lr, Color c)
    {
        float t = 0f;
        const float dur = 0.25f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(1f, 0f, t / dur);
            lr.startColor = new Color(c.r, c.g, c.b, a);
            lr.endColor   = new Color(c.r, c.g, c.b, a);
            yield return null;
        }
        if (lr != null) Destroy(lr.gameObject);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Vector2 GetAimDirection()
    {
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        return ((Vector2)(mouseWorld - transform.position)).normalized;
    }

    private Vector2 GetAimWorldPosition()
    {
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0f;
        return mouseWorld;
    }

    private void SpawnBeamVisual(Vector2 start, Vector2 end, SpellData spell)
    {
        var beamObj = new GameObject("BeamVisual");
        var lr = beamObj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);

        Color beamColor = ProjectileHandler.GetSpellColor(spell);

        lr.startWidth = 0.25f;
        lr.endWidth = 0.15f;
        lr.startColor = beamColor;
        lr.endColor = beamColor;

        // Use same URP-safe material pattern as HitEffectSpawner
        lr.material = HitEffectSpawner.GetAdditiveParticleMaterial();
        lr.sortingLayerName = "Entities";
        lr.sortingOrder = 200;

        // Fade out over 0.15s
        StartCoroutine(FadeBeam(lr, beamColor, 0.15f));
    }

    private IEnumerator FadeBeam(LineRenderer lr, Color color, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, t / duration);
            Color c = new Color(color.r, color.g, color.b, a);
            lr.startColor = c;
            lr.endColor = c;
            lr.startWidth = Mathf.Lerp(0.25f, 0.05f, t / duration);
            lr.endWidth = Mathf.Lerp(0.15f, 0.02f, t / duration);
            yield return null;
        }
        Destroy(lr.gameObject);
    }

    private static Sprite _pixelSprite;
    private static Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return _pixelSprite;
    }

    private static Sprite circleSprite;
    private static Sprite CreateCircleSprite()
    {
        if (circleSprite != null) return circleSprite;
        int size = 16;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        float center = (size - 1) / 2f;
        float radiusSq = center * center;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center, dy = y - center;
                tex.SetPixel(x, y, dx * dx + dy * dy <= radiusSq
                    ? Color.white : Color.clear);
            }
        tex.Apply();
        circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f), size);
        return circleSprite;
    }
}
