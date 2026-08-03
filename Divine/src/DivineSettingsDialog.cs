using System;
using Vintagestory.API.Client;

namespace Divine;

public sealed class DivineSettingsDialog : GuiDialog
{
    private readonly DivineConfig config;
    private readonly Action save;
    private long lastSliderSaveAtMs;
    private int pendingDivineSightStrength;

    private static readonly string[] ColorPresets =
    {
        "Green", "Red", "Orange", "Yellow", "Cyan", "Blue", "Purple", "White"
    };

    public override string ToggleKeyCombinationCode => "divinesettings";
    public override double DrawOrder => 0.9;
    public override double InputOrder => 0.4;

    public DivineSettingsDialog(ICoreClientAPI capi, DivineConfig config, Action save) : base(capi)
    {
        this.config = config;
        this.save = save;
    }

    public override bool TryOpen()
    {
        EnsureComposed();
        return base.TryOpen();
    }

    public override bool TryOpen(bool withFocus)
    {
        EnsureComposed();
        return base.TryOpen(withFocus);
    }

    private void EnsureComposed()
    {
        if (SingleComposer != null) return;
        ComposeDialog();
    }

    private void ComposeDialog()
    {
        pendingDivineSightStrength = config.DivineSightStrength;
        ElementBounds dialogBounds = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, 960, 800);
        ElementBounds bgBounds = ElementBounds.Fixed(0, 0, 960, 800);

