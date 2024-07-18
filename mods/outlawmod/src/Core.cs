
using System;
using System.Diagnostics;
using System.Reflection;
using ProtoBuf;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.API.Config;
using ExpandedAiTasks;

namespace OutlawMod
{
    [ProtoContract]
    public class OutlawModConfig
    {
        [ProtoMember(1)]
        public bool EnableLooters = true;
        
        [ProtoMember(2)]
        public bool EnablePoachers = true;

        [ProtoMember(3)]
        public bool EnableBrigands = true;

        [ProtoMember(4)]
        public bool EnableYeomen = true;

        [ProtoMember(5)]
        public bool EnableDeserters = true;

        [ProtoMember(6)]
        public bool EnableBannermen = true;

        [ProtoMember(7)]
        public bool EnableFeralHounds = true;

        [ProtoMember(8)]
        public bool EnableHuntingHounds = true;

        [ProtoMember(9)]
        public float StartingSpawnSafeZoneRadius = 500f;

        [ProtoMember(10)]
        public bool StartingSafeZoneHasLifetime = true;

        [ProtoMember(11)]
        public bool StartingSafeZoneShrinksOverLifetime = true;

        [ProtoMember(12)]
        public float StartingSpawnSafeZoneLifetimeInDays = 45f;

        [ProtoMember(13)]
        public bool ClaimedLandBlocksOutlawSpawns = true;

        [ProtoMember(14)]
        public bool OutlawsUseClassicVintageStoryVoices = false;

        [ProtoMember(15)]
        public float SneakAttackDamageMultRanged = 1.5f;

        [ProtoMember(16)]
        public float SneakAttackDamageMultMelee = 2.5f;

        [ProtoMember(17)]
        public bool DevMode = false;
    }

    public class Core : ModSystem
    {
        ICoreAPI api;
        ICoreServerAPI sapi;
        ICoreClientAPI capi;

        private Harmony harmony;

        OutlawModConfig config = new OutlawModConfig();

