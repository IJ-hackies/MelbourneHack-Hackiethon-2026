# Enemy Scaling Reference

Stats and spawn counts are scaled by stage number. This document defines what is fixed vs. scalable for each enemy, and what the scaling system may touch at runtime.

---

## Fixed for ALL enemies

Never modified by the scaling system:

| Stat | Reason |
|---|---|
| `AttackRange` | Identity stat — enforced read-only in `EnemyBase`. Changing it turns a melee enemy into a ranged one, not a harder version. |
| `AttackAnimDuration`, `DamageHitFrame`, `HitEffectDelay` | Tied to animation clip lengths. Changing them desyncs the damage moment from the animation. |
| `WanderRadius` | Spatial behavior tied to map/room size, not difficulty. |
| `MinWanderTime`, `MaxWanderTime`, `MinIdleTime`, `MaxIdleTime` | Minor flavor. Scaling these buys almost nothing meaningful. |

---

## Scalable for ALL enemies

From `EnemyBase` and the `Health` component — applies to every enemy type:

| Stat | Direction | How to set | Notes |
|---|---|---|---|
| `Health.MaxHealth` | ↑ | `health.SetMaxHealth(value)` | Already supports proportional rescaling of current HP |
| `MoveSpeed` | ↑ | `enemy.MoveSpeed = value` | Cap it — enemies become unreadable above ~5–6 |
| `AttackDamage` | ↑ | `enemy.AttackDamage = value` | Core damage ramp |
| `AttackCooldown` | ↓ | `enemy.AttackCooldown = value` | Attacks more frequently. Hard floor of `0.05f` enforced in setter |

---

## Per-enemy unique scalable stats

### EvilPaladin — `MeleeChaseAI`

> `EvilPaladinAI.cs` is deprecated. EvilPaladin now uses `MeleeChaseAI` directly (Walk Prefix: `ep_walk`, Attack Prefix: `ep_punch`).

No unique scalables. All scaling is through the shared stats above.

---

### Bear — `MeleeChaseAI`

| Stat | Direction | How to set | Notes |
|---|---|---|---|
| `BleedDuration` | ↑ | `bear.BleedDuration = value` | How long the bleed DoT persists after a hit. Longer = more sustained pressure |
| `BleedDamageMultiplier` | **FIXED** | — | Always 10% of `AttackDamage` per tick. Changing this makes it feel like a different enemy |

---

### Wizard (Fire & Ice) — `WizardAI`

| Stat | Direction | How to set | Notes |
|---|---|---|---|
| `ProjectileSpeed` | ↑ | `wizard.ProjectileSpeed = value` | Harder to dodge at high stages |
| `HomingStrength` | ↑ | `wizard.HomingStrength = value` | Turn rate in radians/sec. Higher = fireballs curve harder toward the player |
| `BurnDuration` *(Fire only)* | ↑ | `wizard.BurnDuration = value` | How long the burn DoT lasts on hit. Scale slowly — runs alongside base attack damage |
| `SlowDuration` *(Ice only)* | ↑ | `wizard.SlowDuration = value` | Longer freeze. Scale slowly — disproportionately punishing |
| `SlowMultiplier` *(Ice only)* | **FIXED** | — | The ice wizard's identity (e.g. `0.65`). Changing it makes it feel like a different enemy |
| `StopHomingRadius` | **FIXED** | — | Distance at which homing locks off. Changing it alters the attack's feel and readability |
| `BurnDamagePercent` *(Fire only)* | **FIXED** | — | Always a fraction of `AttackDamage`. Identity stat like `SlowMultiplier` |

---

### Ghost — `GhostAI`

| Stat | Direction | How to set | Notes |
|---|---|---|---|
| `ProjectileSpeed` | ↑ | `ghost.ProjectileSpeed = value` | Homing goo is harder to escape |
| `ZoneDuration` | ↑ | `ghost.ZoneDuration = value` | Freeze zone lingers longer |
| `ZoneRadius` | ↑ | `ghost.ZoneRadius = value` | Larger area denial |
| `ConjureDuration` | **FIXED** | — | Tied to the conjure animation timing. Changing it desyncs the visual |

---

### DragonNewt — `DragonNewtAI`

| Stat | Direction | How to set | Notes |
|---|---|---|---|
| `ShockwaveCount` | ↑ | `dn.ShockwaveCount = value` | More projectiles in the ring = fewer gaps to dodge through. Integers only. |
| `ShockwaveSpeed` | ↑ | `dn.ShockwaveSpeed = value` | Shockwaves harder to outrun |
| `ShockwaveLifetime` | ↑ | `dn.ShockwaveLifetime = value` | Larger effective radius before expiry |
| `JumpAnimSpeed` | **FIXED** | — | Drives timing calculations in `Start()`. Changing at runtime breaks landing sync. |

---

### Alien — `AlienAI`

| Stat | Direction | How to set | Notes |
|---|---|---|---|
| `ProjectileSpeed` | ↑ | `alien.ProjectileSpeed = value` | Only meaningful unique stat |

> Alien projectiles apply a 2-second poison on hit. This is hardcoded in `Attack()` and is not runtime-scalable.

---

### Vampire — `VampireAI`

No unique scalables. `TelegraphDuration` is derived automatically as `AttackCooldown * 0.55f` — it scales correctly when `AttackCooldown` is lowered. No manual intervention needed.

> Vampire extends `AlienAI` but overrides `Attack()` to use `BloodBeamProjectile` instead of a fireball. `ProjectileSpeed` from `AlienAI` has no effect on the Vampire.

---

## Enemy-received status effects (`StatusEffectHandler`)

`StatusEffectHandler` is added to enemy prefabs and is the target of player spell effects. These are not scaled by the difficulty system — they are properties of the player's spells.

| Effect | What it does |
|---|---|
| `ApplyBurn(damage, duration)` | 10% of damage per tick every 0.5s. Refreshes on reapply. Orange tint. |
| `ApplyFreeze(duration)` | Stops movement, triples attack cooldown. Supersedes slow and stun. Icy blue tint. |
| `ApplySlow(duration)` | Reduces move speed to 40%. Skipped if frozen or stunned. Pale blue tint. |
| `ApplyStun(duration)` | Stops movement, disables attacking. Skipped if frozen. Yellow tint. |
| `ApplyPoison(damage, duration)` | 5% of damage × stacks per second. Stacks up to 3×. Green tint. |

---

## Spawn count

Enemy count per floor is controlled by Gemini via `enemy_spawns[].count` in the Floor Manifest. It is not a per-instance stat and is not handled by the runtime scaling system.

---

## Suggested scaling formula

A simple starting curve, tuneable per stat:

```
scaledValue = baseStat * (1 + stage * multiplier)
```

| Stat | Suggested multiplier |
|---|---|
| `MaxHealth` | `0.10` (10% per stage) |
| `AttackDamage` | `0.08` (8% per stage) |
| `AttackCooldown` | `-0.04` (reduce by 4% per stage, floored at `0.05`) |
| `MoveSpeed` | `0.05` (5% per stage, cap ~5.5) |
| `ProjectileSpeed` | `0.06` |
| `HomingStrength` | `0.05` |
| `BurnDuration` | `0.07` |
| `BleedDuration` | `0.08` |
| `ZoneDuration` | `0.07` |
| `ZoneRadius` | `0.05` |
| `ShockwaveCount` | `+1 every 3 stages` (integer step) |
| `ShockwaveSpeed` | `0.06` |
| `ShockwaveLifetime` | `0.05` |
| `SlowDuration` | `0.06` |
