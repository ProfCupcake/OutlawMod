using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Util;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using ExpandedAiTasks.Managers;

namespace ExpandedAiTasks
{
    public struct EntityTargetPairing
    {

        Entity _entityTargeting;
        Entity _targetEntity;

        Vec3d _lastPathUpdatePos = null;
        Vec3d _lastKnownPos = null;
        Vec3d _lastKnownMotion = null;
        public Entity entityTargeting
        {
            get { return _entityTargeting; }
            private set { _entityTargeting = value; }
        }

        public Entity targetEntity
        {
            get { return _targetEntity; }
            set { _targetEntity = value; }
        }

        public Vec3d lastPathUpdatePos
        {
            get { return _lastPathUpdatePos; }
            private set { _lastPathUpdatePos = value; }
        }

        public Vec3d lastKnownPos
        {
            get { return _lastKnownPos; }
            set { _lastKnownPos = value; }
        }

        public Vec3d lastKnownMotion
        {
            get { return _lastKnownMotion; }
            set { _lastKnownMotion = value; }
        }

        public EntityTargetPairing( Entity entityTargeting, Entity targetEntity, Vec3d lastPathUpdatePos, Vec3d lastKnownPos, Vec3d lastKnownMotion )
        {
            _entityTargeting = entityTargeting;
            _targetEntity = targetEntity;
            _lastPathUpdatePos = lastPathUpdatePos;
            _lastKnownPos = lastKnownPos;
            _lastKnownMotion = lastKnownMotion == null ? null : lastKnownMotion.Clone();
        }
    }

    public static class AiUtility
    {
        private const float ALWAYS_ALLOW_TELEPORT_BEYOND_RANGE_FROM_PLAYER = 80;
        private const float BLOCK_TELEPORT_WHEN_PLAYER_CLOSER_THAN = 30;
        private const float BLOCK_TELEPORT_AFTER_COMBAT_DURATION = 30000;

        private const float HERD_ALERT_RANGE = 15;

        public static string GetAiTaskName( AiTaskBase task )
        {
            return AiTaskRegistry.TaskCodes[task.GetType()];
        }

        public static void SetGuardedEntity(Entity ent, Entity entToGuarded)
        {
            if (entToGuarded is EntityPlayer)
            {
                EntityPlayer guardedPlayer = entToGuarded as EntityPlayer;
                ent.WatchedAttributes.SetString("guardedPlayerUid", guardedPlayer.PlayerUID);
                ent.WatchedAttributes.MarkPathDirty("guardedPlayerUid");
            }

            ent.WatchedAttributes.SetLong("guardedEntityId", entToGuarded.EntityId);
            ent.WatchedAttributes.MarkPathDirty("guardedEntityId");
        }
        /*
          _              _        _   _   _             _             
         | |    __ _ ___| |_     / \ | |_| |_ __ _  ___| | _____ _ __ 
         | |   / _` / __| __|   / _ \| __| __/ _` |/ __| |/ / _ \ '__|
         | |__| (_| \__ \ |_   / ___ \ |_| || (_| | (__|   <  __/ |   
         |_____\__,_|___/\__| /_/   \_\__|\__\__,_|\___|_|\_\___|_|   

         */
        public static void SetLastAttacker( Entity ent, DamageSource damageSource)
        {

            Entity attacker = damageSource.SourceEntity;

            if (attacker is EntityProjectile && damageSource.CauseEntity != null)
            {
                attacker = damageSource.CauseEntity;
            }

            if (attacker is EntityPlayer)
            {

                if (CanTargetPlayer(attacker as EntityPlayer))
                {
                    ent.Attributes.SetString("lastPlayerAttackerUid", (attacker as EntityPlayer).PlayerUID);
                    ent.Attributes.SetDouble("lastTimeAttackedMs", ent.World.ElapsedMilliseconds);

                    if (ent.Attributes.HasAttribute("lastEntAttackerEntityId"))
                        ent.Attributes.RemoveAttribute("lastEntAttackerEntityId");
                }
            }
            else if (attacker != null)
            {
                if ( attacker is EntityAgent )
                {
                    EntityAgent attackerAgent = attacker as EntityAgent;
                    if ( AiUtility.AreMembersOfSameHerd(attackerAgent, ent) );
                        return;
                }

                ent.Attributes.SetLong("lastEntAttackerEntityId", attacker.EntityId);
                ent.Attributes.SetDouble("lastTimeAttackedMs", ent.World.ElapsedMilliseconds);

                if (ent.Attributes.HasAttribute("lastPlayerAttackerUid"))
                    ent.Attributes.RemoveAttribute("lastPlayerAttackerUid");
            }

            ent.Attributes.MarkAllDirty();
        }

