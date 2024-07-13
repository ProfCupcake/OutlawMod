using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using ExpandedAiTasks;
using ExpandedAiTasks.Managers;

namespace OutlawMod
{
    /////////////////////////////////////////////////////////////////////////////
    /// PATCHING AI TASKS TO ADD PLAY SOUND EVENTS FOR VS CLASIC OUTLAW VOICES///
    /////////////////////////////////////////////////////////////////////////////

    [HarmonyPatch(typeof(AiTaskMeleeAttack))]
    public class AiTaskMeleeAttackOverride
    {
        [HarmonyPrepare]
        static bool Prepare(MethodBase original, Harmony harmony)
        {
            return true;
        }


        [HarmonyPatch("StartExecute")]
        [HarmonyPostfix]
        static void OverrideAddSoundCallToStartExecute(AiTaskMeleeAttack __instance)
        {
            if (__instance.entity.Alive)
            {
                __instance.entity.PlayEntitySound("meleeattack", null, true);
            }
        }
    }

    [HarmonyPatch(typeof(AiTaskFleeEntity))]
    public class AiTaskFleeEntityOverride
    {
        [HarmonyPrepare]
        static bool Prepare(MethodBase original, Harmony harmony)
        {
            return true;
        }


        [HarmonyPatch("StartExecute")]
        [HarmonyPostfix]
        static void OverrideAddSoundCallToStartExecute(AiTaskFleeEntity __instance)
        {
            if (__instance.entity.Alive)
            {
                __instance.entity.PlayEntitySound("fleeentity", null, true);
            }
        }
    }

    [HarmonyPatch(typeof(AiTaskSeekEntity))]
    public class AiTaskSeekEntityOverride
    {
        [HarmonyPrepare]
        static bool Prepare(MethodBase original, Harmony harmony)
        {
            return true;
        }


        [HarmonyPatch("StartExecute")]
        [HarmonyPostfix]
        static void OverrideAddSoundCallToStartExecute(AiTaskSeekEntity __instance)
        {
            if (__instance.entity.Alive)
            {
                __instance.entity.PlayEntitySound("seekentity", null, true);
            }
        }
    }
}