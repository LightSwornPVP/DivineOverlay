using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ChestOrganizer;

[HarmonyPatch]
public static class Patch_RightClickOpenAll {
    private static ICoreClientAPI api;
    private static System.Func<BlockSelection, bool> openAll;
    private static System.Func<bool> isEnabled;
    private static BlockSelection pendingSelection;
    private static long pendingUntilMs;

    public static void Setup(ICoreClientAPI api, System.Func<BlockSelection, bool> openAll, System.Func<bool> isEnabled) {
        Patch_RightClickOpenAll.api = api;
        Patch_RightClickOpenAll.openAll = openAll;
        Patch_RightClickOpenAll.isEnabled = isEnabled;
    }

    public static IEnumerable<MethodBase> TargetMethods() {
        HashSet<MethodBase> methods = new();
        foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(BlockEntityOpenableContainer).Assembly)) {
            if (!typeof(Block).IsAssignableFrom(type)) continue;
            if (!LooksLikeContainerBlock(type)) continue;

            MethodInfo method = AccessTools.Method(type, "OnBlockInteractStart");
            if (method != null && methods.Add(method)) yield return method;
        }
    }

    private static bool LooksLikeContainerBlock(System.Type type) {
        string name = type.Name.ToLowerInvariant();
        return name.Contains("container")
            || name.Contains("chest")
            || name.Contains("trunk")
            || name.Contains("crate")
            || name.Contains("basket")
            || name.Contains("shelf");
    }

    public static bool Prefix(object[] __args) {
        if (api?.World?.Player == null) return true;
        if (!(isEnabled?.Invoke() ?? false)) return true;

        IPlayer player = __args?.OfType<IPlayer>().FirstOrDefault();
        if (player == null || player != api.World.Player) return true;

        BlockSelection selection = __args.OfType<BlockSelection>().FirstOrDefault();
        if (selection == null) return true;

        BlockEntityOpenableContainer container = api.World.BlockAccessor.GetBlockEntity(selection.Position) as BlockEntityOpenableContainer;
        if (container == null) return true;

        pendingSelection = selection;
        pendingUntilMs = api.ElapsedMilliseconds + 750;
        return true;
    }

    public static bool TryConsumePending(GuiDialogBlockEntityInventory dialog) {
        if (api?.World?.Player == null || dialog == null) return false;
        if (pendingSelection == null || api.ElapsedMilliseconds > pendingUntilMs) return false;
        if (dialog.BlockEntityPosition != pendingSelection.Position) return false;

        BlockSelection selection = pendingSelection;
        pendingSelection = null;
        pendingUntilMs = 0;

        api.Event.EnqueueMainThreadTask(() => {
            if (Patch_ChestDialog.BlockCloseInventory(dialog.TryClose)) {
                MergedInventory.MergeFromDialog(dialog, api);
                openAll?.Invoke(selection);
            }
        }, "divine-rightclick-openall");

        return true;
    }
}
