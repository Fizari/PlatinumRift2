using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

//version 19/07/2016

//todo
//ratios : compute next role to spawn (centralize code) (revoir la generation des roles ) DONE ?
//role : add call when target is reached 
//save paths in cache to gain time

//quand un explorateur se rend vers son objectif et capture des zones targ�t�e par d'autre explorateurs plus distants
//optimiser explorateurs avec proximit� (sans calcul de path)
//Generaliser le code pour pouvoir cr�er des roles facilement

//attackers : attribution des targets
//attackers et explorers implementer la proximite

#region Debug

public static class Debug
{
    static bool activated = true;

    public static void Print(string msg)
    {
        if (activated)
            Console.Error.WriteLine(msg);
    }
}

#endregion

#region Reflection 

public static class Reflection
{
    private static List<TypeInfo> _roleAssemblies;

    public static List<TypeInfo> RoleAssemblies
    {
        get
        {
            if (_roleAssemblies == null)
            {
                InitRoleAssemblies();
            }
            return _roleAssemblies;
        }
    }

    public static object Construct(this Type type)
    {
        var constructors = type.GetConstructors();
        return constructors[0].Invoke(new object[] { });
    }

    private static void InitRoleAssemblies()
    {
        var currentAssembly = typeof(Reflection).GetTypeInfo().Assembly;
        _roleAssemblies = currentAssembly.DefinedTypes.Where(type => type.ImplementedInterfaces.Any(inter => inter == typeof(IRole))).ToList();
    }

    public static void ForEachRole(this List<TypeInfo> rolesAssemblies, Action<Type> action)
    {
        foreach (var assembly in rolesAssemblies)
        {
            action(assembly.AsType());
        }
    }

    public static void ForEachRole(Action<Type> action)
    {
        foreach (var assembly in RoleAssemblies)
        {
            action(assembly.AsType());
        }
    }
}

#endregion

#region Move
public class Moves
{
    List<Move> moves;

    public Moves()
    {
        moves = new List<Move>();
    }

    public void Add(int nb, int from, int to)
    {
        moves.Add(new Move(nb, from, to));
    }

    public void Refresh()
    {
        moves.Clear();
    }

    public void Print()
    {
        var s = String.Concat(moves);
        Console.WriteLine(s);
    }
}

public class Move
{
    public int Nb { get; set; }
    public int From { get; set; }
    public int To { get; set; }

    public Move(int nb, int from, int to)
    {
        Nb = nb;
        From = from;
        To = to;
    }

    public override string ToString()
    {
        return Nb + " " + From + " " + To + " ";
    }
}

#endregion

#region Role

public interface IRole
{
    string Name { get; }

    PrioritizedTargets ComputeTargets(ZonesData zonesData);
    void AttributeTargets(PodsData podsData, PrioritizedTargets targets, Map map);
    void TargetIsReached(Pod p);
}

public class Attacker : IRole
{
    public string Name
    {
        get
        {
            return this.GetType().Name;
        }
    }

    public Attacker()
    {

    }

    public PrioritizedTargets ComputeTargets(ZonesData zonesData)
    {
        var prioAttackZones = new PrioritizedTargets();
        foreach (var zone in zonesData.EnemyZones)
        {
            if (zone == zonesData.EnemyBase)
            {
                prioAttackZones.Add(10, zone);
            }
            else
            {
                if (zone.Platinum > 0)
                    prioAttackZones.Add(zone.Platinum, zone);
            }
        }
        prioAttackZones.Sort();
        Debug.Print(prioAttackZones.ToString());
        return prioAttackZones;
    }

    public void AttributeTargets(PodsData podsData, PrioritizedTargets targets, Map map)
    {
        var target = targets.Collection.ElementAt(0).Key;
        var offset = 0;
        var attackers = podsData.GetPodCollectionWithRole(typeof(Attacker)).ToList();
        var nbTotalAttackers = attackers.Count;
        Debug.Print("total attackers : " + attackers.Count);
        targets.ForEachZone(zone =>
        {
            var nbPodsToAssign = (int)Math.Round(targets.GetPercent(zone) * nbTotalAttackers);
            if (offset + nbPodsToAssign < nbTotalAttackers)
            {
                attackers.GetRange(offset, nbPodsToAssign).ForEach(pod =>
                {
                    pod.AssignTarget(target);
                });
                offset += nbPodsToAssign;
                if (nbPodsToAssign == 0)
                {
                    attackers.GetRange(offset, 1).ForEach(pod =>
                    {
                        pod.AssignTarget(target);
                    });
                    offset += 1;
                }
                Debug.Print("[" + zone.Id + "] " + nbPodsToAssign + "         new offset : " + offset);
            }
        });
        /*
        foreach (var pod in podsData.GetPodCollectionWithRole(typeof(Attacker)))
        {
            if (pod.IsWaiting)
            {
                pod.Target = new Target(pod.Position, target);
                Debug.Print("Attribute attacker pod : "+pod.Position.Id+" target : " + targets.Collection.ElementAt(0).Key.Id);
            }

        }
        */
    }