        public override double ExecuteOrder()
        {
            return 0.1;
        }

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);
            ReadConfigFromJson(api);
            ApplyConfigPatchFlags(api);
        }

        public override void Start(ICoreAPI api)
        {
            this.api = api;

            //Broadcast Outlaw Mod Config to Clients.
            api.Network.RegisterChannel("outlawModConfig").RegisterMessageType<OutlawModConfig>();

            base.Start(api);              

            harmony = new Harmony("com.grifthegnome.outlawmod.causeofdeath");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            //Deploy Expanded Ai Tasks
            ExpandedAiTasksDeployment.Deploy(api);

            RegisterEntitiesShared();
            RegisterBlocksShared();
            RegisterBlockEntitiesShared();
            RegisterItemsShared();

        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            capi.Network.GetChannel("outlawModConfig").SetMessageHandler<OutlawModConfig>(OnConfigFromServer);

            api.Event.LevelFinalize += () =>
            {
                //Events we can run after the level finalizes.
            };
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            base.StartServerSide(api);

            api.Event.ServerRunPhase(EnumServerRunPhase.GameReady, () => {
                ApplyConfigGlobals();
            });
            api.Event.ServerRunPhase(EnumServerRunPhase.GameReady, () =>
            {
                //Initialize our static instance of our spawn evaluator.
                OutlawSpawnEvaluator.Initialize(api as ICoreServerAPI);
            });

            sapi.Event.PlayerJoin += OnPlayerJoin;
        }

        public override void Dispose()
        {
            harmony.UnpatchAll(harmony.Id);
            base.Dispose();
        }

        private void RegisterEntitiesShared()
        {
            api.RegisterEntity("EntityOutlaw", typeof(EntityOutlaw));
        }

        private void RegisterBlocksShared()
        {
            api.RegisterBlockClass("BlockStocks", typeof(BlockStocks));
            api.RegisterBlockClass("BlockHeadOnSpear", typeof(BlockHeadOnSpear));
            api.RegisterBlockClass("BlockBannerCaptured", typeof(BlockBannerCaptured));
        }

        private void RegisterBlockEntitiesShared()
        {
            api.RegisterBlockEntityClass("BlockEntityOutlawSpawnBlocker", typeof(BlockEntityOutlawSpawnBlocker));
        }
        private void RegisterItemsShared()
        {
            api.RegisterItemClass("ItemOutlawHead", typeof(ItemOutlawHead));
        }

        private void ReadConfigFromJson(ICoreAPI api)
        {
            try
            {
                OutlawModConfig modConfig = api.LoadModConfig<OutlawModConfig>("OutlawModConfig.json");

                if (modConfig != null)
                {
                    config = modConfig;
                }
                else
                {
                    //We don't have a valid config.
                    throw new Exception();
                }
                    
            }
            catch (Exception e)
            {
                api.World.Logger.Error("Failed loading OutlawModConfig.json, Will initialize new one", e);
                config = new OutlawModConfig();
                api.StoreModConfig( config, "OutlawModConfig.json");
            }
            
            // Called on both sides
        }

        private void ApplyConfigPatchFlags(ICoreAPI api)
        {
            //Enable/Disable Outlaw Types
            api.World.Config.SetBool("enableLooters", config.EnableLooters);
            api.World.Config.SetBool("enablePoachers", config.EnablePoachers);
            api.World.Config.SetBool("enableBrigands", config.EnableBrigands);
            api.World.Config.SetBool("enableYeoman", config.EnableYeomen);
            api.World.Config.SetBool("enableDeserters", config.EnableDeserters);
            api.World.Config.SetBool("enableBannermen", config.EnableDeserters && config.EnableBannermen);
            api.World.Config.SetBool("enableFeralHounds", config.EnableFeralHounds);
            api.World.Config.SetBool("enableHuntingHounds", config.EnableHuntingHounds);
        }

        private void ApplyConfigGlobals()
        {
            //Start Spawn Safe Zone Vars
            OMGlobalConstants.startingSpawnSafeZoneRadius           = config.StartingSpawnSafeZoneRadius;
            OMGlobalConstants.startingSafeZoneHasLifetime           = config.StartingSafeZoneHasLifetime;
            OMGlobalConstants.startingSafeZoneShrinksOverlifetime   = config.StartingSafeZoneShrinksOverLifetime;
            OMGlobalConstants.startingSpawnSafeZoneLifetimeInDays   = config.StartingSpawnSafeZoneLifetimeInDays;
            OMGlobalConstants.claimedLandBlocksOutlawSpawns         = config.ClaimedLandBlocksOutlawSpawns;

            //Classic Voice Setting
            OMGlobalConstants.outlawsUseClassicVintageStoryVoices   = config.OutlawsUseClassicVintageStoryVoices;

            //Sneak Attacks
            OMGlobalConstants.sneakAttackDamageMultRanged           = config.SneakAttackDamageMultRanged;
            OMGlobalConstants.sneakAttackDamageMultMelee            = config.SneakAttackDamageMultMelee;

            //Devmode
            OMGlobalConstants.devMode = config.DevMode;

            //Store an up-to-date version of the config so any new fields that might differ between mod versions are added without altering user values.
            api.StoreModConfig(config, "OutlawModConfig.json");

        }

        private void OnConfigFromServer(OutlawModConfig networkMessage)
        {
            this.config = networkMessage;
            ApplyConfigGlobals();
        }

        private void OnPlayerJoin(IServerPlayer connectPlayer)
        {
            //Set sneak attack values for Expanded Ai Tasks.
            EntityPlayer playerEnt = connectPlayer.Entity;
            AiUtility.SetMeleeSneakAttackMultiplierForPlayer( playerEnt, OMGlobalConstants.sneakAttackDamageMultMelee );
            AiUtility.SetRangedSneakAttackMultiplierForPlayer(playerEnt, OMGlobalConstants.sneakAttackDamageMultRanged);
        }
    }
}
