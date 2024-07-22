using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Vintagestory.GameContent;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using ExpandedAiTasks.DataTypes;

namespace ExpandedAiTasks.Managers
{
 /*
  _____       _   _ _           _             _                     
 | ____|_ __ | |_(_) |_ _   _  | |    ___  __| | __ _  ___ _ __ ___ 
 |  _| | '_ \| __| | __| | | | | |   / _ \/ _` |/ _` |/ _ \ '__/ __|
 | |___| | | | |_| | |_| |_| | | |__|  __/ (_| | (_| |  __/ |  \__ \
 |_____|_| |_|\__|_|\__|\__, | |_____\___|\__,_|\__, |\___|_|  |___/
                        |___/                   |___/       
 */

    //This is a data structure that builds a search tree out of every entity in game by path code.
    //It can quickly and efficiently return lists of entites matching either a complete or wildcard code.
    //It is recomended that you only use this system in cases where searching for a narrow set of entities by name
    //will return a smaller number of objects than using EnitityPartitions to search all entities in a radius.
    //In short, if you are doing massive radius searches to find specific entities, try using this instead.
    //Small radius EntityPartion searchs are likely still cheaper than this in many cases.
    public struct EntityLedger
    {
        private Dictionary<string, EntityLedgerEntry> ledgerEntries;
        private EntityLedgerEntrySearchData searchData;
        long lastEntIDAdded = -1;
        long lastEntItemIDAdded = -1;

        public EntityLedger() 
        { 
            ledgerEntries = new Dictionary<string, EntityLedgerEntry>();
        }

        public void ShutdownCleanup()
        {
            foreach( EntityLedgerEntry entry in ledgerEntries.Values ) 
            {
                entry.ShutdownCleanup();
            }

            ledgerEntries.Clear();

            lastEntIDAdded = -1;
            lastEntItemIDAdded = -1;
        }

        public void AddEntityToLedger( Entity entity )
        {
            //We can run into situations where an object saved in a chunk has the same entity ID as a loaded entity in the world.
            //In these cases, the loaded object is deleted on load. We need to handle the case where the entity IDs match, but the entities are diffrent.
            //This is a native Vintage Story issue. In these cases, we simply add the new projectile, because the deleted entity will be removed the next time the managed entity array is checked.
            /*
            if (entity.EntityId == lastEntIDAdded)
            {
                Entity dupeEnt = entity.World.GetEntityById(lastEntIDAdded);
                Debug.Assert(dupeEnt != entity, "We are trying to add Entity " + entity.Code.ToString() + " to Entity Ledger, but It Already Exists in the Ledger.");
            }
            */

            string codeStart = entity.FirstCodePart();
            AssetLocation searchCode = entity.Code;

            if (ledgerEntries.ContainsKey( codeStart ) )
            {
                ledgerEntries[ codeStart ].AddEntity(searchCode, entity );
            }
            else
            {
                ledgerEntries.Add(codeStart, new EntityLedgerEntry(searchCode, entity));
            }

            lastEntIDAdded = entity.EntityId;
        }

