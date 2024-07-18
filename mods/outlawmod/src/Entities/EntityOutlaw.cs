using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using ExpandedAiTasks;
using System.Collections.Generic;
using System.Linq;

namespace OutlawMod
{

    public class EntityOutlaw: EntityHumanoid
    {
        protected const int COMPANION_SPAWN_INTERVAL_MS = 125;
        protected const int COMPANION_SPAWN_FAIL_LIMIT = 5;

        protected List<long> callbacks = new List<long>();
        protected List<string> companionSpawnQueue = new List<string>();
        protected int currentSpawnQueueIndex = 0;
        protected int companionSpawnFailCount = 0;

        public static OrderedDictionary<string, TraderPersonality> Personalities = new OrderedDictionary<string, TraderPersonality>()
        {
            { "formal", new TraderPersonality(1, 1, 0.9f) },
            { "balanced", new TraderPersonality(1.2f, 0.9f, 1.1f) },
            { "lazy", new TraderPersonality(1.65f, 0.7f, 0.9f) },
            { "rowdy", new TraderPersonality(0.75f, 1f, 1.8f) },
        };

        public string Personality
        {
            get { return WatchedAttributes.GetString("personality", "rowdy"); }
            set
            {
                WatchedAttributes.SetString("personality", value);
                talkUtil?.SetModifiers(Personalities[value].ChorldDelayMul, Personalities[value].PitchModifier, Personalities[value].VolumneModifier);

            }
        }

        public EntityTalkUtil talkUtil;

        //Look at Entity.cs in the VAPI project for more functions you can override.

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);

