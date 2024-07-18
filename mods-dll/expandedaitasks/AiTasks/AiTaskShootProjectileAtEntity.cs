using System;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using Vintagestory.GameContent;
using ExpandedAiTasks.Managers;

namespace ExpandedAiTasks
{
    public class AiTaskShootProjectileAtEntity : AiTaskBaseExpandedTargetable
    {
        const float FRIENDLY_FIRE_DETECTION_ANGLE = 5.0f;

        int durationMs;
        int releaseAtMs;

        float minDist = 3f;
        float maxDist = 15f;
        float maxVertDist = 10f;

        //Accuracy Vars
        float minRangeDistOffTarget = 0.0f; //this is the number of blocks off target a projectile will stray at min range.
        float maxRangeDistOffTarget = 0.0f; //this is the number of blocks off target a projectile will stray at max range.
        float maxVelocity = 1.0f;

        float newTargetDistOffTarget = 0.0f;
        float newTargetZeroingTime = 0.0f;

        //Damage and Damage Falloff Vars
        float damage = 1.0f;
        int damageTier = 0;
        float damageFalloffPercent = 0.0f;      //Percentage reduction do base damage when falloff distance hits max.
        float damageFalloffStartDist = -1.0f;   //Distance in blocks where damage falloff begins.
        float damageFalloffEndDist = -1.0f;     //Distance in blocks where damage falloff hits full percent value.

        string projectileItem = "arrow-copper";
        string dummyProjectile = "dummyarrow-copper";
        bool projectileRemainsInWorld = false;
        float projectileBreakOnImpactChance = 0.0f;

        bool stopIfPredictFriendlyFire = false;
        bool leadTarget = true;
        bool arcShots = true;
        bool fireOnLastKnownPosition = true;
        float lastKnownPositionTimeoutMs = 5000f;

        //Advance After Shooting Behavior
        bool advanceAfterShooting = true;

        //Ammo
        bool hasLimitedAmmo = false;
        bool ammoRegenDuringCombat = false;
        int ammoRegenIntervalMs = 0;
        int maxAmmo = 0;
        int ammo = 0;

        Entity targetLastFrame = null;
        double dtSinceTargetAquired = 0.0f;
        Vec3d targetLKP = null;
        double lastTimeSeenTarget = 0;

        double lastShotTime = 0;
        double lastAmmoRegenTime = 0;

        float accum = 0;
        bool didShoot;
        bool stopNow;

        float minTurnAnglePerSec;
        float maxTurnAnglePerSec;
        float curTurnRadPerSec;

        Random rnd;

        Entity guardTargetAttackedByEntity = null;