        public void AddEntityItemToLedger( Entity entity )
        {
            if (entity == null)
                return;

            if (entity.State == EnumEntityState.Despawned)
                return;

            Debug.Assert(entity is EntityItem);

            EntityItem item = (EntityItem)entity;
            Debug.Assert(item.Itemstack != null, "Cannot add item to ledger, Itemstack is null!");

            //We can run into situations where an object saved in a chunk has the same entity ID as a loaded entity in the world.
            //In these cases, the loaded object is deleted on load. We need to handle the case where the entity IDs match, but the entities are diffrent.
            //This is a native Vintage Story issue. In these cases, we simply add the new projectile, because the deleted entity will be removed the next time the managed entity array is checked.
            /*
            if ( entity.EntityId == lastEntItemIDAdded )
            {
                Entity dupeEnt = entity.World.GetEntityById( lastEntItemIDAdded );
                Debug.Assert(dupeEnt != entity, "We are trying to add EntityItem with Item Stack " + item.Itemstack + " to Entity Ledger, but It Already Exists in the Ledger.");
            }
            */

            if ( item.Itemstack.Block == null )
            {
                Debug.Assert(item.Itemstack.Item != null);

                //If we loaded an item that has a garbage code, early out, we won't be able to search it anyways.
                if (item.Itemstack.Item.Code == null)
                    return;

            }
            else 
            {
                //If we loaded an item that has a garbage code, early out, we won't be able to search it anyways.
                if (item.Itemstack.Block.Code == null)
                    return;
            }

            string codeStart = item.Itemstack.Block != null ? item.Itemstack.Block.Code.FirstCodePart() : item.Itemstack.Item.Code.FirstCodePart();
            AssetLocation searchCode = item.Itemstack.Block != null ? item.Itemstack.Block.Code : item.Itemstack.Item.Code;

            if (ledgerEntries.ContainsKey(codeStart))
            {
                ledgerEntries[codeStart].AddEntity(searchCode, entity);
            }
            else
            {
                ledgerEntries.Add(codeStart, new EntityLedgerEntry(searchCode, entity));
            }

            lastEntItemIDAdded = entity.EntityId;
        }

        public List<Entity> GetAllEntitiesMatchingCode(AssetLocation assetLocation)
        {
            string codeStart = assetLocation.FirstCodePart();

            if ( ledgerEntries.ContainsKey( codeStart ) )
            {
                return ledgerEntries[codeStart].GetAllEntitiesMatchingCode(assetLocation, searchData);
            }

            return EntityManager.emptyList;
        }

        private struct EntityLedgerEntry
        {
            private AssetLocation assetLocation;
            private ManagedEntityArray meaEntityLedgerEntry;
            private Dictionary<string, EntityLedgerEntry> subLedgers = new Dictionary<string, EntityLedgerEntry>();
            private EntityLedgerEntrySearchData searchData = new EntityLedgerEntrySearchData();
            public EntityLedgerEntry(AssetLocation assetLocation, Entity entity, int pathLimit = -1)
            {
                string[] codeVariants = GetAllCodeVariantsForAssetLocation(assetLocation, pathLimit);
                int pathDepth = codeVariants.Length - 1;

                //Create Our Ledger Entry
                this.assetLocation = new AssetLocation(codeVariants[0]);
                meaEntityLedgerEntry = new ManagedEntityArray();
                meaEntityLedgerEntry.AddEntity(entity);

                if (codeVariants.Length > 1)
                {
                    subLedgers.Add(codeVariants[1], new EntityLedgerEntry(new AssetLocation(codeVariants[codeVariants.Length - 1]), entity, pathDepth - 1));
                }
            }

            public void ShutdownCleanup()
            {
                meaEntityLedgerEntry.Clear();
                foreach( EntityLedgerEntry entry in subLedgers.Values )
                {
                    entry.ShutdownCleanup();
                }

                assetLocation = null;
                subLedgers.Clear();
            }

            public void AddEntity(AssetLocation assetLocation, Entity entity, int pathLimit = -1)
            {
                string[] codeVariants = GetAllCodeVariantsForAssetLocation(assetLocation, pathLimit);
                int pathDepth = codeVariants.Length - 1;

                //Create Our Ledger Entry
                meaEntityLedgerEntry.AddEntity(entity);

                if (codeVariants.Length > 1)
                {
                    if (subLedgers.ContainsKey(codeVariants[1]))
                        subLedgers[codeVariants[1]].AddEntity(new AssetLocation(codeVariants[codeVariants.Length - 1]), entity, pathDepth - 1);
                    else
                        subLedgers.Add(codeVariants[1], new EntityLedgerEntry(new AssetLocation(codeVariants[codeVariants.Length - 1]), entity, pathDepth - 1));
                }
            }

