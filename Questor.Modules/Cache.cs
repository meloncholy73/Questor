﻿// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
// 
//     Please look in the accompanying license.htm file for the license that 
//     applies to this source code. (a copy can also be found at: 
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------
namespace Questor.Modules
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;
    using System.Xml.XPath;
    using DirectEve;

    public class Cache
    {
        /// <summary>
        ///   Singleton implementation
        /// </summary>
        private static Cache _instance = new Cache();

        /// <summary>
        ///   Active Drones
        /// </summary>
        private List<EntityCache> _activeDrones;

        private DirectAgent _agent;

        /// <summary>
        ///   Agent cache
        /// </summary>
        private long? _agentId;

        /// <summary>
        ///   Approaching cache
        /// </summary>
        //private int? _approachingId;
        private EntityCache _approaching;

        /// <summary>
        ///   Returns all non-empty wrecks and all containers
        /// </summary>
        private List<EntityCache> _containers;

        /// <summary>
        ///   Entities cache (all entities within 256km)
        /// </summary>
        private List<EntityCache> _entities;

        /// <summary>
        ///   Entities by Id
        /// </summary>
        private Dictionary<long, EntityCache> _entitiesById;

        /// <summary>
        ///   Returns the agent mission (note only for the agent we are running missions for!)
        /// </summary>
        private DirectAgentMission _mission;

        /// <summary>
        ///   Module cache
        /// </summary>
        private List<ModuleCache> _modules;

        /// <summary>
        ///   Priority targets (e.g. warp scramblers or mission kill targets)
        /// </summary>
        private List<PriorityTarget> _priorityTargets;

        /// <summary>
        ///   Star cache
        /// </summary>
        private EntityCache _star;

        /// <summary>
        ///   Station cache
        /// </summary>
        private List<EntityCache> _stations;

        /// <summary>
        ///   Targeted by cache
        /// </summary>
        private List<EntityCache> _targetedBy;

        /// <summary>
        ///   Targeting cache
        /// </summary>
        private List<EntityCache> _targeting;

        /// <summary>
        ///   Targets cache
        /// </summary>
        private List<EntityCache> _targets;

        /// <summary>
        ///   Returns all unlooted wrecks & containers
        /// </summary>
        private List<EntityCache> _unlootedContainers;

        private List<DirectWindow> _windows;

        public Cache()
        {
            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            ShipTargetValues = new List<ShipTargetValue>();
            var values = XDocument.Load(Path.Combine(path, "ShipTargetValues.xml"));
            foreach (var value in values.Root.Elements("ship"))
                ShipTargetValues.Add(new ShipTargetValue(value));

            InvTypesById = new Dictionary<int, InvType>();
            var invTypes = XDocument.Load(Path.Combine(path, "InvTypes.xml"));
            foreach (var element in invTypes.Root.Elements("invtype"))
                InvTypesById.Add((int) element.Attribute("id"), new InvType(element));

            _priorityTargets = new List<PriorityTarget>();
            LastModuleTargetIDs = new Dictionary<long, long>();
            TargetingIDs = new Dictionary<long, DateTime>();
            _entitiesById = new Dictionary<long, EntityCache>();

            LootedContainers = new HashSet<long>();
            IgnoreTargets = new HashSet<string>();
            MissionItems = new List<string>();
        }

        /// <summary>
        ///   List of containers that have been looted
        /// </summary>
        public HashSet<long> LootedContainers { get; private set; }

        /// <summary>
        ///   List of targets to ignore
        /// </summary>
        public HashSet<string> IgnoreTargets { get; private set; }

        public static Cache Instance
        {
            get { return _instance; }
        }

        public DirectEve DirectEve { get; set; }

        public Dictionary<int, InvType> InvTypesById { get; private set; }

        /// <summary>
        ///   List of ship target values, higher target value = higher kill priority
        /// </summary>
        public List<ShipTargetValue> ShipTargetValues { get; private set; }

        /// <summary>
        ///   Best damage type for the mission
        /// </summary>
        public DamageType DamageType { get; set; }

        /// <summary>
        ///   Returns the maximum weapon distance
        /// </summary>
        public int WeaponRange
        {
            get
            {
                // Get ammmo based on current damage type
                var ammo = Settings.Instance.Ammo.Where(a => a.DamageType == DamageType);

                // Is our ship's cargo available?
                var cargo = DirectEve.GetShipsCargo();
                if (cargo.IsReady)
                    ammo = ammo.Where(a => cargo.Items.Any(i => a.TypeId == i.TypeId && i.Quantity >= Settings.Instance.MinimumAmmoCharges));

                // Return 0 if there's no ammo left
                if (ammo.Count() == 0)
                    return 0;

                // Return max range
                return ammo.Max(a => a.Range);
            }
        }

        /// <summary>
        ///   Last target for a certain module
        /// </summary>
        public Dictionary<long, long> LastModuleTargetIDs { get; private set; }

        /// <summary>
        ///   Targeting delay cache (used by LockTarget)
        /// </summary>
        public Dictionary<long, DateTime> TargetingIDs { get; private set; }

        /// <summary>
        ///   Used for Drones to know that it should retract drones
        /// </summary>
        public bool IsMissionPocketDone { get; set; }

        public DirectAgent Agent
        {
            get
            {
                if (!_agentId.HasValue)
                {
                    _agent = DirectEve.GetAgentByName(Settings.Instance.AgentName);
                    _agentId = _agent.AgentId;
                }

                if (_agent == null)
                    _agent = DirectEve.GetAgentById(_agentId.Value);

                return _agent;
            }
        }

        public IEnumerable<ModuleCache> Modules
        {
            get
            {
                if (_modules == null)
                    _modules = DirectEve.Modules.Select(m => new ModuleCache(m)).ToList();

                return _modules;
            }
        }

        public IEnumerable<ModuleCache> Weapons
        {
            get { return Modules.Where(m => m.GroupId == Settings.Instance.WeaponGroupId); }
        }

        public IEnumerable<EntityCache> Containers
        {
            get
            {
                if (_containers == null)
                    _containers = Entities.Where(e => e.IsContainer && e.HaveLootRights && (e.GroupId != (int) Group.Wreck || !e.IsWreckEmpty)).ToList();

                return _containers;
            }
        }

        public IEnumerable<EntityCache> UnlootedContainers
        {
            get
            {
                if (_unlootedContainers == null)
                    _unlootedContainers = Entities.Where(e => e.IsContainer && e.HaveLootRights && (!LootedContainers.Contains(e.Id) || e.GroupId == (int) Group.Wreck)).OrderBy(e => e.Distance).ToList();

                return _unlootedContainers;
            }
        }

        public IEnumerable<EntityCache> Targets
        {
            get
            {
                if (_targets == null)
                    _targets = Entities.Where(e => e.IsTarget).ToList();

                // Remove the target info (its been targeted)
                foreach (var target in _targets.Where(t => TargetingIDs.ContainsKey(t.Id)))
                    TargetingIDs.Remove(target.Id);

                return _targets;
            }
        }

        public IEnumerable<EntityCache> Targeting
        {
            get
            {
                if (_targeting == null)
                    _targeting = Entities.Where(e => e.IsTargeting).ToList();

                return _targeting;
            }
        }

        public IEnumerable<EntityCache> TargetedBy
        {
            get
            {
                if (_targetedBy == null)
                    _targetedBy = Entities.Where(e => e.IsTargetedBy).ToList();

                return _targetedBy;
            }
        }

        public IEnumerable<EntityCache> Entities
        {
            get
            {
                if (!InSpace)
                    return new List<EntityCache>();

                if (_entities == null)
                    _entities = DirectEve.Entities.Select(e => new EntityCache(e)).Where(e => e.IsValid).ToList();

                return _entities;
            }
        }

        public bool InSpace
        {
            get { return DirectEve.Session.IsInSpace && !DirectEve.Session.IsInStation && DirectEve.Session.IsReady; }
        }

        public bool InStation
        {
            get { return DirectEve.Session.IsInStation && !DirectEve.Session.IsInSpace && DirectEve.Session.IsReady; }
        }

        public bool InWarp
        {
            get { return DirectEve.ActiveShip.Entity.Mode == 3; }
        }

        public IEnumerable<EntityCache> ActiveDrones
        {
            get
            {
                if (_activeDrones == null)
                    _activeDrones = DirectEve.ActiveDrones.Select(d => new EntityCache(d)).ToList();

                return _activeDrones;
            }
        }

        public IEnumerable<EntityCache> Stations
        {
            get
            {
                if (_stations == null)
                    _stations = Entities.Where(e => e.CategoryId == (int) CategoryID.Station).ToList();

                return _stations;
            }
        }

        public EntityCache Star
        {
            get
            {
                if (_star == null)
                    _star = Entities.Where(e => e.CategoryId == (int) CategoryID.Celestial && e.GroupId == (int) Group.Star).FirstOrDefault();

                return _star;
            }
        }

        public IEnumerable<EntityCache> PriorityTargets
        {
            get
            {
                _priorityTargets.RemoveAll(pt => pt.Entity == null);
                return _priorityTargets.OrderBy(pt => pt.Priority).ThenBy(pt => pt.Entity.Distance).Select(pt => pt.Entity);
            }
        }

        public EntityCache Approaching
        {
            get
            {
                if (_approaching == null)
                {
                    var ship = DirectEve.ActiveShip.Entity;
                    if (ship.IsValid)
                        _approaching = EntityById(ship.FollowId);
                }

                return _approaching != null && _approaching.IsValid ? _approaching : null;
            }
            set
            {
                if (value != null)
                {
                    _approaching = value;
                    //_approachingId = value.ID;
                }
                else
                {
                    _approaching = null;
                    //_approachingId = null;
                }
            }
        }

        public List<DirectWindow> Windows
        {
            get
            {
                if (_windows == null)
                    _windows = DirectEve.Windows;

                return _windows;
            }
        }

        public DirectAgentMission Mission
        {
            get
            {
                if (_mission == null)
                {
                    var missions = DirectEve.AgentMissions;
                    if (missions == null)
                        return null;

                    foreach (var mission in missions)
                    {
                        // Did we accept this mission?
                        if (mission.AgentId != Agent.AgentId)
                            continue;

                        _mission = mission;
                        break;
                    }
                }

                return _mission;
            }
        }

        /// <summary>
        ///   Returns the mission objectives from
        /// </summary>
        public List<string> MissionItems { get; private set; }

        /// <summary>
        ///   Returns the item that needs to be brought on the mission
        /// </summary>
        /// <returns></returns>
        public string BringMissionItem { get; private set; }

        /// <summary>
        ///   Filter illegal path-characters from the mission name
        /// </summary>
        public string MissionName
        {
            get { return FilterPath(Mission.Name); }
        }

        public DirectWindow GetWindowByCaption(string caption)
        {
            return Windows.FirstOrDefault(w => w.Caption.Contains(caption));
        }

        public DirectWindow GetWindowByName(string name)
        {
            // Special cases
            if (name == "Local")
                return Windows.FirstOrDefault(w => w.Name.StartsWith("chatchannel_solarsystemid"));

            return Windows.FirstOrDefault(w => w.Name == name);
        }

        /// <summary>
        ///   Return entities by name
        /// </summary>
        /// <param name = "name"></param>
        /// <returns></returns>
        public IEnumerable<EntityCache> EntitiesByName(string name)
        {
            return Entities.Where(e => e.Name == name).ToList();
        }

        /// <summary>
        ///   Return a cached entity by Id
        /// </summary>
        /// <param name = "id"></param>
        /// <returns></returns>
        public EntityCache EntityById(long id)
        {
            if (_entitiesById.ContainsKey(id))
                return _entitiesById[id];

            var entity = Entities.FirstOrDefault(e => e.Id == id);
            _entitiesById[id] = entity;
            return entity;
        }

        /// <summary>
        ///   Returns the first mission bookmark that starts with a certain string
        /// </summary>
        /// <returns></returns>
        public DirectAgentMissionBookmark GetMissionBookmark(string startsWith)
        {
            // Get the missons
            var mission = Instance.Mission;
            if (mission == null)
                return null;

            // Did we accept this mission?
            if (mission.State != (int) MissionState.Accepted || mission.AgentId != Instance.Agent.AgentId)
                return null;

            // Get the mission bookmarks
            foreach (var bookmark in mission.Bookmarks)
            {
                // Does it start with what we want?
                if (!bookmark.Title.ToLower().StartsWith(startsWith.ToLower()))
                    continue;

                return bookmark;
            }

            return null;
        }

        /// <summary>
        ///   Returns agent mission by agent id
        /// </summary>
        /// <param name = "agentId"></param>
        /// <returns></returns>
        public DirectAgentMission MissionByAgentId(long agentId)
        {
            return DirectEve.AgentMissions.FirstOrDefault(m => m.AgentId == agentId);
        }

        /// <summary>
        ///   Return a bookmark by id
        /// </summary>
        /// <param name = "bookmarkId"></param>
        /// <returns></returns>
        public DirectBookmark BookmarkById(long bookmarkId)
        {
            return DirectEve.Bookmarks.FirstOrDefault(b => b.BookmarkId == bookmarkId);
        }

        /// <summary>
        ///   Returns bookmarks that start with the supplied label
        /// </summary>
        /// <param name = "label"></param>
        /// <returns></returns>
        public List<DirectBookmark> BookmarksByLabel(string label)
        {
            return DirectEve.Bookmarks.Where(b => !string.IsNullOrEmpty(b.Title) && b.Title.StartsWith(label)).ToList();
        }

        /// <summary>
        ///   Invalidate the cached items
        /// </summary>
        public void InvalidateCache()
        {
            _windows = null;
            _unlootedContainers = null;
            _star = null;
            _stations = null;
            _modules = null;
            _targets = null;
            _targeting = null;
            _targetedBy = null;
            _entities = null;
            _agent = null;
            _approaching = null;
            _mission = null;
            _activeDrones = null;
            _containers = null;
            _priorityTargets.ForEach(pt => pt.ClearCache());
            _entitiesById.Clear();
        }

        public string FilterPath(string path)
        {
            if (path == null)
                return string.Empty;

            path = path.Replace("\"", "");
            path = path.Replace("?", "");
            path = path.Replace("\\", "");
            path = path.Replace("/", "");
            path = path.Replace("'", "");
            path = path.Replace("*", "");
            path = path.Replace(":", "");
            path = path.Replace(">", "");
            path = path.Replace("<", "");
            path = path.Replace(".", "");
            path = path.Replace(",", "");
            while (path.IndexOf("  ") >= 0)
                path = path.Replace("  ", " ");
            return path.Trim();
        }

        /// <summary>
        ///   Loads mission objectives from XML file
        /// </summary>
        /// <param name = "pocketId"></param>
        /// <returns></returns>
        public IEnumerable<Action> LoadMissionActions(int pocketId)
        {
            if (Mission == null)
                return new Action[0];

            var missionXmlPath = Path.Combine(Settings.Instance.MissionsPath, MissionName + ".xml");
            if (!File.Exists(missionXmlPath))
                return new Action[0];

            try
            {
                var xdoc = XDocument.Load(missionXmlPath);
                var pockets = xdoc.Root.Element("pockets").Elements("pocket");
                foreach (var pocket in pockets)
                {
                    if ((int) pocket.Attribute("id") != pocketId)
                        continue;

                    if (pocket.Element("damagetype") != null)
                        DamageType = (DamageType) Enum.Parse(typeof (DamageType), (string) pocket.Element("damagetype"), true);

                    var actions = new List<Action>();
                    var elements = pocket.Element("actions");
                    if (elements != null)
                    {
                        foreach (var element in elements.Elements("action"))
                        {
                            var action = new Action();
                            action.State = (ActionState) Enum.Parse(typeof (ActionState), (string) element.Attribute("name"), true);
                            foreach (var parameter in element.Elements("parameter"))
                                action.AddParameter((string) parameter.Attribute("name"), (string) parameter.Attribute("value"));
                            actions.Add(action);
                        }
                    }
                    return actions;
                }

                return new Action[0];
            }
            catch (Exception ex)
            {
                Logging.Log("Error loading mission XML file [" + ex.Message + "]");
                return new Action[0];
            }
        }

        /// <summary>
        ///   Refresh the mission items
        /// </summary>
        public void RefreshMissionItems()
        {
            // Clear out old items
            MissionItems.Clear();
            BringMissionItem = string.Empty;

            if (Mission == null)
                return;

            var missionXmlPath = Path.Combine(Settings.Instance.MissionsPath, MissionName + ".xml");
            if (!File.Exists(missionXmlPath))
                return;

            try
            {
                var xdoc = XDocument.Load(missionXmlPath);
                var items = ((IEnumerable) xdoc.XPathEvaluate("//action[(@name='loot') or (@name='Loot')]/parameter[(@name='item') or (@name='Item')]/@value")).Cast<XAttribute>().Select(a => (string) a);
                MissionItems.AddRange(items);

                BringMissionItem = (string) xdoc.Root.Element("bring");
            }
            catch (Exception ex)
            {
                Logging.Log("Error loading mission XML file [" + ex.Message + "]");
            }
        }

        /// <summary>
        ///   Remove targets from priority list
        /// </summary>
        /// <param name = "targets"></param>
        public bool RemovePriorityTargets(IEnumerable<EntityCache> targets)
        {
            return _priorityTargets.RemoveAll(pt => targets.Any(t => t.Id == pt.EntityID)) > 0;
        }

        /// <summary>
        ///   Add priority targets
        /// </summary>
        /// <param name = "targets"></param>
        /// <param name = "priority"></param>
        public void AddPriorityTargets(IEnumerable<EntityCache> targets, Priority priority)
        {
            foreach (var target in targets)
            {
                if (_priorityTargets.Any(pt => pt.EntityID == target.Id))
                    continue;

                _priorityTargets.Add(new PriorityTarget {EntityID = target.Id, Priority = priority});
            }
        }

        /// <summary>
        ///   Calculate distance from me
        /// </summary>
        /// <param name = "x"></param>
        /// <param name = "y"></param>
        /// <param name = "z"></param>
        /// <returns></returns>
        public double DistanceFromMe(double x, double y, double z)
        {
            var curX = DirectEve.ActiveShip.Entity.X;
            var curY = DirectEve.ActiveShip.Entity.Y;
            var curZ = DirectEve.ActiveShip.Entity.Z;

            return Math.Sqrt((curX - x)*(curX - x) + (curY - y)*(curY - y) + (curZ - z)*(curZ - z));
        }

        /// <summary>
        ///   Create a bookmark
        /// </summary>
        /// <param name = "label"></param>
        public void CreateBookmark(string label)
        {
            DirectEve.BookmarkCurrentLocation(label, "");
        }
    }
}