    public void TargetIsReached(Pod p)
    {
        p.RemoveTarget();
    }

}

public class Defender : IRole
{
    public string Name
    {
        get
        {
            return this.GetType().Name;
        }
    }

    public Defender()
    {

    }

    public void AttributeTargets(PodsData podsData, PrioritizedTargets targets, Map map)
    {

    }

    public PrioritizedTargets ComputeTargets(ZonesData zonesData)
    {
        return null;
    }

    public void TargetIsReached(Pod p)
    {

    }
}
public class Explorer : IRole
{
    public string Name
    {
        get
        {
            return this.GetType().Name;
        }
    }

    public Explorer()
    {

    }

    Stack<Zone> GetPathToClosestNeutralZone(Zone source, Map map)
    {
        var path = map.GetPath(source, z =>
        {
            return z.IsNeutral && z.AssignedMissions.Count == 0;
        });

        return path;
    }

    public void AttributeTargets(PodsData podsData, PrioritizedTargets targets, Map map)
    {
        if (targets == null)
            return;
        var pods = podsData.GetPodCollectionWithRole(typeof(Explorer));
        var waiters = pods.Where(p => p.IsWaiting).ToList();
        pods.ForEach(pod =>
        {
            /*
            if (pod.IsWaiting)
            {
                if (targets.Collection.Count == 0)
                {
                    pod.Draft();
                }
                else
                {
                    //here proxy
                    var bestPlatZone = MaxPlatInNeighbors(pod.Position.Neighbors);
                    if (bestPlatZone != null)
                    {
                        Debug.Print("Proxy : " + pod.Position.Id + "  ->   " + bestPlatZone.Id);
                        pod.AssignTarget(bestPlatZone);
                        targets.Remove(pod.Target.Dest);
                    }
                    else
                    {
                        var pathFound = GetPathToClosestNeutralZone(pod.Position, map);
                        if (pathFound != null)
                        {
                            pod.AssignTarget(pathFound);
                            targets.Remove(pod.Target.Dest);
                        }
                        else
                        {
                            Debug.Print("[Error] GetPathToClosestNeutralZone returned null for " + pod.Position.Id);
                        }
                    }
                }
            }
            */
            if (targets.Collection.Count == 0 && pod.IsWaiting)
            {
                pod.Draft();
            }
            else
            {
                var bestPlatZone = MaxPlatInNeighbors(pod.Position.Neighbors);
                if (bestPlatZone != null)
                {
                    if (!pod.IsWaiting)
                    {
                        pod.RemoveTarget();
                    }
                    pod.AssignTarget(bestPlatZone);
                }
                else
                {
                    if (!pod.IsWaiting)
                    {
                        var destAssignedMission = pod.Target.Dest.AssignedMissions;
                        if (destAssignedMission.Count > 1)
                        {
                            FindAndAssignNextNeutralZone(targets, pod, map);
                        }
                    }
                    else
                    {
                        FindAndAssignNextNeutralZone(targets, pod, map);
                    }
                }
            }
        });
    }

    void FindAndAssignNextNeutralZone(PrioritizedTargets targets, Pod pod, Map map)
    {
        var pathFound = GetPathToClosestNeutralZone(pod.Position, map);
        if (pathFound != null)
        {
            pod.AssignTarget(pathFound);
            targets.Remove(pod.Target.Dest);
        }
        else
        {
            Debug.Print("[Error] GetPathToClosestNeutralZone returned null for " + pod.Position.Id);
        }
    }

    Zone MaxPlatInNeighbors(HashSet<Zone> neighbors)
    {
        var maxPlat = 0;
        Zone zMax = null;
        foreach (var neighbor in neighbors)
        {
            if (neighbor.Platinum > maxPlat && neighbor.IsNeutral && neighbor.AssignedMissions.Count == 0)
            {
                maxPlat = neighbor.Platinum;
                zMax = neighbor;
            }
        }
        return zMax;
    }

    public PrioritizedTargets ComputeTargets(ZonesData zonesData)
    {
        var unassignedNeutralZones = zonesData.NeutralZones.Where(z => z.AssignedMissions.Count == 0).ToList();
        return new PrioritizedTargets(unassignedNeutralZones);
    }

    public void TargetIsReached(Pod p)
    {
        p.RemoveTarget();
    }
}


#endregion

#region Prioritized Target