            public List<Entity> GetAllEntitiesMatchingCode(AssetLocation assetLocation, EntityLedgerEntrySearchData searchData)
            {
                string shortString = assetLocation.ToShortString();
                int pathDepth = shortString.Count(x => x == '-');
                string[] codeVariants = GetAllCodeVariantsForAssetLocation(assetLocation, -1);

                //Configure our static search data before beginning our search.
                searchData.assetLocation = assetLocation;
                searchData.codeVariants = codeVariants;
                searchData.pathDepthMax = pathDepth + 1;
                searchData.pathDepthCurrent = 0;
                return CrawlLedgerEntryTree(searchData);
            }

            public List<Entity> CrawlLedgerEntryTree(EntityLedgerEntrySearchData searchData)
            {
                if (searchData.assetLocation == assetLocation)
                {
                    return meaEntityLedgerEntry.GetManagedList();
                }
                else
                {
                    searchData.pathDepthCurrent++;

                    if (searchData.pathDepthMax == searchData.pathDepthCurrent)
                        return EntityManager.emptyList;

                    if (subLedgers.ContainsKey(searchData.codeVariants[searchData.pathDepthCurrent]))
                        return subLedgers[searchData.codeVariants[searchData.pathDepthCurrent]].CrawlLedgerEntryTree(searchData);
                    else
                        return EntityManager.emptyList;
                }
            }

            private string[] GetAllCodeVariantsForAssetLocation(AssetLocation assetLocation, int pathLimit = -1)
            {
                string shortString = assetLocation.ToShortString();

                int pathDepth = shortString.Count(x => x == '-');

                if (pathLimit > -1)
                    pathDepth = Math.Min(pathDepth, pathLimit);

                AssetLocation currentPath = assetLocation.Clone();
                currentPath.Domain = ""; //Clear Domain;

                //Generate the path name at every potential wildcard.
                string[] codeVariants = new string[pathDepth + 1];
                for (int i = pathDepth; i >= 0; i--)
                {
                    codeVariants[i] = currentPath.Path;

                    if (i < pathDepth)
                        codeVariants[i] += "-*";

                    string endVariant = currentPath.EndVariant();
                    if (endVariant != "")
                        currentPath = currentPath.WithoutPathAppendix("-" + endVariant);
                }

                return codeVariants;
            }
        }

        private struct EntityLedgerEntrySearchData
        {
            public AssetLocation assetLocation;
            public string[] codeVariants;
            public int pathDepthMax;
            public int pathDepthCurrent;
        }
    }

    /*
      _____       _   _ _           ____  _ _           ____            _                 
     | ____|_ __ | |_(_) |_ _   _  |  _ \(_) |__  ___  / ___| _   _ ___| |_ ___ _ __ ___  
     |  _| | '_ \| __| | __| | | | | | | | | '_ \/ __| \___ \| | | / __| __/ _ \ '_ ` _ \ 
     | |___| | | | |_| | |_| |_| | | |_| | | |_) \__ \  ___) | |_| \__ \ ||  __/ | | | | |
     |_____|_| |_|\__|_|\__|\__, | |____/|_|_.__/|___/ |____/ \__, |___/\__\___|_| |_| |_|
                            |___/                             |___/                                          
    */

    public enum EDibsReason
    {
        Eat,
        Attack,
        Heal,
        Pickup,
        Follow,
    }

    public struct EntityDibsDatabase
    {
        private Dictionary<Entity, EntityDibsData> dibsByEntity = new Dictionary<Entity, EntityDibsData>();

        static List<EDibsReason> staleReasons = new List<EDibsReason>();
        static List<Entity> staleClaimants = new List<Entity>();

        public EntityDibsDatabase()
        {

        }

        public void ShutdownCleanup()
        {
            foreach(EntityDibsData dibsData in dibsByEntity.Values)
            {
                dibsData.ShutdownCleanup();
            }

            dibsByEntity.Clear();
        }

        public void CallDibsOnEntity(Entity claimant, Entity target, EDibsReason reason, long durationMs)
        {
            if (dibsByEntity.ContainsKey(target))
            {
                dibsByEntity[target].AddOrUpdateDibsDataForClaimant(claimant, reason, durationMs);
            }
            else
            {
                EntityDibsData dibsData = new EntityDibsData(claimant, target, reason, durationMs);
                dibsByEntity.Add(target, dibsData);
            }
        }