        public AiTaskShootProjectileAtEntity(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            this.rnd = new Random((int)(entity.EntityId + entity.World.ElapsedMilliseconds));

            partitionUtil = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();

            base.LoadConfig(taskConfig, aiConfig);

            this.durationMs = taskConfig["durationMs"].AsInt(1500);
            this.releaseAtMs = taskConfig["releaseAtMs"].AsInt(1000);
            this.mincooldown = taskConfig["mincooldown"].AsInt(0);
            this.maxcooldown = taskConfig["maxcooldown"].AsInt(0);
            this.minDist = taskConfig["minDist"].AsFloat(3f);
            this.maxDist = taskConfig["maxDist"].AsFloat(15f);
            this.maxVertDist = taskConfig["maxVertDist"].AsFloat(this.maxDist * 0.75f);
            this.minRangeDistOffTarget = taskConfig["minRangeDistOffTarget"].AsFloat(0.0f);
            this.maxRangeDistOffTarget = taskConfig["maxRangeDistOffTarget"].AsFloat(0.0f);
            this.maxVelocity = taskConfig["maxVelocity"].AsFloat(1.0f);
            this.newTargetDistOffTarget = taskConfig["newTargetDistOffTarget"].AsFloat(0.0f);
            this.newTargetZeroingTime = taskConfig["newTargetZeroingTime"].AsFloat(0.0f);
            this.damage = taskConfig["damage"].AsFloat(1.0f);
            this.damageTier = taskConfig[ "damageTier" ].AsInt(0);
            this.damageFalloffPercent = taskConfig["damageFalloffPercent"].AsFloat(0.0f);
            this.damageFalloffStartDist = taskConfig["damageFalloffStartDist"].AsFloat(-1.0f);
            this.damageFalloffEndDist = taskConfig["damageFalloffEndDist"].AsFloat(-1.0f);
            this.projectileItem = taskConfig["projectileItem"].AsString("arrow-copper");
            this.dummyProjectile = taskConfig["dummyProjectile"].AsString("dummyarrow-copper");
            this.projectileRemainsInWorld = taskConfig["projectileRemainsInWorld"].AsBool(false);
            this.projectileBreakOnImpactChance = taskConfig[ "projectileBreakOnImpactChance"].AsFloat(0.0f);
            this.stopIfPredictFriendlyFire = taskConfig["stopIfPredictFriendlyFire"].AsBool(false);
            this.leadTarget = taskConfig["leadTarget"].AsBool(true);
            this.arcShots = taskConfig["arcShots"].AsBool(true);
            this.fireOnLastKnownPosition = taskConfig["fireOnLastKnownPosition"].AsBool(true);
            this.lastKnownPositionTimeoutMs = taskConfig["lastKnownPositionTimeoutMs"].AsFloat(5000f);

            this.advanceAfterShooting = taskConfig["advanceAfterShooting"].AsBool(true);

            //Ammo
            this.hasLimitedAmmo = taskConfig["hasLimitedAmmo"].AsBool(false);
            this.ammoRegenDuringCombat = taskConfig["ammoRegenDuringCombat"].AsBool(false);
            this.ammoRegenIntervalMs = taskConfig["ammoRegenIntervalMs"].AsInt(0);
            this.maxAmmo = taskConfig["maxAmmo"].AsInt(0);

            ammo = maxAmmo;

            //Error checking for bad json values.
            Debug.Assert(damageFalloffPercent >= 0.0f && damageFalloffPercent <= 1.0f, "AiTaskValue damageFalloffPercent must be a 0.0 to 1.0 value.");
            Debug.Assert(damageFalloffStartDist < damageFalloffEndDist || damageFalloffEndDist < 0.0f, "AiTaskValue damageFalloffStartDist: " + damageFalloffStartDist + " cannot be greater than damageFalloffEndDist: " + damageFalloffEndDist);
            Debug.Assert(projectileBreakOnImpactChance >= 0.0f && projectileBreakOnImpactChance <= 1.0, "AiTaskValue projectileBreakOnImpactChance must be a 0.0 to 1.0 value.");
        }


