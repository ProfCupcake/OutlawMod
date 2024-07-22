
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using ExpandedAiTasks.Managers;
using ExpandedAiTasks.Behaviors;

namespace ExpandedAiTasks
{
    public static class ExpandedAiTasksDeployment
    {
        static bool hasDeployed = false;
        public static void Deploy( ICoreAPI api )
        {
            //Apply AiExpandedTask Patches if they haven't already been applied.
            if (ExpandedAiTasksHarmonyPatcher.ShouldPatch())
                ExpandedAiTasksHarmonyPatcher.ApplyPatches();

            if ( api.Side == EnumAppSide.Server && !hasDeployed )
            {
                ICoreServerAPI serverAPI = api as ICoreServerAPI;

                RegisterAiTasksOnServer();
                IlluminationManager.Init(api as ICoreServerAPI);
                AwarenessManager.Init();
                serverAPI.Event.OnEntityDespawn += IlluminationManager.OnDespawn;
                serverAPI.Event.OnEntityDespawn += AwarenessManager.OnDespawn;

                serverAPI.Event.OnEntityDeath += AwarenessManager.OnDeath;

                //Set up a timer to clean the dibs system every minute.
                serverAPI.Event.Timer(EntityManager.CleanDibsSystem, 60.0f);

                serverAPI.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, () => {
                    //Clean up all manager stuff.
                    //If we don't it persists between loads.
                    EntityManager.ShutdownCleanup();
                    AwarenessManager.ShutdownCleanup();
                    IlluminationManager.ShutdownCleanup();
                });
            }

            RegisterAiTasksShared();
            RegisterEntitiesShared(api);
            RegisterEntityBehaviors(api);

            hasDeployed = true;
        }
        private static void RegisterAiTasksOnServer()
        {
            //We need to make sure we don't double register with outlaw mod, if that mod loaded first.
            if (!AiTaskRegistry.TaskTypes.ContainsKey("shootatentity"))
                AiTaskRegistry.Register<AiTaskShootProjectileAtEntity>("shootatentity");

            if (!AiTaskRegistry.TaskTypes.ContainsKey("engageentity"))
                AiTaskRegistry.Register<AiTaskPursueAndEngageEntity>("engageentity");

            if (!AiTaskRegistry.TaskTypes.ContainsKey("stayclosetoherd"))
                AiTaskRegistry.Register<AiTaskStayCloseToHerd>("stayclosetoherd");

            if (!AiTaskRegistry.TaskTypes.ContainsKey("eatdead"))
                AiTaskRegistry.Register<AiTaskEatDeadEntities>("eatdead");

            if (!AiTaskRegistry.TaskTypes.ContainsKey("morale"))
                AiTaskRegistry.Register<AiTaskMorale>("morale");

            if (!AiTaskRegistry.TaskTypes.ContainsKey("melee"))
                AiTaskRegistry.Register<AiTaskExpandedMeleeAttack>("melee");

            if (!AiTaskRegistry.TaskTypes.ContainsKey("guard"))
                AiTaskRegistry.Register<AiTaskGuard>("guard");

            if (!AiTaskRegistry.TaskTypes.ContainsKey("reacttoprojectiles"))
                AiTaskRegistry.Register<AiTaskReactToProjectiles>("reacttoprojectiles");

            if (!AiTaskRegistry.TaskTypes.ContainsKey("playanimationatrange"))
                AiTaskRegistry.Register<AiTaskPlayAnimationAtRangeFromTarget>("playanimationatrange");
        }

        private static void RegisterAiTasksShared()
        {
            //We need to make sure we don't double register with outlaw mod, if that mod loaded first.
            if (!AiTaskRegistry.TaskTypes.ContainsKey("shootatentity"))
                AiTaskRegistry.Register("shootatentity", typeof(AiTaskShootProjectileAtEntity));

            if (!AiTaskRegistry.TaskTypes.ContainsKey("engageentity"))
                AiTaskRegistry.Register("engageentity", typeof(AiTaskPursueAndEngageEntity));

            if (!AiTaskRegistry.TaskTypes.ContainsKey("stayclosetoherd"))
                AiTaskRegistry.Register("stayclosetoherd", typeof(AiTaskStayCloseToHerd));

            if (!AiTaskRegistry.TaskTypes.ContainsKey("eatdead"))
                AiTaskRegistry.Register("eatdead", typeof(AiTaskEatDeadEntities));

            if (!AiTaskRegistry.TaskTypes.ContainsKey("morale"))
                AiTaskRegistry.Register("morale", typeof(AiTaskMorale));

            if (!AiTaskRegistry.TaskTypes.ContainsKey("melee"))
                AiTaskRegistry.Register("melee", typeof(AiTaskExpandedMeleeAttack));

            if (!AiTaskRegistry.TaskTypes.ContainsKey("guard"))
                AiTaskRegistry.Register("guard", typeof(AiTaskGuard));

            if (!AiTaskRegistry.TaskTypes.ContainsKey("reacttoprojectiles"))
                AiTaskRegistry.Register("reacttoprojectiles", typeof(AiTaskReactToProjectiles));

            if (!AiTaskRegistry.TaskTypes.ContainsKey("playanimationatrange"))
                AiTaskRegistry.Register("playanimationatrange", typeof(AiTaskPlayAnimationAtRangeFromTarget));
        }

        private static void RegisterEntitiesShared( ICoreAPI api )
        {
            api.RegisterEntity("EntityAIProjectile", typeof(EntityAIProjectile));
            api.RegisterEntity("EntityAIProjectileExplosive", typeof(EntityAIProjectileExplosive));
        }

        private static void RegisterEntityBehaviors( ICoreAPI api )
        {
            api.RegisterEntityBehaviorClass("lodrepulseagents", typeof(EntityBehaviorLODRepulseAgents));
        }
    }
}