        public void ReleaseDibsOnEntity(Entity claimant, Entity target, EDibsReason reason)
        {
            if ( dibsByEntity.ContainsKey(target) )
            {
                dibsByEntity[target].RemoveDibsDataForClaimant(claimant, reason);
            }
        }

        public bool HasDibsOnEntity(Entity claimant, Entity target, EDibsReason reason)
        {
            if (dibsByEntity.ContainsKey(target))
                CleanData(target);

            if (dibsByEntity.ContainsKey(target))
            {
                if (dibsByEntity[target].dibsByReason.ContainsKey(reason))
                {
                    if (dibsByEntity[target].dibsByReason[reason].dibsTimeoutByEntity.ContainsKey(claimant))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool IsEntityClaimedForAnyReason(Entity target)
        {
            if (dibsByEntity.ContainsKey(target))
                CleanData(target);

            if (dibsByEntity.ContainsKey(target))
            {
                if (dibsByEntity[target].dibsByReason.Count > 0)
                    return true;
            }

            return false;
        }

        public int CountEntityClaimantsForAnyReason( Entity target )
        {
            int claimantCount = 0;

            if (dibsByEntity.ContainsKey(target))
                CleanData(target);

            if (dibsByEntity.ContainsKey(target))
            {
                foreach ( EDibsReason reason in dibsByEntity[target].dibsByReason.Keys )
                {
                    claimantCount += dibsByEntity[target].dibsByReason[reason].dibsTimeoutByEntity.Count;
                }
            }

            return claimantCount;
        }

        public bool IsEntityClaimedForReason(Entity target, EDibsReason reason )
        {
            if (dibsByEntity.ContainsKey(target))
                CleanData(target);

            if ( dibsByEntity.ContainsKey(target) )
            {
                if (dibsByEntity[target].dibsByReason.ContainsKey(reason) )
                {
                    if ( dibsByEntity[target].dibsByReason[reason].dibsTimeoutByEntity.Count() > 0 )
                        return true;
                }
            }

            return false;
        }

        public int CountEntityClaimantsForReason(Entity target, EDibsReason reason)
        {
            int claimantCount = 0;

            if (dibsByEntity.ContainsKey(target))
                CleanData(target);

            if (dibsByEntity.ContainsKey(target))
            {
                if (dibsByEntity[target].dibsByReason.ContainsKey (reason) ) 
                {
                    claimantCount += dibsByEntity[target].dibsByReason[reason].dibsTimeoutByEntity.Count;
                }
            }

            return claimantCount;
        }

        public void CleanTree()
        {
            foreach( Entity target in dibsByEntity.Keys ) 
            {
                CleanData(target);
            }
        }

        private void CleanData( Entity target )
        {
            //Walk and clean the tree of entries that are null, shouldDespawn, or timedout.
            Debug.Assert( dibsByEntity.ContainsKey(target) );

            dibsByEntity[target].CleanData(target.World.ElapsedMilliseconds);
            
            if ( target.ShouldDespawn || dibsByEntity[target].GetEntryCount() == 0)
                dibsByEntity.Remove(target);
        }

        private struct EntityDibsData
        {
            public Dictionary<EDibsReason, DibsReasonEntry> dibsByReason = new Dictionary<EDibsReason, DibsReasonEntry>();

            public EntityDibsData( Entity claimant, Entity target, EDibsReason reason, long duationMs )
            {
                AddOrUpdateDibsDataForClaimant(claimant, reason, duationMs);
            }

            public void ShutdownCleanup()
            {
                foreach( DibsReasonEntry reasonEntry in dibsByReason.Values )
                {
                    reasonEntry.ShutdownCleanup();
                }

                dibsByReason.Clear();
            }

            public void AddOrUpdateDibsDataForClaimant( Entity claimant, EDibsReason reason, long durationMs )
            {
                if (dibsByReason.ContainsKey(reason) )
                {
                    dibsByReason[reason].AddOrUpdateClaimant(claimant, durationMs);
                }
                else
                {
                    DibsReasonEntry dibsReasonData = new DibsReasonEntry(claimant, durationMs);
                    dibsByReason.Add(reason, dibsReasonData);
                }
            }

            public void RemoveDibsDataForClaimant( Entity claimant, EDibsReason reason )
            {
                if (dibsByReason.ContainsKey(reason))
                {
                    dibsByReason[reason].RemoveClaimant(claimant);
                }
            }

            public int GetEntryCount()
            {
                return dibsByReason.Count;
            }

            public void CleanData( long elapsedMS )
            {
                int index = 0;
                staleReasons.Clear();
                foreach( EDibsReason reason in dibsByReason.Keys )
                {
                    dibsByReason[reason].CleanData(elapsedMS);
                    
                    if( dibsByReason[reason].GetEntryCount() == 0 )
                        staleReasons.Add(reason);
                    
                    index++;
                }

                foreach( EDibsReason reason in staleReasons )
                {
                    dibsByReason.Remove(reason);
                }
            }

            public struct DibsReasonEntry
            {
                public Dictionary<Entity, long> dibsTimeoutByEntity = new Dictionary<Entity, long>();

                public DibsReasonEntry( Entity claimant, long durationMs )
                {
                    long timeoutMs = claimant.World.ElapsedMilliseconds + durationMs;
                    dibsTimeoutByEntity.Add(claimant, timeoutMs);
                }

                public void ShutdownCleanup()
                {
                    dibsTimeoutByEntity.Clear();
                }

                public int GetEntryCount()
                {
                    return dibsTimeoutByEntity.Count;
                }

                public void AddOrUpdateClaimant( Entity claimant, long durationMs )
                {
                    long timeoutMs = claimant.World.ElapsedMilliseconds + durationMs;
                    if ( dibsTimeoutByEntity.ContainsKey(claimant) )
                    {
                        dibsTimeoutByEntity[claimant] = timeoutMs;
                    }
                    else
                    {
                        dibsTimeoutByEntity.Add(claimant, timeoutMs);
                    }
                }

                public void RemoveClaimant( Entity claimant ) 
                {
                    if (dibsTimeoutByEntity.ContainsKey(claimant))
                    {
                        dibsTimeoutByEntity.Remove(claimant);
                    }
                }

                public void CleanData(long elapsedMS) 
                {
                    staleClaimants.Clear();

                    int index = 0;
                    foreach( Entity claimant in dibsTimeoutByEntity.Keys )
                    {
                        Debug.Assert( claimant != null );
                        if (claimant.ShouldDespawn)
                            staleClaimants.Add(claimant);
                        else if (dibsTimeoutByEntity[claimant] < elapsedMS)
                            staleClaimants.Add(claimant);

                        index++;
                    }

                    foreach( Entity claimant in staleClaimants )
                    {
                        if (claimant != null)
                            dibsTimeoutByEntity.Remove(claimant);
                    }
                }
            }
        }
    }

    /*   
     _____       _   _ _           __  __                                   
    | ____|_ __ | |_(_) |_ _   _  |  \/  | __ _ _ __   __ _  __ _  ___ _ __ 
    |  _| | '_ \| __| | __| | | | | |\/| |/ _` | '_ \ / _` |/ _` |/ _ \ '__|
    | |___| | | | |_| | |_| |_| | | |  | | (_| | | | | (_| | (_| |  __/ |   
    |_____|_| |_|\__|_|\__|\__, | |_|  |_|\__,_|_| |_|\__,_|\__, |\___|_|   
                           |___/                            |___/            
    */

    public static class EntityManager
    {
        /////////////////
        //ENTITY LEDGER//
        /////////////////
        private static EntityLedger entityLedger = new EntityLedger();
        private static EntityLedger itemLedger = new EntityLedger();
        public  static List<Entity> emptyList = new List<Entity>();

        ///////////////
        //DIBS SYSTEM//
        ///////////////
        private static EntityDibsDatabase entityDibsDatabase = new EntityDibsDatabase();

        /////////////////
        //ENTITY ARRAYS//
        /////////////////
        private static ManagedEntityArray _meaEntityProjectiles = new ManagedEntityArray();
        private static ManagedEntityArray _meaEntityProjectilesInFlight = new ManagedEntityArray();
        private static ManagedEntityArray _meaEntityAIProjectiles = new ManagedEntityArray();
        private static ManagedEntityArray _meaEntityAIProjectilesInFlight = new ManagedEntityArray();
        private static long lastProjectileEntIDAdded = -1;
        private static long lastAIProjectileEntIDAdded = -1;

        public static List<EntityProjectile> entityProjectiles
        {
            get
            {
                List<EntityProjectile> entityProjectiles = new List<EntityProjectile>();
                foreach ( Entity projectile in _meaEntityProjectiles.GetManagedList() )
                {
                    entityProjectiles.Add( projectile as EntityProjectile );
                }

                return entityProjectiles;
            }
        }

        public static List<EntityProjectile> entityProjectilesInFlight
        {
            get
            {
                List<EntityProjectile> entityProjectilesInFlight = new List<EntityProjectile>();
                _meaEntityProjectilesInFlight.FilterByCheckResult(ProjectileIsInFlight);
                foreach( Entity projectile in _meaEntityProjectilesInFlight.GetManagedList() )
                {
                    entityProjectilesInFlight.Add( projectile as EntityProjectile );
                }

                return entityProjectilesInFlight;
            }
        }

        public static List<EntityAIProjectile> entityAiProjectiles
        {
            get
            {
                List<EntityAIProjectile> entityAiProjectiles = new List<EntityAIProjectile>();
                foreach( Entity aiProjectile in _meaEntityAIProjectiles.GetManagedList() )
                {
                    entityAiProjectiles.Add( aiProjectile as EntityAIProjectile );
                }

                return entityAiProjectiles;
            }
        }

        public static List<EntityAIProjectile> entityAiProjectilesInFlight
        {
            get
            {
                List<EntityAIProjectile> entityAiProjectilesInFlight = new List<EntityAIProjectile>();
                _meaEntityAIProjectilesInFlight.FilterByCheckResult(ProjectileIsInFlight);
                foreach ( Entity aiProjectile in _meaEntityAIProjectilesInFlight.GetManagedList() )
                {
                    entityAiProjectilesInFlight.Add( aiProjectile as EntityAIProjectile );
                }

                return entityAiProjectilesInFlight;
            }
        }

        public static void ShutdownCleanup()
        {
            //Clean Up Managed Arrays
            _meaEntityProjectiles.Clear();
            _meaEntityProjectilesInFlight.Clear();
            _meaEntityAIProjectiles.Clear();
            _meaEntityAIProjectilesInFlight.Clear();

            //Clean Up Dibs System
            entityDibsDatabase.ShutdownCleanup();

            //Unload Entity Ledger
            entityLedger.ShutdownCleanup();
            itemLedger.ShutdownCleanup();

            lastProjectileEntIDAdded = -1;
            lastAIProjectileEntIDAdded = -1;
        }

        private static bool ProjectileIsInFlight( Entity entity )
        {
            return entity.ApplyGravity;
        }

        public static void RegisterEntityWithEntityLedger( Entity entity )
        {
            if ( entity.Code.ToString() == "game:item" )
            {
                itemLedger.AddEntityItemToLedger( entity );
            }
            else
            {
                entityLedger.AddEntityToLedger(entity);
            }
        }

        public static List<Entity> GetAllEntitiesMatchingCode( AssetLocation assetLocation )
        {
            return entityLedger.GetAllEntitiesMatchingCode(assetLocation);
        }

        public static List<Entity> GetAllEntityItemsMatchingCode( AssetLocation assetLocation ) 
        {
            return itemLedger.GetAllEntitiesMatchingCode(assetLocation);
        }

        public static Entity GetNearestEntityItemMatchingCodes(Vec3d position, double radius, AssetLocation[] codes, ActionConsumable<Entity> matches = null)
        {
            Entity nearestEntityItem = null;
            double radiusSqr = radius * radius;
            double nearestDistanceSqr = radiusSqr;
            
            List<Entity>[] entityItemListsMatchingCodes = new List<Entity>[codes.Length];
            for ( int i = 0; i < codes.Length; i++ ) 
            {
                entityItemListsMatchingCodes[i] = GetAllEntityItemsMatchingCode(codes[i]);
            }

            foreach( List<Entity> entityList in entityItemListsMatchingCodes ) 
            { 
                foreach( Entity entity in entityList ) 
                { 
                    double distanceSqr = entity.SidedPos.SquareDistanceTo(position);
                    if ( distanceSqr < nearestDistanceSqr && matches(entity) ) 
                    {
                        nearestDistanceSqr = distanceSqr;
                        nearestEntityItem = entity;
                    }
                }
            }

            return nearestEntityItem;
        }

        public static void OnEntityProjectileSpawn(Entity entity)
        {
            if ( entity is EntityProjectile )
            {
                RegisterEntityProjectile(entity);
            }
                
        }

        public static void RegisterEntityProjectile( Entity entity )
        {
            Debug.Assert(entity is EntityProjectile);

            //We can run into situations where an object saved in a chunk has the same entity ID as a loaded entity in the world.
            //In these cases, the loaded object is deleted on load. We need to handle the case where the entity IDs match, but the entities are diffrent.
            //This is a native Vintage Story issue. In these cases, we simply add the new projectile, because the deleted entity will be removed the next time the managed entity array is checked.
            /*
            if (entity.EntityId == lastProjectileEntIDAdded)
            {
                Entity dupeEnt = entity.World.GetEntityById(lastProjectileEntIDAdded);
                Debug.Assert(dupeEnt != entity, "We are trying to add EntityProjectile " + entity.Code.ToString() + " to Entity Manager Projectile Tracking, but It Already Exists in the system.");
            }
            */

            _meaEntityProjectiles.AddEntity(entity);
            _meaEntityProjectilesInFlight.AddEntity(entity);

            lastProjectileEntIDAdded = entity.EntityId;
        }

        public static void RegisterEntityAIProjectile( Entity entity )
        {
            Debug.Assert(entity is EntityAIProjectile);

            //We can run into situations where an object saved in a chunk has the same entity ID as a loaded entity in the world.
            //In these cases, the loaded object is deleted on load. We need to handle the case where the entity IDs match, but the entities are diffrent.
            //This is a native Vintage Story issue. In these cases, we simply add the new projectile, because the deleted entity will be removed the next time the managed entity array is checked.
            /*
            if ( entity.EntityId == lastAIProjectileEntIDAdded )
            {
                Entity dupeEnt = entity.World.GetEntityById(lastAIProjectileEntIDAdded);
                Debug.Assert(dupeEnt != entity, "We are trying to add EntityAIProjectile " + entity.Code.ToString() + " to Entity Manager AI Projectile Tracking, but It Already Exists in the system.");
            }
            */

            _meaEntityAIProjectiles.AddEntity(entity);
            _meaEntityAIProjectilesInFlight.AddEntity(entity);

            lastAIProjectileEntIDAdded = entity.EntityId;
        }

        public static bool IsRegisteredAsEntityProjectile(Entity entity)
        {
            Debug.Assert( entity is EntityProjectile );
            return _meaEntityProjectiles.Contains(entity);
        }

        public static bool IsRegisteredAsEntityAIProjectile( Entity entity )
        {
            Debug.Assert(entity is EntityAIProjectile);
            return _meaEntityAIProjectiles.Contains(entity);
        }

        private static List<EntityProjectile> projectilesInRange = new List<EntityProjectile>();
        public static List<EntityProjectile> GetAllEntityProjectilesWithinRangeOfPos( Vec3d pos, float range )
        {
            List<EntityProjectile> projectiles = entityProjectiles;
            projectilesInRange.Clear();
            foreach ( EntityProjectile projectile in projectiles )
            {
                if( projectile.ServerPos.SquareDistanceTo( pos ) <= range * range )
                {
                    projectilesInRange.Add(projectile);
                }
            }

            return projectilesInRange;
        }

        public static List<EntityProjectile> GetAllEntityProjectilesInFlightWithinRangeOfPos(Vec3d pos, float range)
        {
            List<EntityProjectile> projectiles = entityProjectilesInFlight;
            projectilesInRange.Clear();
            foreach (EntityProjectile projectile in projectiles)
            {
                if (projectile.ServerPos.SquareDistanceTo(pos) <= range * range)
                {
                    projectilesInRange.Add(projectile);
                }
            }

            return projectilesInRange;
        }

        private static List<EntityAIProjectile> aiProjectilesInRange = new List<EntityAIProjectile>();

        public static List<EntityAIProjectile> GetAllEntityAIProjectilesWithinRangeOfPos( Vec3d pos, float range) 
        {
            List<EntityAIProjectile> aiProjectiles = entityAiProjectiles;
            aiProjectilesInRange.Clear();
            foreach ( EntityAIProjectile aiProjectile in aiProjectiles)
            {
                if( aiProjectile.ServerPos.SquareDistanceTo( pos ) <= range * range )
                {
                    aiProjectilesInRange.Add(aiProjectile);
                }
            }

            return aiProjectilesInRange;
        }

        public static List<EntityAIProjectile> GetAllEntityAIProjectilesInFlightWithinRangeOfPos(Vec3d pos, float range)
        {
            List<EntityAIProjectile> aiProjectiles = entityAiProjectilesInFlight;
            aiProjectilesInRange.Clear();
            foreach ( EntityAIProjectile projectile in aiProjectiles )
            {
                if( projectile.ServerPos.SquareDistanceTo(pos) <= range * range )
                {
                    aiProjectilesInRange.Add(projectile);
                }
            }

            return aiProjectilesInRange;
        }

        public static Entity GetNearestEntity(List<Entity> entities, Vec3d position, double radius, ActionConsumable<Entity> matches = null)
        {
            Entity nearestEntity = null;
            double radiusSqr = radius * radius;
            double nearestDistanceSqr = radiusSqr;

            foreach (Entity entity in entities)
            {
                double distSqr = entity.SidedPos.SquareDistanceTo(position);

                if (distSqr < nearestDistanceSqr && matches(entity))
                {
                    nearestDistanceSqr = distSqr;
                    nearestEntity = entity;
                }
            }

            return nearestEntity;
        }

        //////////////////
        //DIBS FUNCTIONS//
        //////////////////

        public static void CallDibsOnEntity( Entity claimant, Entity target, EDibsReason reason, long durationMs )
        {
            entityDibsDatabase.CallDibsOnEntity(claimant, target, reason, durationMs);
        }

        public static void ReleaseDibsOnEntity( Entity claimant, Entity target, EDibsReason reason )
        {
            entityDibsDatabase.ReleaseDibsOnEntity( claimant, target, reason );
        }

        public static bool HasDibsOnEntity( Entity claimant, Entity target, EDibsReason reason )
        {
            return entityDibsDatabase.HasDibsOnEntity(claimant, target, reason);
        }

        public static void CleanDibsSystem()
        {
            entityDibsDatabase.CleanTree();
        }

        public static bool IsEntityClaimedForAnyReason(Entity target)
        {
            return entityDibsDatabase.IsEntityClaimedForAnyReason(target);
        }

        public static int CountEntityClaimantsForAnyReason(Entity target)
        {
            return entityDibsDatabase.CountEntityClaimantsForAnyReason(target);
        }

        public static bool IsEntityClaimedForReason(Entity target, EDibsReason reason)
        {
            return entityDibsDatabase.IsEntityClaimedForReason(target, reason);
        }

        public static int CountEntityClaimantsForReason(Entity target, EDibsReason reason)
        {
            return entityDibsDatabase.CountEntityClaimantsForReason(target, reason);
        }

    }
}