            //Assign Personality for Classic Voice
            if ( World.Side == EnumAppSide.Client )
            {
                this.talkUtil = new EntityTalkUtil(api as ICoreClientAPI, this);

                Personality = Personalities.GetKeyAtIndex(World.Rand.Next(Personalities.Count));

                AssetLocation voiceSound = new AssetLocation(properties.Attributes?["classicVoice"].ToString());
                
                if (voiceSound != null)
                    talkUtil.soundName = voiceSound;

                this.Personality = this.Personality; // to update the talkutil
            }
        }

        private void BuildCompanionSpawnQueue()
        {
            if (this.Properties.Attributes.KeyExists("companions"))
            {
                IAttribute companionsAttribute = this.Properties.Attributes["companions"].ToAttribute();
                TreeAttribute[] companionsAsTree = companionsAttribute?.GetValue() as TreeAttribute[];

                for (int i = 0; i < companionsAsTree.Length; i++)
                {
                    Debug.Assert(companionsAsTree[i].HasAttribute("code"), "companions for " + this.Code.Path + " is missing code: at entry " + i);
                    Debug.Assert(companionsAsTree[i].HasAttribute("countMin"), "companions for " + this.Code.Path + " is missing countMin: at entry " + i);
                    Debug.Assert(companionsAsTree[i].HasAttribute("countMax"), "companions for " + this.Code.Path + " is missing countMax: at entry " + i);

                    string code     = companionsAsTree[i].GetString("code");
                    double countMin = companionsAsTree[i].GetDouble("countMin");
                    double countMax = companionsAsTree[i].GetDouble("countMax");

                    int count = (int)MathUtility.GraphClampedValue(0, 1, countMin, countMax, this.World.Rand.NextDouble());

                    Debug.Assert(code != this.Code.ToShortString(), this.Code.Path + " has companion " + code + " cyclical spawning detected!");

                    for( int j = 0; j < count; j++ )
                    {
                        companionSpawnQueue.Add(code);
                    }
                }
            }
        }

        /// <summary>
        /// Called when after the got loaded from the savegame (not called during spawn)
        /// </summary>
        public override void OnEntityLoaded()
        {
            base.OnEntityLoaded();

            if ( Api.Side == EnumAppSide.Server) 
            { 
                //Build our list of companions if it exists.
                BuildCompanionSpawnQueue();

                if (companionSpawnQueue.Count > 0)
                    AttemptSpawnCompanion(0f);
            }
        }

        /// <summary>
        /// Called when the entity spawns (not called when loaded from the savegame).
        /// </summary>
        public override void OnEntitySpawn()
        {
            base.OnEntitySpawn();

            if ( !OMGlobalConstants.devMode && Api.Side == EnumAppSide.Server)
            {
                //Check if the Outlaw is blocked by spawn rules.
                if ( !OutlawSpawnEvaluator.CanSpawnOutlaw( Pos.XYZ, this.Code ) )
                {
                    Utility.DebugLogToPlayerChat(Api as ICoreServerAPI, "Cannot Spawn " + this.Code.Path + " at: " + this.Pos + ". See Debug Log for Details.");
                    this.Die(EnumDespawnReason.Removed, null);
                    
                    return;
                }
            }

            if ( Api.Side == EnumAppSide.Server ) 
            { 
                //Build our list of companions if it exists.
                BuildCompanionSpawnQueue();

                if ( companionSpawnQueue.Count > 0 )
                    AttemptSpawnCompanion(0f);
            } 
        }

        protected void AttemptSpawnCompanion(float dt)
        {
            if (Api.Side == EnumAppSide.Server)
            {
                currentSpawnQueueIndex = this.Attributes.GetInt("currentSpawnQueueIndex", 0);

                if ( currentSpawnQueueIndex >= companionSpawnQueue.Count )
                {
                    this.Attributes.SetBool("hasSpawnedCompanions", true);
                    return;
                }

                if ( this.Attributes.HasAttribute("hasSpawnedCompanions") )
                {
                    if (this.Attributes.GetBool("hasSpawnedCompanions", false))
                        return;
                }                

                string codeToSpawn = companionSpawnQueue[currentSpawnQueueIndex];

                AssetLocation code = new AssetLocation(codeToSpawn);
                if (code == null)
                    return;

                EntityProperties companionProperties = this.World.GetEntityType(code);

                //Handle the case where the entry is invalid, or has been disabled by mod flags.
                //Continue to advance the queue in case later companions have valid codes.
                if (companionProperties == null)
                {
                    currentSpawnQueueIndex++;
                    if (currentSpawnQueueIndex < companionSpawnQueue.Count)
                    {
                        long callbackId = this.World.RegisterCallback(AttemptSpawnCompanion, COMPANION_SPAWN_INTERVAL_MS);
                        callbacks.Add(callbackId);
                    }
                    
                    return;
                }

                Cuboidf collisionBox = companionProperties.SpawnCollisionBox;

                // Delay companion spawning if we're colliding
                if (this.World.CollisionTester.IsColliding(this.World.BlockAccessor, collisionBox, this.ServerPos.XYZ, false))
                {
                    if (companionSpawnFailCount <= COMPANION_SPAWN_FAIL_LIMIT)
                    {
                        long callbackId = this.World.RegisterCallback(AttemptSpawnCompanion, 3000);
                        callbacks.Add(callbackId);
                    }
                    else
                    {
                        //If we fail to spawn our companions after a certain number of tries, this is a bad spawn spot and we should clean up our group so we don't lag the server looking for a spawn location forever.
                        CompanionSpawnFailCleanup();
                    }

                    companionSpawnFailCount++;

                    return;
                }

                Entity companionEnt = this.World.ClassRegistry.CreateEntity(companionProperties);

                if (companionEnt == null)
                    return;

                companionEnt.ServerPos.SetFrom(this.ServerPos);
                companionEnt.Pos.SetFrom(companionEnt.ServerPos);

                if (companionEnt is EntityAgent)
                {
                    EntityAgent companionAgent = (EntityAgent)companionEnt;
                    companionAgent.HerdId = this.HerdId;

                    AiUtility.SetGuardedEntity(companionAgent, this);
                }

                this.World.SpawnEntity(companionEnt);
                currentSpawnQueueIndex++;
                companionSpawnFailCount = 0;
                this.Attributes.SetInt("currentSpawnQueueIndex", currentSpawnQueueIndex);

                //Keep spawning until we exaust the queue.
                if (currentSpawnQueueIndex < companionSpawnQueue.Count)
                {
                    long callbackId = this.World.RegisterCallback(AttemptSpawnCompanion, COMPANION_SPAWN_INTERVAL_MS);
                    callbacks.Add(callbackId);
                    return;
                }
                else
                {
                    this.Attributes.SetBool("hasSpawnedCompanions", true);
                }
                    
            }
        }

        protected void CompanionSpawnFailCleanup()
        {
            //If we fail to spawn our companions after a certain number of reps, we should fail and clean ourselves up.
            List<Entity> herdMembers = AiUtility.GetMasterHerdList(this);

            foreach (Entity member in herdMembers) 
            {
                member.Die(EnumDespawnReason.Expire, null);
            }

            this.Die(EnumDespawnReason.Expire, null);
        }

        /// <summary>
        /// Called when the entity despawns
        /// </summary>
        /// <param name="despawn"></param>
        public override void OnEntityDespawn(EntityDespawnData despawnData )
        {
            base.OnEntityDespawn(despawnData);

            foreach (long callbackId in callbacks)
            {
                this.World.UnregisterCallback(callbackId);
            }
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);

            if (World.Side == EnumAppSide.Client && OMGlobalConstants.outlawsUseClassicVintageStoryVoices )
            {
                talkUtil.OnGameTick(dt);
            }
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            base.OnReceivedServerPacket(packetid, data);

            if (talkUtil == null)
                return;

            switch ( packetid )
            {
                
                case 1001: //hurt

                    if (!Alive) 
                        return;
                        
                    talkUtil.Talk(EnumTalkType.Hurt);

                    break;
                
                case 1002: //Death

                    talkUtil.Talk(EnumTalkType.Death);

                    break;

                case 1003: //Melee Attack, Shoot at Entity

                    if (!Alive)
                        return;

                    talkUtil.Talk(EnumTalkType.Complain);

                    break;

                case 1004: //Flee Entity, Morale Route

                    if (!Alive)
                        return;

                    talkUtil.Talk(EnumTalkType.Hurt2);

                    break;

                case 1005: //Seek Entity

                    if (!Alive)
                        return;

                    talkUtil.Talk(EnumTalkType.Laugh);

                    break;

                case 1006: //Engage Entity

                    if (!Alive)
                        return;

                    talkUtil.Talk(EnumTalkType.Laugh);

                    break;

            }
            
        }

        public override bool ShouldReceiveDamage(DamageSource damageSource, float damage)
        {
            
            Entity attacker = damageSource.SourceEntity;
            if (attacker is EntityProjectile && damageSource.CauseEntity != null)
            {
                attacker = damageSource.CauseEntity;
            }

            if ( attacker is EntityAgent )
            {
                //We are not allowed to do friendly fire damage to herd members.
                if (AiUtility.AreMembersOfSameHerd(attacker, this))
                    return false;
            }
                
            return true;
        }

        public override void PlayEntitySound(string type, IPlayer dualCallByPlayer = null, bool randomizePitch = true, float range = 24)
        {
            //If the config says use classic vintage story voices, use instrument voices.
            if ( OMGlobalConstants.outlawsUseClassicVintageStoryVoices )
            {
                if (World.Side == EnumAppSide.Server)
                {
                    switch (type)
                    {
                        case "hurt":
                            (World.Api as ICoreServerAPI).Network.BroadcastEntityPacket(this.EntityId, 1001);
                            return;

                        case "death":
                            (World.Api as ICoreServerAPI).Network.BroadcastEntityPacket(this.EntityId, 1002);
                            return;

                        case "meleeattack":
                            (World.Api as ICoreServerAPI).Network.BroadcastEntityPacket(this.EntityId, 1003);
                            return;

                        case "melee":
                            (World.Api as ICoreServerAPI).Network.BroadcastEntityPacket(this.EntityId, 1003);
                            return;

                        case "shootatentity":
                            (World.Api as ICoreServerAPI).Network.BroadcastEntityPacket(this.EntityId, 1003);
                            return;

                        case "fleeentity":
                            (World.Api as ICoreServerAPI).Network.BroadcastEntityPacket(this.EntityId, 1004);
                            return;

                        case "morale":
                            (World.Api as ICoreServerAPI).Network.BroadcastEntityPacket(this.EntityId, 1004);
                            return;

                        case "seekentity":
                            (World.Api as ICoreServerAPI).Network.BroadcastEntityPacket(this.EntityId, 1005);
                            return;
                        case "engageentity":
                            (World.Api as ICoreServerAPI).Network.BroadcastEntityPacket(this.EntityId, 1006);
                            return;


                    }
                }
                else if (World.Side == EnumAppSide.Client)
                {
                    //Certain sound events originate on the client.
                    switch (type)
                    {
                        case "idle":
                            talkUtil.Talk(EnumTalkType.Idle);
                            return;
                    }
                }
            }
            else
            {
                //Otherwise use Outlaw Mod VO Lines.
                base.PlayEntitySound(type, dualCallByPlayer, randomizePitch, range);
            }            
        }
    }
}