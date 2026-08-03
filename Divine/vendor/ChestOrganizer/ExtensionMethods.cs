using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ChestOrganizer;
public static class ExtensionMethods {
    public static AssetLocation? FindOpenSound(this BlockEntityOpenableContainer self) {
        Block block = self.Api.World.BlockAccessor.GetBlock(self.Pos);
        return block.Attributes?["openSound"]?.AsAssetLocation(block.Code.Domain);
    }

    public static AssetLocation? FindCloseSound(this BlockEntityOpenableContainer self) {
        Block block = self.Api.World.BlockAccessor.GetBlock(self.Pos);
        return block.Attributes?["closeSound"]?.AsAssetLocation(block.Code.Domain)
            ?? block.Attributes?["openSound"]?.AsAssetLocation(block.Code.Domain);
    }

    public static string GetDialogTitle(this BlockEntityOpenableContainer self) {
        if (self is BlockEntityGenericContainer generic) {
            return Lang.Get(generic.dialogTitleLangCode);
        } else if (self is BlockEntityGenericTypedContainer typed) {
            return typed.DialogTitle;
        }
        self.Api.Logger.Warning($"Could not get dialog title for container entity of type {self.GetType().FullName}.");
        return null;
    }

    public static int FindColumns(this BlockEntity self) 
        => (self is BlockEntityGenericTypedContainer typed) ? typed.quantityColumns : 4;

    public static InventoryBase FindInventory(this BlockEntity self) {
        if (self is BlockEntityOpenableContainer openable) return openable.Inventory;

        var type = self.GetType();
        var flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;
        var prop = type.GetProperty("Inventory", flags);
        if (prop?.GetValue(self) is InventoryBase propInventory) return propInventory;

        var field = type.GetField("inventory", flags) ?? type.GetField("Inventory", flags);
        if (field?.GetValue(self) is InventoryBase fieldInventory) return fieldInventory;

        return null;
    }

    public static string FindDialogTitle(this BlockEntity self) {
        if (self is BlockEntityOpenableContainer openable) {
            return openable.GetDialogTitle();
        }

        return self.Block?.Code?.Path ?? self.GetType().Name;
    }


    public static AssetLocation AsAssetLocation(this JsonObject self, string domain) {
        var value = self.AsString();
        if (value == null) return null;
        return AssetLocation.Create(value, domain);
    }

    public static (double, double) ClosestInside(this Rectangled rect, double x, double y) 
        => (x.ClosestInRange(rect.X, rect.Width), y.ClosestInRange(rect.Y, rect.Height));

    public static double ClosestInRange(this double value, double min, double length) {
        if (value < min) return min;
        double max = min + length;
        if (value > max) return max;
        return value;
    }

    private static ScrolledBounds scrollBounds = null;

    public static GuiComposer BeginScroll(this GuiComposer composer, ScrolledBounds bounds, string key = null) {
        scrollBounds = bounds;
        return bounds.BeginScroll(composer, key);
    }

    public static GuiComposer EndScroll(this GuiComposer composer) {
        var bounds = scrollBounds;
        scrollBounds = null;
        return bounds?.EndScroll(composer) ?? composer;
    }

    public static bool ModifierDown(this ICoreClientAPI self, Modifier modifiers) {
        var keys = self.Input.KeyboardKeyState;
        bool and = IsSet(Modifier.And);
        if (Check(Modifier.Shift,   GlKeys.LShift,   GlKeys.RShift)  ) return !and;
        if (Check(Modifier.Control, GlKeys.LControl, GlKeys.RControl)) return !and;
        if (Check(Modifier.Alt,     GlKeys.LAlt,     GlKeys.RAlt)    ) return !and;
        return and;

        bool Check(Modifier m, GlKeys key1, GlKeys key2)
            => IsSet(m) && ((keys[(int) key1] || keys[(int) key2]) == !and);

        bool IsSet(Modifier m) 
            => (modifiers & m) != 0;
    }
}

public enum Modifier {
    Shift   = 1,
    Control = 2,
    Alt     = 4,

    Or      = 0,
    And     = 8,
}
