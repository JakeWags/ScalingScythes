using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ScalingScythes
{
    public class ScalingScythesModSystem : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            // Register our custom scythe item class
            api.RegisterItemClass("ItemScytheTiered", typeof(ItemScytheTiered));
        }
    }
}