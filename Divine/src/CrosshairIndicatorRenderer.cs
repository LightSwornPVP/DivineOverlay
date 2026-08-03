using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace Divine;

public sealed class CrosshairIndicatorRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly DivineConfig config;
    private readonly LoadedTexture whiteTexture;
    private readonly WireframeCube targetWireframe;
    private LoadedTexture? targetInfoTexture;
    private LoadedTexture? healthTagTexture;
    private LoadedTexture? awarenessTexture;
    private string targetInfoText = "";
    private string healthTagText = "";
    private string awarenessText = "";
    private readonly float originalMinBrightness;
    private Entity? assistedTarget;
    private Entity? focusTarget;
    private Entity? lastTarget;
    private long assistedTargetSeenAtMs;
    private long lastSoundCueAtMs;
    private long lastDamageFlashAtMs;
    private long targetAcquiredAtMs;
    private int closeMissFrames;
    private bool divineSightApplied;
    private float lastDivineSightBrightness = -1f;
    private AwarenessSnapshot cachedAwareness = new();
    private Entity? cachedPriorityTarget;
    private long nextAwarenessScanAtMs;

    public double RenderOrder => 1.05;
    public int RenderRange => 0;

    public CrosshairIndicatorRenderer(ICoreClientAPI capi, DivineConfig config)
    {
        this.capi = capi;
        this.config = config;
        originalMinBrightness = capi.Settings.Float["minbrightness"];
        whiteTexture = new LoadedTexture(capi, 0, 1, 1);
        capi.Render.LoadOrUpdateTextureFromRgba(new[] { unchecked((int)0xffffffff) }, false, 0, ref whiteTexture);
        targetWireframe = WireframeCube.CreateUnitCube(capi, ColorUtil.WhiteArgb);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (capi.HideGuis || capi.World?.Player == null) return;

        if (stage == EnumRenderStage.AfterFinalComposition)
        {
            DrawWorldTargetOverlay();
            return;
        }

        DrawDivineSight();

        if (!config.CrosshairEnabled) return;
        if (config.CombatOnly && !HasHeldItem()) return;

        RefreshAwarenessIfNeeded();

        EntitySelection? selection = capi.World.Player.CurrentEntitySelection;
        bool attackHeld = config.LeftClickFocus && IsLeftMouseHeld();
        Entity? target = selection?.Entity;
        bool isDirectTarget = IsValidTarget(target);
        if (isDirectTarget)
        {
            assistedTarget = target;
            if (attackHeld)
            {
                focusTarget = target;
            }

            assistedTargetSeenAtMs = capi.ElapsedMilliseconds;
        }
        else
        {
            if (!attackHeld)
            {
                focusTarget = null;
            }

            target = attackHeld && IsValidTarget(focusTarget)
                ? focusTarget
                : GetAssistedTarget() ?? cachedPriorityTarget;
        }

        AwarenessSnapshot awareness = cachedAwareness;
        DrawAwareness(awareness);

        if (!IsValidTarget(target)) return;

        TrackTarget(target!);
        double range = GetMeleeRange();
        double distance = DistanceToTarget(target!);
        bool inRange = distance <= range;
        bool barelyInRange = distance <= range + 0.75;
        bool lowHealth = config.ShowLowHealthHighlight && IsLowHealth(target!);
        bool lowStamina = config.StaminaWarning && IsLowStamina();
        bool focusActive = attackHeld && IsValidTarget(focusTarget) && target == focusTarget;
        bool aimDrift = focusActive && capi.World.Player.CurrentEntitySelection?.Entity != target;
        Vec4f color = GetTargetColor(target!, distance, range, inRange, barelyInRange, lowHealth, lowStamina);

        if (inRange && config.SoundCueEnabled) PlayReadySound();

        DrawCrosshair(color, !isDirectTarget, inRange);
        if (config.TargetBrackets) DrawTargetBrackets(color, inRange);
        if (config.SwingTimingCue) DrawSwingTimingCue(inRange, barelyInRange);
        if (config.AimDriftWarning && aimDrift) DrawAimDriftWarning();
        if (config.SnapIndicator && aimDrift) DrawSnapIndicator(target!);
        DrawTargetInfo(target!, distance, range, awareness);
        if (config.ShowTargetHealthTag) DrawTargetHealthTag(target!, color);
    }

    public void Dispose()
    {
        if (divineSightApplied)
        {
            capi.Settings.Float["minbrightness"] = originalMinBrightness;
        }
        whiteTexture.Dispose();
        targetWireframe.Dispose();
        targetInfoTexture?.Dispose();
        healthTagTexture?.Dispose();
        awarenessTexture?.Dispose();
    }

    private bool IsValidTarget(Entity? target)
    {
        if (target == null || !target.Alive) return false;
        if (target == capi.World.Player.Entity) return false;
        if (config.PlayersOnly && target is not EntityPlayer) return false;
        if (config.HostileOnly && !LooksHostile(target)) return false;
        return target is EntityAgent || target is EntityPlayer;
    }

    private bool NeedsAwarenessScan()
    {
        return config.HostilePriority
            || config.DangerSense
            || config.ThreatMeter
            || config.AmbushWarning
            || config.RetreatCue
            || config.ChaseWarning
            || config.PackWarning
            || config.CompassPings
            || config.TorchReminder;
    }

    private void RefreshAwarenessIfNeeded()
    {
        if (!NeedsAwarenessScan())
        {
            cachedAwareness = new AwarenessSnapshot();
            cachedPriorityTarget = null;
            nextAwarenessScanAtMs = capi.ElapsedMilliseconds + 1000;
            return;
        }

        long now = capi.ElapsedMilliseconds;
        if (now < nextAwarenessScanAtMs) return;

        nextAwarenessScanAtMs = now + 650;
        cachedAwareness = BuildAwareness(out cachedPriorityTarget);
    }

    private Entity? GetPriorityTarget()
    {
        return cachedPriorityTarget;
    }

    private double DistanceToTarget(Entity target)
    {
        EntityPlayer? player = capi.World.Player.Entity;
        if (player == null) return double.MaxValue;

        return player.Pos.XYZ.DistanceTo(target.Pos.XYZ);
    }

    private double GetMeleeRange()
    {
        if (!config.WeaponAwareRange) return 4.5;

        ItemStack? stack = capi.World.Player.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        string code = stack?.Collectible?.Code?.Path?.ToLowerInvariant() ?? "";
        if (config.PerWeaponProfiles)
        {
            if (code.Contains("spear")) return 5.6;
            if (code.Contains("halberd") || code.Contains("pike")) return 5.9;
            if (code.Contains("sword")) return 4.7;
            if (code.Contains("axe")) return 4.35;
            if (code.Contains("knife") || code.Contains("dagger")) return 3.55;
        }

        return 4.5;
    }

    private Entity? GetAssistedTarget()
    {
        int strength = GameMath.Clamp(config.TargetAssistStrength, 0, 100);
        if (strength <= 0 || !IsValidTarget(assistedTarget)) return null;

        long holdMs = config.LeftClickFocus && IsLeftMouseHeld() ? 1500 + strength * 18L : 100 + strength * 10L;
        return capi.ElapsedMilliseconds - assistedTargetSeenAtMs <= holdMs ? assistedTarget : null;
    }

    private Vec4f GetTargetColor(Entity target, double distance, double range, bool inRange, bool barelyInRange, bool lowHealth, bool lowStamina)
    {
        if (lowStamina) return new Vec4f(0.25f, 0.65f, 1f, 0.98f);
        if (lowHealth) return new Vec4f(1f, 0.85f, 0.1f, 0.98f);
        if (config.EliteMarker && LooksElite(target)) return new Vec4f(1f, 0.25f, 1f, 0.98f);

        if (config.RangeBandColors)
        {
            if (inRange) return ToColor(config.ReadyColor);
            if (barelyInRange) return new Vec4f(1f, 0.88f, 0.12f, 0.95f);
            if (distance <= range + 2.5) return ToColor(config.TargetColor);
        }

        return ToColor(inRange ? config.ReadyColor : config.TargetColor);
    }

    private void TrackTarget(Entity target)
    {
        if (lastTarget != target)
        {
            lastTarget = target;
            targetAcquiredAtMs = capi.ElapsedMilliseconds;
            closeMissFrames = 0;
            if (config.DamageFeedbackFlash) lastDamageFlashAtMs = capi.ElapsedMilliseconds;
            return;
        }

        if (DistanceToTarget(target) <= GetMeleeRange() + 0.75 && capi.World.Player.CurrentEntitySelection?.Entity == null)
        {
            closeMissFrames++;
        }
    }

    private void DrawCrosshair(Vec4f color, bool assisted, bool inRange)
    {
        float cx = capi.Render.FrameWidth / 2f;
        float cy = capi.Render.FrameHeight / 2f;
        float size = GameMath.Clamp(config.CrosshairSize, 8, 64);
        if (config.BiggerTargetIndicator)
        {
            size += inRange ? 6 : 3;
        }

        if (config.ReticlePresets && config.HostileOnly)
        {
            size += 2;
        }

        if (assisted)
        {
            size += GameMath.Clamp(config.TargetAssistStrength, 0, 100) * 0.08f;
        }

        if (config.ShowHitCooldownPulse && inRange)
        {
            double pulse = (Math.Sin(capi.ElapsedMilliseconds / 85.0) + 1.0) * 0.5;
            size += (float)(pulse * 4.0);
        }

        if (config.DamageFeedbackFlash && capi.ElapsedMilliseconds - lastDamageFlashAtMs < 220)
        {
            size += 6;
        }

        float thickness = GameMath.Clamp(config.CrosshairThickness, 1, 12);
        float gap = Math.Max(3, thickness + 1);

        DrawRect(cx - thickness / 2, cy - size, thickness, size - gap, color);
        DrawRect(cx - thickness / 2, cy + gap, thickness, size - gap, color);
        DrawRect(cx - size, cy - thickness / 2, size - gap, thickness, color);
        DrawRect(cx + gap, cy - thickness / 2, size - gap, thickness, color);
    }

    private void DrawTargetBrackets(Vec4f color, bool inRange)
    {
        float cx = capi.Render.FrameWidth / 2f;
        float cy = capi.Render.FrameHeight / 2f;
        float half = inRange ? 42 : 50;
        float len = 14;
        float thick = 3;

        DrawRect(cx - half, cy - half, len, thick, color);
        DrawRect(cx - half, cy - half, thick, len, color);
        DrawRect(cx + half - len, cy - half, len, thick, color);
        DrawRect(cx + half - thick, cy - half, thick, len, color);
        DrawRect(cx - half, cy + half - thick, len, thick, color);
        DrawRect(cx - half, cy + half - len, thick, len, color);
        DrawRect(cx + half - len, cy + half - thick, len, thick, color);
        DrawRect(cx + half - thick, cy + half - len, thick, len, color);
    }

    private void DrawSwingTimingCue(bool inRange, bool barelyInRange)
    {
        float cx = capi.Render.FrameWidth / 2f;
        float cy = capi.Render.FrameHeight / 2f + 94;
        Vec4f color = inRange
            ? new Vec4f(0.1f, 1f, 0.25f, 0.9f)
            : barelyInRange
                ? new Vec4f(1f, 0.85f, 0.1f, 0.9f)
                : new Vec4f(1f, 0.15f, 0.12f, 0.75f);

        DrawRect(cx - 42, cy, 84, 5, color);
        if (inRange)
        {
            double pulse = (Math.Sin(capi.ElapsedMilliseconds / 80.0) + 1.0) * 0.5;
            DrawRect(cx - 14, cy - 6, 28, 3 + (float)(pulse * 4), color);
        }
    }

    private void DrawAimDriftWarning()
    {
        float cx = capi.Render.FrameWidth / 2f;
        float cy = capi.Render.FrameHeight / 2f;
        Vec4f color = new Vec4f(1f, 0.08f, 0.04f, 0.9f);
        DrawRect(cx - 72, cy - 92, 144, 4, color);
        DrawRect(cx - 72, cy + 88, 144, 4, color);
    }

    private void DrawSnapIndicator(Entity target)
    {
        EntityPlayer? player = capi.World.Player.Entity;
        if (player == null) return;

        Vec3d toTarget = target.Pos.XYZ.AddCopy(0, 1.2, 0).SubCopy(player.Pos.XYZ.AddCopy(0, 1.6, 0));
        double desiredYaw = Math.Atan2(-toTarget.X, -toTarget.Z);
        double yawDelta = NormalizeAngle(desiredYaw - player.Pos.Yaw);
        float cx = capi.Render.FrameWidth / 2f;
        float cy = capi.Render.FrameHeight / 2f;
        float dir = yawDelta >= 0 ? 1f : -1f;
        float strength = GameMath.Clamp((float)Math.Abs(yawDelta), 0f, 1.2f);
        Vec4f color = new Vec4f(1f, 0.78f, 0.08f, 0.95f);

        float x = cx + dir * (78 + strength * 28);
        DrawRect(x - 10, cy - 3, 20, 6, color);
        DrawRect(x + dir * 8, cy - 11, 6, 22, color);
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > Math.PI) angle -= Math.PI * 2;
        while (angle < -Math.PI) angle += Math.PI * 2;
        return angle;
    }

    private void DrawRect(float x, float y, float width, float height, Vec4f color)
    {
        capi.Render.Render2DTexture(whiteTexture.TextureId, x, y, width, height, 200, color);
    }

    private void DrawDivineSight()
    {
        if (!config.DivineSightEnabled || config.DivineSightStrength <= 0)
        {
            if (divineSightApplied)
            {
                capi.Settings.Float["minbrightness"] = originalMinBrightness;
                divineSightApplied = false;
                lastDivineSightBrightness = originalMinBrightness;
            }

            return;
        }

        float strength = GameMath.Clamp(config.DivineSightStrength, 0, 100) / 100f;
        if (divineSightApplied && Math.Abs(lastDivineSightBrightness - strength) < 0.001f) return;

        capi.Settings.Float["minbrightness"] = strength;
        lastDivineSightBrightness = strength;
        divineSightApplied = true;
    }

    private void DrawWorldTargetOverlay()
    {
        if (!config.CrosshairEnabled || !config.ShowHitboxOverlay) return;

        Entity? target = GetOverlayTarget();
        if (!IsValidTarget(target)) return;

        double range = GetMeleeRange();
        double distance = DistanceToTarget(target!);
        bool inRange = distance <= range;
        bool barelyInRange = distance <= range + 0.75;
        bool lowHealth = config.ShowLowHealthHighlight && IsLowHealth(target!);
        bool lowStamina = config.StaminaWarning && IsLowStamina();
        bool focusActive = config.LeftClickFocus && IsLeftMouseHeld() && target == focusTarget;
        bool aimDrift = focusActive && capi.World.Player.CurrentEntitySelection?.Entity != target;
        Vec4f color = aimDrift
            ? new Vec4f(1f, 0.15f, 0.08f, 0.9f)
            : GetTargetColor(target!, distance, range, inRange, barelyInRange, lowHealth, lowStamina);

        if (config.ShowHitboxOverlay)
        {
            DrawEntityWireframe(target!, color, focusActive);
        }
    }

    private Entity? GetOverlayTarget()
    {
        Entity? selected = capi.World.Player.CurrentEntitySelection?.Entity;
        if (IsValidTarget(selected)) return selected;
        if (config.LeftClickFocus && IsLeftMouseHeld() && IsValidTarget(focusTarget)) return focusTarget;
        return GetAssistedTarget() ?? cachedPriorityTarget;
    }

    private void DrawEntityWireframe(Entity target, Vec4f color, bool focusActive)
    {
        Cuboidf? box = target.SelectionBox ?? target.CollisionBox;
        if (box == null) return;

        EntityPlayer? player = capi.World.Player.Entity;
        if (player == null || player.Pos.XYZ.DistanceTo(target.Pos.XYZ) > Math.Max(GetMeleeRange() + 3, 8)) return;

        float thickness = (focusActive ? 2.2f : 1.6f) * ClientSettings.Wireframethickness;
        targetWireframe.Render(
            capi,
            target.Pos.X + box.X1,
            target.Pos.Y + box.Y1,
            target.Pos.Z + box.Z1,
            box.XSize,
            box.YSize,
            box.ZSize,
            thickness,
            color
        );
    }

    private void DrawTargetInfo(Entity target, double distance, double range, AwarenessSnapshot awareness)
    {
        if (!config.ShowTargetName && !config.ShowTargetDistance && !config.GuardHint && !config.LastHitTracker && !config.TrainingMode) return;

        string text = BuildTargetInfo(target, distance, range, awareness);
        if (string.IsNullOrWhiteSpace(text)) return;

        LoadedTexture? texture = GetHealthTagTexture(text);
        if (texture == null || texture.TextureId <= 0) return;

        float x = capi.Render.FrameWidth / 2f - texture.Width / 2f;
        float y = capi.Render.FrameHeight / 2f + GameMath.Clamp(config.CrosshairSize, 8, 64) + 18;
        capi.Render.Render2DTexture(texture.TextureId, x, y, texture.Width, texture.Height, 210);
    }

    private void DrawTargetHealthTag(Entity target, Vec4f accentColor)
    {
        bool hasHealth = TryGetHealth(target, out float health, out float maxHealth);
        if (!hasHealth)
        {
            health = 1;
            maxHealth = 1;
        }

        float pct = GameMath.Clamp(health / maxHealth, 0f, 1f);
        string name = target.GetName();
        if (string.IsNullOrWhiteSpace(name)) name = target.Code?.Path ?? "Target";
        string text = hasHealth
            ? $"{name}  {Math.Ceiling(health):0}/{Math.Ceiling(maxHealth):0}"
            : $"{name}  health ?";
        LoadedTexture? texture = GetTargetInfoTexture(text);
        if (texture == null || texture.TextureId <= 0) return;

        Vec3d screenPos;
        if (capi.World.Player.CurrentEntitySelection?.Entity == target)
        {
            screenPos = GetDirectTargetScreenAnchor(target);
        }
        else
        {
            Vec3d tagPos = target.Pos.XYZ.AddCopy(0, GetEntityHeight(target) + 0.35, 0);
            if (!TryProjectToScreen(tagPos, out screenPos))
            {
                screenPos = GetDirectTargetScreenAnchor(target);
            }
        }

        float cx = GameMath.Clamp((float)screenPos.X, 120, capi.Render.FrameWidth - 120);
        float y = GameMath.Clamp((float)screenPos.Y, 80, capi.Render.FrameHeight - 180);

        float barWidth = 190;
        float barHeight = 9;
        float x = cx - barWidth / 2f;
        Vec4f back = new Vec4f(0.02f, 0.02f, 0.02f, 0.72f);
        Vec4f fill = !hasHealth
            ? new Vec4f(0.55f, 0.55f, 0.55f, 0.96f)
            : pct <= 0.25f
            ? new Vec4f(1f, 0.12f, 0.08f, 0.96f)
            : pct <= 0.55f
                ? new Vec4f(1f, 0.78f, 0.08f, 0.96f)
                : new Vec4f(0.15f, 1f, 0.28f, 0.96f);

        DrawRect(x - 5, y - texture.Height - 7, barWidth + 10, texture.Height + barHeight + 17, back);
        capi.Render.Render2DTexture(texture.TextureId, cx - texture.Width / 2f, y - texture.Height - 2, texture.Width, texture.Height, 214);
        DrawRect(x, y + 4, barWidth, barHeight, new Vec4f(0f, 0f, 0f, 0.82f));
        DrawRect(x, y + 4, barWidth * pct, barHeight, fill);
        DrawRect(x, y + 4, barWidth, 1.5f, accentColor);
    }

    private static float GetEntityHeight(Entity target)
    {
        Cuboidf? box = target.SelectionBox ?? target.CollisionBox;
        if (box != null) return Math.Max(0.5f, box.YSize);
        return target is EntityPlayer ? 1.8f : 1.2f;
    }

    private Vec3d GetDirectTargetScreenAnchor(Entity target)
    {
        double distance = Math.Max(1.0, DistanceToTarget(target));
        double verticalLift = GameMath.Clamp(760.0 / distance, 62.0, 142.0);
        return new Vec3d(capi.Render.FrameWidth / 2.0, capi.Render.FrameHeight / 2.0 - verticalLift, distance);
    }

    private bool TryProjectToScreen(Vec3d worldPos, out Vec3d screenPos)
    {
        screenPos = new Vec3d();

        EntityPlayer player = capi.World.Player.Entity;
        Vec3d eyePos = player.Pos.XYZ.AddCopy(player.LocalEyePos);
        Vec3d rel = worldPos.SubCopy(eyePos);
        double horizontalDistance = Math.Sqrt(rel.X * rel.X + rel.Z * rel.Z);
        if (horizontalDistance <= 0.01) return false;

        double yawToTarget = Math.Atan2(-rel.X, -rel.Z);
        double yawDelta = NormalizeAngle(yawToTarget - player.Pos.Yaw);
        if (Math.Abs(yawDelta) > Math.PI / 2) return false;

        double fovY = GameMath.DEG2RAD * 70.0;
        double aspect = Math.Max(0.1, capi.Render.FrameWidth / Math.Max(1.0, capi.Render.FrameHeight));
        double fovX = 2.0 * Math.Atan(Math.Tan(fovY / 2.0) * aspect);
        double focalX = capi.Render.FrameWidth / (2.0 * Math.Tan(fovX / 2.0));
        double verticalLift = GameMath.Clamp(900.0 / Math.Max(1.0, horizontalDistance), 42.0, 150.0);

        screenPos.X = capi.Render.FrameWidth / 2.0 + Math.Tan(yawDelta) * focalX;
        screenPos.Y = capi.Render.FrameHeight / 2.0 - verticalLift;
        screenPos.Z = horizontalDistance;
        return true;
    }

    private string BuildTargetInfo(Entity target, double distance, double range, AwarenessSnapshot awareness)
    {
        string name = target.GetName();
        if (string.IsNullOrWhiteSpace(name)) name = target.Code?.Path ?? "Target";

        List<string> parts = new();
        if (config.ShowTargetName) parts.Add(name);
        if (config.ShowTargetDistance) parts.Add($"{distance:0.0}m/{range:0.0}m");
        if (config.GuardHint && HasShieldReady()) parts.Add("guard");
        if (config.LastHitTracker && targetAcquiredAtMs > 0) parts.Add($"{Math.Max(0, (capi.ElapsedMilliseconds - targetAcquiredAtMs) / 1000)}s");
        if (config.TrainingMode && closeMissFrames > 20) parts.Add("close miss");
        if (config.PackWarning && awareness.Hostiles >= 3) parts.Add("pack");

        return string.Join("  ", parts);
    }

    private LoadedTexture? GetTargetInfoTexture(string text)
    {
        if (targetInfoTexture != null && targetInfoText == text) return targetInfoTexture;

        targetInfoTexture?.Dispose();
        targetInfoText = text;
        targetInfoTexture = capi.Gui.TextTexture.GenTextTexture(text, CairoFont.WhiteSmallText());
        return targetInfoTexture;
    }

    private LoadedTexture? GetHealthTagTexture(string text)
    {
        if (healthTagTexture != null && healthTagText == text) return healthTagTexture;

        healthTagTexture?.Dispose();
        healthTagText = text;
        healthTagTexture = capi.Gui.TextTexture.GenTextTexture(text, CairoFont.WhiteSmallText());
        return healthTagTexture;
    }

    private AwarenessSnapshot BuildAwareness(out Entity? priorityTarget)
    {
        AwarenessSnapshot snapshot = new();
        priorityTarget = null;
        EntityPlayer? player = capi.World.Player.Entity;
        if (player == null) return snapshot;

        float radius = GameMath.Clamp(config.AwarenessRadius, 8, 64);
        double bestPriorityDistance = double.MaxValue;
        foreach (Entity entity in capi.World.GetEntitiesAround(player.Pos.XYZ, radius, 16))
        {
            if (!IsValidTarget(entity) || !LooksHostile(entity)) continue;

            double distance = player.Pos.XYZ.DistanceTo(entity.Pos.XYZ);
            snapshot.Hostiles++;
            snapshot.NearestDistance = Math.Min(snapshot.NearestDistance, distance);
            if (distance <= 7) snapshot.CloseHostiles++;
            if (distance <= 5) snapshot.Chasing = true;
            if (IsBehindPlayer(player, entity)) snapshot.BehindHostiles++;
            if (config.HostilePriority && distance <= 5.5 && distance < bestPriorityDistance)
            {
                bestPriorityDistance = distance;
                priorityTarget = entity;
            }
        }

        return snapshot;
    }

    private void DrawAwareness(AwarenessSnapshot awareness)
    {
        if (!config.DangerSense && !config.ThreatMeter && !config.AmbushWarning && !config.RetreatCue && !config.ChaseWarning && !config.NightSafetyCue && !config.TorchReminder) return;

        List<string> parts = new();
        if (config.ThreatMeter && awareness.Hostiles > 0)
        {
            string level = awareness.CloseHostiles >= 2 ? "critical" : awareness.Hostiles >= 3 ? "danger" : "alert";
            parts.Add($"threat: {level}");
        }

        if (config.DangerSense && awareness.NearestDistance < double.MaxValue) parts.Add($"near {awareness.NearestDistance:0}m");
        if (config.AmbushWarning && awareness.BehindHostiles > 0) parts.Add("behind");
        if (config.ChaseWarning && awareness.Chasing) parts.Add("pursued");
        if (config.RetreatCue && awareness.CloseHostiles > 0 && IsLowPlayerHealth()) parts.Add("retreat");
        if (config.NightSafetyCue && IsNight()) parts.Add("night");
        if (config.TorchReminder && IsDarkAndUnlit() && awareness.Hostiles > 0) parts.Add("light");
        if (config.CompassPings && awareness.BehindHostiles > 0) DrawEdgeWarning(new Vec4f(1f, 0.25f, 0.15f, 0.85f));

        if (parts.Count == 0) return;

        string text = string.Join("  |  ", parts);
        LoadedTexture? texture = GetAwarenessTexture(text);
        if (texture == null || texture.TextureId <= 0) return;

        float x = capi.Render.FrameWidth / 2f - texture.Width / 2f;
        float y = capi.Render.FrameHeight * 0.18f;
        capi.Render.Render2DTexture(texture.TextureId, x, y, texture.Width, texture.Height, 210);
    }

    private LoadedTexture? GetAwarenessTexture(string text)
    {
        if (awarenessTexture != null && awarenessText == text) return awarenessTexture;

        awarenessTexture?.Dispose();
        awarenessText = text;
        awarenessTexture = capi.Gui.TextTexture.GenTextTexture(text, CairoFont.WhiteSmallText());
        return awarenessTexture;
    }

    private void DrawEdgeWarning(Vec4f color)
    {
        float w = capi.Render.FrameWidth;
        float h = capi.Render.FrameHeight;
        DrawRect(w * 0.5f - 34, h - 88, 68, 5, color);
        DrawRect(w * 0.5f - 24, h - 78, 48, 5, color);
    }

    private bool HasHeldItem()
    {
        ItemStack? stack = capi.World.Player.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        return stack?.Collectible != null;
    }

    private bool HasShieldReady()
    {
        ItemStack? stack = capi.World.Player.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        string code = stack?.Collectible?.Code?.Path?.ToLowerInvariant() ?? "";
        return code.Contains("shield") || code.Contains("buckler");
    }

    private bool IsLowStamina()
    {
        float stamina = capi.World.Player.Entity.WatchedAttributes.GetFloat("stamina", -1f);
        float maxStamina = capi.World.Player.Entity.WatchedAttributes.GetFloat("maxstamina", -1f);
        return stamina >= 0 && maxStamina > 0 && stamina / maxStamina < 0.25f;
    }

    private bool IsLowPlayerHealth()
    {
        EntityPlayer player = capi.World.Player.Entity;
        float health = player.WatchedAttributes.GetFloat("health", -1f);
        float maxHealth = player.WatchedAttributes.GetFloat("maxhealth", -1f);
        return health >= 0 && maxHealth > 0 && health / maxHealth < 0.35f;
    }

    private bool IsNight()
    {
        double hour = capi.World.Calendar.HourOfDay;
        return hour >= 20 || hour <= 5;
    }

    private bool IsDarkAndUnlit()
    {
        ItemStack? stack = capi.World.Player.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        string code = stack?.Collectible?.Code?.Path?.ToLowerInvariant() ?? "";
        return IsNight() && !code.Contains("torch") && !code.Contains("lantern");
    }

    private bool IsLeftMouseHeld()
    {
        object input = capi.Input;
        Type type = input.GetType();
        foreach (string name in new[] { "MouseButtonState", "MouseButtonsDown", "MouseButtonDown" })
        {
            object? value = type.GetProperty(name)?.GetValue(input);
            if (ReadMouseState(value)) return true;
        }

        foreach (string name in new[] { "IsMouseButtonDown", "MouseButtonPressed", "MouseButtonDown" })
        {
            foreach (object arg in new object[] { EnumMouseButton.Left, 0 })
            {
                try
                {
                    object? result = type.GetMethod(name, new[] { arg.GetType() })?.Invoke(input, new[] { arg });
                    if (result is bool down && down) return true;
                }
                catch
                {
                    // Some Vintage Story builds expose different input helpers.
                }
            }
        }

        return false;
    }

    private static bool ReadMouseState(object? value)
    {
        if (value is bool down) return down;
        if (value is bool[] states && states.Length > (int)EnumMouseButton.Left) return states[(int)EnumMouseButton.Left];
        if (value is IReadOnlyList<bool> list && list.Count > (int)EnumMouseButton.Left) return list[(int)EnumMouseButton.Left];
        return false;
    }

    private static bool LooksHostile(Entity target)
    {
        string code = target.Code?.Path?.ToLowerInvariant() ?? "";
        return code.Contains("drifter")
            || code.Contains("locust")
            || code.Contains("wolf")
            || code.Contains("bear")
            || code.Contains("hyena")
            || code.Contains("bell")
            || code.Contains("shiver")
            || code.Contains("bowtorn")
            || code.Contains("sawblade")
            || code.Contains("nightmare");
    }

    private static bool LooksElite(Entity target)
    {
        string code = target.Code?.Path?.ToLowerInvariant() ?? "";
        return code.Contains("corrupt")
            || code.Contains("nightmare")
            || code.Contains("deep")
            || code.Contains("boss");
    }

    private static bool IsBehindPlayer(EntityPlayer player, Entity target)
    {
        Vec3d toTarget = target.Pos.XYZ.SubCopy(player.Pos.XYZ);
        double yaw = player.Pos.Yaw;
        Vec3d forward = new Vec3d(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        toTarget.Y = 0;
        double length = Math.Sqrt(toTarget.X * toTarget.X + toTarget.Z * toTarget.Z);
        if (length <= 0.01) return false;

        toTarget.X /= length;
        toTarget.Z /= length;
        double dot = forward.X * toTarget.X + forward.Y * toTarget.Y + forward.Z * toTarget.Z;
        return dot < -0.25;
    }

    private static bool IsLowHealth(Entity target)
    {
        if (!TryGetHealth(target, out float health, out float maxHealth)) return false;

        return health / maxHealth <= 0.25f;
    }

    private static bool TryGetHealth(Entity target, out float health, out float maxHealth)
    {
        EntityBehaviorHealth? healthBehavior = target.GetBehavior<EntityBehaviorHealth>();
        if (healthBehavior != null)
        {
            health = healthBehavior.Health;
            maxHealth = healthBehavior.MaxHealth;
            if (health >= 0 && maxHealth > 0) return true;
        }

        health = target.WatchedAttributes.GetFloat("health", -1f);
        maxHealth = target.WatchedAttributes.GetFloat("maxhealth", -1f);
        return health >= 0 && maxHealth > 0;
    }

    private void PlayReadySound()
    {
        long now = capi.ElapsedMilliseconds;
        if (now - lastSoundCueAtMs < 1200) return;

        lastSoundCueAtMs = now;
        Vec3d pos = capi.World.Player.Entity.Pos.XYZ;
        capi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/tick"), pos.X, pos.Y, pos.Z, null, false, 8f, 0.18f);
    }

    private static Vec4f ToColor(float[] values)
    {
        float Read(int index, float fallback) => values != null && values.Length > index ? values[index] : fallback;
        return new Vec4f(Read(0, 1), Read(1, 1), Read(2, 1), Read(3, 1));
    }

    private struct AwarenessSnapshot
    {
        public int Hostiles;
        public int CloseHostiles;
        public int BehindHostiles;
        public bool Chasing;
        public double NearestDistance;

        public AwarenessSnapshot()
        {
            Hostiles = 0;
            CloseHostiles = 0;
            BehindHostiles = 0;
            Chasing = false;
            NearestDistance = double.MaxValue;
        }
    }
}