public class PrioritizedTargets
{
    Dictionary<Zone, int> pTargets;
    int totalPriority;

    public PrioritizedTargets()
    {
        pTargets = new Dictionary<Zone, int>();
        totalPriority = 0;
    }

    public PrioritizedTargets(List<Zone> listZones)
    {
        pTargets = new Dictionary<Zone, int>();
        foreach (var z in listZones)
        {
            Add(1, z);
        }
        totalPriority = listZones.Count;
    }

    public Dictionary<Zone, int> Collection { get { return pTargets; } }

    public void Add(int priority, Zone zone)
    {
        if (pTargets.Keys.Contains(zone))
        {
            pTargets[zone] += priority;
        }
        else
        {
            pTargets.Add(zone, priority);
        }
        totalPriority += priority;
    }

    public bool Remove(Zone zone)
    {
        return pTargets.Remove(zone);
    }

    public void Sort()
    {
        var list = pTargets.ToList();
        list.Sort((x, y) => y.Value.CompareTo(x.Value));
        pTargets = list.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public float GetPercent(Zone key)
    {
        return (float)(Collection[key] / (float)totalPriority);
    }

    public void ForEach(Action<KeyValuePair<Zone, int>> action)
    {
        foreach (var kv in Collection)
        {
            action(kv);
        }
    }

    public void ForEachZone(Action<Zone> action)
    {
        foreach (var zone in Collection.Keys)
        {
            action(zone);
        }
    }

    public string ToString()
    {
        var output = "";
        foreach (var kv in pTargets)
        {
            output += "[" + kv.Key.Id + "] " + kv.Value + " (" + GetPercent(kv.Key) + ") | ";
        }
        return output;
    }
}

#endregion

#region Ratios
public class Ratios
{
    //ratios are percents
    private Dictionary<Type, float> _rolesRatios;

    public Ratios()
    {
        _rolesRatios = new Dictionary<Type, float>();
        Reflection.ForEachRole(role =>
        {
            _rolesRatios.Add(role, 0f);
        });
    }

    public void ComputeRatios(int nbPods, int nbZonesTotal, int nbZonesOwned, int nbNeutralZones, int nbZonesToDefend)
    {
        var rExplorers = (float)nbNeutralZones / (float)nbZonesTotal;
        var rAttackers = (1.0f - rExplorers) * 0.9f;
        var rDefenders = rAttackers * 0.1f;
        //explorers = zNeutres/zTotal
        //attackers = (nbPods - explorers) * 0.9
        //defenders = nbPods - explorers - attackers
        //_rolesRatios[typeof(Explorer)] = 0.7f;
        //_rolesRatios[typeof(Attacker)] = 0.3f;
        _rolesRatios[typeof(Explorer)] = rExplorers;
        _rolesRatios[typeof(Attacker)] = rAttackers;
        _rolesRatios[typeof(Defender)] = rDefenders;
    }

    public Type GetNextSpawn(PodsData pods)
    {
        var totalPods = pods.OwnPods.Count;
        var iRoleAssemblies = Reflection.RoleAssemblies; // todo
        float maxRatio = 0f;
        Type maxType = null;
        foreach (var iRole in iRoleAssemblies)
        {
            var roleType = iRole.AsType();

            var currentRatio = pods.GetPodCollectionWithRole(roleType).Count / (float)totalPods;
            var goalRatio = _rolesRatios[roleType];

            var prioritizeSpawn = goalRatio / currentRatio;

            //Debug.Print("["+roleType.Name+"] Current Ratio : " + currentRatio + " | Goal : " + goalRatio + " | Prioritized : "+ prioritizeSpawn);

            if (prioritizeSpawn > maxRatio)
            {
                maxRatio = prioritizeSpawn;
                maxType = roleType;
            }
        }
        //Debug.Print("_");

        return maxType;
    }
}

#endregion

#region Target

public class Target
{
    private Zone _source;
    private Zone _dest;

    public Stack<Zone> Path { get; set; }
    public Zone Dest { get { return _dest; } }
    public Zone Source { get { return _source; } }

    public Target(Zone source, Zone dest)
    {
        _source = source;
        _dest = dest;
        this.Path = null;
    }

    public Target(Stack<Zone> Path)
    {
        this.Path = Path;
        _source = Path.Peek();
        _dest = Path.Last();
    }

    public Zone NextMove
    {
        get
        {
            if (Path == null || Path.Count == 0)
                return null;
            else
                return Path.Peek();
        }
    }

    public bool IsReached { get { return Path.Count == 0; } }
    public bool IsInitialized
    {
        get
        {
            return Path != null;
        }
    }
    public void Initialize(Stack<Zone> Path)
    {
        this.Path = Path;
    }

}

#endregion

#region pod

public class Pod
{
    private Target _target;

