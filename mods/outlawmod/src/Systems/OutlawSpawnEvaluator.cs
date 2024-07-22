
using System;
using System.Diagnostics;
using System.Collections.Generic;
using Vintagestory.API.Server;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace OutlawMod
{
    public static class OutlawSpawnEvaluator
    {
        static ICoreServerAPI sapi;

        static POIRegistry poiregistry;

        private const float MAX_SPAWN_EXCLUSION_POI_SEARCH_DIST = 250;

        const string SPAWN_EXCLUSION_POI_TYPE = "outlawSpawnBlocker";

        //Spawn Exclusion Search Vars
        private static bool spawnIsBlockedByBlocker = false; //This must be reset whenever we do an OnEntitySpawn call. It is set by the PoiMatcher function to skip additional once we've found a ISpawnBlocker that will block our spawn request.
        private static Vec3d currentSpawnTryPosition;

        private static bool levelReadyForOutlawSpawns = false;

        public static void Initialize( ICoreServerAPI server )
        {
            sapi = server;
            poiregistry = sapi.ModLoader.GetModSystem<POIRegistry>();
            levelReadyForOutlawSpawns = true;
        }

        public static void Shutdown()
        {
            levelReadyForOutlawSpawns = false;
        }

        public static bool CanSpawnOutlaw( Vec3d position, AssetLocation code )
        {
            //Only run on server side, because POIs are serveside only.
            Debug.Assert(sapi.Side == EnumAppSide.Server, "CanSpawn function is running on the client, this must only run server side.");

            currentSpawnTryPosition = position;
            spawnIsBlockedByBlocker = false;

            //Make it so outlaws only get blocked if any players are in survival mode.
            if (!OMGlobalConstants.devMode)
            {
                ////////////////////////////////////////////
                ///BLOCKED BY START SPAWN SAFE ZONE CHECK///
                ////////////////////////////////////////////
                if ( SpawnExcludedBySafeZone(position, code) )
                    return false;

                /////////////////////////////////
                ///BLOCKED BY LAND CLAIM CHECK///
                /////////////////////////////////             
                if (SpawnExcludedByLandClaim(position, code))
                    return false;

                ////////////////////////////////////////
                ///BLOCKED BY SPAWN BLOCKER POI CHECK///
                //////////////////////////////////////// 
                if (SpawnExcludedBySymbolOfJustice(position, code))
                    return false;    
            }

            return true;
        }

        public static bool CanSpawnHound(Vec3d position, AssetLocation code)
        {
            //Only run on server side, because POIs are serveside only.
            Debug.Assert(sapi.Side == EnumAppSide.Server, "CanSpawn function is running on the client, this must only run server side.");

            currentSpawnTryPosition = position;
            spawnIsBlockedByBlocker = false;

            //Make it so outlaws only get blocked if any players are in survival mode.
            if (!OMGlobalConstants.devMode)
            {
                ////////////////////////////////////////////
                ///BLOCKED BY START SPAWN SAFE ZONE CHECK///
                ////////////////////////////////////////////
                if (SpawnExcludedBySafeZone(position, code))
                    return false;

                /////////////////////////////////
                ///BLOCKED BY LAND CLAIM CHECK///
                /////////////////////////////////             
                if (SpawnExcludedByLandClaim(position, code))
                    return false;

                ////////////////////////////////////////
                ///BLOCKED BY SPAWN BLOCKER POI CHECK///
                //////////////////////////////////////// 
                if (SpawnExcludedBySymbolOfJustice(position, code))
                    return false;
            }

            return true;
        }

        private static bool SpawnExcludedBySafeZone(Vec3d position, AssetLocation code)
        {
            if (!levelReadyForOutlawSpawns)
                return true;

            double totalDays = sapi.World.Calendar.ElapsedDays;
            double safeZoneDaysLeft = Math.Max(OMGlobalConstants.startingSpawnSafeZoneLifetimeInDays - totalDays, 0);
            double safeZoneRadius = OMGlobalConstants.startingSpawnSafeZoneRadius;

            //If our starting safety zone has a lifetime (any negative value means never despawn).
            if (OMGlobalConstants.startingSafeZoneHasLifetime)
            {
                //If we are cofigured to shrink the safe zone over time. Figure out how big the spawn zone should be on this calender day.
                if (OMGlobalConstants.startingSafeZoneShrinksOverlifetime)
                {
                    safeZoneRadius = MathUtility.GraphClampedValue(OMGlobalConstants.startingSpawnSafeZoneLifetimeInDays, 0, OMGlobalConstants.startingSpawnSafeZoneRadius, 0, safeZoneDaysLeft);
                }
                //If we are cofigured to have our safe zone to come to a hard stop after X days.
                else
                {
                    safeZoneRadius = safeZoneDaysLeft > 0 ? safeZoneRadius : 0;
                }
            }

            //If we have an eternal safe zone, or a zone that has lifetime days remaining, try to block spawns.
            if (!OMGlobalConstants.startingSafeZoneHasLifetime || (safeZoneDaysLeft > 0))
            {
                //We cannot access the default world spawnpoint until chunks are loaded.
                if (sapi.WorldManager.AllLoadedChunks.Count == 0)
                    return true;

                //Make it so Outlaws can't spawn in the starting area.
                EntityPos defaultWorldSpawn = sapi.World.DefaultSpawnPosition;
                double distToWorldSpawnSqr = currentSpawnTryPosition.HorizontalSquareDistanceTo(defaultWorldSpawn.XYZ);

                if (distToWorldSpawnSqr < (safeZoneRadius * safeZoneRadius))
                {
                    Utility.DebugLogMessage(sapi as ICoreAPI, "Cannot Spawn " + code.Path + " at: " + currentSpawnTryPosition + ". Too Close to Player Starting Spawn Area.");
                    return true;
                }
            }

            return false;
        }

        private static bool SpawnExcludedByLandClaim(Vec3d position, AssetLocation code)
        {
            //Do Not Allow Outlaws to Spawn on Claimed Land. (If Config Says So)
            if (OMGlobalConstants.claimedLandBlocksOutlawSpawns)
            {
                List<LandClaim> landclaims = sapi.World.Claims.All;
                foreach (LandClaim landclaim in landclaims)
                {
                    if (landclaim.PositionInside(currentSpawnTryPosition))
                    {
                        Utility.DebugLogMessage(sapi as ICoreAPI, "Cannot Spawn " + code.Path + " at: " + currentSpawnTryPosition + ". Attempted to spawn on landclaim " + landclaim.Description);
                        return true;
                    }

                }
            }

            return false;
        }

        private static bool SpawnExcludedBySymbolOfJustice(Vec3d position, AssetLocation code)
        {
            //Do Not Allow Outlaws to Spawn near BlockEntityOutlawDeterents.
            poiregistry.WalkPois(currentSpawnTryPosition, MAX_SPAWN_EXCLUSION_POI_SEARCH_DIST, SpawnExclusionMatcher);
            if (spawnIsBlockedByBlocker == true)
            {
                Utility.DebugLogMessage(sapi as ICoreAPI, "Cannot Spawn " + code.Path + " at: " + currentSpawnTryPosition + ". Attempted to spawn within spawn blocker");
                return true;
            }

            return false;
        }

        public static bool SpawnExclusionMatcher(IPointOfInterest poi)
        {
            if (poi.Type == SPAWN_EXCLUSION_POI_TYPE && !spawnIsBlockedByBlocker)
            {
                IOutlawSpawnBlocker spawnBlocker = (IOutlawSpawnBlocker)poi;

                float distToBlockerSqr = spawnBlocker.Position.SquareDistanceTo(currentSpawnTryPosition);
                float blockingRange = spawnBlocker.blockingRange();

                //We have found a blocker that blocks our spawn, no need to run compuations on other pois.
                if (distToBlockerSqr <= blockingRange * blockingRange)
                    spawnIsBlockedByBlocker = true;

                return true;
            }

            return false;
        }

    }
}