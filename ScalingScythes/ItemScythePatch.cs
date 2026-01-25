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

        // Use Postfix to perform additional harvesting after vanilla logic completes
        [HarmonyPatch("performActions")]
        [HarmonyPostfix]
        public static void PerformActions_Postfix(
            ItemScythe __instance,
            float secondsPassed,
            EntityAgent byEntity,
            ItemSlot slot,
            BlockSelection blockSelection)
        {
            string metal = GetMaterialVariant(__instance);

            // Only do extra harvesting for upgraded tiers
            if (string.IsNullOrEmpty(metal) || metal == "copper") return;

            if (blockSelection == null) return;

            EntityPlayer entityPlayer = byEntity as EntityPlayer;
            IPlayer player = entityPlayer?.Player;
            if (player == null) return;

            // Reset flag when animation cycle restarts
            if (secondsPassed < 0.1f)
            {
                byEntity.Attributes.RemoveAttribute("didExtendedHarvest");
                return;
            }

            // Check if vanilla just finished breaking blocks
            if (!byEntity.Attributes.GetBool("didBreakBlocks", false)) return;

            // Check if we already did our extended harvest this cycle
            if (byEntity.Attributes.GetBool("didExtendedHarvest", false)) return;

            bool harvestable = __instance.CanMultiBreak(byEntity.World.BlockAccessor.GetBlock(blockSelection.Position));

            if (harvestable && byEntity.World.Side == EnumAppSide.Server)
            {
                // Perform additional harvesting beyond vanilla's 3x2 area
                HarvestExtendedArea(__instance, blockSelection.Position, player, slot, metal);
                byEntity.Attributes.SetBool("didExtendedHarvest", true);
            }
        }

        private static void HarvestExtendedArea(ItemScythe scythe, BlockPos centerPos, IPlayer player, ItemSlot slot, string metal)
        {
            int[] dimensions = GetSearchDimensions(metal);
            int maxBlocks = GetTierBlocks(metal);

            // Collect harvestable blocks in extended area
            List<BlockPos> harvestableBlocks = new List<BlockPos>();
            Vec3d centerVec = centerPos.ToVec3d().Add(0.5, 0.5, 0.5);

            int rangeX = dimensions[0] / 2;
            int rangeZ = dimensions[1] / 2;

            // Search the full area
            for (int dx = -rangeX; dx <= rangeX; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -rangeZ; dz <= rangeZ; dz++)
                    {
                        // Skip vanilla's 3x2 area (already harvested)
                        if (dx >= -1 && dx <= 1 && dz >= -1 && dz <= 0) continue;

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

            // Calculate how many blocks vanilla already harvested (roughly 5 for copper)
            int vanillaHarvested = 5;
            int additionalBlocks = maxBlocks - vanillaHarvested;

            // Harvest additional blocks beyond vanilla's limit
            int harvested = 0;
            foreach (BlockPos pos in harvestableBlocks)
            {
                if (harvested >= additionalBlocks) break;

                if (player.Entity.World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak))
                {
                    // Use vanilla's breakMultiBlock method - this handles grass stubble, 
                    // drop spawning, and everything else the same way vanilla does
                    var breakMethod = typeof(ItemScythe).GetMethod("breakMultiBlock",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (breakMethod != null)
                    {
                        breakMethod.Invoke(scythe, new object[] { pos, player });
                    }
                    else
                    {
                        // Fallback if reflection fails
                        player.Entity.World.BlockAccessor.BreakBlock(pos, player, 1f);
                        player.Entity.World.BlockAccessor.MarkBlockDirty(pos);
                    }

                    scythe.DamageItem(player.Entity.World, player.Entity, slot, 1);
                    harvested++;

                    if (slot.Itemstack == null) break;
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
                "copper" => 5,
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