    public IRole Role { get; set; }
    public Zone Position { get; set; }

    public Pod(Zone pos)
    {
        this.Position = pos;
        this.Target = null;
    }

    public bool IsDefender
    {
        get
        {
            return HasRole && Role.GetType() == typeof(Defender);
        }
    }
    public bool IsExplorer
    {
        get
        {
            return HasRole && Role.GetType() == typeof(Explorer);
        }
    }
    public bool IsAttacker
    {
        get
        {
            return HasRole && Role.GetType() == typeof(Attacker);
        }
    }

    public bool IsWaiting
    {
        get
        {
            return Target == null;
        }
    }

    public bool HasRole
    {
        get
        {
            return Role != null;
        }
    }

    public Target Target
    {
        get
        {
            return _target;
        }
        private set
        {
            _target = value;
            if (value != null)
                _target.Dest.AssignMission(this);
        }
    }

    public void AssignTarget(Zone target)
    {
        Target = new Target(Position, target);
    }

    public void AssignTarget(Stack<Zone> targetPath)
    {
        Target = new Target(targetPath);
    }

    public void RemoveTarget()
    {
        Target.Dest.UnassignMission(this);
        Target = null;
    }

    public Zone NextMove
    {
        get
        {
            if (Target != null)
                return Target.NextMove;
            else
            {
                Console.Error.WriteLine("[Error] getter nextmove : null exception");
                return null;
            }
        }
    }

    public void Walk()
    {
        if (Target == null)
        {
            Console.Error.WriteLine("[" + Position + "] Can't walk : no target");
            return;
        }
        var goTo = Target.Path.Pop();
        Position.LeavePod(this);
        goTo.ReceivePod(this);
        this.Position = goTo;
        if (Target.IsReached)//TODO check
        {
            Role.TargetIsReached(this);
        }
    }

    public void Draft()
    {
        this.Role = null;
        if (Target != null)
        {
            Target.Dest.UnassignMission(this);
            this.Target = null;
        }
    }
}

#endregion

#region PodCollection

public class PodCollection : Collection<Pod>
{
    public Type CollectionRole { get; private set; }

    public bool IsSpecialized
    {
        get
        {
            return CollectionRole != null;
        }
    }

    public PodCollection()
    {
        CollectionRole = null;
    }

    public PodCollection(Type role)
    {
        this.CollectionRole = role;
    }

    protected override void InsertItem(int index, Pod item)
    {
        if (CollectionRole == null || item.Role.GetType() == CollectionRole)
            base.InsertItem(index, item);
        else
            Debug.Print("[Error] Can't add pod with role " + item.Role.GetType().ToString() + " to pod collection of " + CollectionRole.ToString());
    }

    public void ForEach(Action<Pod> action)
    {
        foreach (var p in this)
        {
            action(p);
        }
    }

}

public class SpecializedPodsCollection : Collection<PodCollection>
{
    public SpecializedPodsCollection()
    {

    }

    public void AddPod(Pod p)
    {
        var added = false;
        foreach (var col in this)
        {
            if (!p.HasRole)
            {
                if (col.CollectionRole == null)
                {
                    col.Add(p);
                    added = true;
                }
            }
            else if (col.CollectionRole == p.Role.GetType())
            {
                col.Add(p);
                added = true;
            }
        }
        if (!added)
            Debug.Print("[Error] Pod not added in SpecializedPodsCollection : " + p.Role.GetType().Name);
    }

    public void RemovePod(Pod p)
    {
        this.ForEach(col =>
        {
            if (col.Remove(p))
                return;
        });
        Debug.Print("[Error] Pod not removed in SpecializedPodsCollection : " + p.Role.GetType().Name);
    }

    public void RemovePodFromCollectionWithRole(Pod p, Type role)
    {
        if (!GetCollectionWithRole(role).Remove(p))
        {
            Debug.Print("[Error] Pod not removed in SpecializedPodsCollection : " + p.Role.GetType().Name);
        }
    }

    public void ClearAll()
    {
        this.ForEach(col => col.Clear());
    }

    public void ClearColectionWithRole(Type role)
    {
        GetCollectionWithRole(role).Clear();
    }

    public PodCollection GetCollectionWithRole(Type role)
    {
        foreach (var podCollection in this)
        {
            if (podCollection.CollectionRole == role)
                return podCollection;
        }
        return null;
    }

    public void ForEach(Action<PodCollection> action)
    {
        foreach (var col in this)
        {
            action(col);
        }
    }
}

#endregion

#region pods infos

public class PodsData
{
    public PodCollection OwnPods { get; set; }
    public PodCollection EnemyPods { get; set; }

    public SpecializedPodsCollection SpecializedPods { get; set; }

