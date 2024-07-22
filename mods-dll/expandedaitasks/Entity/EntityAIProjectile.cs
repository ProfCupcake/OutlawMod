using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ExpandedAiTasks
{
    public class EntityAIProjectile : Entity
    {
        protected bool beforeCollided;
        protected bool stuck;

        protected long msLaunch;
        protected long msCollide;

        protected Vec3d motionBeforeCollide = new Vec3d();

        protected CollisionTester collTester = new CollisionTester();

        public Entity firedBy;
        public float weight = 0.1f;
        public float damage;
        public int damageTier = 0;
        public ItemStack projectileStack;
        public float dropOnImpactChance = 0f;
        public bool damageStackOnImpact = false;
        
        protected Cuboidf collisionTestBox;

        protected EntityPartitioning ep;

        public override bool ApplyGravity
        {
            get { return !stuck; }
        }

        public override bool IsInteractable
        {
            get { return false; }
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);

            msLaunch = World.ElapsedMilliseconds;

            collisionTestBox = SelectionBox.Clone().OmniGrowBy(0.05f);

            //if (api.Side == EnumAppSide.Server) - why only server side? This makes arrows fly through entities on the client
            {
                GetBehavior<EntityBehaviorPassivePhysics>().OnPhysicsTickCallback = OnPhysicsTickCallback;
                ep = api.ModLoader.GetModSystem<EntityPartitioning>();
            }

            GetBehavior<EntityBehaviorPassivePhysics>().collisionYExtra = 0f; // Slightly cheap hax so that stones/arrows don't collid with fences
        }

        protected virtual void OnPhysicsTickCallback(float dtFac)
        {
            if (ShouldDespawn || !Alive) return;
            if (World.ElapsedMilliseconds <= msCollide + 500) return;

            var pos = SidedPos;

            if (pos.Motion.X == 0 && pos.Motion.Y == 0 && pos.Motion.Z == 0) return;  // don't do damage if stuck in ground


            Cuboidd projectileBox = SelectionBox.ToDouble().Translate(pos.X, pos.Y, pos.Z);

            if (pos.Motion.X < 0) 
                projectileBox.X1 += pos.Motion.X * dtFac;
            else 
                projectileBox.X2 += pos.Motion.X * dtFac;
            
            if (pos.Motion.Y < 0) 
                projectileBox.Y1 += pos.Motion.Y * dtFac;
            else 
                projectileBox.Y2 += pos.Motion.Y * dtFac;
            
            if (pos.Motion.Z < 0) 
                projectileBox.Z1 += pos.Motion.Z * dtFac;
            else 
                projectileBox.Z2 += pos.Motion.Z * dtFac;

            ep.WalkEntities(pos.XYZ, 5f, (e) => {
                if (e.EntityId == this.EntityId || (firedBy != null && e.EntityId == firedBy.EntityId && World.ElapsedMilliseconds - msLaunch < 500) || !e.IsInteractable) return true;

                Cuboidd eBox = e.SelectionBox.ToDouble().Translate(e.ServerPos.X, e.ServerPos.Y, e.ServerPos.Z);

                if (eBox.IntersectsOrTouches(projectileBox))
                {
                    ImpactOnEntity(e);
                    return false;
                }

                return true;
            }, EnumEntitySearchType.Creatures);
        }


        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            
            if (ShouldDespawn) 
                return;

            EntityPos pos = SidedPos;

            stuck = Collided || collTester.IsColliding(World.BlockAccessor, collisionTestBox, pos.XYZ) || WatchedAttributes.GetBool("stuck");
            if (Api.Side == EnumAppSide.Server) WatchedAttributes.SetBool("stuck", stuck);

            double impactSpeed = Math.Max(motionBeforeCollide.Length(), pos.Motion.Length());

            if (stuck)
            {
                if (Api.Side == EnumAppSide.Client) ServerPos.SetFrom(Pos);
                IsColliding(pos, impactSpeed);
                return;
            }
            else
            {
                AiProjectileSetRotation();
            }

            if (TryAttackEntity(impactSpeed))
            {
                return;
            }

            beforeCollided = false;
            motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
        }


        public override void OnCollided()
        {
            EntityPos pos = SidedPos;

            IsColliding(SidedPos, Math.Max(motionBeforeCollide.Length(), pos.Motion.Length()));
            motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
        }


        protected virtual void IsColliding(EntityPos pos, double impactSpeed)
        {
            pos.Motion.Set(0, 0, 0);

            if (!beforeCollided && World is IServerWorldAccessor && World.ElapsedMilliseconds > msCollide + 500)
            {
                if (impactSpeed >= 0.07)
                {
                    World.PlaySoundAt(new AssetLocation("sounds/arrow-impact"), this, null, false, 32);

                    // Resend position to client
                    WatchedAttributes.MarkAllDirty();

                    if (World.Rand.NextDouble() > dropOnImpactChance)
                        Die();
                }

                TryAttackEntity(impactSpeed);

                msCollide = World.ElapsedMilliseconds;

                beforeCollided = true;
            }
        }


        protected virtual bool TryAttackEntity(double impactSpeed)
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
                    return false;

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


        protected virtual void ImpactOnEntity(Entity entity)
        {
            if (!Alive) 
                return;

            EntityPos pos = SidedPos;

            IServerPlayer fromPlayer = null;
            if (firedBy is EntityPlayer)
            {
                fromPlayer = (firedBy as EntityPlayer).Player as IServerPlayer;
            }

            bool targetIsPlayer = entity is EntityPlayer;
            bool targetIsCreature = entity is EntityAgent;
            bool canDamage = true;

            ICoreServerAPI sapi = World.Api as ICoreServerAPI;
            if (fromPlayer != null)
            {
                if (targetIsPlayer && (!sapi.Server.Config.AllowPvP || !fromPlayer.HasPrivilege("attackplayers"))) canDamage = false;
                if (targetIsCreature && !fromPlayer.HasPrivilege("attackcreatures")) canDamage = false;
            }

            msCollide = World.ElapsedMilliseconds;

            pos.Motion.Set(0, 0, 0);

            if (canDamage && World.Side == EnumAppSide.Server)
            {
                World.PlaySoundAt(new AssetLocation("sounds/arrow-impact"), this, null, false, 24);

                float dmg = damage;
                if (firedBy != null) dmg *= firedBy.Stats.GetBlended("rangedWeaponsDamage");

                bool didDamage = entity.ReceiveDamage(new DamageSource()
                {
                    Source = fromPlayer != null ? EnumDamageSource.Player : EnumDamageSource.Entity,
                    SourceEntity = this,
                    CauseEntity = firedBy,
                    Type = EnumDamageType.PiercingAttack,
                    DamageTier = damageTier
                }, dmg);

                float kbresist = entity.Properties.KnockbackResistance;
                entity.SidedPos.Motion.Add(kbresist * pos.Motion.X * weight, kbresist * pos.Motion.Y * weight, kbresist * pos.Motion.Z * weight);

                if (World.Rand.NextDouble() > dropOnImpactChance)
                    Die();

                if (firedBy is EntityPlayer && didDamage)
                    World.PlaySoundFor(new AssetLocation("sounds/player/projectilehit"), (firedBy as EntityPlayer).Player, false, 24);
            }
        }


        public virtual void AiProjectileSetRotation()
        {
            EntityPos pos = (World is IServerWorldAccessor) ? ServerPos : Pos;

            double speed = pos.Motion.Length();

            if (speed > 0.01)
            {
                pos.Pitch = 0;
                pos.Yaw =
                    GameMath.PI + (float)Math.Atan2(pos.Motion.X / speed, pos.Motion.Z / speed)
                    + GameMath.Cos((World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f
                ;
                pos.Roll =
                    -(float)Math.Asin(GameMath.Clamp(-pos.Motion.Y / speed, -1, 1))
                    + GameMath.Sin((World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f
                ;
            }
        }


        public override bool CanCollect(Entity byEntity)
        {
            return Alive && World.ElapsedMilliseconds - msLaunch > 1000 && ServerPos.Motion.Length() < 0.01;
        }

        public override ItemStack OnCollected(Entity byEntity)
        {
            projectileStack.ResolveBlockOrItem(World);
            return projectileStack;
        }


        public override void OnCollideWithLiquid()
        {
            base.OnCollideWithLiquid();
        }

        public override void ToBytes(BinaryWriter writer, bool forClient)
        {
            base.ToBytes(writer, forClient);
            writer.Write(beforeCollided);
            projectileStack.ToBytes(writer);
        }

        public override void FromBytes(BinaryReader reader, bool fromServer)
        {
            base.FromBytes(reader, fromServer);
            beforeCollided = reader.ReadBoolean();
            projectileStack = new ItemStack(reader);
        }
    }
}
