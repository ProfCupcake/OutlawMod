
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace OutlawMod
{

    public class CustomSpawnConditions : ModSystem
    {
        ICoreServerAPI sapi;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Server;
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            this.sapi = api;

            sapi.Event.OnTrySpawnEntity += Event_OnTrySpawnEntity;
            sapi.Event.OnEntityDespawn += Event_OnEntityDespawn;
        }

        private bool Event_OnTrySpawnEntity(IBlockAccessor blockAccessor, ref EntityProperties properties, Vec3d spawnPosition, long herdId)
        {

            if (OMGlobalConstants.devMode)
            {
                if (properties.Code.Path.StartsWithFast("drifter"))
                    return true;

                if (properties.Code.Path.StartsWithFast("butterfly"))
                    return true;

                string message = "[Outlaw Mod Debug] Attempting to spawn entity " + properties.Code.Path;
                sapi.Logger.Debug(message);
            }

            string type = properties.Code.FirstPathPart();

            //This may be a good location to spawn things that have to spawn in specific locations or on specific block materials.
            switch (type)
            {
                case "looter":
                case "poacher-spear":
                case "poacher-archer":
                case "bandit-axe":
                case "bandit-spear":
                case "bandit-knife":
                case "yeoman-archer":
                case "deserter-archer":
                case "deserter-footman":
                case "deserter-bannerman-*":
                    return ShouldSpawnOutlawOfType(ref properties, spawnPosition);
                case "hound-feral":
                case "hound-hunting":
                    return ShouldSpawnHoundOfType(ref properties, spawnPosition);
                    
            }

            return true;
        }

        private void Event_OnEntityDespawn(Entity ent, EntityDespawnData despawnData)
        {
            if (OMGlobalConstants.devMode)
            {
                string type = ent.Code.FirstPathPart();

                //This is here so we can debug despawning.
                switch (type)
                {
                    case "looter":
                    case "poacher-spear":
                    case "poacher-archer":
                    case "bandit-axe":
                    case "bandit-spear":
                    case "bandit-knife":
                    case "yeoman-archer":
                    case "deserter-archer":
                    case "deserter-footman":
                    case "deserter-bannerman":
                    case "hound-feral":
                    case "hound-hunting":
                        string message = "[Outlaw Mod Debug] Despawning entity " + ent.Code.Path;
                        sapi.Logger.Debug(message);
                        break;
                }
            }
        }

        private bool ShouldSpawnOutlawOfType( ref EntityProperties properties, Vec3d spawnPosition )
        {
            //Check spawn rules.
            return OutlawSpawnEvaluator.CanSpawnOutlaw(spawnPosition, properties.Code);
        }

        private bool ShouldSpawnHoundOfType( ref EntityProperties properties, Vec3d spawnPosition)
        {
            //Check spawn rules.
            return OutlawSpawnEvaluator.CanSpawnHound(spawnPosition, properties.Code);
        }

    }
}