    public PodsData()
    {
        OwnPods = new PodCollection();
        EnemyPods = new PodCollection();

        SpecializedPods = new SpecializedPodsCollection();
        foreach (var iRole in Reflection.RoleAssemblies)
        {
            SpecializedPods.Add(new PodCollection(iRole.AsType()));
        }
        SpecializedPods.Add(new PodCollection()); // without roles
    }

    public PodCollection GetPodCollectionWithRole(Type role)
    {
        foreach (var col in SpecializedPods)
        {
            if (col.CollectionRole == role)
                return col;
        }
        Debug.Print("[Error] Specialized collection not found : " + role.Name);
        return null;
    }

    public void ClearSpecializedPods()
    {
        SpecializedPods.ClearAll();
    }

    public void ClearMainGroups()
    {
        OwnPods.Clear();
        EnemyPods.Clear();
    }

    public void UpdateSubGroups()
    {
        ClearSpecializedPods();
        OwnPods.ForEach(p => SpecializedPods.AddPod(p));
    }

    public void SpawnPod(Zone pos, int nbToSpawn = 1)
    {
        for (int i = 0; i < nbToSpawn; i++)
        {
            var pod = new Pod(pos);
            OwnPods.Add(pod);
            SpecializedPods.AddPod(pod);
            pos.ReceivePod(pod);
        }
    }

    public void KillPod(Pod p)
    {
        if (p == null)
            return;
        if (!OwnPods.Remove(p))
            Console.Error.WriteLine("[Error] can't remove pod at " + p.Position.Id + " from OwnPods");
    }
}

#endregion

#region Map

public class Map
{
    private int _nbOwnZones;

    Zone[] zones;
    public int ZoneCount { get; set; }
    public ZonesData ZonesData { get; set; }

    public Map(int nbZones)
    {
        ZoneCount = nbZones;
        zones = new Zone[nbZones];
        ZonesData = new ZonesData();
    }

    Zone LowestFScoreValue(HashSet<Zone> openSet, int[] fScore)
    {
        if (openSet.Count == 0)
        {
            Console.Error.WriteLine("[Error] Lowest fscrore value calculation : openset is empty");
            return null;
        }
        var minZone = openSet.ElementAt(0);
        var min = fScore[minZone.Id];
        openSet.ToList().ForEach(
            z =>
            {
                if (fScore[z.Id] < min)
                {
                    min = fScore[z.Id];
                    minZone = z;
                }
            });
        return minZone;
    }

    int heuristicCostEstimate(Zone current)
    {
        return ZoneCount / 2;
    }

    Stack<Zone> AStar(Zone source, Func<Zone, bool> isGoal)
    {
        //INIT
        var closedSet = new HashSet<Zone>();
        var openSet = new HashSet<Zone>();
        openSet.Add(source);
        var cameFrom = new Zone[ZoneCount];
        int[] gScore = new int[ZoneCount];
        int[] fScore = new int[ZoneCount];
        for (int i = 0; i < ZoneCount; i++)
        {
            gScore[i] = -1;
            fScore[i] = -1;
            cameFrom[i] = null;
        }
        gScore[source.Id] = 0;
        fScore[source.Id] = ZoneCount / 2; // heuristic cost estimated

        //MAIN
        while (openSet.Count > 0)
        {
            var current = LowestFScoreValue(openSet, fScore);
            if (isGoal(current))
            {
                return reconstructPath(cameFrom, current);
            }

            openSet.Remove(current);
            closedSet.Add(current);
            foreach (var neighbor in current.Neighbors)
            {
                if (closedSet.Contains(neighbor))
                    continue;
                var tentative_gScore = gScore[current.Id] + 1;//1 = distance neighbors
                if (!openSet.Contains(neighbor))
                    openSet.Add(neighbor);
                else if (tentative_gScore >= gScore[neighbor.Id])
                    continue;

                cameFrom[neighbor.Id] = current;
                gScore[neighbor.Id] = tentative_gScore;
                fScore[neighbor.Id] = gScore[neighbor.Id] + heuristicCostEstimate(neighbor);
            }
        };
        return null;
    }

    Stack<Zone> reconstructPath(Zone[] cameFrom, Zone current)
    {
        var path = new List<Zone>();
        path.Add(current);
        if (cameFrom[current.Id] == null)
            return new Stack<Zone>(path);
        while (cameFrom[cameFrom[current.Id].Id] != null)
        {
            current = cameFrom[current.Id];
            path.Add(current);
        }

        return new Stack<Zone>(path);
    }

    int minPosList(int[] weights, List<int> list)
    {
        int min = -1;
        int indice = 0;
        foreach (int i in list)
        {
            if ((weights[i] < min && weights[i] >= 0) || min == -1)
            {
                min = weights[i];
                indice = i;
            }
        }
        return indice;
    }

