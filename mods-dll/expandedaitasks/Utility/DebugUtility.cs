using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;


namespace ExpandedAiTasks
{
    public static class DebugUtility
    {
        public static void DebugDrawPosition(IWorldAccessor world, Vec3d pos, int red, int green, int blue)
        {
            BlockPos blockPos = new BlockPos((int)pos.X, (int)pos.Y, (int)pos.Z);
            DebugDrawBlockLocation(world, blockPos, red, green, blue);
        }
        public static void DebugDrawBlockLocation(IWorldAccessor world, BlockPos blockPos, int red, int green, int blue )
        {
            // Debug visualization
            Debug.Assert(red >= 0 && red <= 255);
            Debug.Assert(green >= 0 && green <= 255);
            Debug.Assert(blue >= 0 && blue <= 255);

            List<BlockPos> blockPositions = new List<BlockPos>();
            blockPositions.Add(blockPos);

            int color = ColorUtil.ColorFromRgba(red, green, blue, 150);
            List<int> colors = new List<int>();
            colors.Add(color);

            IPlayer player = world.AllOnlinePlayers[0];
            world.HighlightBlocks(player, 2, blockPositions, colors, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
        }

        public static void DebugTargetPositionAndLaskKnownPositionBlockLocation(IWorldAccessor world, Vec3d targetPos, Vec3d lkpPos )
        {
            BlockPos targetBlockPos = new BlockPos((int)targetPos.X, (int)targetPos.Y, (int)targetPos.Z);
            BlockPos lkpBlockPos = new BlockPos((int)lkpPos.X, (int)lkpPos.Y, (int)lkpPos.Z);

            // Debug visualization
            List<BlockPos> blockPositions = new List<BlockPos>();
            blockPositions.Add(targetBlockPos);
            blockPositions.Add(lkpBlockPos);

            int colorTarget = ColorUtil.ColorFromRgba(255, 0, 0, 150);
            int colorLKP = ColorUtil.ColorFromRgba(0, 255, 0, 150);
            List<int> colors = new List<int>();
            colors.Add(colorTarget);
            colors.Add(colorLKP);

            IPlayer player = world.AllOnlinePlayers[0];
            world.HighlightBlocks(player, 2, blockPositions, colors, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
        }

        public static void DebugTargetPositionAndLaskKnownPositionandCurrentNavPositionBlockLocation(IWorldAccessor world, Vec3d targetPos, Vec3d lkpPos, PathTraverserBase pathTraverser)
        {
            BlockPos targetBlockPos = new BlockPos((int)targetPos.X, (int)targetPos.Y, (int)targetPos.Z);
            BlockPos lkpBlockPos = new BlockPos((int)lkpPos.X, (int)lkpPos.Y, (int)lkpPos.Z);
            BlockPos currentNavBlockPos = new BlockPos((int)pathTraverser.CurrentTarget.X, (int)pathTraverser.CurrentTarget.Y, (int)pathTraverser.CurrentTarget.Z);

            // Debug visualization
            List<BlockPos> blockPositions = new List<BlockPos>();
            blockPositions.Add(targetBlockPos);
            blockPositions.Add(lkpBlockPos);
            blockPositions.Add(currentNavBlockPos);

            int colorTarget = ColorUtil.ColorFromRgba(255, 0, 0, 150);  //TARGET POS RED
            int colorLKP = ColorUtil.ColorFromRgba(0, 255, 0, 150);     //LKP GREEN
            int colorNav = ColorUtil.ColorFromRgba(0, 0, 255, 150);     //NAV BLUE
            List<int> colors = new List<int>();
            colors.Add(colorTarget);
            colors.Add(colorLKP);
            colors.Add(colorNav);

            IPlayer player = world.AllOnlinePlayers[0];
            world.HighlightBlocks(player, 2, blockPositions, colors, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
        }

        private static BlockSelection blockSel = null;
        private static EntitySelection entitySel = null;
        private static BlockSelection raytraceBlockSel = null;
        private static EntitySelection raytraceEntityRay = null;
        private static List<BlockPos> raycastDebugDrawBlockPositions = new List<BlockPos>();
        private static List<int> raycastDebugColors = new List<int>();
        public static void DebugDrawRayTrace(IWorldAccessor world, Vec3d startPos, Vec3d endPos, BlockFilter BlockFilter, EntityFilter EntFilter)
        {
            raycastDebugDrawBlockPositions.Clear();
            raycastDebugColors.Clear();

            world.RayTraceForSelection(startPos, endPos, ref blockSel, ref entitySel, BlockFilter, EntFilter);
            world.RayTraceForSelection(startPos, endPos, ref raytraceBlockSel, ref raytraceEntityRay, RayTrace_BlockFilter, RayTrace_EntityFilter);

            for (int i = 0; i < raycastDebugDrawBlockPositions.Count; i++ )
            {
                raycastDebugColors.Add(ColorUtil.ColorFromRgba(255, 164, 0, 150));
            }

            IPlayer player = world.AllOnlinePlayers[0];
            world.HighlightBlocks(player, 2, raycastDebugDrawBlockPositions, raycastDebugColors, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
        }

        private static bool RayTrace_BlockFilter(BlockPos pos, Block block)
        {
            raycastDebugDrawBlockPositions.Add(pos);

            if (blockSel == null)
                return false;

            if (block == blockSel.Block)
                return true;

            return false;
        }

        private static bool RayTrace_EntityFilter(Entity ent)
        {
            if ( entitySel == null )
                return false;

            if (ent == entitySel.Entity) 
                return true;

            return false;
        }

        public static void DebugDrawScentSystem(IWorldAccessor world, Vec3d smellPos, Vec3d scentPos, Vec3d smellToScent, Vec3d windDir )
        {
            BlockPos smellPosBlockPos = smellPos.AsBlockPos;
            BlockPos scentPosBlockPos = scentPos.AsBlockPos;
            BlockPos smellToScentBlockPos = (smellPos + (smellToScent * 2)).AsBlockPos;
            BlockPos windDirBlockPos = (scentPos + (windDir * 2)).AsBlockPos;


            // Debug visualization
            List<BlockPos> blockPositions = new List<BlockPos>();
            blockPositions.Add(smellPosBlockPos);
            blockPositions.Add(scentPosBlockPos);
            blockPositions.Add(smellToScentBlockPos);
            blockPositions.Add(windDirBlockPos);

            int colorSmell = ColorUtil.ColorFromRgba(255, 136, 0, 150);
            int colorScent = ColorUtil.ColorFromRgba(0, 255, 0, 150);
            int colorSmellToScent = ColorUtil.ColorFromRgba(255, 0, 0, 150);
            int colorWind = ColorUtil.ColorFromRgba(0, 0, 255, 150);
            List<int> colors = new List<int>();
            colors.Add(colorSmell);
            colors.Add(colorScent);
            colors.Add(colorSmellToScent);
            colors.Add(colorWind);

            IPlayer player = world.AllOnlinePlayers[0];
            world.HighlightBlocks(player, 2, blockPositions, colors, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
        }

        public static void DebugDrawBlockPath(  )
        {
            /*
            // Debug visualization
            List<BlockPos> poses = new List<BlockPos>();
            List<int> colors = new List<int>();
            int i = 0;

            foreach (var node in waypoints)
            {
                poses.Add(node.AsBlockPos);
                colors.Add(ColorUtil.ColorFromRgba(128, 128, Math.Min(255, 128 + i * 8), 150));
                i++;
            }

            poses.Add(desiredTarget.AsBlockPos);
            colors.Add(ColorUtil.ColorFromRgba(128, 0, 255, 255));

            IPlayer player = entity.World.AllOnlinePlayers[0];
            entity.World.HighlightBlocks(player, 2, poses,
                colors,
                API.Client.EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary
            );
            */
        }

        //Note: This function networks a debug message, use this sparingly because it can cause massive hitches.
        public static void DebugLogToPlayerChat(ICoreServerAPI sapi, string text)
        {
            string message = "[Expanded AI Tasks Debug] " + text;

            IPlayer[] playersOnline = sapi.World.AllOnlinePlayers;
            foreach (IPlayer player in playersOnline)
            {
                IServerPlayer serverPlayer = player as IServerPlayer;
                serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }

        public static void DebugLogMessage(ICoreAPI api, string text)
        {
            string message = "[Expanded AI Tasks Debug] " + text;
            api.Logger.Debug(message);
        }

    }
}
