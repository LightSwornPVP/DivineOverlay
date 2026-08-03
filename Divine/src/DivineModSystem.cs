using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Divine;

public sealed class DivineModSystem : ModSystem
{
    private const int CoordinateOffset = 512000;
    private ICoreClientAPI? capi;
    private DivineConfig config = new();
    private CrosshairIndicatorRenderer? renderer;
    private DivineSettingsDialog? settingsDialog;
    private readonly Dictionary<string, long> knownCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<WaypointMemory>> waypointHistoryByResource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<InventoryBase, Action<int>> inventorySubscriptions = new();
    private readonly Dictionary<InventoryBase, Dictionary<int, string?>> slotCodesByInventory = new();
    private bool pickupTrackingReady;
    private long pickupInitListenerId;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        config = LoadConfig(api);
        NormalizeConfig(api);

        renderer = new CrosshairIndicatorRenderer(api, config);
        api.Event.RegisterRenderer(renderer, EnumRenderStage.Ortho, "divine-crosshair");
        api.Event.RegisterRenderer(renderer, EnumRenderStage.AfterFinalComposition, "divine-target-overlay");
        pickupInitListenerId = api.Event.RegisterGameTickListener(EnsurePickupTrackingReady, 1000);

        settingsDialog = new DivineSettingsDialog(api, config, SaveConfig);
        api.Gui.RegisterDialog(settingsDialog);
        api.Input.RegisterHotKey(
            "divinesettings",
            "Open Divine settings",
            GlKeys.C,
            HotkeyType.GUIOrOtherControls,
            altPressed: true
        );
        api.Input.SetHotKeyHandler("divinesettings", _ =>
        {
            ToggleSettingsDialog();
            return true;
        });

        api.ChatCommands
            .Create("divine")
            .WithDescription("Open Divine settings")
            .HandleWith(args =>
            {
                bool opened = ToggleSettingsDialog();
                return opened
                    ? TextCommandResult.Success("Opened Divine settings.")
                    : TextCommandResult.Success("Could not open Divine settings. Check client-main.log.");
            });

        api.Input.RegisterHotKey(
            "divinepanicwaypoint",
            "Drop Divine danger waypoint",
            GlKeys.D,
            HotkeyType.GUIOrOtherControls,
            altPressed: true,
            ctrlPressed: true
        );
        api.Input.SetHotKeyHandler("divinepanicwaypoint", _ => CreatePanicWaypoint());

