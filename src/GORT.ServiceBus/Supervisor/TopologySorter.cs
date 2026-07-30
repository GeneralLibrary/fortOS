using GORT.Core;

namespace GORT.ServiceBus.Supervisor;

internal static class TopologySorter
{
    internal static IReadOnlyList<IReadOnlyList<ServiceDefinition>> SortLevels(IEnumerable<ServiceDefinition> services)
    {
        var byId = services.ToDictionary(s => s.ServiceId, StringComparer.Ordinal);
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
