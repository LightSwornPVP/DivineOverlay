using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ChestOrganizer;
public class Main : ModSystem {
    public const string ID = "chestorganizer";
    public const string Hotkey = ID + ".openall";

    private static Harmony harmony;

    private ICoreClientAPI api;

    public override void StartPre(ICoreAPI api)
        => (harmony ??= new Harmony(ID)).PatchAll();

    public override void Dispose()
        => harmony?.UnpatchAll(ID);

    public override void StartClientSide(ICoreClientAPI api) {
        this.api = api;
        Patch_ChestDialog.Setup(api);
        Icons.Setup(api);

        api.Input.RegisterHotKey(
            Hotkey,
            "Open all containers in range",
            GlKeys.R,
            HotkeyType.CharacterControls);
        api.Input.SetHotKeyHandler(Hotkey, _ => OpenAllInRange());
    }

    public bool OpenAllInRange(BlockSelection clickedSelection = null) {
        var player = api.World.Player;
        if (player.WorldData.CurrentGameMode == EnumGameMode.Creative) return false;
        var reinforcement = api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();

        float range = player.WorldData.PickingRange;
        float rangesq = range * range;
        var eyePos = player.Entity.Pos.XYZ.Add(player.Entity.LocalEyePos - 0.5f);
        var accessor = api.World.BlockAccessor;
        List<BlockEntity> chests = new();

        if (clickedSelection?.Position != null && !api.OpenedGuis.OfType<GuiDialogBlockEntityInventory>().Any(dialog => dialog.BlockEntityPosition == clickedSelection.Position))
        {
            var clicked = accessor.GetBlockEntity(clickedSelection.Position);
            if (clicked?.FindInventory() != null
                && IsWithinReach(clickedSelection.Position, eyePos, rangesq)
                && !reinforcement.IsLockedForInteract(clickedSelection.Position, player))
            {
                chests.Add(clicked);
            }
        }

        accessor.WalkBlocks((eyePos - range).AsBlockPos, (eyePos + (range + 1.0f)).AsBlockPos, Step);
        MergedInventory.MergeRange(chests.OrderBy(chest => ChestDistanceSq(chest.Pos, eyePos)), api);

        return chests.Count > 0;

        void Step(Block b, int x, int y, int z) {
            BlockPos pos = new(x, y, z);
            if (!IsWithinReach(pos, eyePos, rangesq)) return;
            var entity = accessor.GetBlockEntity(pos);
            bool locked = reinforcement.IsLockedForInteract(pos, player);
            if (!locked && entity?.FindInventory() != null && !chests.Contains(entity)) {
                chests.Add(entity);
            }
        }
    }

    private static double ChestDistanceSq(BlockPos pos, Vec3d eyePos) {
        double dx = eyePos.X - (pos.X + 0.5);
        double dy = eyePos.Y - (pos.Y + 0.5);
        double dz = eyePos.Z - (pos.Z + 0.5);
        return dx * dx + dy * dy + dz * dz;
    }

    private static bool IsWithinReach(BlockPos pos, Vec3d eyePos, float rangesq) {
        double nearestX = GameMath.Clamp(eyePos.X, pos.X, pos.X + 1);
        double nearestY = GameMath.Clamp(eyePos.Y, pos.Y, pos.Y + 1);
        double nearestZ = GameMath.Clamp(eyePos.Z, pos.Z, pos.Z + 1);
        double dx = eyePos.X - nearestX;
        double dy = eyePos.Y - nearestY;
        double dz = eyePos.Z - nearestZ;
        return dx * dx + dy * dy + dz * dz <= rangesq;
    }

    private bool IsRightClickOpenAllEnabled() {
        Divine.DivineConfig config = api.LoadModConfig<Divine.DivineConfig>("Divine.json") ?? new();
        return config.StorageRightClickOpenAll;
    }
}