        SingleComposer = capi.Gui
            .CreateCompo("divine-settings", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar("Divine Settings", OnTitleBarClose)
            .BeginChildElements(bgBounds)
            .AddStaticText("Crosshair", CairoFont.WhiteSmallText(), ElementBounds.Fixed(28, 48, 180, 24))
            .AddStaticText("Enabled", CairoFont.WhiteDetailText(), ElementBounds.Fixed(54, 84, 150, 24))
            .AddSwitch(value => Update(() => config.CrosshairEnabled = value), ElementBounds.Fixed(28, 78, 34, 34), "crosshairEnabled")
            .AddStaticText("Size", CairoFont.WhiteDetailText(), ElementBounds.Fixed(28, 126, 70, 24))
            .AddSlider(value => UpdateSlider(() => config.CrosshairSize = Clamp(value, 8, 64)), ElementBounds.Fixed(108, 124, 150, 24), "crosshairSize")
            .AddStaticText("Thickness", CairoFont.WhiteDetailText(), ElementBounds.Fixed(28, 168, 80, 24))
            .AddSlider(value => UpdateSlider(() => config.CrosshairThickness = Clamp(value, 1, 12)), ElementBounds.Fixed(108, 166, 150, 24), "crosshairThickness")
            .AddStaticText("Ready color", CairoFont.WhiteDetailText(), ElementBounds.Fixed(28, 212, 95, 24))
            .AddButton(config.ReadyColorPreset, () => CycleReadyColor(), ElementBounds.Fixed(130, 206, 120, 32), EnumButtonStyle.Normal, "readyColor")
            .AddStaticText("Target color", CairoFont.WhiteDetailText(), ElementBounds.Fixed(28, 256, 95, 24))
            .AddButton(config.TargetColorPreset, () => CycleTargetColor(), ElementBounds.Fixed(130, 250, 120, 32), EnumButtonStyle.Normal, "targetColor")
            .AddStaticText("Assist", CairoFont.WhiteDetailText(), ElementBounds.Fixed(28, 302, 70, 24))
            .AddSlider(value => UpdateSlider(() => config.TargetAssistStrength = Clamp(value, 0, 100)), ElementBounds.Fixed(108, 300, 150, 24), "assistStrength")
            .AddStaticText("Targeting", CairoFont.WhiteSmallText(), ElementBounds.Fixed(282, 48, 180, 24))
            .AddStaticText("Players only", CairoFont.WhiteDetailText(), ElementBounds.Fixed(308, 84, 160, 24))
            .AddSwitch(value => Update(() => config.PlayersOnly = value), ElementBounds.Fixed(282, 78, 34, 34), "playersOnly")
            .AddStaticText("Combat only", CairoFont.WhiteDetailText(), ElementBounds.Fixed(308, 124, 160, 24))
            .AddSwitch(value => Update(() => config.CombatOnly = value), ElementBounds.Fixed(282, 118, 34, 34), "combatOnly")
            .AddStaticText("Hostile only", CairoFont.WhiteDetailText(), ElementBounds.Fixed(308, 164, 160, 24))
            .AddSwitch(value => Update(() => config.HostileOnly = value), ElementBounds.Fixed(282, 158, 34, 34), "hostileOnly")
            .AddStaticText("Hostile priority", CairoFont.WhiteDetailText(), ElementBounds.Fixed(308, 204, 160, 24))
            .AddSwitch(value => Update(() => config.HostilePriority = value), ElementBounds.Fixed(282, 198, 34, 34), "hostilePriority")
            .AddStaticText("Weapon range", CairoFont.WhiteDetailText(), ElementBounds.Fixed(308, 244, 160, 24))
            .AddSwitch(value => Update(() => config.WeaponAwareRange = value), ElementBounds.Fixed(282, 238, 34, 34), "weaponRange")
            .AddStaticText("Per weapon", CairoFont.WhiteDetailText(), ElementBounds.Fixed(308, 284, 160, 24))
            .AddSwitch(value => Update(() => config.PerWeaponProfiles = value), ElementBounds.Fixed(282, 278, 34, 34), "perWeapon")
            .AddStaticText("Feedback", CairoFont.WhiteSmallText(), ElementBounds.Fixed(482, 48, 180, 24))
            .AddStaticText("Range colors", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 84, 160, 24))
            .AddSwitch(value => Update(() => config.RangeBandColors = value), ElementBounds.Fixed(482, 78, 34, 34), "rangeColors")
            .AddStaticText("Bigger target", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 124, 160, 24))
            .AddSwitch(value => Update(() => config.BiggerTargetIndicator = value), ElementBounds.Fixed(482, 118, 34, 34), "biggerTarget")
            .AddStaticText("Brackets", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 164, 160, 24))
            .AddSwitch(value => Update(() => config.TargetBrackets = value), ElementBounds.Fixed(482, 158, 34, 34), "targetBrackets")
            .AddStaticText("Target name", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 204, 160, 24))
            .AddSwitch(value => Update(() => config.ShowTargetName = value), ElementBounds.Fixed(482, 198, 34, 34), "targetName")
            .AddStaticText("Distance", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 244, 160, 24))
            .AddSwitch(value => Update(() => config.ShowTargetDistance = value), ElementBounds.Fixed(482, 238, 34, 34), "targetDistance")
            .AddStaticText("Low health", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 284, 160, 24))
            .AddSwitch(value => Update(() => config.ShowLowHealthHighlight = value), ElementBounds.Fixed(482, 278, 34, 34), "lowHealth")
            .AddStaticText("Cooldown pulse", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 324, 160, 24))
            .AddSwitch(value => Update(() => config.ShowHitCooldownPulse = value), ElementBounds.Fixed(482, 318, 34, 34), "cooldownPulse")
            .AddStaticText("Sound cue", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 364, 160, 24))
            .AddSwitch(value => Update(() => config.SoundCueEnabled = value), ElementBounds.Fixed(482, 358, 34, 34), "soundCue")
            .AddStaticText("Hit flash", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 404, 160, 24))
            .AddSwitch(value => Update(() => config.DamageFeedbackFlash = value), ElementBounds.Fixed(482, 398, 34, 34), "hitFlash")
            .AddStaticText("Stamina warn", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 444, 160, 24))
            .AddSwitch(value => Update(() => config.StaminaWarning = value), ElementBounds.Fixed(482, 438, 34, 34), "staminaWarn")
            .AddStaticText("Guard hint", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 484, 160, 24))
            .AddSwitch(value => Update(() => config.GuardHint = value), ElementBounds.Fixed(482, 478, 34, 34), "guardHint")
            .AddStaticText("Awareness", CairoFont.WhiteSmallText(), ElementBounds.Fixed(682, 48, 180, 24))
            .AddStaticText("Danger sense", CairoFont.WhiteDetailText(), ElementBounds.Fixed(708, 84, 160, 24))
            .AddSwitch(value => Update(() => config.DangerSense = value), ElementBounds.Fixed(682, 78, 34, 34), "dangerSense")
            .AddStaticText("Ambush warn", CairoFont.WhiteDetailText(), ElementBounds.Fixed(708, 124, 160, 24))
            .AddSwitch(value => Update(() => config.AmbushWarning = value), ElementBounds.Fixed(682, 118, 34, 34), "ambushWarning")
            .AddStaticText("Threat meter", CairoFont.WhiteDetailText(), ElementBounds.Fixed(708, 164, 160, 24))
            .AddSwitch(value => Update(() => config.ThreatMeter = value), ElementBounds.Fixed(682, 158, 34, 34), "threatMeter")
            .AddStaticText("Last hit", CairoFont.WhiteDetailText(), ElementBounds.Fixed(708, 204, 160, 24))
            .AddSwitch(value => Update(() => config.LastHitTracker = value), ElementBounds.Fixed(682, 198, 34, 34), "lastHit")
            .AddStaticText("Chase warn", CairoFont.WhiteDetailText(), ElementBounds.Fixed(708, 244, 160, 24))
            .AddSwitch(value => Update(() => config.ChaseWarning = value), ElementBounds.Fixed(682, 238, 34, 34), "chaseWarning")
            .AddStaticText("Retreat cue", CairoFont.WhiteDetailText(), ElementBounds.Fixed(708, 284, 160, 24))
            .AddSwitch(value => Update(() => config.RetreatCue = value), ElementBounds.Fixed(682, 278, 34, 34), "retreatCue")
            .AddStaticText("Elite marker", CairoFont.WhiteDetailText(), ElementBounds.Fixed(708, 324, 160, 24))
            .AddSwitch(value => Update(() => config.EliteMarker = value), ElementBounds.Fixed(682, 318, 34, 34), "eliteMarker")
            .AddStaticText("Pack warning", CairoFont.WhiteDetailText(), ElementBounds.Fixed(708, 364, 160, 24))
            .AddSwitch(value => Update(() => config.PackWarning = value), ElementBounds.Fixed(682, 358, 34, 34), "packWarning")
            .AddStaticText("Compass pings", CairoFont.WhiteDetailText(), ElementBounds.Fixed(708, 404, 160, 24))
            .AddSwitch(value => Update(() => config.CompassPings = value), ElementBounds.Fixed(682, 398, 34, 34), "compassPings")
            .AddStaticText("Night cue", CairoFont.WhiteDetailText(), ElementBounds.Fixed(708, 444, 160, 24))
            .AddSwitch(value => Update(() => config.NightSafetyCue = value), ElementBounds.Fixed(682, 438, 34, 34), "nightCue")
            .AddStaticText("Torch reminder", CairoFont.WhiteDetailText(), ElementBounds.Fixed(708, 484, 160, 24))
            .AddSwitch(value => Update(() => config.TorchReminder = value), ElementBounds.Fixed(682, 478, 34, 34), "torchReminder")
            .AddStaticText("Training", CairoFont.WhiteDetailText(), ElementBounds.Fixed(54, 390, 160, 24))
            .AddSwitch(value => Update(() => config.TrainingMode = value), ElementBounds.Fixed(28, 384, 34, 34), "trainingMode")
            .AddStaticText("Reticle presets", CairoFont.WhiteDetailText(), ElementBounds.Fixed(54, 430, 160, 24))
            .AddSwitch(value => Update(() => config.ReticlePresets = value), ElementBounds.Fixed(28, 424, 34, 34), "reticlePresets")
            .AddStaticText("Panic waypoint", CairoFont.WhiteDetailText(), ElementBounds.Fixed(54, 470, 160, 24))
            .AddSwitch(value => Update(() => config.PanicWaypoint = value), ElementBounds.Fixed(28, 464, 34, 34), "panicWaypoint")
            .AddStaticText("Resource danger", CairoFont.WhiteDetailText(), ElementBounds.Fixed(54, 510, 160, 24))
            .AddSwitch(value => Update(() => config.ResourceDangerOverlay = value), ElementBounds.Fixed(28, 504, 34, 34), "resourceDanger")
            .AddStaticText("Divine Sight", CairoFont.WhiteDetailText(), ElementBounds.Fixed(54, 550, 160, 24))
            .AddSwitch(value => Update(() => config.DivineSightEnabled = value), ElementBounds.Fixed(28, 544, 34, 34), "divineSight")
            .AddStaticText("Sight strength", CairoFont.WhiteDetailText(), ElementBounds.Fixed(28, 594, 120, 24))
            .AddSlider(value => UpdatePendingDivineSight(value), ElementBounds.Fixed(148, 592, 110, 24), "divineSightStrength")
            .AddStaticText("Storage", CairoFont.WhiteSmallText(), ElementBounds.Fixed(282, 366, 180, 24))
            .AddStaticText("Right-click opens all", CairoFont.WhiteDetailText(), ElementBounds.Fixed(308, 404, 170, 24))
            .AddSwitch(value => Update(() => config.StorageRightClickOpenAll = value), ElementBounds.Fixed(282, 398, 34, 34), "storageRightClick")
            .AddStaticText("Press R to open all nearby containers", CairoFont.WhiteDetailText(), ElementBounds.Fixed(282, 446, 190, 48))
            .AddStaticText("Awareness radius", CairoFont.WhiteDetailText(), ElementBounds.Fixed(682, 528, 150, 24))
            .AddSlider(value => UpdateSlider(() => config.AwarenessRadius = Clamp(value, 8, 64)), ElementBounds.Fixed(682, 556, 170, 24), "awarenessRadius")
            .AddStaticText("Combat aids", CairoFont.WhiteSmallText(), ElementBounds.Fixed(482, 586, 180, 24))
            .AddStaticText("Hitbox overlay", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 624, 160, 24))
            .AddSwitch(value => Update(() => config.ShowHitboxOverlay = value), ElementBounds.Fixed(482, 618, 34, 34), "hitboxOverlay")
            .AddStaticText("Health tag", CairoFont.WhiteDetailText(), ElementBounds.Fixed(682, 624, 150, 24))
            .AddSwitch(value => Update(() => config.ShowTargetHealthTag = value), ElementBounds.Fixed(656, 618, 34, 34), "healthTag")
            .AddStaticText("Left-click focus", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 664, 160, 24))
            .AddSwitch(value => Update(() => config.LeftClickFocus = value), ElementBounds.Fixed(482, 658, 34, 34), "leftClickFocus")
            .AddStaticText("Timing cue", CairoFont.WhiteDetailText(), ElementBounds.Fixed(682, 664, 150, 24))
            .AddSwitch(value => Update(() => config.SwingTimingCue = value), ElementBounds.Fixed(656, 658, 34, 34), "timingCue")
            .AddStaticText("Drift warning", CairoFont.WhiteDetailText(), ElementBounds.Fixed(508, 704, 160, 24))
            .AddSwitch(value => Update(() => config.AimDriftWarning = value), ElementBounds.Fixed(482, 698, 34, 34), "driftWarning")
            .AddStaticText("Snap cue", CairoFont.WhiteDetailText(), ElementBounds.Fixed(682, 704, 150, 24))
            .AddSwitch(value => Update(() => config.SnapIndicator = value), ElementBounds.Fixed(656, 698, 34, 34), "snapCue")
            .AddButton("Done", OnDone, ElementBounds.Fixed(826, 750, 100, 30), EnumButtonStyle.Normal, "done")
            .EndChildElements()
            .Compose();

        SingleComposer.GetSwitch("crosshairEnabled").SetValue(config.CrosshairEnabled);
        SingleComposer.GetSwitch("playersOnly").SetValue(config.PlayersOnly);
        SingleComposer.GetSlider("crosshairSize").SetValues(Clamp(config.CrosshairSize, 8, 64), 8, 64, 1);
        SingleComposer.GetSlider("crosshairThickness").SetValues(Clamp(config.CrosshairThickness, 1, 12), 1, 12, 1);
        SingleComposer.GetSlider("assistStrength").SetValues(Clamp(config.TargetAssistStrength, 0, 100), 0, 100, 1);
        SingleComposer.GetSwitch("combatOnly").SetValue(config.CombatOnly);
        SingleComposer.GetSwitch("hostileOnly").SetValue(config.HostileOnly);
        SingleComposer.GetSwitch("hostilePriority").SetValue(config.HostilePriority);
        SingleComposer.GetSwitch("weaponRange").SetValue(config.WeaponAwareRange);
        SingleComposer.GetSwitch("perWeapon").SetValue(config.PerWeaponProfiles);
        SingleComposer.GetSwitch("rangeColors").SetValue(config.RangeBandColors);
        SingleComposer.GetSwitch("biggerTarget").SetValue(config.BiggerTargetIndicator);
        SingleComposer.GetSwitch("targetBrackets").SetValue(config.TargetBrackets);
        SingleComposer.GetSwitch("targetName").SetValue(config.ShowTargetName);
        SingleComposer.GetSwitch("targetDistance").SetValue(config.ShowTargetDistance);
        SingleComposer.GetSwitch("lowHealth").SetValue(config.ShowLowHealthHighlight);
        SingleComposer.GetSwitch("cooldownPulse").SetValue(config.ShowHitCooldownPulse);
        SingleComposer.GetSwitch("soundCue").SetValue(config.SoundCueEnabled);
        SingleComposer.GetSwitch("hitFlash").SetValue(config.DamageFeedbackFlash);
        SingleComposer.GetSwitch("staminaWarn").SetValue(config.StaminaWarning);
        SingleComposer.GetSwitch("guardHint").SetValue(config.GuardHint);
        SingleComposer.GetSwitch("dangerSense").SetValue(config.DangerSense);
        SingleComposer.GetSwitch("ambushWarning").SetValue(config.AmbushWarning);
        SingleComposer.GetSwitch("threatMeter").SetValue(config.ThreatMeter);
        SingleComposer.GetSwitch("lastHit").SetValue(config.LastHitTracker);
        SingleComposer.GetSwitch("chaseWarning").SetValue(config.ChaseWarning);
        SingleComposer.GetSwitch("retreatCue").SetValue(config.RetreatCue);
        SingleComposer.GetSwitch("eliteMarker").SetValue(config.EliteMarker);
        SingleComposer.GetSwitch("packWarning").SetValue(config.PackWarning);
        SingleComposer.GetSwitch("compassPings").SetValue(config.CompassPings);
        SingleComposer.GetSwitch("nightCue").SetValue(config.NightSafetyCue);
        SingleComposer.GetSwitch("torchReminder").SetValue(config.TorchReminder);
        SingleComposer.GetSwitch("trainingMode").SetValue(config.TrainingMode);
        SingleComposer.GetSwitch("reticlePresets").SetValue(config.ReticlePresets);
        SingleComposer.GetSwitch("panicWaypoint").SetValue(config.PanicWaypoint);
        SingleComposer.GetSwitch("resourceDanger").SetValue(config.ResourceDangerOverlay);
        SingleComposer.GetSwitch("divineSight").SetValue(config.DivineSightEnabled);
        SingleComposer.GetSlider("divineSightStrength").SetValues(Clamp(pendingDivineSightStrength, 0, 100), 0, 100, 1);
        SingleComposer.GetSwitch("storageRightClick").SetValue(config.StorageRightClickOpenAll);
        SingleComposer.GetSwitch("hitboxOverlay").SetValue(config.ShowHitboxOverlay);
        SingleComposer.GetSwitch("healthTag").SetValue(config.ShowTargetHealthTag);
        SingleComposer.GetSwitch("leftClickFocus").SetValue(config.LeftClickFocus);
        SingleComposer.GetSwitch("driftWarning").SetValue(config.AimDriftWarning);
        SingleComposer.GetSwitch("timingCue").SetValue(config.SwingTimingCue);
        SingleComposer.GetSwitch("snapCue").SetValue(config.SnapIndicator);
        SingleComposer.GetSlider("awarenessRadius").SetValues(Clamp(config.AwarenessRadius, 8, 64), 8, 64, 1);
    }

    private void OnTitleBarClose()
    {
        ApplyPendingDivineSight();
        save();
        TryClose();
    }

    private bool OnDone()
    {
        ApplyPendingDivineSight();
        save();
        TryClose();
        return true;
    }

    private bool CycleReadyColor()
    {
        CycleColor(config.ReadyColorPreset, true);
        return true;
    }

    private bool CycleTargetColor()
    {
        CycleColor(config.TargetColorPreset, false);
        return true;
    }

    private void CycleColor(string current, bool readyColor)
    {
        int index = Array.FindIndex(ColorPresets, value => value.Equals(current, StringComparison.OrdinalIgnoreCase));
        string next = ColorPresets[(index + 1 + ColorPresets.Length) % ColorPresets.Length];
        DivineModSystem.ApplyColorPreset(config, next, readyColor);
        save();
    }

    private void Update(Action change)
    {
        change();
        save();
    }

    private bool UpdateSlider(Action change)
    {
        change();
        long now = capi.ElapsedMilliseconds;
        if (now - lastSliderSaveAtMs > 250)
        {
            lastSliderSaveAtMs = now;
            save();
        }

        return true;
    }

    private bool UpdatePendingDivineSight(int value)
    {
        pendingDivineSightStrength = Clamp(value, 0, 100);
        return true;
    }

    private void ApplyPendingDivineSight()
    {
        config.DivineSightStrength = Clamp(pendingDivineSightStrength, 0, 100);
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