        public override bool ShouldExecute()
        {
            if (whenInEmotionState != null && bhEmo?.IsInEmotionState(whenInEmotionState) != true) 
                return false;

            if (whenNotInEmotionState != null && bhEmo?.IsInEmotionState(whenNotInEmotionState) == true) 
                return false;
            
            if (whenInEmotionState == null && rand.NextDouble() > 0.5f) 
                return false;
            
            if (cooldownUntilMs > entity.World.ElapsedMilliseconds) 
                return false;

            if (entity.Swimming)
                return false;

            //If we have limited ammo check to see if we have enough to fire.
            if ( hasLimitedAmmo )
            {
                HandleAmmoRegen();

                //We don't have enough ammo to shoot.
                if (ammo < 1)
                    return false;
            }
                

            float range = maxDist;
            float vertRange = maxVertDist;
            targetEntity = null;

            if (stopIfPredictFriendlyFire)
            {
                UpdateHerdCount();
            }

            Vec3d ownPos = entity.ServerPos.XYZ;

            if (entity.World.ElapsedMilliseconds - attackedByEntityMs > 30000)
            {
                attackedByEntity = null;
            }

            if (retaliateAttacks && attackedByEntity != null && attackedByEntity.Alive && IsTargetableEntity(attackedByEntity, range, true) && AwarenessManager.IsAwareOfTarget(entity, attackedByEntity, range, vertRange))
            {
                targetEntity = attackedByEntity;
            }
            else if (guardTargetAttackedByEntity != null && guardTargetAttackedByEntity.Alive)
            {
                if (AwarenessManager.IsAwareOfTarget(entity, guardTargetAttackedByEntity, range, vertRange))
                    targetEntity = guardTargetAttackedByEntity;
            }
            else
            {
                guardTargetAttackedByEntity = null;
            }

            if (targetEntity == null || !targetEntity.Alive)
            {
                targetEntity = partitionUtil.GetNearestInteractableEntity(entity.ServerPos.XYZ, range, (e) => IsTargetableEntity(e, range) && AwarenessManager.IsAwareOfTarget(entity, e, range, vertRange));
            }

            //Reset our zeroing accuracy. (May need changes to play nice with LKP)
            if ( targetEntity != targetLastFrame)
            {
                dtSinceTargetAquired = 0.0f;
            }

            //If we can fire on last known position and we had a target and last know position last frame, but our current target is null.
            if (fireOnLastKnownPosition && targetLastFrame != null && targetLastFrame.Alive && targetLKP != null && targetEntity == null)
            {
                double dtSinceTargetSeen = entity.World.ElapsedMilliseconds - lastTimeSeenTarget;
                double distSqrToLKP = targetLKP.SquareDistanceTo(entity.ServerPos.XYZ);

                //If we haven't timed out on our target's last know position and we are in range.
                if (dtSinceTargetSeen <= lastKnownPositionTimeoutMs && distSqrToLKP <= range * range )
                {
                    targetEntity = targetLastFrame;
                }
                else
                {
                    targetLastFrame = targetEntity;
                    targetLKP = null;
                }     
            }
            else
            {
                targetLastFrame = targetEntity;
                targetLKP = null;
            }
            
            
            if ( targetEntity != null)
            {
                //If the target is too close to fire upon.
                if( ownPos.SquareDistanceTo(targetEntity.ServerPos.XYZ) <= minDist * minDist)
                    return false;
            }

            return targetEntity != null;
        }

        public override void StartExecute()
        {
            accum = 0;
            didShoot = false;
            stopNow = false;

            if ( fireOnLastKnownPosition && AwarenessManager.IsAwareOfTarget(entity, targetEntity, maxDist, maxVertDist))
            {
                targetLKP = targetEntity.ServerPos.XYZ.Add(0, targetEntity.LocalEyePos.Y, 0);
                lastTimeSeenTarget = entity.World.ElapsedMilliseconds;
            }

            if (entity?.Properties.Server?.Attributes != null)
            {
                minTurnAnglePerSec = entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetFloat("minTurnAnglePerSec", 250);
                maxTurnAnglePerSec = entity.Properties.Server.Attributes.GetTreeAttribute("pathfinder").GetFloat("maxTurnAnglePerSec", 450);
            }
            else
            {
                minTurnAnglePerSec = 250;
                maxTurnAnglePerSec = 450;
            }

            curTurnRadPerSec = minTurnAnglePerSec + (float)entity.World.Rand.NextDouble() * (maxTurnAnglePerSec - minTurnAnglePerSec);
            curTurnRadPerSec *= GameMath.DEG2RAD * 50 * 0.02f;

            //entity.PlayEntitySound("shootatentity", null, true);

            //Play sound for preparing to shoot.
            if ( !entity.Swimming )
                entity.PlayEntitySound("drawSound", null, true, maxDist);

        }

