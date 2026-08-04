using FortOS.Core;

namespace FortOS.ServiceBus.Supervisor;

internal static class TopologySorter
{
    /// <summary>
    /// Groups services into start/stop levels via Kahn's algorithm: a level contains every service
    /// whose dependencies have all been scheduled in earlier levels. The result is a valid partial
    /// order that respects every declared "DependsOn" edge.
    /// <list type="bullet">
    /// <item>indegree = number of still-unscheduled dependencies of each service;</item>
    /// <item>ready = services with no remaining dependencies (sorted for deterministic output);</item>
    /// <item>a full pass that schedules fewer services than exist proves a dependency cycle.</item>
    /// </list>
    /// </summary>
    /// <exception cref="CircularDependencyException">When the dependency graph contains a cycle.</exception>
    internal static IReadOnlyList<IReadOnlyList<ServiceDefinition>> SortLevels(IEnumerable<ServiceDefinition> services)
    {
        var byId = services.ToDictionary(s => s.ServiceId, StringComparer.Ordinal);
        // Count dependencies (edges) that point at known services; unknown ids are ignored so a
        // missing dependency cannot deadlock the whole topology.
        var indegree = byId.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var dependents = byId.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var service in byId.Values)
        {
            foreach (var dependency in service.DependsOn.Distinct(StringComparer.Ordinal))
            {
                if (!byId.ContainsKey(dependency))
                {
                    continue;
                }

                indegree[service.ServiceId]++;
                dependents[dependency].Add(service.ServiceId);
            }
        }

        var ready = new SortedSet<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key), StringComparer.Ordinal);
        var levels = new List<IReadOnlyList<ServiceDefinition>>();
        var visited = 0;
        while (ready.Count > 0)
        {
            var currentIds = ready.ToArray();
            ready.Clear();
            levels.Add(currentIds.Select(id => byId[id]).ToArray());
            visited += currentIds.Length;

            foreach (var id in currentIds)
            {
                foreach (var dependent in dependents[id])
                {
                    indegree[dependent]--;
                    if (indegree[dependent] == 0)
                    {
                        ready.Add(dependent);
                    }
                }
            }
        }

        if (visited != byId.Count)
        {
            var remaining = indegree.Where(pair => pair.Value > 0).Select(pair => pair.Key).Order(StringComparer.Ordinal);
            throw new CircularDependencyException($"Service dependencies contain a cycle: {string.Join(" -> ", remaining)}");
        }

        return levels;
    }
}
