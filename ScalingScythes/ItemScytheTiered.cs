using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Newtonsoft.Json.Linq;

namespace ScalingScythes
{
    public class ItemScytheTiered : Item
    {
        // Define harvest sizes for each tier [width, depth]
        private static readonly Dictionary<string, int[]> TierHarvestSizes = new Dictionary<string, int[]>
        {
            { "copper", new int[] { 3, 2 } },          // 6 blocks (vanilla)
            { "tinbronze", new int[] { 3, 3 } },       // 9 blocks
            { "bismuthbronze", new int[] { 3, 4 } },   // 12 blocks
            { "blackbronze", new int[] { 3, 5 } },     // 15 blocks
            { "iron", new int[] { 4, 4 } },            // 16 blocks
            { "meteoriciron", new int[] { 4, 5 } },    // 20 blocks
            { "steel", new int[] { 5, 5 } }            // 25 blocks
        };

        private int harvestWidth = 3;
        private int harvestDepth = 2;
        private string[] allowedPrefixes;
        private string[] disallowedSuffixes;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // Load the allowed/disallowed block types from attributes
            this.allowedPrefixes = this.Attributes?["codePrefixes"]?.AsArray<string>() ?? new string[] { "crop", "tallgrass", "flower" };
            this.disallowedSuffixes = this.Attributes?["disallowedSuffixes"]?.AsArray<string>() ?? new string[0];

            // Determine the harvest size for this tier
            string metal = GetMaterialVariant();
            if (!string.IsNullOrEmpty(metal) && TierHarvestSizes.ContainsKey(metal))
            {
                int[] size = TierHarvestSizes[metal];
                harvestWidth = size[0];
                harvestDepth = size[1];
                api.Logger.Warning($"[ScalingScythes] {Code?.Path}: Set harvest size to {harvestWidth}x{harvestDepth}");
            }
        }

        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            if (blockSel == null) return;

            IPlayer player = (byEntity as EntityPlayer)?.Player;
            if (player == null) return;

            byEntity.World.Api.Logger.Error($"[ScalingScythes] OnHeldAttackStart - Starting harvest with {harvestWidth}x{harvestDepth}");

