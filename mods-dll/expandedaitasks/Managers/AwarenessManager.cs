using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace ExpandedAiTasks.Managers
{
    public struct AwarenessData
    {
        private bool _isAware;
        private double _lastComputationTime;

        public AwarenessData(bool isAware, double lastComputationTime)
        {
            _isAware = isAware;
            _lastComputationTime = lastComputationTime;
        }

        public bool isAware
        {
            get 
            { 
                return _isAware; 
            }
            set
            {
                _isAware = value;
            }
        }

        public double lastComputationTime
        {
            get
            {
                return _lastComputationTime;
            }

            set 
            { 
                _lastComputationTime = value;
            }
        }

    }
    public static class AwarenessManager
    {
        ///////////////////////////////
        //AWARENESS MANAGER VARIABLES//
        ///////////////////////////////

        private static Dictionary<long, Dictionary<long, AwarenessData>> awarenessData = new Dictionary<long, Dictionary<long, AwarenessData>>();

        //Note: This value should be as high as we can make it without its effect being perceptable to players.
        private const float AWARENESS_STALE_AFTER_TIME_MS = 250f;


        /////////////////////////////
        //AWARENESS CHECK VARIABLES//
        /////////////////////////////
        //To Do: Make these attributes we can read from an AI file.
        private const double AI_HEARING_AWARENESS_SNEAK_MODIFIER = 0.0;
        private const double AI_HEARING_AWARENESS_STANDNG_MODIFIER = 1.0;
        private const double AI_HEARING_AWARENESS_WALK_MODIFIER = 1.2;
        private const double AI_HEARING_AWARENESS_SPRINT_MODIFIER = 2.0;
        private const double AI_HEARING_AWARENESS_SWIMMING_MODIFIER = 0.0;

        private const double AI_VISION_AWARENESS_SNEAK_MODIFIER = 0.20;
        private const double AI_VISION_AWARENESS_STANDNG_MODIFIER = 0.5;
        private const double AI_VISION_AWARENESS_WALK_MODIFIER = 1.0;
        private const double AI_VISION_AWARENESS_SPRINT_MODIFIER = 1.0;

        private const double AI_HEARING_RANGE = 3.5;

        private const double MAX_LIGHT_LEVEL = 22;//12;
        private const double MIN_LIGHT_LEVEL = 4;
        private const double MAX_LIGHT_LEVEL_DETECTION_DIST = 60;
        private const double MIN_LIGHT_LEVEL_DETECTION_DIST = 2;

        //To Do: Consider narrowing AI FOV at night.
        public const float DEFAULT_AI_VISION_FOV = 120;
        public const float DEFAULT_AI_SCENT_WIND_FOV = 20;

        private const string BUTTERFLY_CODE = "butterfly";
        private static List<string> alwaysIgnoreEntityCodes = new List<string>();

        public static void Init()
        {
            alwaysIgnoreEntityCodes.Add(BUTTERFLY_CODE);
        }

        public static void ShutdownCleanup()
        {
            foreach ( Dictionary<long,AwarenessData> awarenessEntry in awarenessData.Values )
            {
                awarenessEntry.Clear();
            }

            awarenessData.Clear();
        }

        public static bool EntityHasAwarenessEntry( Entity ent )
        {
            return awarenessData.ContainsKey(ent.EntityId);
        }

        public static bool EntityHasAwarenessEntryForTargetEntity(Entity ent, Entity targetEnt)
        {
            return awarenessData[ent.EntityId].ContainsKey(targetEnt.EntityId);
        }

        public static bool EntityAwarenessEntryForTargetEntityIsStale( Entity ent, Entity targetEnt)
        {
            return ent.World.ElapsedMilliseconds > awarenessData[ent.EntityId][targetEnt.EntityId].lastComputationTime + AWARENESS_STALE_AFTER_TIME_MS;
        }

        public static bool EntityIsAwareOfTargetEntity( Entity ent, Entity targetEnt ) 
        {
            return awarenessData[ent.EntityId][targetEnt.EntityId].isAware;
        }

        public static void UpdateOrCreateEntityAwarenessEntryForTargetEntity( Entity ent, Entity targetEnt, bool isAware )
        {
            if ( !EntityHasAwarenessEntry( ent ) )
            {
                awarenessData.Add( ent.EntityId, new Dictionary<long, AwarenessData>() );
                awarenessData[ent.EntityId].Add(targetEnt.EntityId, new AwarenessData(isAware, ent.World.ElapsedMilliseconds));
            }
            else if ( !EntityHasAwarenessEntryForTargetEntity(ent, targetEnt) )
            {
                awarenessData[ent.EntityId].Add( targetEnt.EntityId, new AwarenessData( isAware, ent.World.ElapsedMilliseconds ) );
            }
            else
            {
                AwarenessData targetData = awarenessData[ent.EntityId][targetEnt.EntityId];

                targetData.isAware = isAware;
                targetData.lastComputationTime = ent.World.ElapsedMilliseconds;

                awarenessData[ent.EntityId][targetEnt.EntityId] = targetData;
            }
        }

        public static bool GetEntityAwarenessForTargetEntity(Entity ent, Entity targetEnt ) 
        {
            return awarenessData[ent.EntityId][targetEnt.EntityId].isAware;
        }

        public static void OnDespawn(Entity entity, EntityDespawnData despawnData)
        {
            CleanUpEntries(entity);
        }

        public static void OnDeath(Entity entity, DamageSource damageSource)
        {
            CleanUpEntries(entity);
        }

        private static void CleanUpEntries( Entity entToCleanup )
        { 
            //remove all target entries.
            foreach( long entID in awarenessData.Keys )
            {
                if (awarenessData[ entID ].ContainsKey(entToCleanup.EntityId) )
                {
                    awarenessData[ entID ].Remove(entToCleanup.EntityId);
                }
            }

            if (awarenessData.ContainsKey(entToCleanup.EntityId))
                awarenessData.Remove(entToCleanup.EntityId);
        }

        /*
             _                                                ____ _               _    
            / \__      ____ _ _ __ ___ _ __   ___  ___ ___   / ___| |__   ___  ___| | __
           / _ \ \ /\ / / _` | '__/ _ \ '_ \ / _ \/ __/ __| | |   | '_ \ / _ \/ __| |/ /
          / ___ \ V  V / (_| | | |  __/ | | |  __/\__ \__ \ | |___| | | |  __/ (__|   < 
         /_/   \_\_/\_/ \__,_|_|  \___|_| |_|\___||___/___/  \____|_| |_|\___|\___|_|\_\

        */

        //1. DONE: We shouldn't run the light check multiple times per frame. Because we are running n number of light checks per n number of HasLOSContactWithTarget calls per frame.
        //2. We should make sure this function is only called where it needs to be called, it is called by the melee function and HasDirectContact may be a better option there.
        //3. This function does many similar things to CanSense, but gets called seperately, we need to determine whether the two should remain seperate.
        public static bool IsAwareOfTarget(Entity searchingEntity, Entity targetEntity, float maxDist, float maxVerDist)
        {
            //A player is always considered aware.
            if (searchingEntity is EntityPlayer)
                return true;

            //Bulk ignore entities that we just don't care about, like butterflies.
            if (AiUtility.EntityCodeInList(targetEntity, alwaysIgnoreEntityCodes))
                return false;

            //We cannot percieve ourself as a target.
            if (searchingEntity == targetEntity)
                return false;

            //If no players are within a reasonable range, don't spot anything just return true to save overhead.
            if (!AiUtility.IsAnyPlayerWithinRangeOfPos(targetEntity.ServerPos.XYZ, 250, targetEntity.World))
                return false;

            //Because traces and light checks are expensive, see if we have already run this calculation this frame and
            //if we have, use the saved value for the entity. This will prevent redundant calls to get the same data.
            if (AwarenessManager.EntityHasAwarenessEntry(searchingEntity))
            {
                if (AwarenessManager.EntityHasAwarenessEntryForTargetEntity(searchingEntity, targetEntity))
                {
                    if (!AwarenessManager.EntityAwarenessEntryForTargetEntityIsStale(searchingEntity, targetEntity))
                    {
                        return AwarenessManager.GetEntityAwarenessForTargetEntity(searchingEntity, targetEntity);
                    }
                }
            }

            Cuboidd cuboidd = targetEntity.SelectionBox.ToDouble().Translate(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
            Vec3d selectionBoxMidPoint = searchingEntity.ServerPos.XYZ.Add(0.0, searchingEntity.SelectionBox.Y2 / 2f, 0.0).Ahead(searchingEntity.SelectionBox.XSize / 2f, 0f, searchingEntity.ServerPos.Yaw);
            double shortestDist = cuboidd.ShortestDistanceFrom(selectionBoxMidPoint);
            double shortestVertDist = Math.Abs(cuboidd.ShortestVerticalDistanceFrom(selectionBoxMidPoint.Y));

            //////////////////////////
            ///BASIC DISTANCE CHECK///
            //////////////////////////

            //Scale Ai Awareness Based on How the player is Moving;
            double aiAwarenessHearingScalar = 1.0;
            double aiAwarenessVisionScalar = 1.0;

            if (targetEntity is EntityPlayer)
            {
                aiAwarenessHearingScalar = GetAiHearingAwarenessScalarForPlayerMovementType((EntityPlayer)targetEntity);
                aiAwarenessVisionScalar = GetAiVisionAwarenessScalarForPlayerMovementType((EntityPlayer)targetEntity);
            }

            double shortestHearingDist = shortestDist * aiAwarenessHearingScalar;
            double shortestHearingVertDist = shortestVertDist * aiAwarenessHearingScalar;
            double shortestVisionDist = shortestDist * aiAwarenessVisionScalar;
            double shortestVisionVertDist = shortestVertDist * aiAwarenessVisionScalar;

            if (shortestDist >= (double)maxDist || shortestVertDist >= (double)maxVerDist)
            {
                AwarenessManager.UpdateOrCreateEntityAwarenessEntryForTargetEntity(searchingEntity, targetEntity, false);
                return false;
            }


            ///////////////////
            ///HEARING CHECK///
            ///////////////////

            //if we can hear the target moving, enage 
            double aiHearingRange = AI_HEARING_RANGE * aiAwarenessHearingScalar;
            if (shortestDist <= aiHearingRange && targetEntity.ServerPos.Motion.LengthSq() > 0)
            {
                AwarenessManager.UpdateOrCreateEntityAwarenessEntryForTargetEntity(searchingEntity, targetEntity, true);
                return true;
            }

            ///////////////
            //SMELL CHECK//
            ///////////////
            if (GetEntitySmellRange(searchingEntity) > 0)
            {
                if (CanEntitySmellPositionWithWind(searchingEntity, targetEntity.ServerPos.XYZ, 0))
                {
                    AwarenessManager.UpdateOrCreateEntityAwarenessEntryForTargetEntity(searchingEntity, targetEntity, true);
                    return true;
                }
            }

            //////////////////////////
            ///EYE-TO-EYE LOS CHECK///
            //////////////////////////
            //If we don't have direct line of sight to the target's eyes.
            Entity[] ignoreEnts = { targetEntity };
            if (!AiUtility.CanEntSeePos(searchingEntity, targetEntity.ServerPos.XYZ.Add(0, targetEntity.LocalEyePos.Y, 0), DEFAULT_AI_VISION_FOV, ignoreEnts))
            {
                AwarenessManager.UpdateOrCreateEntityAwarenessEntryForTargetEntity(searchingEntity, targetEntity, false);
                return false;
            }


            /////////////////
            ///LIGHT CHECK///
            /////////////////

            //If this Ai can see in the dark, we don't need to check lights.
            if (EntityHasNightVison(searchingEntity))
            {
                AwarenessManager.UpdateOrCreateEntityAwarenessEntryForTargetEntity(searchingEntity, targetEntity, true);
                return true;
            }


            //If no players are within a close range, don't bother with illumination checks.
            if (!AiUtility.IsAnyPlayerWithinRangeOfPos(targetEntity.ServerPos.XYZ, 60, targetEntity.World))
            {
                AwarenessManager.UpdateOrCreateEntityAwarenessEntryForTargetEntity(searchingEntity, targetEntity, true);
                return true;
            }


            //This ensures we only run one full illumination update every 500ms.
            int lightLevel = IlluminationManager.GetIlluminationLevelForEntity(targetEntity);
            double lightLevelDist = MathUtility.GraphClampedValue(MIN_LIGHT_LEVEL, MAX_LIGHT_LEVEL, MIN_LIGHT_LEVEL_DETECTION_DIST, MAX_LIGHT_LEVEL_DETECTION_DIST, (double)lightLevel);
            double lightLevelVisualAwarenessDist = lightLevelDist * aiAwarenessVisionScalar;

            if (shortestDist <= lightLevelVisualAwarenessDist)
            {
                AwarenessManager.UpdateOrCreateEntityAwarenessEntryForTargetEntity(searchingEntity, targetEntity, true);
                return true;
            }

            AwarenessManager.UpdateOrCreateEntityAwarenessEntryForTargetEntity(searchingEntity, targetEntity, false);
            return false;
        }

        /*
             _                                               _   _ _   _ _ _ _         
            / \__      ____ _ _ __ ___ _ __   ___  ___ ___  | | | | |_(_) (_) |_ _   _ 
           / _ \ \ /\ / / _` | '__/ _ \ '_ \ / _ \/ __/ __| | | | | __| | | | __| | | |
          / ___ \ V  V / (_| | | |  __/ | | |  __/\__ \__ \ | |_| | |_| | | | |_| |_| |
         /_/   \_\_/\_/ \__,_|_|  \___|_| |_|\___||___/___/  \___/ \__|_|_|_|\__|\__, |
                                                                                 |___/ 
        */

        public static float GetEntitySmellRange(Entity entity)
        {
            if (entity.Properties.Attributes.KeyExists("smellRange"))
            {
                return entity.Properties.Attributes["smellRange"].AsFloat();
            }

            return 0.0f;
        }

        public static bool CanEntitySmellPositionWithWind(Entity entity, Vec3d pos, float scentRange)
        {
            Vec3d posWindSpeed = entity.World.BlockAccessor.GetWindSpeedAt(pos);

            double maxScentDot = Math.Cos((DEFAULT_AI_SCENT_WIND_FOV / 2) * (Math.PI / 180));

            Vec3d entityToTarget = entity.ServerPos.XYZ - pos;
            entityToTarget.Normalize();

            double dot = entityToTarget.Dot(posWindSpeed.Clone().Normalize());

            //DebugUtility.DebugDrawScentSystem(entity.World, entity.ServerPos.XYZ, pos, entityToTarget, posWindSpeed);

            if (dot > maxScentDot)
            {
                double windLength = posWindSpeed.Length();
                double smellRange = (GetEntitySmellRange(entity) * windLength) + scentRange;
                return entity.ServerPos.SquareDistanceTo(pos) <= smellRange * smellRange;
            }

            return false;
        }


        public static double GetAiHearingAwarenessScalarForPlayerMovementType(EntityPlayer playerEnt)
        {
            if (playerEnt.Swimming)
            {
                return AI_HEARING_AWARENESS_SWIMMING_MODIFIER;
            }
            else if (playerEnt.Controls.Sneak && playerEnt.OnGround)
            {
                return AI_HEARING_AWARENESS_SNEAK_MODIFIER;
            }
            else if (playerEnt.Controls.Sprint && playerEnt.OnGround)
            {
                return AI_HEARING_AWARENESS_SPRINT_MODIFIER;
            }
            else if (playerEnt.Controls.TriesToMove && playerEnt.OnGround)
            {
                return AI_HEARING_AWARENESS_WALK_MODIFIER;
            }

            return AI_HEARING_AWARENESS_STANDNG_MODIFIER;
        }

        public static double GetAiVisionAwarenessScalarForPlayerMovementType(EntityPlayer playerEnt)
        {
            if (playerEnt.Controls.Sneak && playerEnt.OnGround)
            {
                return AI_VISION_AWARENESS_SNEAK_MODIFIER;
            }
            else if (playerEnt.Controls.Sprint && playerEnt.OnGround)
            {
                return AI_VISION_AWARENESS_SPRINT_MODIFIER;
            }
            else if (playerEnt.Controls.TriesToMove && playerEnt.OnGround)
            {
                return AI_VISION_AWARENESS_WALK_MODIFIER;
            }

            return AI_VISION_AWARENESS_STANDNG_MODIFIER;
        }

        public static bool EntityHasNightVison(Entity entity)
        {
            if (entity.Properties.Attributes.KeyExists("hasNightVision"))
            {
                return entity.Properties.Attributes["hasNightVision"].AsBool();
            }

            return false;
        }

    }
}