    Stack<Zone> dijkstra(int source, int target)
    {
        //INIT
        int[] prev = new int[ZoneCount];
        int[] weights = new int[ZoneCount];
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = -1;
            prev[i] = -1;
        }
        weights[source] = 0;
        //MAIN
        List<int> pasVu = new List<int>();
        for (int i = 0; i < zones.Length; i++)
        {
            pasVu.Add(zones[i].Id);
        }
        while (pasVu.Any())
        {
            int x = minPosList(weights, pasVu);
            pasVu.Remove(x);
            foreach (Zone z in zones[x].Neighbors)
            {
                //Console.Error.WriteLine(x);
                if (weights[z.Id] < 0 || weights[z.Id] > weights[x] + 1)
                {
                    weights[z.Id] = weights[x] + 1;
                    prev[z.Id] = x;
                }
            }
        }
        Stack<Zone> path = new Stack<Zone>();
        int y = target;
        while (y != source)
        {
            path.Push(zones[y]);
            y = prev[y];
        }

        return path;
    }

    public Stack<Zone> GetPath(int from, int to)
    {
        return AStar(zones[from], z => z.Id == to);
    }

    public Stack<Zone> GetPath(Zone from, Zone to)
    {
        return AStar(from, z => z == to);
    }

    public Stack<Zone> GetPath(Zone from, Func<Zone, bool> isGoal)
    {
        return AStar(from, isGoal);
    }

    public Zone ZoneFromId(int id)
    {
        return zones[id];
    }

    public void InitZone(int zId, int plat)
    {
        zones[zId] = new Zone(zId, plat);
    }

    public void AddLink(int z1, int z2)
    {
        zones[z1].AddNeighbor(zones[z2]);
        zones[z2].AddNeighbor(zones[z1]);
    }

    public void UpdateZone(int zId, int owner, int[] playersPods, bool visible, int plat)
    {
        var currentZone = zones[zId];
        currentZone.Update(owner, playersPods, visible, plat);

        //check own
        if (currentZone.IsOwned && !ZonesData.OwnZones.Contains(currentZone))
        {
            ZonesData.OwnZones.Add(currentZone);
        }
        else if (!currentZone.IsOwned && ZonesData.OwnZones.Contains(currentZone))
        {
            ZonesData.OwnZones.Remove(currentZone);
        }

        //check enemy
        if (currentZone.IsEnemy && !ZonesData.EnemyZones.Contains(currentZone))
        {
            //Debug.Print("adding enemy zone in base: " + currentZone.Id);
            ZonesData.EnemyZones.Add(currentZone);
        }
        else if (!currentZone.IsEnemy && ZonesData.EnemyZones.Contains(currentZone))
        {
            //Debug.Print("removing enemy zone in base: " + currentZone.Id);
            ZonesData.EnemyZones.Remove(currentZone);
        }

        //check neutral
        if (currentZone.IsNeutral && !ZonesData.NeutralZones.Contains(currentZone))
        {
            ZonesData.NeutralZones.Add(currentZone);
        }
        else if (!currentZone.IsNeutral && ZonesData.NeutralZones.Contains(currentZone))
        {
            ZonesData.NeutralZones.Remove(currentZone);
        }

        if (currentZone.Platinum > 2 && !ZonesData.PlatZones.Contains(currentZone))
            ZonesData.PlatZones.Add(currentZone);
    }
}

#endregion

#region Zone

public class Zone
{
    public int Id { get; set; }
    public int Platinum { get; set; }
    public HashSet<Zone> Neighbors { get; set; }
    public int OwnerId { get; set; }
    public int[] PlayersPods { get; set; } //pods for each players
    public bool Visible { get; set; }
    public List<Pod> Pods { get; set; }
    public List<Pod> AssignedMissions { get; set; }
    public HashSet<Zone> Walkable { get; set; } //list of zones we can walk from this one (ie we can flee only to neutral and own zones)
    public int NbEnemyPods { get; set; }

    public Dictionary<Zone, Stack<Zone>> PathSaved
    {
        get; set;
    }

    private int _nbPodsToAlter;

    public Zone(int id, int plat)
    {
        this.Id = id;
        this.Platinum = plat;
        Neighbors = new HashSet<Zone>();
        PlayersPods = new int[4] { 0, 0, 0, 0 };
        Pods = new List<Pod>();
        _nbPodsToAlter = 0;
        OwnerId = -1;
        AssignedMissions = new List<Pod>();
        PathSaved = new Dictionary<Zone, Stack<Zone>>();
    }