        public override bool ContinueExecute(float dt)
        {
            AiUtility.UpdateLastTimeEntityInCombatMs(entity);

            if (targetEntity == null)
                return false;

            if (entity.Swimming)
                return false;

            if ( fireOnLastKnownPosition && AwarenessManager.IsAwareOfTarget(entity, targetEntity, maxDist, maxVertDist))
            {
                targetLKP = targetEntity.ServerPos.XYZ.Add(0, targetEntity.LocalEyePos.Y, 0);
                lastTimeSeenTarget = entity.World.ElapsedMilliseconds;
            }

            if (fireOnLastKnownPosition && targetLKP == null)
                return false;

            if ( hasLimitedAmmo )
            {
                //We don't have enough ammo to shoot.
                if (ammo < 1 && !didShoot)
                    return false;
            }

            Vec3f targetVec = new Vec3f();

            if (fireOnLastKnownPosition)
            {
                targetVec.Set(
                    (float)(targetLKP.X - entity.ServerPos.X),
                    (float)(targetLKP.Y - entity.ServerPos.Y),
                    (float)(targetLKP.Z - entity.ServerPos.Z)
                );
            }
            else
            {
                targetVec.Set(
                    (float)(targetEntity.ServerPos.X - entity.ServerPos.X),
                    (float)(targetEntity.ServerPos.Y - entity.ServerPos.Y),
                    (float)(targetEntity.ServerPos.Z - entity.ServerPos.Z)
                );
            }
            

            float desiredYaw = (float)Math.Atan2(targetVec.X, targetVec.Z);

            float yawDist = GameMath.AngleRadDistance(entity.ServerPos.Yaw, desiredYaw);
            entity.ServerPos.Yaw += GameMath.Clamp(yawDist, -curTurnRadPerSec * dt, curTurnRadPerSec * dt);
            entity.ServerPos.Yaw = entity.ServerPos.Yaw % GameMath.TWOPI;

            if (Math.Abs(yawDist) > 0.02) 
                return true;

            if (animMeta != null)
            {
                //animMeta.EaseInSpeed = 1f;
               // animMeta.EaseOutSpeed = 1f;
                entity.AnimManager.StartAnimation(animMeta);
            }

            accum += dt;
            dtSinceTargetAquired += dt;

            //Extra: We should look at what it would take to have a json bool, movingResetsAccuracy. That would force an AI's accuracy to reset if it is force to move from it's firing position.

            //If the target is too close to fire upon, cancel the attack.
            if (targetEntity != null && entity.ServerPos.SquareDistanceTo(targetEntity.ServerPos.XYZ) <= minDist * minDist)
                return false;

            if (accum > releaseAtMs / 1000f && !didShoot)
            {
                didShoot = true;

                double pitchDir = 1.0;
                double yawDir = 1.0;

                double pitchCoinToss = rnd.NextDouble();
                double yawCoinToss = rnd.NextDouble();

                if (pitchCoinToss > 0.5f)
                    pitchDir = -1.0f;

                if (yawCoinToss > 0.5f)
                    yawDir = -1.0f;

                //Bug Potential: if the BehindCopy is not set far enought from the shooter, the arrow will collide with the shooter and result in weird behaviors.
                Vec3d shotStartPosition = entity.SidedPos.BehindCopy(0.75).XYZ.Add(0, entity.LocalEyePos.Y, 0);
                Vec3d shotTargetPos = fireOnLastKnownPosition ? targetLKP : targetEntity.ServerPos.XYZ.Add(0, targetEntity.LocalEyePos.Y, 0);

                if (leadTarget)
                    shotTargetPos = CalculateInterceptLocation(shotStartPosition, entity.ServerPos.Motion, maxVelocity * 2, shotTargetPos, targetEntity.ServerPos.Motion);

                //Arc Shots When Our Velocity Is Not Enough For A Straight Shot.
                double gravityStrength = (GlobalConstants.GravityPerSecond) / (2.0 / GlobalConstants.PhysicsFrameTime);
                double distanceToTarget = shotTargetPos.DistanceTo(shotStartPosition);

                double trajectoryAngle;
                if (arcShots && CalculateLaunchAngle(distanceToTarget, maxVelocity, gravityStrength, out trajectoryAngle))
                {
                    //Aim above the target's head the farther we are away.
                    double trajectoryHeight = Math.Tan(trajectoryAngle * (Math.PI / 180)) * distanceToTarget;
                    shotTargetPos = shotTargetPos.Add(0, trajectoryHeight, 0);
                }

                double accuracyDistOffTarget = 0.0f;
                if (newTargetZeroingTime > 0 && newTargetDistOffTarget > 0)
                    accuracyDistOffTarget = MathUtility.GraphClampedValue(0.0, newTargetZeroingTime, newTargetDistOffTarget, 0.0, dtSinceTargetAquired);

                float distToTargetSqr = shotStartPosition.SquareDistanceTo(shotTargetPos);
                double distanceOffTarget = MathUtility.GraphClampedValue(minDist * minDist, maxDist * maxDist, minRangeDistOffTarget, maxRangeDistOffTarget, distToTargetSqr);

                double rndPitch = ((rnd.NextDouble() * distanceOffTarget) + accuracyDistOffTarget) * pitchDir;
                double rndYaw = ((rnd.NextDouble() * distanceOffTarget) + accuracyDistOffTarget) * yawDir;

                Vec3d shotDriftDirection = new Vec3d(0.0f, rndPitch, rndYaw);
                Vec3d shotTargetPosWithDrift = shotTargetPos.Add(shotDriftDirection.X, shotDriftDirection.Y, shotDriftDirection.Z);

                Vec3d velocity = (shotTargetPosWithDrift - shotStartPosition).Normalize() * maxVelocity;

                //If we care about shooting friendlies and we are going to shoot a friendly, early out.
                if (stopIfPredictFriendlyFire && WillFriendlyFire(shotStartPosition.Clone(), shotTargetPosWithDrift.Clone()))
                    return false;

                float projectileDamage = GetProjectileDamageAfterFalloff(distToTargetSqr);

                ShootProjectile(shotStartPosition, velocity, projectileDamage, damageTier, projectileBreakOnImpactChance);

                lastShotTime = world.ElapsedMilliseconds;

                if (hasLimitedAmmo)
                {
                    ammo--;
                    Debug.Assert(ammo >= 0);
                }
            }

            return accum < durationMs / 1000f && !stopNow;
        }