        api.Input.RegisterHotKey(
            "divinesight",
            "Toggle Divine Sight",
            GlKeys.B,
            HotkeyType.GUIOrOtherControls,
            altPressed: true,
            ctrlPressed: true
        );
        api.Input.SetHotKeyHandler("divinesight", _ => ToggleDivineSight());
    }

    public override void Dispose()
    {
        if (capi != null && renderer != null)
        {
            capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Ortho);
            capi.Event.UnregisterRenderer(renderer, EnumRenderStage.AfterFinalComposition);
        }

        if (capi != null && pickupInitListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(pickupInitListenerId);
            pickupInitListenerId = 0;
        }

        foreach ((InventoryBase inventory, Action<int> handler) in inventorySubscriptions)
        {
            inventory.SlotModified -= handler;
        }

        inventorySubscriptions.Clear();
        slotCodesByInventory.Clear();
    }

    private static DivineConfig LoadConfig(ICoreClientAPI api)
    {
        DivineConfig? loaded = api.LoadModConfig<DivineConfig>("Divine.json");
        if (loaded != null) return loaded;

        loaded = new DivineConfig();
        api.StoreModConfig(loaded, "Divine.json");
        return loaded;
    }

    private void NormalizeConfig(ICoreClientAPI api)
    {
        bool changed = false;
        if (config.TrackedItemContains == null || config.TrackedItemContains.Length == 0 || ContainsTrackedValue("orebit") || !ContainsTrackedValue("nugget"))
        {
            config.TrackedItemContains = new[] { "ore", "bit", "nugget", "resin", "clay" };
            changed = true;
        }

        if (config.MinimumDistanceBetweenSameResourceWaypoints <= 0)
        {
            config.MinimumDistanceBetweenSameResourceWaypoints = 50;
            changed = true;
        }

        if (config.InventoryScanIntervalMs < 1000)
        {
            config.InventoryScanIntervalMs = 2000;
            changed = true;
        }

        if (config.ReadyColor == null || config.ReadyColor.Length < 4)
        {
            ApplyColorPreset(config, config.ReadyColorPreset, true);
            changed = true;
        }

        if (config.TargetColor == null || config.TargetColor.Length < 4)
        {
            ApplyColorPreset(config, config.TargetColorPreset, false);
            changed = true;
        }

        changed |= NormalizeText(config.OreWaypointIcon, "pick", value => config.OreWaypointIcon = value);
        changed |= NormalizeText(config.OreWaypointColor, "orange", value => config.OreWaypointColor = value);
        changed |= NormalizeText(config.OreWaypointPrefix, "Ore", value => config.OreWaypointPrefix = value);
        changed |= NormalizeText(config.ClayWaypointIcon, "circle", value => config.ClayWaypointIcon = value);
        changed |= NormalizeText(config.ClayWaypointColor, "yellow", value => config.ClayWaypointColor = value);
        changed |= NormalizeText(config.ClayWaypointPrefix, "Clay", value => config.ClayWaypointPrefix = value);
        changed |= NormalizeText(config.ResinWaypointIcon, "star1", value => config.ResinWaypointIcon = value);
        changed |= NormalizeText(config.ResinWaypointColor, "green", value => config.ResinWaypointColor = value);
        changed |= NormalizeText(config.ResinWaypointPrefix, "Resin", value => config.ResinWaypointPrefix = value);
        changed |= NormalizeInt(config.TargetAssistStrength, 0, 100, value => config.TargetAssistStrength = value);
        changed |= NormalizeInt(config.CrosshairSize, 8, 64, value => config.CrosshairSize = value);
        changed |= NormalizeInt(config.CrosshairThickness, 1, 12, value => config.CrosshairThickness = value);
        changed |= NormalizeInt(config.AwarenessRadius, 8, 64, value => config.AwarenessRadius = value);
        changed |= NormalizeInt(config.DivineSightStrength, 0, 100, value => config.DivineSightStrength = value);
        if (changed)
        {
            api.StoreModConfig(config, "Divine.json");
        }
    }

    private void SaveConfig()
    {
        capi?.StoreModConfig(config, "Divine.json");
    }

    private static bool NormalizeText(string value, string fallback, Action<string> assign)
    {
        if (!string.IsNullOrWhiteSpace(value)) return false;

        assign(fallback);
        return true;
    }

    private static bool NormalizeInt(int value, int min, int max, Action<int> assign)
    {
        int normalized = Math.Max(min, Math.Min(max, value));
        if (normalized == value) return false;

        assign(normalized);
        return true;
    }

    private bool ToggleSettingsDialog()
    {
        if (settingsDialog == null) return false;

        if (settingsDialog.IsOpened())
        {
            return settingsDialog.TryClose();
        }

        try
        {
            bool opened = settingsDialog.TryOpen(true);
            if (!opened)
            {
                capi?.Logger.Warning("[Divine] Settings dialog TryOpen returned false.");
            }

            return opened;
        }
        catch (Exception ex)
        {
            capi?.Logger.Error("[Divine] Failed to open settings dialog: {0}", ex);
            capi?.ShowChatMessage("Divine settings failed to open. Check client-main.log.");
            return false;
        }
    }

    private bool ContainsTrackedValue(string value)
    {
        foreach (string tracked in config.TrackedItemContains)
        {
            if (tracked.Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private void EnsurePickupTrackingReady(float dt)
    {
        if (pickupTrackingReady || capi?.World?.Player?.InventoryManager == null) return;

        IPlayerInventoryManager invManager = capi.World.Player.InventoryManager;
        foreach (InventoryBase inv in invManager.InventoriesOrdered)
        {
            SubscribeInventory(inv);
            CacheInventory(inv);
        }

        pickupTrackingReady = true;
        capi.Event.UnregisterGameTickListener(pickupInitListenerId);
        pickupInitListenerId = 0;
    }

    private void SubscribeInventory(InventoryBase inventory)
    {
        if (inventorySubscriptions.ContainsKey(inventory)) return;

        Action<int> handler = slotId =>
        {
            OnInventorySlotChanged(inventory, slotId);
        };

        inventory.SlotModified += handler;
        inventorySubscriptions[inventory] = handler;
    }

    private void CacheInventory(InventoryBase inventory)
    {
        Dictionary<int, string?> slotCodes = new();
        slotCodesByInventory[inventory] = slotCodes;

        for (int slotId = 0; slotId < inventory.Count; slotId++)
        {
            ItemSlot slot = inventory[slotId];
            ItemStack? stack = slot.Itemstack;
            string? path = stack?.Collectible?.Code?.Path;
            slotCodes[slotId] = path;
            if (stack == null || path == null || !IsProbablyTrackedCode(path)) continue;

            knownCounts[path] = CountMatchingItems(path);
        }
    }

    private void OnInventorySlotChanged(InventoryBase inventory, int slotId)
    {
        if (capi?.World?.Player?.InventoryManager == null) return;

        ItemSlot slot = inventory[slotId];
        ItemStack? stack = slot.Itemstack;
        string? path = stack?.Collectible?.Code?.Path;
        if (!slotCodesByInventory.TryGetValue(inventory, out Dictionary<int, string?>? slotCodes))
        {
            slotCodes = new Dictionary<int, string?>();
            slotCodesByInventory[inventory] = slotCodes;
        }

        slotCodes.TryGetValue(slotId, out string? previousPath);
        slotCodes[slotId] = path;

        bool previousWasTracked = previousPath != null && IsProbablyTrackedCode(previousPath);
        bool currentIsTracked = path != null && IsProbablyTrackedCode(path);
        if (!previousWasTracked && !currentIsTracked) return;

        long previousCount = 0;
        if (currentIsTracked)
        {
            knownCounts.TryGetValue(path!, out previousCount);
        }

        if (previousWasTracked && !string.Equals(previousPath, path, StringComparison.OrdinalIgnoreCase))
        {
            knownCounts[previousPath!] = CountMatchingItems(previousPath!);
        }

        if (!config.WaypointsEnabled || stack == null || path == null || !currentIsTracked) return;

        string name = stack.GetName();
        if (!IsTracked(path, name)) return;

        long currentCount = CountMatchingItems(path);
        knownCounts[path] = currentCount;

        if (currentCount <= previousCount) return;

        ResourceKind kind = GetResourceKind(path, name);
        TryCreateWaypoint(new PickupSnapshot(path, BuildResourceTitle(path, name, kind), currentCount, kind));
    }

    private long CountMatchingItems(string itemCodePath)
    {
        long count = 0;

        foreach (InventoryBase inventory in inventorySubscriptions.Keys)
        {
            foreach (ItemSlot slot in inventory)
            {
                ItemStack? stack = slot.Itemstack;
                string? path = stack?.Collectible?.Code?.Path;
                if (stack == null || !itemCodePath.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;

                count += stack.StackSize;
            }
        }

        return count;
    }

    private bool IsTracked(string itemCodePath, string itemName)
    {
        string searchable = $"{itemCodePath} {itemName}";
        bool looksLikeOre = IsOreBits(itemCodePath, itemName);
        bool looksLikeClay = itemCodePath.Contains("clay", StringComparison.OrdinalIgnoreCase)
            || itemName.Contains("clay", StringComparison.OrdinalIgnoreCase);
        bool looksLikeResin = itemCodePath.Contains("resin", StringComparison.OrdinalIgnoreCase)
            || itemName.Contains("resin", StringComparison.OrdinalIgnoreCase);

        if (looksLikeOre && !config.TrackOreBits) return false;
        if (looksLikeClay && !config.TrackClay) return false;
        if (looksLikeResin && !config.TrackResin) return false;

        bool matchesConfig = false;
        foreach (string needle in config.TrackedItemContains)
        {
            if (string.IsNullOrWhiteSpace(needle)) continue;
            if (!searchable.Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;

            matchesConfig = true;
            break;
        }

        return matchesConfig || looksLikeOre || looksLikeClay || looksLikeResin;
    }

    private bool IsProbablyTrackedCode(string itemCodePath)
    {
        bool looksLikeOre = itemCodePath.Contains("ore", StringComparison.OrdinalIgnoreCase)
            || itemCodePath.Contains("bit", StringComparison.OrdinalIgnoreCase)
            || itemCodePath.Contains("nugget", StringComparison.OrdinalIgnoreCase);
        bool looksLikeClay = itemCodePath.Contains("clay", StringComparison.OrdinalIgnoreCase);
        bool looksLikeResin = itemCodePath.Contains("resin", StringComparison.OrdinalIgnoreCase);

        if (looksLikeOre && config.TrackOreBits) return true;
        if (looksLikeClay && config.TrackClay) return true;
        if (looksLikeResin && config.TrackResin) return true;

        return false;
    }

    public static void ApplyColorPreset(DivineConfig config, string preset, bool readyColor)
    {
        string normalizedPreset = string.IsNullOrWhiteSpace(preset) ? (readyColor ? "Green" : "Red") : preset;
        float[] color = normalizedPreset.ToLowerInvariant() switch
        {
            "white" => new[] { 1.0f, 1.0f, 1.0f, 0.95f },
            "cyan" => new[] { 0.1f, 0.9f, 1.0f, 0.95f },
            "blue" => new[] { 0.2f, 0.45f, 1.0f, 0.95f },
            "purple" => new[] { 0.75f, 0.3f, 1.0f, 0.95f },
            "yellow" => new[] { 1.0f, 0.9f, 0.15f, 0.95f },
            "orange" => new[] { 1.0f, 0.55f, 0.1f, 0.95f },
            "red" => new[] { 1.0f, 0.2f, 0.15f, 0.95f },
            _ => new[] { 0.1f, 1.0f, 0.25f, 0.95f }
        };

        if (readyColor)
        {
            config.ReadyColorPreset = normalizedPreset;
            config.ReadyColor = color;
        }
        else
        {
            config.TargetColorPreset = normalizedPreset;
            config.TargetColor = color;
        }
    }

    private void TryCreateWaypoint(PickupSnapshot snapshot)
    {
        if (capi == null) return;

        EntityPlayer? entity = capi.World.Player.Entity;
        if (entity == null) return;

        Vec3d pos = entity.Pos.XYZ;
        long now = capi.ElapsedMilliseconds;
        if (!waypointHistoryByResource.TryGetValue(snapshot.Code, out List<WaypointMemory>? history))
        {
            history = new List<WaypointMemory>();
            waypointHistoryByResource[snapshot.Code] = history;
        }

        double minDistance = Math.Max(0, config.MinimumDistanceBetweenSameResourceWaypoints);
        long cooldownMs = Math.Max(1, config.WaypointCooldownSeconds) * 1000L;
        foreach (WaypointMemory mark in history)
        {
            if (mark.Position.DistanceTo(pos) < minDistance) return;
            if (now - mark.CreatedAtMs < cooldownMs) return;
        }

        string title = CleanTitle(snapshot.Title);
        if (config.ResourceDangerOverlay && HasNearbyHostile(pos, 18))
        {
            title = $"{title} danger";
        }

        string command = BuildWaypointCommand(pos, title, snapshot.Kind);

        history.Add(new WaypointMemory(pos.Clone(), now));
        capi.SendChatMessage(command);
        capi.ShowChatMessage($"Divine marked {title}");
    }

    private string BuildWaypointCommand(Vec3d pos, string title, ResourceKind kind)
    {
        int x = (int)Math.Round(pos.X - CoordinateOffset);
        int y = (int)Math.Round(pos.Y);
        int z = (int)Math.Round(pos.Z - CoordinateOffset);
        ResourceWaypointStyle style = GetWaypointStyle(kind);

        return FormattableString.Invariant($"/waypoint addati {style.Icon} {x} {y} {z} {style.Color} {title}");
    }

    private bool CreatePanicWaypoint()
    {
        if (capi == null || !config.PanicWaypoint) return false;

        EntityPlayer? entity = capi.World.Player.Entity;
        if (entity == null) return false;

        Vec3d pos = entity.Pos.XYZ;
        int x = (int)Math.Round(pos.X - CoordinateOffset);
        int y = (int)Math.Round(pos.Y);
        int z = (int)Math.Round(pos.Z - CoordinateOffset);
        capi.SendChatMessage(FormattableString.Invariant($"/waypoint addati circle {x} {y} {z} red Danger"));
        capi.ShowChatMessage("Divine marked Danger");
        return true;
    }

    private bool ToggleDivineSight()
    {
        if (capi == null) return false;

        config.DivineSightEnabled = !config.DivineSightEnabled;
        if (config.DivineSightEnabled && config.DivineSightStrength <= 0)
        {
            config.DivineSightStrength = 75;
        }

        SaveConfig();
        capi.ShowChatMessage(config.DivineSightEnabled
            ? $"Divine Sight on ({config.DivineSightStrength}%)"
            : "Divine Sight off");
        return true;
    }

    private bool HasNearbyHostile(Vec3d pos, float radius)
    {
        if (capi == null) return false;

        foreach (Entity entity in capi.World.GetEntitiesAround(pos, radius, 12))
        {
            if (!LooksHostile(entity)) continue;
            return true;
        }

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

    private string BuildResourceTitle(string itemCodePath, string itemName, ResourceKind kind)
    {
        string cleanName = CleanTitle(itemName);
        string prefix = GetWaypointStyle(kind).Prefix;

        return $"{prefix}: {cleanName}";
    }

    private ResourceKind GetResourceKind(string itemCodePath, string itemName)
    {
        if (itemCodePath.Contains("resin", StringComparison.OrdinalIgnoreCase)
            || itemName.Contains("resin", StringComparison.OrdinalIgnoreCase))
        {
            return ResourceKind.Resin;
        }

        if (itemCodePath.Contains("clay", StringComparison.OrdinalIgnoreCase)
            || itemName.Contains("clay", StringComparison.OrdinalIgnoreCase))
        {
            return ResourceKind.Clay;
        }

        if (IsOreBits(itemCodePath, itemName))
        {
            return ResourceKind.Ore;
        }

        return ResourceKind.Other;
    }

    private ResourceWaypointStyle GetWaypointStyle(ResourceKind kind)
    {
        return kind switch
        {
            ResourceKind.Ore => new ResourceWaypointStyle(config.OreWaypointIcon, config.OreWaypointColor, config.OreWaypointPrefix),
            ResourceKind.Clay => new ResourceWaypointStyle(config.ClayWaypointIcon, config.ClayWaypointColor, config.ClayWaypointPrefix),
            ResourceKind.Resin => new ResourceWaypointStyle(config.ResinWaypointIcon, config.ResinWaypointColor, config.ResinWaypointPrefix),
            _ => new ResourceWaypointStyle("circle", config.WaypointColor, "Resource")
        };
    }

    private static bool IsOreBits(string itemCodePath, string itemName)
    {
        return itemCodePath.Contains("ore", StringComparison.OrdinalIgnoreCase)
            || itemName.Contains("ore", StringComparison.OrdinalIgnoreCase)
            || itemName.Contains("bit", StringComparison.OrdinalIgnoreCase)
            || itemCodePath.Contains("bit", StringComparison.OrdinalIgnoreCase)
            || itemName.Contains("nugget", StringComparison.OrdinalIgnoreCase)
            || itemCodePath.Contains("nugget", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanTitle(string title)
    {
        string cleaned = title.Replace("\"", "").Replace("\n", " ").Replace("\r", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "resource" : cleaned;
    }

    private struct PickupSnapshot
    {
        public string Code;
        public string Title;
        public long Count;
        public ResourceKind Kind;

        public PickupSnapshot(string code, string title, long count, ResourceKind kind)
        {
            Code = code;
            Title = title;
            Count = count;
            Kind = kind;
        }
    }

    private enum ResourceKind
    {
        Other,
        Ore,
        Clay,
        Resin
    }

    private readonly struct ResourceWaypointStyle
    {
        public readonly string Icon;
        public readonly string Color;
        public readonly string Prefix;

        public ResourceWaypointStyle(string icon, string color, string prefix)
        {
            Icon = string.IsNullOrWhiteSpace(icon) ? "circle" : icon;
            Color = string.IsNullOrWhiteSpace(color) ? "orange" : color;
            Prefix = string.IsNullOrWhiteSpace(prefix) ? "Resource" : prefix;
        }
    }

    private readonly struct WaypointMemory
    {
        public readonly Vec3d Position;
        public readonly long CreatedAtMs;

        public WaypointMemory(Vec3d position, long createdAtMs)
        {
            Position = position;
            CreatedAtMs = createdAtMs;
        }
    }
}
