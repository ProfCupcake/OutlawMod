using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;
using System.Collections;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ExpandedAiTasks
{
    public class EntityAIProjectileExplosive : EntityAIProjectile
    {
        public EnumBlastType blastType { get; set; }
        public float blastRadius { get; set; }
        public float injureRadius { get; set; }

        bool shouldDetonate = false;
        bool hasDetonated = false;

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);

            Debug.Assert(this.Properties.Attributes.KeyExists("blastType"), "EntityAIProjectileExplosive must have blastType Attribute");
            Debug.Assert(this.Properties.Attributes.KeyExists("blastRadius"), "EntityAIProjectileExplosive must have blastType blastRadius");
            Debug.Assert(this.Properties.Attributes.KeyExists("injureRadius"), "EntityAIProjectileExplosive must have blastType injureRadius");

            blastType = (EnumBlastType)this.Properties.Attributes["blastType"].AsInt();
            blastRadius = this.Properties.Attributes["blastRadius"].AsFloat();
            injureRadius = this.Properties.Attributes["injureRadius"].AsFloat();
        }

        public override bool ShouldReceiveDamage(DamageSource damageSource, float damage)
        {
            return false;
        }

        protected override void OnPhysicsTickCallback(float dtFac)
        {
            base.OnPhysicsTickCallback(dtFac);

            if ( shouldDetonate && !ShouldDespawn )
            {
                if (World.Side == EnumAppSide.Server)
                    DetonateProjectile(this.ServerPos.AsBlockPos);
                
                Die();
            }
        }
        
        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);

            if ( shouldDetonate && !ShouldDespawn)
            {
                if (World.Side == EnumAppSide.Server)
                    DetonateProjectile(this.ServerPos.AsBlockPos);
                
                Die();
            }
        }

        protected override void IsColliding(EntityPos pos, double impactSpeed)
        {
            pos.Motion.Set(0, 0, 0);

            if (!beforeCollided && World is IServerWorldAccessor && World.ElapsedMilliseconds > msCollide + 500)
            {
                if (impactSpeed >= 0.07)
                {
                    World.PlaySoundAt(new AssetLocation("sounds/arrow-impact"), this, null, false, 32);

                    // Resend position to client
                    WatchedAttributes.MarkAllDirty();

                    shouldDetonate = true;
                }

                //TryAttackEntity(impactSpeed);

                msCollide = World.ElapsedMilliseconds;

                beforeCollided = true;
            }
        }

        /*
        protected override bool TryAttackEntity(double impactSpeed)
        {
            if (World is IClientWorldAccessor || World.ElapsedMilliseconds <= msCollide + 250) return false;
            if (impactSpeed <= 0.01) return false;

            EntityPos pos = SidedPos;

            Cuboidd projectileBox = SelectionBox.ToDouble().Translate(ServerPos.X, ServerPos.Y, ServerPos.Z);

            // We give it a bit of extra leeway of 50% because physics ticks can run twice or 3 times in one game tick 
            if (ServerPos.Motion.X < 0)
                projectileBox.X1 += 1.5 * ServerPos.Motion.X;
            else
                projectileBox.X2 += 1.5 * ServerPos.Motion.X;

            if (ServerPos.Motion.Y < 0)
                projectileBox.Y1 += 1.5 * ServerPos.Motion.Y;
            else
                projectileBox.Y2 += 1.5 * ServerPos.Motion.Y;

            if (ServerPos.Motion.Z < 0)
                projectileBox.Z1 += 1.5 * ServerPos.Motion.Z;
            else
                projectileBox.Z2 += 1.5 * ServerPos.Motion.Z;

            Entity entity = World.GetNearestEntity(ServerPos.XYZ, 5f, 5f, (e) => {
                
                if (e.EntityId == this.EntityId || !e.IsInteractable) 
                    return false;

                if (firedBy != null && e.EntityId == firedBy.EntityId && World.ElapsedMilliseconds - msLaunch < 500)
                {
                    return false;
                }

                Cuboidd eBox = e.SelectionBox.ToDouble().Translate(e.ServerPos.X, e.ServerPos.Y, e.ServerPos.Z);

                return eBox.IntersectsOrTouches(projectileBox);
            });

            if (entity != null)
            {
                ImpactOnEntity(entity);
                return true;
            }


            return false;
        }
        */

        protected override void ImpactOnEntity(Entity entity)
        {
            if (!Alive)
                return;

            EntityPos pos = SidedPos;

            ICoreServerAPI sapi = World.Api as ICoreServerAPI;

            msCollide = World.ElapsedMilliseconds;

            pos.Motion.Set(0, 0, 0);

            if ( World.Side == EnumAppSide.Server)
            {
                World.PlaySoundAt(new AssetLocation("sounds/arrow-impact"), this, null, false, 24);

                float dmg = damage;

                if (firedBy != null) 
                    dmg *= firedBy.Stats.GetBlended("rangedWeaponsDamage");

                bool didDamage = entity.ReceiveDamage(new DamageSource()
                {
                    Source = EnumDamageSource.Entity,
                    SourceEntity = this,
                    CauseEntity = firedBy,
                    Type = EnumDamageType.PiercingAttack,
                    DamageTier = damageTier
                }, dmg);

                shouldDetonate = true;

                float kbresist = entity.Properties.KnockbackResistance;
                entity.SidedPos.Motion.Add(kbresist * pos.Motion.X * weight, kbresist * pos.Motion.Y * weight, kbresist * pos.Motion.Z * weight);
            }
        }

        private void DetonateProjectile( BlockPos blockPos )
        {
            if (!Alive)
                return;

            if (hasDetonated)
                return;

            hasDetonated = true;

            Debug.Assert( Api.Side == EnumAppSide.Server );
            BlockUtility.CreateExplosion((ICoreServerAPI)Api, blockPos, blastType, blastRadius, injureRadius);
        }
    }
}