    public string ZoneToString()
    {
        return "id " + Id +
            "platinum " + Platinum +
            "neighbors " + HashToString(Neighbors) +
            "ownerId " + OwnerId +
            "pods :  " + PlayersPods.ToString();
    }
    public string HashToString(HashSet<Zone> h)
    {
        string s = "[";
        foreach (Zone i in h)
        {
            s += i.Id + " ";
        }
        return s + "]";
    }

    public void AddNeighbor(Zone n)
    {
        if (!Neighbors.Contains(n))
            Neighbors.Add(n);
    }

    int ComputeEnemyPods()
    {
        var nbEnemyPods = 0;
        for (int i = 0; i < PlayersPods.Length; i++)
        {
            if (PlayersPods[i] > 0 && i != Game.MY_ID)
                nbEnemyPods += PlayersPods[i];
        }
        return nbEnemyPods;
    }

    void ComputeWalkable()
    {
        if (NbEnemyPods > 0)
        {
            Walkable = new HashSet<Zone>();
            foreach (var neighbor in Neighbors)
            {
                if (neighbor.IsNeutral)
                    Walkable.Add(neighbor);
            }
        }
        else
            Walkable = Neighbors;
    }

    public void Update(int ownerId, int[] playersPods, bool visible, int plat)
    {
        if (visible)
        {
            OwnerId = ownerId;
        }
        PlayersPods = playersPods;
        Visible = visible;
        var newPlat = plat;
        if (newPlat > Platinum)
        {
            Platinum = newPlat;
            Debug.Print(Id + " PLAT UPDATE " + newPlat);
        }

        _nbPodsToAlter = PlayersPods[Game.MY_ID] - Pods.Count;

        IsOwned = OwnerId == Game.MY_ID;
        IsEnemy = OwnerId != Game.MY_ID && OwnerId != -1;
        IsNeutral = OwnerId == -1;
        NbEnemyPods = ComputeEnemyPods();
        ComputeWalkable();
        if (Id == 110)
            Debug.Print(Id + " STATE : plat " + Platinum + " | Visible " + Visible + " | Assigned mission " + assignToString());
    }

    string assignToString()
    {
        var s = "";
        foreach (var pod in AssignedMissions)
            s += pod.Position.Id + ",";
        return s;
    }

    public int NbPodsToAlter
    {
        get { return _nbPodsToAlter; }
    }

    public bool IsOwned
    {
        get; set;
    }

    public bool IsEnemy
    {
        get; set;
    }

    public bool IsNeutral
    {
        get; set;
    }
    public void ReceivePod(Pod pod)
    {
        Pods.Add(pod);
    }
    public void LeavePod(Pod pod)
    {
        Pods.Remove(pod);
    }

    public void AssignMission(Pod p)
    {
        AssignedMissions.Add(p);
    }
    public void UnassignMission(Pod p)
    {
        if (p == null)
            return;
        if (AssignedMissions.Contains(p))
            AssignedMissions.Remove(p);
        else
            Console.Error.WriteLine("[Error] Can't remove mission for pod : " + p.Position.Id);
    }

}

#endregion

#region zones infos

public class ZonesData
{
    public List<Zone> OwnZones { get; set; }
    public List<Zone> EnemyZones { get; set; }

    public List<Zone> PlatZones { get; set; }
    public List<Zone> NeutralZones { get; set; }

    public Zone OwnBase { get; set; }
    public Zone EnemyBase { get; set; }

    public ZonesData()
    {
        OwnZones = new List<Zone>();
        EnemyZones = new List<Zone>();
        PlatZones = new List<Zone>();
        NeutralZones = new List<Zone>();
    }

    public void DeduceBases()
    {
        OwnBase = OwnZones.FirstOrDefault();
        EnemyBase = EnemyZones.FirstOrDefault();
        Debug.Print("Own base : " + OwnBase.Id + " | Enemy base : " + EnemyBase.Id);
    }

}

#endregion

#region Game

public class Game
{
    public static int MY_ID;
    public static int NB_PLAYERS;

    Moves moves;
    List<IRole> roles;

    int ZoneCount;
    Map map;

    PodsData podsData;

    Ratios ratios;

    public Game(int myId, int playerCount, int zoneCount)
    {
        MY_ID = myId;
        NB_PLAYERS = playerCount;
        ZoneCount = zoneCount;
        map = new Map(zoneCount);
        podsData = new PodsData();
        ratios = new Ratios();
        moves = new Moves();
        roles = new List<IRole>() { new Attacker(), new Defender(), new Explorer() };
    }

    public void InitZone(int zId, int plat)
    {
        map.InitZone(zId, plat);
    }
    public void AddLink(int z1, int z2)
    {
        map.AddLink(z1, z2);
    }

