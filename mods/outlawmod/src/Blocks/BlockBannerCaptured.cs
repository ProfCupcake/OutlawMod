using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace OutlawMod
{
    public class BlockBannerCaptured : Block
    {

        private const int CLEARANCE_ABOVE_NEEDED = 4;

        public override void OnLoaded(ICoreAPI api)
        {
            //We need the client to not cull the bottom side of the heads at any time.
            if (api.Side == EnumAppSide.Client)
            {
                FaceCullMode = EnumFaceCullMode.NeverCull;
            }

            base.OnLoaded(api);
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            if (!CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            {
                return false;
            }

            for (int i = 1; i <= CLEARANCE_ABOVE_NEEDED; i++)
            {
                Block blockAbove = world.BlockAccessor.GetBlock(blockSel.Position.AddCopy(0, i, 0));

                //There is a block above us.
                if (blockAbove != null)
                {
                    if (blockAbove.SideSolid[BlockFacing.DOWN.Index])
                    {
                        return false;
                    }

                }
            }

            //Check for solid below
            BlockFacing[] horVer = SuggestedHVOrientation(byPlayer, blockSel);
            AssetLocation blockCode = CodeWithVariants(new string[] { "horizontalorientation" }, new string[] { horVer[0].Code });
            Block block = world.BlockAccessor.GetBlock(blockCode);
            
            if (block == null) 
                return false;

            Block blockBelow = world.BlockAccessor.GetBlock(blockSel.Position.DownCopy());
            bool solidBelow = blockBelow.SideSolid[BlockFacing.UP.Index];

            if (!solidBelow)
                return false;

            world.BlockAccessor.SetBlock(block.BlockId, blockSel.Position);

            return true;
        }
    }
}
