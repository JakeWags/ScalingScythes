using HarmonyLib;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ScalingScythes
{
    [HarmonyPatch(typeof(ItemScythe))]
    public class ItemScythePatch
    {
        // Patch the MultiBreakQuantity getter to return tier-based values
        [HarmonyPatch("MultiBreakQuantity", MethodType.Getter)]
        [HarmonyPostfix]
        public static void MultiBreakQuantity_Postfix(ItemScythe __instance, ref int __result)
        {
            string metal = GetMaterialVariant(__instance);
            if (string.IsNullOrEmpty(metal) || metal == "copper") return;

            int tierBlocks = GetTierBlocks(metal);
            if (tierBlocks > 0)
            {
                __result = tierBlocks;
            }
        }

        // Patch performActions to use custom search area
        [HarmonyPatch("performActions")]
        [HarmonyPrefix]
        public static bool PerformActions_Prefix(
            ItemScythe __instance,
            float secondsPassed,
            EntityAgent byEntity,
            ItemSlot slot,
            BlockSelection blockSelection)
        {
            string metal = GetMaterialVariant(__instance);

            // Let vanilla handle copper
            if (string.IsNullOrEmpty(metal) || metal == "copper")
            {
                return true;
            }

            // Custom implementation for upgraded tiers
            if (blockSelection == null) return false;

            EntityPlayer entityPlayer = byEntity as EntityPlayer;
            IPlayer player = entityPlayer?.Player;
            if (player == null) return false;

            // Check if enough time has passed and we haven't broken blocks yet
            bool harvestable = __instance.CanMultiBreak(byEntity.World.BlockAccessor.GetBlock(blockSelection.Position));

            if (harvestable && secondsPassed > 0.75f && !byEntity.Attributes.GetBool("didPlayScytheSound", false))
            {
                byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/scythe1"), byEntity, player, true, 16f, 1f);
                byEntity.Attributes.SetBool("didPlayScytheSound", true);
            }

            if (harvestable && secondsPassed > 1.05f && !byEntity.Attributes.GetBool("didBreakBlocks", false))
            {
                if (byEntity.World.Side == EnumAppSide.Server && byEntity.World.Claims.TryAccess(player, blockSelection.Position, EnumBlockAccessFlags.BuildOrBreak))
                {
                    HarvestCustomArea(__instance, blockSelection.Position, player, slot, metal);
                }
                byEntity.Attributes.SetBool("didBreakBlocks", true);
            }

            return false; // Skip original method
        }

        private static void HarvestCustomArea(ItemScythe scythe, BlockPos centerPos, IPlayer player, ItemSlot slot, string metal)
        {
            int[] dimensions = GetSearchDimensions(metal);
            int maxBlocks = GetTierBlocks(metal);

            // Collect harvestable blocks
            List<BlockPos> harvestableBlocks = new List<BlockPos>();
            Vec3d centerVec = centerPos.ToVec3d().Add(0.5, 0.5, 0.5);

            int rangeX = dimensions[0] / 2;
            int rangeZ = dimensions[1] / 2;

            for (int dx = -rangeX; dx <= rangeX; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -rangeZ; dz <= rangeZ; dz++)
                    {
                        BlockPos pos = centerPos.AddCopy(dx, dy, dz);
                        Block block = player.Entity.World.BlockAccessor.GetBlock(pos);

                        if (scythe.CanMultiBreak(block))
                        {
                            harvestableBlocks.Add(pos);
                        }
                    }
                }
            }

            // Sort by distance from center
            harvestableBlocks.Sort((a, b) =>
            {
                double distA = centerVec.SquareDistanceTo(a.X + 0.5, a.Y + 0.5, a.Z + 0.5);
                double distB = centerVec.SquareDistanceTo(b.X + 0.5, b.Y + 0.5, b.Z + 0.5);
                return distA.CompareTo(distB);
            });

            // Harvest up to maxBlocks
            int harvested = 0;
            foreach (BlockPos pos in harvestableBlocks)
            {
                if (harvested >= maxBlocks) break;
                if (player.Entity.World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak))
                {
                    Block block = player.Entity.World.BlockAccessor.GetBlock(pos);

                    // Handle grass specially - leave stubble
                    if (block.Code.Path.StartsWith("tallgrass-"))
                    {
                        // Try to get the "eaten" variant (stubble)
                        Block trimmedBlock = player.Entity.World.GetBlock(block.CodeWithVariant("tallgrass", "eaten"));

                        if (trimmedBlock != null && trimmedBlock != block)
                        {
                            // Give drops to player
                            block.OnBlockBroken(player.Entity.World, pos, player);

                            // Place stubble (don't call BreakBlock again, just replace)
                            player.Entity.World.BlockAccessor.SetBlock(trimmedBlock.BlockId, pos);

                            // Make it temporary so it grows back
                            BlockEntityTransient be = player.Entity.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityTransient;
                            if (be != null)
                            {
                                be.ConvertToOverride = block.Code.ToShortString();
                            }
                        }
                        else
                        {
                            // No eaten variant, just break normally
                            player.Entity.World.BlockAccessor.BreakBlock(pos, player, 1f);
                        }
                    }
                    else
                    {
                        // For crops and other harvestables, break normally
                        player.Entity.World.BlockAccessor.BreakBlock(pos, player, 1f);
                    }

                    player.Entity.World.BlockAccessor.MarkBlockDirty(pos);
                    scythe.DamageItem(player.Entity.World, player.Entity, slot, 1);
                    harvested++;

                    if (slot.Itemstack == null) break; // Tool broke
                }
            }
        }

        private static string GetMaterialVariant(ItemScythe scythe)
        {
            if (scythe?.Code == null) return null;
            string code = scythe.Code.Path;
            if (!code.StartsWith("scythe-")) return null;
            return code.Substring(7);
        }

        private static int GetTierBlocks(string metal)
        {
            return metal switch
            {
                "copper" => 5,        // Vanilla is actually 5, not 6!
                "tinbronze" => 9,
                "bismuthbronze" => 12,
                "blackbronze" => 15,
                "iron" => 16,
                "meteoriciron" => 20,
                "steel" => 25,
                _ => -1
            };
        }

        private static int[] GetSearchDimensions(string metal)
        {
            return metal switch
            {
                "copper" => new int[] { 3, 2 },
                "tinbronze" => new int[] { 3, 3 },
                "bismuthbronze" => new int[] { 3, 4 },
                "blackbronze" => new int[] { 3, 5 },
                "iron" => new int[] { 4, 4 },
                "meteoriciron" => new int[] { 4, 5 },
                "steel" => new int[] { 5, 5 },
                _ => new int[] { 3, 2 }
            };
        }
    }
}