    public void Refresh()
    {
        moves.Refresh();
    }

    public void UpdateZone(int id, int owner, int[] playersPods, bool visible, int plat)
    {
        map.UpdateZone(id, owner, playersPods, visible, plat);
        var currentZone = map.ZoneFromId(id);
        SpawnZonePods(currentZone);
        KillZonePods(currentZone);
    }
    public void SpawnZonePods(Zone z)
    {
        if (z.NbPodsToAlter > 0)
        {
            podsData.SpawnPod(z, z.NbPodsToAlter);
        }
    }

    public void KillZonePods(Zone z)
    {
        if (z.NbPodsToAlter < 0)
        {
            for (int i = 0; i < Math.Abs(z.NbPodsToAlter); i++)
            {
                var podToKill = z.Pods.ElementAt(0);
                if (podToKill.Target != null)
                    podToKill.Target.Dest.UnassignMission(podToKill);
                podsData.KillPod(podToKill);
                z.Pods.Remove(podToKill);
            }
        }
    }

    public void AttributeRoles()
    {
        var toRemove = new List<Pod>();
        foreach (var pod in podsData.GetPodCollectionWithRole(null))
        {
            var nextRole = ratios.GetNextSpawn(podsData);
            pod.Role = (IRole)nextRole.Construct();
            podsData.SpecializedPods.AddPod(pod);
        }
        podsData.SpecializedPods.ClearColectionWithRole(null);
    }

    public void MovePods()
    {
        foreach (var pod in podsData.OwnPods)
        {
            if (pod.Target != null)
            {
                if (!pod.Target.IsInitialized)
                {
                    var path = map.GetPath(pod.Target.Source.Id, pod.Target.Dest.Id);
                    pod.Target.Initialize(path);
                }
                if (pod.Position.Walkable.Contains(pod.NextMove))
                {
                    moves.Add(1, pod.Position.Id, pod.NextMove.Id);
                    pod.Walk();
                }
            }
        }
    }
    public void Run()
    {
        podsData.UpdateSubGroups();
        //attribution des roles
        ratios.ComputeRatios(podsData.OwnPods.Count, map.ZoneCount, map.ZonesData.OwnZones.Count, map.ZonesData.NeutralZones.Count, 0);
        AttributeRoles();
        foreach (var role in roles)
        {
            //calcul des cibles
            var targets = role.ComputeTargets(map.ZonesData);
            //assignations des missions
            role.AttributeTargets(podsData, targets, map);
        }
        //deplacement des pods
        MovePods();
        moves.Print();
    }
    public void DeduceBases()
    {
        map.ZonesData.DeduceBases();
    }
}

#endregion

#region main

class Player
{
    static Game game;

    static void Main(string[] args)
    {
        int turn = 0;
        string[] inputs;
        inputs = Console.ReadLine().Split(' ');
        var playerCount = int.Parse(inputs[0]); // the amount of players (2 to 4)
        var myId = int.Parse(inputs[1]); // my player ID (0, 1, 2 or 3)
        var zoneCount = int.Parse(inputs[2]); // the amount of zones on the map
        var linkCount = int.Parse(inputs[3]); // the amount of links between all zones
        game = new Game(myId, playerCount, zoneCount);
        for (int i = 0; i < zoneCount; i++)
        {
            inputs = Console.ReadLine().Split(' ');
            int zoneId = int.Parse(inputs[0]); // this zone's ID (between 0 and zoneCount-1)
            int platinumSource = int.Parse(inputs[1]); // the amount of Platinum this zone can provide per game turn
            game.InitZone(zoneId, platinumSource);
        }
        for (int i = 0; i < linkCount; i++)
        {
            inputs = Console.ReadLine().Split(' ');
            int zone1 = int.Parse(inputs[0]);
            int zone2 = int.Parse(inputs[1]);
            game.AddLink(zone1, zone2);
        }

        // game loop
        while (true)
        {
            game.Refresh();

            int platinumUNUSED = int.Parse(Console.ReadLine()); // my available Platinum
            for (int i = 0; i < zoneCount; i++)
            {
                inputs = Console.ReadLine().Split(' ');
                int zId = int.Parse(inputs[0]); // this zone's ID
                int ownerId = int.Parse(inputs[1]);
                int pods0 = int.Parse(inputs[2]);
                int pods1 = int.Parse(inputs[3]);
                bool visible = int.Parse(inputs[4]) == 1;
                int platZone = int.Parse(inputs[5]);

                game.UpdateZone(zId, ownerId, new int[] { pods0, pods1 }, visible, platZone);

            }

            if (turn == 0)
            {
                game.DeduceBases();
            }

            game.Run();

            Console.WriteLine("WAIT");
            turn++;
        }
    }

}
#endregion