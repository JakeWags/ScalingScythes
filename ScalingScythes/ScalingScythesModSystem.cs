using HarmonyLib;
using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ScalingScythes
{
    public class ScalingScythesModSystem : ModSystem
    {
        private Harmony harmony;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            // Apply Harmony patches
            harmony = new Harmony("com.grompgilberton.scalingscythes");
            harmony.PatchAll();

            api.Logger.Notification("[ScalingScythes] Harmony patches applied successfully");
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll("com.grompgilberton.scalingscythes");
            base.Dispose();
        }
    }
}