        public static Entity GetLastAttacker( Entity ent )
        {
            string Uid = ent.Attributes.GetString("lastPlayerAttackerUid");
            if (Uid != null)
            {
                return ent.World.PlayerByUid(Uid)?.Entity;
            }

            long entId = ent.Attributes.GetLong("lastEntAttackerEntityId", 0L);
            return ent.World.GetEntityById(entId);
        }

        public static double GetLastTimeAttackedMs( Entity ent)
        {
            //There's an issue where where lastTimeAttackedMs this is saved on the ent, which we don't want.
            //Until we can find a way to store and update this at runtime without native behavior saving it,
            //we have to manually zero out the loaded bad value. 
            double lastAttackedMs = ent.Attributes.GetDouble("lastTimeAttackedMs");
            if ( lastAttackedMs > ent.World.ElapsedMilliseconds)
            {
                ent.Attributes.SetDouble("lastTimeAttackedMs", 0);
                lastAttackedMs = 0;
            }

            return lastAttackedMs;
        }

        public static bool AttackWasFromProjectile( DamageSource damageSource )
        {
            return damageSource.SourceEntity is EntityProjectile;
        }

        /*
           ____                _           _     _   _ _   _ _ _ _         
          / ___|___  _ __ ___ | |__   __ _| |_  | | | | |_(_) (_) |_ _   _ 
         | |   / _ \| '_ ` _ \| '_ \ / _` | __| | | | | __| | | | __| | | |
         | |__| (_) | | | | | | |_) | (_| | |_  | |_| | |_| | | | |_| |_| |
          \____\___/|_| |_| |_|_.__/ \__,_|\__|  \___/ \__|_|_|_|\__|\__, |
                                                                     |___/  
        */

