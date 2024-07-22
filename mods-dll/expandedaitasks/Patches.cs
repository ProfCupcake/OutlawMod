using HarmonyLib;
using System.Reflection;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using ExpandedAiTasks.Managers;


namespace ExpandedAiTasks
{
    public static class ExpandedAiTasksHarmonyPatcher
    {
        private static Harmony harmony;

        public static bool ShouldPatch()
        {
            return harmony == null;
        }

        public static void ApplyPatches()
        {
            Debug.Assert(ShouldPatch(), "ExpandedAiTasks Harmony patches have already been applied, call ShouldPatch to determine if this method should be called.");
            harmony = new Harmony("com.grifthegnome.expandedaitasks.aitaskpatches");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    //////////////////////////////////////////////////////////////
    ///PATCHING TO ADD ENTITIES INTO ENTITY LEDGER ON INITIALIZE//
    //////////////////////////////////////////////////////////////
    [HarmonyPatch(typeof(Entity))]
    public class AfterInitializedOverride
    {
        [HarmonyPrepare]
        static bool Prepare(MethodBase original, Harmony harmony)
        {
            return true;
        }

        [HarmonyPatch("AfterInitialized")]
        [HarmonyPostfix]
        static void OverrideAfterInitialized(Entity __instance, bool onFirstSpawn)
        {
            if (__instance.Api.Side == EnumAppSide.Server)
            {
                EntityManager.RegisterEntityWithEntityLedger(__instance);

                if (__instance is EntityProjectile && !EntityManager.IsRegisteredAsEntityProjectile(__instance))
                    EntityManager.RegisterEntityProjectile(__instance);

                if (__instance is EntityAIProjectile && !EntityManager.IsRegisteredAsEntityAIProjectile(__instance))
                    EntityManager.RegisterEntityAIProjectile(__instance);
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////
    ///PATCHING TO ADD A UNIVERAL SET LOCATION FOR LAST ENTITY TO ATTACK ON ENTITY AGENT//
    //////////////////////////////////////////////////////////////////////////////////////

    [HarmonyPatch(typeof(EntityAgent))]
    public class ReceiveDamageOverride
    {
        [HarmonyPrepare]
        static bool Prepare(MethodBase original, Harmony harmony)
        {
            return true;
        }      

        [HarmonyPatch("ReceiveDamage")]
        [HarmonyPostfix]
        static void OverrideReceiveDamage(EntityAgent __instance, DamageSource damageSource, float damage)
        {
            if (__instance.Alive)
            {
                Entity prevAttacker = AiUtility.GetLastAttacker(__instance);
                AiUtility.SetLastAttacker(__instance, damageSource);
                Entity newAttacker = AiUtility.GetLastAttacker(__instance);

                if (newAttacker != null && newAttacker != prevAttacker)
                    AiUtility.TryNotifyHerdMembersToAttack( __instance, AiUtility.GetLastAttacker(__instance), null, null, null, AiUtility.GetHerdAlertRangeForEntity(__instance), true );
            }
        }
    }

    //////////////////////////////////////////////////////////////
    /// PATCHING ENTITY HEALTH BEHAVIOR TO ALLOW SNEAK ATTACKS ///
    //////////////////////////////////////////////////////////////

    [HarmonyPatch(typeof(EntityBehaviorHealth))]
    public class OnEntityReceiveDamageOverride
    {
        [HarmonyPrepare]
        static bool Prepare(MethodBase original, Harmony harmony)
        {
            if (original != null)
            {

                foreach (var patched in harmony.GetPatchedMethods())
                {
                    if (patched.Name == original.Name)
                        return false;
                }
            }

            return true;
        }

        [HarmonyPatch("OnEntityReceiveDamage")]
        [HarmonyPrefix]
        static void OverrideOnEntityReceiveDamage(EntityBehaviorHealth __instance, DamageSource damageSource, ref float damage)
        {
            Entity attacker = damageSource.SourceEntity;

            if (attacker is EntityProjectile && damageSource.CauseEntity != null)
            {
                attacker = damageSource.CauseEntity;
            }

            //Players should not be able to sneak attack eachother.
            if (__instance.entity is EntityPlayer)
                return;

            //Give player super sneak attack damage if the target is not in combat and has not been in combat for 30 seconds.
            if (attacker is EntityPlayer && !AiUtility.IsInCombat(__instance.entity) && __instance.entity.World.ElapsedMilliseconds - AiUtility.GetLastTimeEntityInCombatMs(__instance.entity) > 30000.0f && damageSource.Type != EnumDamageType.Heal)
            {
                if (!AwarenessManager.IsAwareOfTarget(__instance.entity, attacker, 60, 60))
                {
                    if (AiUtility.AttackWasFromProjectile(damageSource))
                    {
                        damage *= attacker.Attributes.GetFloat(ExpandedAiTaskConsts.RANGED_SNEAK_ATTACK_ATTRIBUTE_KEY, 1.0f);
                    }
                    else
                    {
                        damage *= attacker.Attributes.GetFloat(ExpandedAiTaskConsts.MELEE_SNEAK_ATTACK_ATTRIBUTE_KEY, 1.0f);
                    }
                }
            }
        }
    }
}