            byEntity.Attributes.SetBool("didBreakBlocks", false);
            byEntity.Attributes.SetBool("didPlayScytheSound", false);
            handling = EnumHandHandling.PreventDefault;
        }

        public override bool OnHeldAttackStep(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            PerformHarvest(secondsPassed, byEntity, slot, blockSel);
            return api.Side == EnumAppSide.Server || secondsPassed < 2f;
        }

        public override void OnHeldAttackStop(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            PerformHarvest(secondsPassed, byEntity, slot, blockSel);
        }

        private void PerformHarvest(float secondsPassed, EntityAgent byEntity, ItemSlot slot, BlockSelection blockSel)
        {
            if (blockSel == null) return;

            IPlayer player = (byEntity as EntityPlayer)?.Player;
            if (player == null) return;

            Block block = api.World.BlockAccessor.GetBlock(blockSel.Position);
            bool harvestable = CanHarvestBlock(block);

            // Play sound at 0.75 seconds
            if (harvestable && secondsPassed > 0.75f && !byEntity.Attributes.GetBool("didPlayScytheSound", false))
            {
                api.World.PlaySoundAt(new AssetLocation("sounds/player/strike"), byEntity, player, true, 16f, 1f);
                byEntity.Attributes.SetBool("didPlayScytheSound", true);
            }

            // Actually harvest at 1.05 seconds
            if (harvestable && secondsPassed > 1.05f && !byEntity.Attributes.GetBool("didBreakBlocks", false))
            {
                if (byEntity.World.Side == EnumAppSide.Server)
                {
                    HarvestArea(blockSel, player, slot);
                }
                byEntity.Attributes.SetBool("didBreakBlocks", true);
            }
        }

        private void HarvestArea(BlockSelection blockSel, IPlayer player, ItemSlot slot)
        {
            api.World.Api.Logger.Error($"[ScalingScythes] HarvestArea called - {harvestWidth}x{harvestDepth}");

            BlockPos centerPos = blockSel.Position;
            int maxBlocks = harvestWidth * harvestDepth;
            int blocksHarvested = 0;

            // Calculate offsets to center the harvest area
            int widthOffset = harvestWidth / 2;
            int depthOffset = harvestDepth / 2;

            // Collect all harvestable blocks with their distance from center
            List<KeyValuePair<BlockPos, double>> harvestableBlocks = new List<KeyValuePair<BlockPos, double>>();

            // Scan 3D volume centered on the clicked block
            // Y ranges from -1 to +1 to catch blocks at different heights (like vanilla)
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -widthOffset; x < harvestWidth - widthOffset; x++)
                {
                    for (int z = -depthOffset; z < harvestDepth - depthOffset; z++)
                    {
                        BlockPos targetPos = centerPos.AddCopy(x, y, z);
                        Block block = api.World.BlockAccessor.GetBlock(targetPos);

                        if (CanHarvestBlock(block) && api.World.Claims.TryAccess(player, targetPos, EnumBlockAccessFlags.BuildOrBreak))
                        {
                            // Calculate distance from center (prioritize closer blocks)
                            double distance = Math.Sqrt(x * x + y * y + z * z);
                            harvestableBlocks.Add(new KeyValuePair<BlockPos, double>(targetPos, distance));
                        }
                    }
                }
            }

            // Sort by distance (closest first)
            harvestableBlocks.Sort((a, b) => a.Value.CompareTo(b.Value));

            // Harvest up to maxBlocks, prioritizing closest ones
            foreach (var entry in harvestableBlocks)
            {
                if (blocksHarvested >= maxBlocks) break;
                if (slot.Itemstack == null) break; // Tool broke

                BlockPos targetPos = entry.Key;
                Block block = api.World.BlockAccessor.GetBlock(targetPos);

                // Handle grass specially - leave stubble
                if (block.Code.Path.StartsWith("tallgrass-"))
                {
                    // Try to get the "eaten" variant (stubble)
                    Block trimmedBlock = api.World.GetBlock(block.CodeWithVariant("tallgrass", "eaten"));

                    if (trimmedBlock != null && trimmedBlock != block)
                    {
                        // Give drops to player
                        block.OnBlockBroken(api.World, targetPos, player);

                        // Place stubble (don't call BreakBlock again, just replace)
                        api.World.BlockAccessor.SetBlock(trimmedBlock.BlockId, targetPos);

                        // Make it temporary so it grows back
                        BlockEntityTransient be = api.World.BlockAccessor.GetBlockEntity(targetPos) as BlockEntityTransient;
                        if (be != null)
                        {
                            be.ConvertToOverride = block.Code.ToShortString();
                        }
                    }
                    else
                    {
                        // No eaten variant, just break normally
                        api.World.BlockAccessor.BreakBlock(targetPos, player, 1f);
                    }
                }
                else
                {
                    // For crops and other harvestables, break normally
                    api.World.BlockAccessor.BreakBlock(targetPos, player, 1f);
                }

                api.World.BlockAccessor.MarkBlockDirty(targetPos);
                blocksHarvested++;

                // Damage the tool for each block
                DamageItem(api.World, player.Entity, slot, 1);
            }

            api.World.Api.Logger.Error($"[ScalingScythes] Harvested {blocksHarvested} blocks (max {maxBlocks})");
        }

        private bool CanHarvestBlock(Block block)
        {
            if (block == null || block.Code == null) return false;

            string blockPath = block.Code.Path;

            // Check allowed prefixes
            bool hasAllowedPrefix = false;
            foreach (string prefix in allowedPrefixes)
            {
                if (blockPath.StartsWith(prefix))
                {
                    hasAllowedPrefix = true;
                    break;
                }
            }

            if (!hasAllowedPrefix) return false;

            // Check disallowed suffixes
            foreach (string suffix in disallowedSuffixes)
            {
                if (blockPath.EndsWith(suffix))
                {
                    return false;
                }
            }

            return true;
        }

        private BlockFacing GetPerpendicularFacing(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.WEST;
            if (facing == BlockFacing.SOUTH) return BlockFacing.EAST;
            if (facing == BlockFacing.EAST) return BlockFacing.NORTH;
            if (facing == BlockFacing.WEST) return BlockFacing.SOUTH;
            return BlockFacing.NORTH;
        }

        private string GetMaterialVariant()
        {
            if (Code == null) return null;
            string code = Code.Path;
            if (!code.StartsWith("scythe-")) return null;
            return code.Substring(7);
        }
    }
}