using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedAiTasks
{
    public static class BlockUtility
    {
        public static void CreateExplosion(ICoreServerAPI sapi, BlockPos pos, EnumBlastType blastType, double destructionRadius, double injureRadius, float blockDropChanceMultiplier = 1f)
	    {
		    destructionRadius = GameMath.Clamp(destructionRadius, 1.0, 16.0);
		    double maxRadius = Math.Max(destructionRadius, injureRadius);
		    if (maxRadius > (double)ShapeUtil.maxShells)
		    {
			    throw new ArgumentOutOfRangeException("Radius cannot be greater than " + ShapeUtil.maxShells);
		    }
		    Vec3f[] shellPositions = ShapeUtil.GetCachedCubicShellNormalizedVectors((int)maxRadius);
		    double minDestroRadius = 0.800000011920929 * destructionRadius;
		    double destroRadiusAdd = 0.40000000596046448 * destructionRadius;
		    BlockPos tmpPos = new BlockPos();
		    int maxRadiusCeil = (int)Math.Ceiling(maxRadius);
		    BlockPos minPos = pos.AddCopy(-maxRadiusCeil);
		    BlockPos maxPos = pos.AddCopy(maxRadiusCeil);
		    
            IBlockAccessorPrefetch prefetchBlockAccessor = sapi.World.GetBlockAccessorPrefetch(true, false);
            prefetchBlockAccessor.PrefetchBlocks(minPos, maxPos);
		    
            DamageSource testSrc = new DamageSource
		    {
			    Source = EnumDamageSource.Explosion,
			    SourcePos = pos.ToVec3d(),
			    Type = EnumDamageType.BluntAttack
		    };
		    Entity[] entities = sapi.World.GetEntitiesAround(pos.ToVec3d(), (float)maxRadius + 2f, (float)maxRadius + 2f, (Entity e) => e.ShouldReceiveDamage(testSrc, (float)injureRadius));
		    Dictionary<long, double> strongestRayOnEntity = new Dictionary<long, double>();
		    for (int k = 0; k < entities.Length; k++)
		    {
			    strongestRayOnEntity[entities[k].EntityId] = 0.0;
		    }
		    ExplosionSmokeParticles particleProvider = new ExplosionSmokeParticles();
		    particleProvider.basePos = new Vec3d((double)pos.X + 0.5, (double)pos.Y + 0.5, (double)pos.Z + 0.5);
		    Dictionary<BlockPos, Block> explodedBlocks = new Dictionary<BlockPos, Block>();
		    Cuboidd testBox = Block.DefaultCollisionBox.ToDouble();
		    
            for (int j = 0; j < shellPositions.Length; j++)
		    {
                double curDestroStrength;
			    double val2 = (curDestroStrength = minDestroRadius + sapi.World.Rand.NextDouble() * destroRadiusAdd);
			    double curInjureStrength = injureRadius;
			    double maxStrength = Math.Max(val2, injureRadius);
			    Vec3f vec = shellPositions[j];
			    
                for (double r = 0.0; r < maxStrength; r += 0.25)
			    {

                    tmpPos.Set(pos.X + (int)((double)vec.X * r + 0.5), pos.Y + (int)((double)vec.Y * r + 0.5), pos.Z + (int)((double)vec.Z * r + 0.5));
				    
                    if (!sapi.World.BlockAccessor.IsValidPos(tmpPos))
				    {
					    break;
				    }
				    
					//This is a check we had to add to fix the native explosion code. It is possible for the tempPos logic above
					//To exceed the bounds of the pre-allocation and cause a silent assert when it exceeds the bounds of the explosion.
					if ( (tmpPos.X < minPos.X) || (tmpPos.Y < minPos.Y) || (tmpPos.Z < minPos.Z) ||
                        (tmpPos.X > maxPos.X) || (tmpPos.Y > maxPos.Y) || (tmpPos.Z > maxPos.Z) )
					{
						break;
					}

                    curDestroStrength -= 0.25;
				    curInjureStrength -= 0.25;
				    
                    if (!explodedBlocks.ContainsKey(tmpPos))
				    {
						Debug.Assert( (tmpPos.X >= minPos.X) && (tmpPos.Y >= minPos.Y) && (tmpPos.Z >= minPos.Z) );
						Debug.Assert( (tmpPos.X <= maxPos.X) && (tmpPos.Y <= maxPos.Y) && (tmpPos.Z <= maxPos.Z) );

						//To Do: Something about this default logic seems to be exceeding the bounds of the prefetch block accessor, investigate.
						Block block = prefetchBlockAccessor.GetBlock(tmpPos);
						//Block block = sapi.World.BlockAccessor.GetBlock(tmpPos);
					    double resist = block.GetBlastResistance(sapi.World, tmpPos, vec, blastType);
					    curDestroStrength -= resist;
					    if (curDestroStrength > 0.0)
					    {
						    explodedBlocks[tmpPos.Copy()] = block;
						    curInjureStrength -= resist;
					    }
					    if (curDestroStrength <= 0.0 && resist > 0.0)
					    {
						    curInjureStrength = 0.0;
					    }
				    }
				    if (curDestroStrength <= 0.0 && curInjureStrength <= 0.0)
				    {
					    break;
				    }
				    if (!(curInjureStrength > 0.0))
				    {
					    continue;
				    }
                    
				    foreach (Entity entity in entities)
				    {
					    testBox.Set(tmpPos.X, tmpPos.Y, tmpPos.Z, tmpPos.X + 1, tmpPos.Y + 1, tmpPos.Z + 1);
					    if (testBox.IntersectsOrTouches(entity.SelectionBox, entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z))
					    {
						    strongestRayOnEntity[entity.EntityId] = Math.Max(strongestRayOnEntity[entity.EntityId], curInjureStrength);
					    }
				    }
			    }
		    }

		    foreach (Entity entityToDamage in entities)
		    {
			    double strength = strongestRayOnEntity[entityToDamage.EntityId];
			    if (strength != 0.0)
			    {
				    double damage = Math.Max(injureRadius / Math.Max(0.5, injureRadius - strength), strength);
				    if (!(damage < 0.25))
				    {
					    DamageSource src = new DamageSource
					    {
						    Source = EnumDamageSource.Explosion,
						    Type = EnumDamageType.BluntAttack,
						    SourcePos = new Vec3d((double)pos.X + 0.5, pos.Y, (double)pos.Z + 0.5)
					    };
					    entityToDamage.ReceiveDamage(src, (float)damage);
				    }
			    }
		    }
		    particleProvider.AddBlocks(explodedBlocks);

            foreach (KeyValuePair<BlockPos, Block> val in explodedBlocks)
		    {
			    if (val.Value.BlockMaterial != 0)
			    {
				    val.Value.OnBlockExploded(sapi.World, val.Key, pos, blastType);
			    }
		    }

			sapi.World.BulkBlockAccessor.Commit();

		    foreach (KeyValuePair<BlockPos, Block> item in explodedBlocks)
		    {
                sapi.World.BlockAccessor.TriggerNeighbourBlockUpdate(item.Key);
		    }
		    string soundName = "effect/smallexplosion";
		    if (destructionRadius > 12.0)
		    {
			    soundName = "effect/largeexplosion";
		    }
		    else if (destructionRadius > 6.0)
		    {
			    soundName = "effect/mediumexplosion";
		    }

		    sapi.World.PlaySoundAt(new AssetLocation("sounds/" + soundName), pos.X, pos.Y, pos.Z, null, randomizePitch: false, (float)(24.0 * Math.Pow(destructionRadius, 0.5)));
		    SimpleParticleProperties explosionFireParticles = ExplosionParticles.ExplosionFireParticles;
		    float mul = (float)destructionRadius / 3f;
		    explosionFireParticles.MinPos.Set(pos.X, pos.Y, pos.Z);
		    explosionFireParticles.MinQuantity = 100f * mul;
		    explosionFireParticles.AddQuantity = (int)(20.0 * Math.Pow(destructionRadius, 0.75));
		    sapi.World.SpawnParticles(explosionFireParticles);
		    
            AdvancedParticleProperties explosionFireTrailParticles = ExplosionParticles.ExplosionFireTrailCubicles;
		    explosionFireTrailParticles.Velocity = new NatFloat[3]
		    {
			    NatFloat.createUniform(0f, 8f + mul),
			    NatFloat.createUniform(3f + mul, 3f + mul),
			    NatFloat.createUniform(0f, 8f + mul)
		    };
		    explosionFireTrailParticles.basePos.Set((double)pos.X + 0.5, (double)pos.Y + 0.5, (double)pos.Z + 0.5);
		    explosionFireTrailParticles.GravityEffect = NatFloat.createUniform(0.5f, 0f);
		    explosionFireTrailParticles.LifeLength = NatFloat.createUniform(1.5f * mul, 0.5f);
		    explosionFireTrailParticles.Quantity = NatFloat.createUniform(30f * mul, 10f);
		    float f2 = (float)Math.Pow(mul, 0.75);
		    explosionFireTrailParticles.Size = NatFloat.createUniform(0.5f * f2, 0.2f * f2);
		    explosionFireTrailParticles.SecondaryParticles[0].Size = NatFloat.createUniform(0.25f * (float)Math.Pow(mul, 0.5), 0.05f * f2);
		    
            sapi.World.SpawnParticles(explosionFireTrailParticles);
            sapi.World.SpawnParticles(particleProvider);

		    TreeAttribute tree = new TreeAttribute();
		    tree.SetBlockPos("pos", pos);
		    tree.SetInt("blasttype", (int)blastType);
		    tree.SetDouble("destructionRadius", destructionRadius);
		    tree.SetDouble("injureRadius", injureRadius);

            sapi.Event.PushEvent("onexplosion", tree);
        }
    }
}