        public static bool CalculateLaunchAngle(double TargetDistance, double ProjectileVelocity, double gravity, out double CalculatedAngle)
        {
            //WHAT WE'VE LEARNED TO FAR. WE NEED TO FIGURE OUT THE GRAVITY CONSTANT THAT WORKS WITH THIS EQUASION, VINTAGE STORY DOESN'T USE 9.8 mps/s FOR GRAVITY.
            double asin = Math.Asin( (gravity * TargetDistance) / (ProjectileVelocity * ProjectileVelocity));

            CalculatedAngle = (0.5f * (asin)) * (180 / Math.PI);
            if (double.IsNaN(CalculatedAngle))
            {
                CalculatedAngle = 0;
                return false;
            }
            return true;
        }

        //first-order intercept using absolute target position
        public static Vec3d CalculateInterceptLocation(Vec3d shooterPosition, Vec3d shooterVelocity, double shotSpeed, Vec3d targetPosition, Vec3d targetVelocity)
        {
            Vec3d targetRelativePosition = targetPosition - shooterPosition;
            Vec3d targetRelativeVelocity = targetVelocity - shooterVelocity;
            double t = CalculateInterceptTime( shotSpeed, targetRelativePosition, targetRelativeVelocity);
            return targetPosition + t * (targetRelativeVelocity);
        }

        //first-order intercept using relative target position
        public static double CalculateInterceptTime
        (
            double shotSpeed,
            Vec3d targetRelativePosition,
            Vec3d targetRelativeVelocity
        )
        {

            double velocitySquared = targetRelativeVelocity.LengthSq();
            if (velocitySquared < 0.001f)
                return 0f;

            double a = velocitySquared - shotSpeed * shotSpeed;

            //handle similar velocities
            if (Math.Abs(a) < 0.001f)
            {
                double t = -targetRelativePosition.LengthSq() / (2f * targetRelativeVelocity.Dot(targetRelativePosition));
                return Math.Max(t, 0f); //don't shoot back in time
            }

            double b = 2f * targetRelativeVelocity.Dot(targetRelativePosition);
            double c = targetRelativePosition.LengthSq();
            double determinant = b * b - 4f * a * c;

            if (determinant > 0f)
            { //determinant > 0; two intercept paths (most common)
                double t1 = (-b + Math.Sqrt(determinant)) / (2f * a),
                        t2 = (-b - Math.Sqrt(determinant)) / (2f * a);
                if (t1 > 0f)
                {
                    if (t2 > 0f)
                        return Math.Min(t1, t2); //both are positive
                    else
                        return t1; //only t1 is positive
                }
                else
                    return Math.Max(t2, 0f); //don't shoot back in time
            }
            else if (determinant < 0f) //determinant < 0; no intercept path
                return 0f;
            else //determinant = 0; one intercept path, pretty much never happens
                return Math.Max(-b / (2f * a), 0f); //don't shoot back in time
        }

