
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

namespace EvilBelow
{
    [ProtoContract]
    public class EvilBelowConfig
    {
        [ProtoMember(1)]
        public float SneakAttackDamageMultRanged = 1.5f;

        [ProtoMember(2)]
        public float SneakAttackDamageMultMelee = 2.5f;

        [ProtoMember(1)]
        public bool DevMode = false;
    }

    public class EBCore : ModSystem
    {
        ICoreAPI api;
        ICoreServerAPI sapi;
        ICoreClientAPI capi;

        private Harmony harmony;

        EvilBelowConfig config = new EvilBelowConfig();

        public override double ExecuteOrder()
        {
            return 0.11;
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
            api.Network.RegisterChannel("evilBelowConfig").RegisterMessageType<EvilBelowConfig>();

            base.Start(api);              

            harmony = new Harmony("com.grifthegnome.evilbelow.patches");
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
            capi.Network.GetChannel("evilBelowConfig").SetMessageHandler<EvilBelowConfig>(OnConfigFromServer);

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
                EBSpawnEvaluator.Initialize(api as ICoreServerAPI);
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
            api.RegisterEntity("EntityEBCreature", typeof(EntityEBCreature));
        }

        private void RegisterBlocksShared()
        {
            //api.RegisterBlockClass("BlockStocks", typeof(BlockStocks));
            //api.RegisterBlockClass("BlockHeadOnSpear", typeof(BlockHeadOnSpear));
        }

        private void RegisterBlockEntitiesShared()
        {
            //api.RegisterBlockEntityClass("BlockEntityOutlawSpawnBlocker", typeof(BlockEntityOutlawSpawnBlocker));
        }
        private void RegisterItemsShared()
        {
            //api.RegisterItemClass("ItemOutlawHead", typeof(ItemOutlawHead));
        }

        private void ReadConfigFromJson(ICoreAPI api)
        {
            try
            {
                EvilBelowConfig modConfig = api.LoadModConfig<EvilBelowConfig>("EvilBelowConfig.json");

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
                api.World.Logger.Error("Failed loading EvilBelowConfig.json, Will initialize new one", e);
                config = new EvilBelowConfig();
                api.StoreModConfig( config, "EvilBelowConfig.json");
            }
            
            // Called on both sides
        }

        private void ApplyConfigPatchFlags(ICoreAPI api)
        {
            //Enable/Disable Outlaw Types
            /*
            api.World.Config.SetBool("enableLooters", config.EnableLooters);
            api.World.Config.SetBool("enablePoachers", config.EnablePoachers);
            api.World.Config.SetBool("enableBrigands", config.EnableBrigands);
            api.World.Config.SetBool("enableYeoman", config.EnableYeomen);
            api.World.Config.SetBool("enableDeserters", config.EnableDeserters);
            api.World.Config.SetBool("enableBannermen", config.EnableDeserters && config.EnableBannermen);
            api.World.Config.SetBool("enableFeralHounds", config.EnableFeralHounds);
            api.World.Config.SetBool("enableHuntingHounds", config.EnableHuntingHounds);
            */
        }

        private void ApplyConfigGlobals()
        {
            //Start Spawn Safe Zone Vars
            /*
            OMGlobalConstants.startingSpawnSafeZoneRadius           = config.StartingSpawnSafeZoneRadius;
            OMGlobalConstants.startingSafeZoneHasLifetime           = config.StartingSafeZoneHasLifetime;
            OMGlobalConstants.startingSafeZoneShrinksOverlifetime   = config.StartingSafeZoneShrinksOverLifetime;
            OMGlobalConstants.startingSpawnSafeZoneLifetimeInDays   = config.StartingSpawnSafeZoneLifetimeInDays;
            OMGlobalConstants.claimedLandBlocksOutlawSpawns         = config.ClaimedLandBlocksOutlawSpawns;

            //Classic Voice Setting
            OMGlobalConstants.outlawsUseClassicVintageStoryVoices   = config.OutlawsUseClassicVintageStoryVoices;
            */

            //Sneak Attacks
            EBGlobalConstants.sneakAttackDamageMultRanged           = config.SneakAttackDamageMultRanged;
            EBGlobalConstants.sneakAttackDamageMultMelee            = config.SneakAttackDamageMultMelee;

            //Devmode
            EBGlobalConstants.devMode = config.DevMode;

            //Store an up-to-date version of the config so any new fields that might differ between mod versions are added without altering user values.
            api.StoreModConfig(config, "EvilBelowConfig.json");

        }

        private void OnConfigFromServer(EvilBelowConfig networkMessage)
        {
            this.config = networkMessage;
            ApplyConfigGlobals();
        }

        private void OnPlayerJoin(IServerPlayer connectPlayer)
        {
            //Set sneak attack values for Expanded Ai Tasks.
            EntityPlayer playerEnt = connectPlayer.Entity;
            AiUtility.SetMeleeSneakAttackMultiplierForPlayer(playerEnt, EBGlobalConstants.sneakAttackDamageMultMelee);
            AiUtility.SetRangedSneakAttackMultiplierForPlayer(playerEnt, EBGlobalConstants.sneakAttackDamageMultRanged);
        }
    }
}