        public static bool IsInCombat(Entity ent)
        {
            //Players are always considered to be in combat because they are not AI.
            if (ent is EntityPlayer)
                return true;

            if (ent is EntityAgent)
            {
                //If we don't have AI Tasks, we cannot be in combat.
                if ( !ent.HasBehavior<EntityBehaviorTaskAI>() )
                    return false;

                AiTaskManager taskManager = ent.GetBehavior<EntityBehaviorTaskAI>().TaskManager;

                if (taskManager != null)
                {
                    IAiTask[] activeTasks = taskManager.ActiveTasksBySlot;
                    foreach (IAiTask task in activeTasks)
                    {
                        if (task is null)
                            continue;

                        if (task is AiTaskBaseTargetable)
                        {
                            AiTaskBaseTargetable baseTargetable = (AiTaskBaseTargetable)task;

                            //If we are fleeing, we are in combat. (Not the same as morale)
                            if (task is AiTaskFleeEntity)
                                return true;

                            //If not an agressive action.
                            if (!baseTargetable.AggressiveTargeting)
                                continue;

                            //If we have a target entity and hostile intent, then we are in combat.
                            if (baseTargetable.TargetEntity != null && baseTargetable.TargetEntity.Alive && !AreMembersOfSameHerd(ent, baseTargetable.TargetEntity))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        public static void UpdateLastTimeEntityInCombatMs( Entity ent )
        {
            ent.Attributes.SetDouble("lastTimeInCombatMs", ent.World.ElapsedMilliseconds);
        }

        public static double GetLastTimeEntityInCombatMs(Entity ent)
        {
            //There's an issue where where lastTimeInCombatMs this is saved on the ent, which we don't want.
            //Until we can find a way to store and update this at runtime without native behavior saving it,
            //we have to manually zero out the loaded bad value. 
            double lastInCombatMs = ent.Attributes.GetDouble("lastTimeInCombatMs");
            if (lastInCombatMs > ent.World.ElapsedMilliseconds)
            {
                ent.Attributes.SetDouble("lastTimeInCombatMs", 0);
                lastInCombatMs = 0;
            }

            return lastInCombatMs;
        }

        public static void SetRangedSneakAttackMultiplierForPlayer( EntityPlayer player, float rangedSneakAttackMult )
        {
            player.Attributes.SetFloat(ExpandedAiTaskConsts.RANGED_SNEAK_ATTACK_ATTRIBUTE_KEY, rangedSneakAttackMult);
        }

        public static void SetMeleeSneakAttackMultiplierForPlayer( EntityPlayer player, float meleeSneakAttackMult )
        {
            player.Attributes.SetFloat(ExpandedAiTaskConsts.MELEE_SNEAK_ATTACK_ATTRIBUTE_KEY, meleeSneakAttackMult);
        }

        /*
          __  __                 _        _   _ _   _ _ _ _         
         |  \/  | ___  _ __ __ _| | ___  | | | | |_(_) (_) |_ _   _ 
         | |\/| |/ _ \| '__/ _` | |/ _ \ | | | | __| | | | __| | | |
         | |  | | (_) | | | (_| | |  __/ | |_| | |_| | | | |_| |_| |
         |_|  |_|\___/|_|  \__,_|_|\___|  \___/ \__|_|_|_|\__|\__, |
                                                              |___/ 
         */

        public static bool IsRoutingFromBattle(Entity ent)
        {
            if (ent is EntityPlayer)
                return false;

            if (ent is EntityAgent)
            {
                AiTaskManager taskManager = ent.GetBehavior<EntityBehaviorTaskAI>().TaskManager;

                if (taskManager != null)
                {
                    IAiTask[] tasks = taskManager.ActiveTasksBySlot;
                    foreach (IAiTask task in tasks)
                    {
                        if (task is AiTaskMorale)
                            return true;
                    }
                }
            }

            return false;
        }

        public static void UpdateLastTimeEntityFailedMoraleMs( Entity ent )
        {
            ent.Attributes.SetDouble("lastTimeFailedMoraleMs", ent.World.ElapsedMilliseconds);
        }

        public static double GetLastTimeEntityFailedMoraleMs(Entity ent)
        {
            //There's an issue where where lastTimeFailedMoraleMs this is saved on the ent, which we don't want.
            //Until we can find a way to store and update this at runtime without native behavior saving it,
            //we have to manually zero out the loaded bad value. 
            double lastFailedMoraleMs = ent.Attributes.GetDouble("lastTimeFailedMoraleMs");
            if (lastFailedMoraleMs > ent.World.ElapsedMilliseconds)
            {
                ent.Attributes.SetDouble("lastTimeFailedMoraleMs", 0);
                lastFailedMoraleMs = 0;
            }

            return lastFailedMoraleMs;
        }

        public static double CalculateInjuryRatio(Entity ent)
        {
            ITreeAttribute treeAttribute = ent.WatchedAttributes.GetTreeAttribute("health");

            if (treeAttribute != null)
            {
                double currentHealth = treeAttribute.GetFloat("currenthealth"); ;
                double maxHealth = treeAttribute.GetFloat("maxhealth"); ;

                return (maxHealth - currentHealth) / maxHealth;
            }

            return 0.0;
        }

        /*
             _    ___   _   _              _   _   _ _   _ _ _ _         
            / \  |_ _| | | | | ___ _ __ __| | | | | | |_(_) (_) |_ _   _ 
           / _ \  | |  | |_| |/ _ \ '__/ _` | | | | | __| | | | __| | | |
          / ___ \ | |  |  _  |  __/ | | (_| | | |_| | |_| | | | |_| |_| |
         /_/   \_\___| |_| |_|\___|_|  \__,_|  \___/ \__|_|_|_|\__|\__, |
                                                                   |___/ 
        */
        public static void SetMasterHerdList( Entity ent, List<Entity> herdList )
        {
            List<long> herdListEntIds = new List<long>();
            foreach( Entity agent in herdList )
            {
                if (agent != null)
                    herdListEntIds.Add(agent.EntityId);
            }

            long[] herdEntIdArray = herdListEntIds.ToArray();
            ent.Attributes.SetBytes("herdMembers", SerializerUtil.Serialize(herdEntIdArray));
        }

        public static List<Entity> GetMasterHerdList( Entity ent )
        {
            List<Entity> herdMembers = new List<Entity>();
            if ( ent.Attributes.HasAttribute("herdMembers") )
            {
                long[] herdEntIdArray = SerializerUtil.Deserialize<long[]>(ent.Attributes.GetBytes("herdMembers"));

                foreach( long id in herdEntIdArray)
                {
                    Entity herdMember = ent.World.GetEntityById(id);

                    if ( herdMember != null )
                        herdMembers.Add( herdMember );
                }
            }

            return herdMembers;
        }

        public static void JoinSameHerdAsEntity( Entity newMember, Entity currentMember )
        {
            Debug.Assert(newMember is EntityAgent, "Only entity agents can join a herd.");
            Debug.Assert(currentMember is EntityAgent, "Entity " + currentMember + " is not a member of a herd");

            EntityAgent newMemberAgent = newMember as EntityAgent;
            EntityAgent currentMemberAgent = currentMember as EntityAgent;

            newMemberAgent.HerdId = currentMemberAgent.HerdId;

            //Remove me from my old herd.
            List<Entity> oldHerdMembers = GetMasterHerdList(newMember);
            oldHerdMembers.Remove(newMember);

            //Inform members of my old herd.
            foreach (Entity herdMember in oldHerdMembers)
                SetMasterHerdList(herdMember, oldHerdMembers);

            //Add me to the new herd.
            List<Entity> newHerdMembers = GetMasterHerdList(currentMember);
            newHerdMembers.Add(newMember);

            //Inform members of my new herd.
            foreach (Entity herdMember in newHerdMembers)
                SetMasterHerdList(herdMember, newHerdMembers);
        }

        public static List<Entity> GetHerdMembersInRangeOfPos( Entity ent, Vec3d pos, float range )
        {
            List<Entity> allHerdMembers = GetMasterHerdList(ent);
            List<Entity> membersInRange = new List<Entity>();
            foreach( Entity member in allHerdMembers)
            {
                double distSqr = member.ServerPos.XYZ.SquareDistanceTo(pos);
                if ( distSqr <= range * range )
                    membersInRange.Add(member);
            }

            return membersInRange;
            
        }

        public static void TryNotifyHerdMembersToAttack(EntityAgent alertEntity, Entity targetEntity, Vec3d lastPathUpdatePos, Vec3d lastKnownPos, Vec3d lastKnownMotion, float alertRange, bool requireAwarenessToNotify )
        {
            if ( alertEntity.HerdId > 0 )
            {
                List<Entity> membersInRange = GetHerdMembersInRangeOfPos( alertEntity, alertEntity.ServerPos.XYZ, alertRange );
                foreach (EntityAgent herdMember in membersInRange)
                {
                    if (herdMember.EntityId != alertEntity.EntityId && herdMember.HerdId == alertEntity.HerdId)
                    {
                        if ( !requireAwarenessToNotify || AwarenessManager.IsAwareOfTarget(herdMember, alertEntity, alertRange, alertRange) )
                        {
                            EntityTargetPairing targetPairing = new EntityTargetPairing(alertEntity, targetEntity, lastPathUpdatePos, lastKnownPos, lastKnownMotion);
                            herdMember.Notify("attackEntity", targetPairing);
                        } 
                    }
                }
            }
        }

        public static float GetHerdAlertRangeForEntity( Entity ent )
        {
            //We can replace this with a Attribute in the future if we want.
            return HERD_ALERT_RANGE;
        }

        public static double CalculateHerdInjuryRatio(List<Entity> herdMembers)
        {
            if (herdMembers.Count == 0)
                return 0;

            double totalCurrentHealth = 0f;
            double totalMaxHealth = 0f;
            foreach (Entity herdMember in herdMembers)
            {
                ITreeAttribute treeAttribute = herdMember.WatchedAttributes.GetTreeAttribute("health");

                if (treeAttribute != null)
                {
                    totalCurrentHealth += treeAttribute.GetFloat("currenthealth"); ;
                    totalMaxHealth += treeAttribute.GetFloat("maxhealth"); ;
                }
            }

            return (totalMaxHealth - totalCurrentHealth) / totalMaxHealth;
        }

        public static double PercentOfHerdAlive(List<Entity> herdMembers)
        {
            if (herdMembers.Count == 0)
                return 0;

            int aliveCount = 0;
            foreach (Entity herdMember in herdMembers)
            {
                if (herdMember.Alive)
                    aliveCount++;
            }

            double percentLiving = (double)aliveCount / (double)herdMembers.Count;
            return percentLiving;
        }

        public static bool AreMembersOfSameHerd(Entity ent1, Entity ent2)
        {
            if (!(ent1 is EntityAgent))
                return false;

            if (!(ent2 is EntityAgent))
                return false;

            EntityAgent agent1 = ent1 as EntityAgent;
            EntityAgent agent2 = ent2 as EntityAgent;

            return agent1.HerdId == agent2.HerdId && agent1.HerdId != 0;
        }

        public static List<Entity> GetHerdMembersInRangeOfPos(List<Entity> herdMembers, Vec3d pos, float range)
        {
            List<Entity> herdMembersInRange = new List<Entity>();
            foreach (Entity herdMember in herdMembers)
            {
                double distSqr = herdMember.ServerPos.XYZ.SquareDistanceTo(pos);

                if (distSqr <= range * range)
                    herdMembersInRange.Add(herdMember);
            }
            return herdMembersInRange;
        }

        public static bool CanRespondToNotify( Entity entity )
        {

            if (!entity.Alive)
                return false;

            return true;
        }

        /*
          ____  _                         _   _ _   _ _ _ _         
         |  _ \| | __ _ _   _  ___ _ __  | | | | |_(_) (_) |_ _   _ 
         | |_) | |/ _` | | | |/ _ \ '__| | | | | __| | | | __| | | |
         |  __/| | (_| | |_| |  __/ |    | |_| | |_| | | | |_| |_| |
         |_|   |_|\__,_|\__, |\___|_|     \___/ \__|_|_|_|\__|\__, |
                        |___/                                 |___/ 
         */

        public static bool CanTargetPlayer( EntityPlayer player )
        {
            EnumGameMode currentGamemode = player.Player.WorldData.CurrentGameMode;
            if (currentGamemode == EnumGameMode.Creative || currentGamemode == EnumGameMode.Spectator)
                return false;

            if (!player.Alive)
                return false;

            return true;
        }

        public static bool IsPlayerWithinRangeOfPos(EntityPlayer player, Vec3d pos, float range)
        {
            double distSqr = player.ServerPos.XYZ.SquareDistanceTo(pos);
            if (distSqr <= range * range)
                return true;

            return false;
        }

        public static bool IsAnyPlayerWithinRangeOfPos(Vec3d pos, float range, IWorldAccessor world)
        {
            IPlayer[] playersOnline = world.AllOnlinePlayers;
            foreach (IPlayer player in playersOnline)
            {
                EntityPlayer playerEnt = player.Entity;
                if (IsPlayerWithinRangeOfPos(playerEnt, pos, range))
                    return true;
            }

            return false;
        }

        public static bool CanAnyPlayerSeePos( Vec3d pos, float autoPassRange, IWorldAccessor world, Entity[] ignoreEnts = null )
        {
            IPlayer[] playersOnline = world.AllOnlinePlayers;
            foreach (IPlayer player in playersOnline)
            {
                EntityPlayer playerEnt = player.Entity;

                if (IsPlayerWithinRangeOfPos(playerEnt, pos, autoPassRange))
                {
                    if (CanEntSeePos(playerEnt, pos, 160, ignoreEnts))
                        return true;
                }
            }

            return false;
        }

        public static bool CanAnyPlayerSeeMe( Entity ent, float autoPassRange, Entity[] ignoreEnts = null)
        {
            Vec3d myEyePos = ent.ServerPos.XYZ.Add(0, ent.LocalEyePos.Y, 0);
            return CanAnyPlayerSeePos( myEyePos, autoPassRange, ent.World, ignoreEnts);
        }

        private static BlockSelection blockSel = null;
        private static EntitySelection entitySel = null;
        private static Entity losTraceSourceEnt = null;
        private static Entity[] ignoreEnts = null;

        /*
          _     ___  ____    _   _ _   _ _ _ _         
         | |   / _ \/ ___|  | | | | |_(_) (_) |_ _   _ 
         | |  | | | \___ \  | | | | __| | | | __| | | |
         | |__| |_| |___) | | |_| | |_| | | | |_| |_| |
         |_____\___/|____/   \___/ \__|_|_|_|\__|\__, |
                                                 |___/ 
         */

        public static bool CanEntSeePos( Entity ent, Vec3d pos, float fov = AwarenessManager.DEFAULT_AI_VISION_FOV, Entity[] entsToIgnore = null)
        {
            blockSel = null;
            entitySel = null;
            losTraceSourceEnt = ent;
            ignoreEnts = entsToIgnore;

            Vec3d entEyePos = ent.ServerPos.XYZ.Add(0, ent.LocalEyePos.Y, 0);
            Vec3d entViewForward = GetEntityForwardViewVector(ent, pos);

            Vec3d entToPos = pos - entEyePos;
            entToPos = entToPos.Normalize();

            double maxViewDot = Math.Cos( (fov / 2) * (Math.PI / 180));
            double dot = entViewForward.Dot(entToPos);

            if (dot > maxViewDot)
            {
                ent.World.RayTraceForSelection(entEyePos, pos, ref blockSel, ref entitySel, CanEntSeePos_BlockFilter, CanEntSeePos_EntityFilter);

                //DebugUtility.DebugDrawRayTrace(ent.World, entEyePos, pos, CanEntSeePos_BlockFilter, CanEntSeePos_EntityFilter);

                if (blockSel == null && entitySel == null)
                    return true;
            }

            return false;
        }

        private static bool CanEntSeePos_BlockFilter(BlockPos pos, Block block)
        {
            //Leaves block visability
            if (block.BlockMaterial == EnumBlockMaterial.Leaves)
                return true;

            //Plants block visability
            if (block.BlockMaterial == EnumBlockMaterial.Plant)
                return true;

            return true;
        }

        private static bool CanEntSeePos_EntityFilter(Entity ent)
        {
            if (ent == losTraceSourceEnt)
                return false;

            if ( ignoreEnts != null )
            {
                if (ignoreEnts.Contains(ent))
                    return false;
            }

            //AI can see through other AI.
            if ( ent is EntityAgent )
            {
                return false;
            }

            return true;
        }

        public static Vec3d GetEntityForwardViewVector(Entity ent, Vec3d pitchPoint)
        {
            if ( ent is EntityPlayer)
                return GetPlayerForwardViewVector(ent);

            return GetAiForwardViewVectorWithPitchTowardsPoint(ent, pitchPoint);
        }

        public static Vec3d GetPlayerForwardViewVector( Entity player)
        {
            Debug.Assert ( player is EntityPlayer );

            Vec3d playerEyePos = player.ServerPos.XYZ.Add(0, player.LocalEyePos.Y, 0);
            Vec3d playerAheadPos = playerEyePos.AheadCopy(1, player.ServerPos.Pitch, player.ServerPos.Yaw);
            return (playerAheadPos - playerEyePos).Normalize();
        }

        public static Vec3d GetAiForwardViewVectorWithPitchTowardsPoint(Entity ent, Vec3d pitchPoint)
        {
            //WORK AROUND FOR VS ENGINE BUG:
            //This split in the view vector function is to adress more VS core engine badness.
            //With ents other than the player, their forward vector is offset by 90 degrees in yaw to the right of their forward, i.e. you get their right vector, not their forward.
            //It's really messy and bad, but we're correcting for it here because it's not clear how deep the issue goes and we can't modify the core engine to fix it.

            //View Forward Issue for Non-Players
            //Non-player entities currentlyhave their pitch locked to the horizon. We need to calculate pitch as if the Ai Is looking above or below the horizon,
            //and only account for Yaw when calculating view forward.

            Vec3d entEyePos = ent.ServerPos.XYZ.Add(0, ent.LocalEyePos.Y, 0);

            double opposite = (pitchPoint.Y - entEyePos.Y);
            int dirScalar = opposite < 0 ? -1 : 1;
            double oppositeSqr = (opposite * opposite) * dirScalar;

            Vec3d dirFromEntToPoint2D = new Vec3d( pitchPoint.X-entEyePos.X, 0, pitchPoint.Z - entEyePos.Z);

            //Try to save the square
            double adjacentSqr = dirFromEntToPoint2D.LengthSq();

            double pitch = Math.Atan2(oppositeSqr, adjacentSqr);

            double pitchDeg = pitch / (Math.PI / 180);

            Vec3d eyePos = ent.ServerPos.XYZ.Add(0, ent.LocalEyePos.Y, 0);
            Vec3d aheadPos = eyePos.AheadCopy(1, pitch, ent.ServerPos.Yaw + (90 * (Math.PI / 180)));
            return (aheadPos - eyePos).Normalize();
        }
      
        /*
          _____    _                       _     _   _ _   _ _ _ _         
         |_   _|__| | ___ _ __   ___  _ __| |_  | | | | |_(_) (_) |_ _   _ 
           | |/ _ \ |/ _ \ '_ \ / _ \| '__| __| | | | | __| | | | __| | | |
           | |  __/ |  __/ |_) | (_) | |  | |_  | |_| | |_| | | | |_| |_| |
           |_|\___|_|\___| .__/ \___/|_|   \__|  \___/ \__|_|_|_|\__|\__, |
                         |_|                                         |___/          
        */

        public static void TryTeleportToEntity(Entity entityToTeleport, Entity targetEntity )
        {
            if (targetEntity == null)
                return;

            if (AiUtility.IsInCombat(entityToTeleport))
                return;

            //We cannot teleport if we were recently in combat.
            if (entityToTeleport.World.ElapsedMilliseconds - AiUtility.GetLastTimeEntityInCombatMs(entityToTeleport) < BLOCK_TELEPORT_AFTER_COMBAT_DURATION)
                return;

            if (AiUtility.IsAnyPlayerWithinRangeOfPos(entityToTeleport.ServerPos.XYZ, BLOCK_TELEPORT_WHEN_PLAYER_CLOSER_THAN, entityToTeleport.World))
                return;

            if (AiUtility.IsAnyPlayerWithinRangeOfPos(targetEntity.ServerPos.XYZ, BLOCK_TELEPORT_WHEN_PLAYER_CLOSER_THAN, entityToTeleport.World))
                return;

            if (AiUtility.CanAnyPlayerSeeMe(entityToTeleport, ALWAYS_ALLOW_TELEPORT_BEYOND_RANGE_FROM_PLAYER))
                return;

            if (AiUtility.CanAnyPlayerSeeMe(targetEntity, ALWAYS_ALLOW_TELEPORT_BEYOND_RANGE_FROM_PLAYER))
                return;

            Vec3d teleportPos = FindDecentTeleportPos(entityToTeleport, targetEntity.ServerPos.XYZ);

            if (teleportPos != null)
                entityToTeleport.TeleportTo(teleportPos);
        }

        private static Vec3d FindDecentTeleportPos(Entity entityToTeleport, Vec3d teleportLocation)
        {
            var ba = entityToTeleport.World.BlockAccessor;
            var rnd = entityToTeleport.World.Rand;

            Vec3d pos = new Vec3d();
            BlockPos bpos = new BlockPos();
            Cuboidf collisionBox = entityToTeleport.CollisionBox;
            int[] yTestOffsets = { 0, -1, 1, -2, 2, -3, 3 };
            for (int i = 0; i < 3; i++)
            {
                double randomXOffset = rnd.NextDouble() * 10 - 5;
                double randomYOffset = rnd.NextDouble() * 10 - 5;

                for (int j = 0; j < yTestOffsets.Length; j++)
                {
                    int yAxisOffset = yTestOffsets[j];
                    pos.Set(teleportLocation.X + randomXOffset, teleportLocation.Y + yAxisOffset, teleportLocation.Z + randomYOffset);

                    // Test if this location is free and clear.
                    if (!entityToTeleport.World.CollisionTester.IsColliding(entityToTeleport.World.BlockAccessor, collisionBox, pos, false))
                    {
                        //POSSIBLE PERFORMANCE HAZARD!!!
                        //This call is effectively 2 X (3 X 7) traces per player if it fails. That's way too much!
                        //If players can't see the entity's foot position.
                        if (!AiUtility.CanAnyPlayerSeePos(pos, ALWAYS_ALLOW_TELEPORT_BEYOND_RANGE_FROM_PLAYER, entityToTeleport.World))
                        {
                            //If players can't see the entity's eye position.
                            if (!AiUtility.CanAnyPlayerSeePos(pos.Add(0, entityToTeleport.LocalEyePos.Y, 0), ALWAYS_ALLOW_TELEPORT_BEYOND_RANGE_FROM_PLAYER, entityToTeleport.World))
                                return pos;
                        }
                    }
                }
            }

            return null;
        }

        /*
           ____                           _   _   _ _   _ _ _ _         
          / ___| ___ _ __   ___ _ __ __ _| | | | | | |_(_) (_) |_ _   _ 
         | |  _ / _ \ '_ \ / _ \ '__/ _` | | | | | | __| | | | __| | | |
         | |_| |  __/ | | |  __/ | | (_| | | | |_| | |_| | | | |_| |_| |
          \____|\___|_| |_|\___|_|  \__,_|_|  \___/ \__|_|_|_|\__|\__, |
                                                                  |___/ 
        */
        public static Vec3d GetCenterMass(Entity ent)
        {
            if (ent.SelectionBox.Empty)
                return ent.SidedPos.XYZ;

            float heightOffset = ent.SelectionBox.Y2 - ent.SelectionBox.Y1;
            return ent.SidedPos.XYZ.Add(0, heightOffset, 0);
        }

        public static bool LocationInLiquid(IWorldAccessor world, Vec3d pos)
        {
            BlockPos blockPos = pos.AsBlockPos;
            Block block = world.BlockAccessor.GetBlock(blockPos);

            if (block != null)
            {
                return block.BlockMaterial == EnumBlockMaterial.Liquid;
            }

            return false;
        }

        public static Vec3d ClampPositionToGround(IWorldAccessor world, Vec3d startingPos, int maxBlockDistance)
        {
            BlockPos posAsBlockPos = startingPos.AsBlockPos;
            BlockPos previousCheckPos = posAsBlockPos.Copy();
            BlockPos currentCheckPos = posAsBlockPos.Copy();

            Block currentBlock = world.BlockAccessor.GetBlock(currentCheckPos);

            if (currentBlock == null)
            {
                return startingPos;
            }
            else
            {
                //our starting point is in solid
                if (IsPositionInSolid( world, startingPos ))
                    return PopPositionAboveGround(world, startingPos, maxBlockDistance);
            }

            int groundCheckTries = 0;
            while (maxBlockDistance > groundCheckTries)
            {
                currentCheckPos = previousCheckPos.DownCopy();
                currentBlock = world.BlockAccessor.GetBlock(currentCheckPos);

                //Check Block Below us.
                if (currentBlock != null)
                {
                    if (IsPositionInSolid(world, currentCheckPos) )
                    {
                        return new Vec3d(startingPos.X, previousCheckPos.Y, startingPos.Z);
                    }

                }

                previousCheckPos = currentCheckPos;
                groundCheckTries++;
            }
            
            return startingPos;

        }

        public static Vec3d PopPositionAboveGround(IWorldAccessor world, Vec3d startingPos, int maxBlockDistance)
        {
            BlockPos posAsBlockPos = startingPos.AsBlockPos;
            BlockPos previousCheckPos = posAsBlockPos.Copy();
            BlockPos currentCheckPos = posAsBlockPos.Copy();

            Block currentBlock = world.BlockAccessor.GetBlock(currentCheckPos);

            if (currentBlock == null)
            {
                return startingPos;
            }
            else
            {
                //our starting point is in solid
                if (!IsPositionInSolid(world, startingPos))
                    return startingPos;
            }

            int groundCheckTries = 0;
            while (maxBlockDistance > groundCheckTries)
            {
                currentCheckPos = previousCheckPos.UpCopy();
                currentBlock = world.BlockAccessor.GetBlock(currentCheckPos);

                //Check Block Below us.
                if (currentBlock != null)
                {
                    if (!IsPositionInSolid(world, currentCheckPos))
                    {
                        return new Vec3d(startingPos.X, currentCheckPos.Y, startingPos.Z);
                    }

                }

                previousCheckPos = currentCheckPos;
                groundCheckTries++;
            }

            return startingPos;
        }

        public static Vec3d MovePositionByBlockInDirectionOfVector( Vec3d positionToMove, Vec3d directionToMove )
        {
            BlockPos endBlockPos = (positionToMove + directionToMove).AsBlockPos;
            return new Vec3d(endBlockPos.X, endBlockPos.Y, endBlockPos.Z);
        }

        public static bool IsPositionInSolid(IWorldAccessor world, Vec3d pos)
        {
            BlockPos blockPos = pos.AsBlockPos;
            return IsPositionInSolid(world, blockPos);
        }

        public static bool IsPositionInSolid(IWorldAccessor world, BlockPos blockPos )
        {
            IBlockAccessor blockAccessor = world.BlockAccessor;
            Block blockAtPos = blockAccessor.GetBlock(blockPos);

            bool solid = blockAtPos.BlockMaterial != EnumBlockMaterial.Air && blockAtPos.BlockMaterial != EnumBlockMaterial.Liquid && blockAtPos.BlockMaterial != EnumBlockMaterial.Snow &&
                blockAtPos.BlockMaterial != EnumBlockMaterial.Plant && blockAtPos.BlockMaterial != EnumBlockMaterial.Leaves;

            if (solid)
            {
                bool confirmedSolid = false;
                foreach (BlockFacing facing in BlockFacing.ALLFACES)
                {
                    if (blockAtPos.SideSolid[facing.Index] == true)
                    {
                        confirmedSolid = true;
                        break;
                    }

                    BlockEntity blockEnt = blockAccessor.GetBlockEntity(blockPos);
                    if (blockAtPos is BlockMicroBlock)
                    {
                        if (blockAccessor.GetBlockEntity(blockPos) is BlockEntityMicroBlock)
                        {
                            BlockEntityMicroBlock microBlockEnt = blockAccessor.GetBlockEntity(blockPos) as BlockEntityMicroBlock;
                            if (microBlockEnt.sideAlmostSolid[facing.Index] == true)
                            {
                                confirmedSolid = true;
                                break;
                            }
                        }
                    }
                }

                solid = confirmedSolid;
            }

            return solid;
        }

        public static bool EntityCodeInList( Entity ent, List<string> codes )
        {
            foreach ( string code in codes ) 
            {
                if (ent.Code.Path == code)
                    return true;

                if (ent.Code.Path.StartsWithFast(code))
                    return true;
            }

            return false;
        }

    }

}