        private float GetProjectileDamageAfterFalloff( float distToTargetSqr )
        {
            if (damageFalloffStartDist < 0.0f)
                damageFalloffStartDist = maxDist;

            if (damageFalloffEndDist < 0.0f)
                damageFalloffEndDist = maxDist;

            float currentFalloffPercentile = (float)MathUtility.GraphClampedValue(damageFalloffStartDist * damageFalloffStartDist, damageFalloffEndDist * damageFalloffEndDist, 0.0f, damageFalloffPercent, distToTargetSqr);

            return damage - (damage * currentFalloffPercentile);
        }

        public override void FinishExecute(bool cancelled)
        {
            base.FinishExecute(cancelled);

            if ( didShoot )
                cooldownUntilMs = entity.World.ElapsedMilliseconds + mincooldown + entity.World.Rand.Next(maxcooldown - mincooldown);

            if (animMeta != null)
            {
                //animMeta.EaseInSpeed = 1f;
                //animMeta.EaseOutSpeed = 1f;
                entity.AnimManager.StopAnimation(animMeta.Code);
            }

            //Advance on the location we fired upon.
            if (advanceAfterShooting)
            {
                EntityTargetPairing targetPairing = new EntityTargetPairing(entity, targetEntity, null, targetLKP, null);
                entity.Notify("pursueEntity", targetPairing);
            }
            
            targetEntity = null;
        }

        public bool WillFriendlyFire( Vec3d firePos, Vec3d shotTargetPos)
        {
            Vec3d shooterToTarget = shotTargetPos - firePos;
            shooterToTarget = shooterToTarget.Normalize();

            foreach(Entity herdMember in herdMembers)
            {
                if (herdMember == entity)
                    continue;

                if (!herdMember.Alive)
                    continue;

                Vec3d shooterHerdMemberEyePos = herdMember.ServerPos.XYZ.Add(0, herdMember.LocalEyePos.Y, 0);
                Vec3d shooterToHerdMember = (shooterHerdMemberEyePos - firePos);
                shooterToHerdMember = shooterToHerdMember.Normalize();
                double dot = shooterToHerdMember.Dot(shooterToTarget);

                double distToFriendSqr = firePos.SquareDistanceTo( shooterHerdMemberEyePos );

                //If we are really bunched up, don't fire;
                //if (distToFriendSqr <= 2 * 2)
                //   return true;

                double friendlyFireDot = Math.Cos(FRIENDLY_FIRE_DETECTION_ANGLE * (Math.PI / 180));
                //If our ally is in our field of fire.
                if (dot >= friendlyFireDot)
                {
                    double distToTargetSqr = firePos.SquareDistanceTo(shotTargetPos);
                    //double distToFriendSqr = firePos.SquareDistanceTo(herdMember.ServerPos.XYZ);

                    //If our friend seems to be between us and our target, don't fire.
                    if ( distToTargetSqr > distToFriendSqr)
                        return true;
                }
                    
            }

            return false;
        }

        protected void ShootProjectile( Vec3d shotStartPosition, Vec3d velocity, float projectileDamage, int projectileDamageTier, float breakChance )
        {
            //Implementation Note: Since Vintage Story can have per-entity gravity diffrences and they also have air drag, I decided to make a dummy projectile entity that has physics settings ideal for the simplest version of the trajectory calculation.
            //This dummy projectile sets its shape and materials to match the assets of a real projectile item that the ai is "fireing." The dummy projectile uses the real projectile item type for its item stack, so that the player picks up the real projectile,
            //and not the dummy when it lands in the world.

            EntityProperties dummyType = entity.World.GetEntityType(new AssetLocation(dummyProjectile));
            EntityAIProjectile projectile = entity.World.ClassRegistry.CreateEntity(dummyType) as EntityAIProjectile;
            projectile.FiredBy = entity;
            projectile.Damage = projectileDamage;
            projectile.damageTier = projectileDamageTier;
            projectile.ProjectileStack = new ItemStack(entity.World.GetItem(new AssetLocation(projectileItem)));

            projectile.DropOnImpactChance = projectileRemainsInWorld ? 1.0f - projectileBreakOnImpactChance : 0.0f;

            projectile.ServerPos.SetPos(shotStartPosition);
            projectile.ServerPos.Motion.Set(velocity);
            projectile.Pos.SetFrom(projectile.ServerPos);

            projectile.World = entity.World;
            projectile.SetRotation();

            entity.World.SpawnEntity(projectile);

            entity.PlayEntitySound("shootSound", null, true, maxDist);
        }
        protected void HandleAmmoRegen()
        {
            //Calculate ammo regen.
            if (ammoRegenDuringCombat)
            {
                if (ammoRegenIntervalMs > 0 && ammo < maxAmmo)
                {
                    if (world.ElapsedMilliseconds >= lastAmmoRegenTime + ammoRegenIntervalMs)
                    {
                        int ammoRegained = (int)((world.ElapsedMilliseconds - lastShotTime) / ammoRegenIntervalMs);
                        ammo = Math.Min(ammoRegained, maxAmmo);
                        lastAmmoRegenTime = world.ElapsedMilliseconds;
                    } 
                }
                else
                {
                    ammo = maxAmmo;
                }
            }
            else
            {
                if (!AiUtility.IsInCombat(entity))
                {
                    if (ammoRegenIntervalMs > 0 && ammo < maxAmmo)
                    {
                        if (world.ElapsedMilliseconds >= lastAmmoRegenTime + ammoRegenIntervalMs)
                        {
                            int ammoRegained = (int)((world.ElapsedMilliseconds - AiUtility.GetLastTimeEntityInCombatMs(entity)) / ammoRegenIntervalMs);
                            ammo = Math.Min(ammoRegained, maxAmmo);
                            lastAmmoRegenTime = world.ElapsedMilliseconds;
                        }    
                    }
                    else
                    {
                        ammo = maxAmmo;
                    }
                }
            }
        }

        public override bool Notify(string key, object data)
        {
            if (!AiUtility.CanRespondToNotify(entity))
                return false;

            if (key == "attackEntity")
            {
                //If we don't have a target, assist our group.
                if (targetEntity == null)
                {
                    //If we are in range of our ally, respond.
                    EntityTargetPairing targetPairing = (EntityTargetPairing)data;
                    Entity herdMember = targetPairing.entityTargeting;
                    Entity newTarget = targetPairing.targetEntity;

                    if (newTarget == null || !IsTargetableEntity(newTarget, maxDist, true) || !AwarenessManager.IsAwareOfTarget(entity, newTarget, maxDist, maxVertDist))
                        return false;

                    //Handle case where a teammate asks us to attack a target, but we have no ammo.
                    if (hasLimitedAmmo && ammo < 1)
                        return false;


                    double distSqr = entity.ServerPos.XYZ.SquareDistanceTo(herdMember.ServerPos.XYZ);
                    if (distSqr <= maxDist * maxDist)
                    {
                        targetEntity = newTarget;

                        targetLKP          = targetPairing.lastKnownPos == null ? targetEntity.ServerPos.XYZ.Add(0, targetEntity.LocalEyePos.Y, 0) : targetPairing.lastKnownPos;
                        lastTimeSeenTarget = entity.World.ElapsedMilliseconds;

                        return true;
                    }
                }
            }
            else if (key == "entityAttackedGuardedEntity")
            {
                //If a guard task tells us our guard target has been attacked, engage the target as if they attacked us.
                if ((Entity)data != null && guardTargetAttackedByEntity != (Entity)data)
                {
                    guardTargetAttackedByEntity = (Entity)data;
                    targetEntity = guardTargetAttackedByEntity;
                    return false;
                }
            }
            //Clear the entity that attacked our guard target.
            else if (key == "guardChaseStop")
            {
                if (targetEntity == guardTargetAttackedByEntity)
                    stopNow = true;

                guardTargetAttackedByEntity = null;
                return false;
            }

            else if (key == "clearTargetHistory")
            {
                ClearTargetHistory();
                return false;
            }

            return false;
        }
    }
}