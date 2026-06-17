using Avalonia.Media.TextFormatting.Unicode;
using AvaloniaGraphControl;
using Microsoft.Msagl.Drawing;
using Neo4j.Driver;
using projekt_bazy.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace projekt_bazy.Services;

public class IntersectionDto
{
    public List<string> Label { get; set; } = new();
    public string Name { get; set; } = "";
}

public class RoadDto
{
    public string Label { get; set; } = "";
    public string Name { get; set; } = "";
    public double Length { get; set; } //edge's length (used in calculating the edge's weight)
    public double MaxSpeed { get; set; } //edge's speed limit
    public int Priority {  get; set; } //edge's priority

    public string From { get; set; } = "";
    public string To { get; set; } = "";
}


public class Neo4jService : IAsyncDisposable
{
    private readonly IDriver _driver;
    private List<string> finalEdgeList;
     public Neo4jService()
    {
        _driver = GraphDatabase.Driver(
            "bolt://localhost:7687",
            AuthTokens.Basic("neo4j", "password"));
    }

    private async Task RecreateGdsGraphAsync()
    {
        await using var session = _driver.AsyncSession();

        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("""
            CALL gds.graph.drop('roadsGraph', false)
            YIELD graphName
            RETURN graphName
            """);
        });

        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("""
            CALL gds.graph.project(
              'roadsGraph',
              '*',
              {
                ONE_WAY: {
                  type: 'ONE_WAY',
                  orientation: 'NATURAL',
                  properties: 'weight'
                },
                TWO_WAY: {
                  type: 'TWO_WAY',
                  orientation: 'UNDIRECTED',
                  properties: 'weight'
                }
              }
            )
            """);
        });
    }

    private async Task SetupGraph(List<string> nodes, string graphId, string start)
    {
        nodes.Insert(0, start);
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx => {
            string query = """"
            UNWIND range(0, size($points) - 2) AS i

            MATCH (source {name: $points[i], graph_name: $graphId})
            MATCH (target {name: $points[i + 1], graph_name: $graphId})

            CALL gds.shortestPath.dijkstra.stream('roadsGraph', {
              sourceNode: source,
              targetNode: target,
              relationshipWeightProperty: 'weight'
            })
            YIELD totalCost, path

            WITH [node IN nodes(path) | node.name] AS nodeNames
            CALL (nodeNames){
            UNWIND CASE
                WHEN size(nodeNames) >= 2 THEN range(0, size(nodeNames) - 2)
                ELSE []
            END AS i
            MATCH (a {graph_name: $graphId, name:nodeNames[i]})-[realEdge {graph_name: $graphId}]-(b {graph_name: $graphId, name:nodeNames[i+1]})
            WHERE (type(realEdge)="ONE_WAY" AND startNode(realEdge)=a AND endNode(realEdge)=b)
            OR type(realEdge)="TWO_WAY"
            RETURN collect(realEdge.name) AS Name
            }
            UNWIND Name AS j
            MATCH ()-[r {name: j, graph_name: $graphId}]-()
            SET r.addon = 0.6, r.weight = coalesce(r.weight,0) * 0.6;
            """";
            await tx.RunAsync(query, new { points = nodes, graphId = graphId });
        });

    }

    private async Task<List<IRecord>?> GetEdges(List<int> priorities,string NodeName, string graphId)
    {
        await using var session = _driver.AsyncSession();

        var result = await session.RunAsync("""
        MATCH (n1 {graph_name: $graph})-[r:TWO_WAY]->(n {name: $node, graph_name: $graph})
        WHERE (r.priority IN $prios OR r.addon = 0.6) AND r.visited = 0
        RETURN r.name AS name, r.weight as weight, r.visited as penalty, r.addon as scalar, n1.name as next
        UNION
        MATCH (n {name: $node, graph_name: $graph})-[r]->(n1 {graph_name: $graph})
        WHERE (r.priority IN $prios OR r.addon = 0.6) AND r.visited = 0
        RETURN r.name AS name, r.weight as weight, r.visited as penalty, r.addon as scalar, n1.name as next
        """,
            new
            {
                graph = graphId,
                node = NodeName,
                prios = priorities
            });

        var records = await result.ToListAsync();
        if(records.Count == 0)
        {
            return null;
        }

        return records;

    }

    private static List<string>? ChooseEdge(List<IRecord>? availableEdges)
    {
        IRecord? chosen = null;
        if (availableEdges == null)
        {
            return null;
        }
        double smallestWeight = double.PositiveInfinity;
        foreach (var edge in availableEdges)
        {
            if (edge["penalty"].As<int>() > 0)
            {
                continue;
            }
            double actualWeight = edge["weight"].As<double>()* edge["scalar"].As<double>()+edge["penalty"].As<int>();
            if(chosen == null || chosen != null && actualWeight < smallestWeight)
            {
                chosen = edge;
                smallestWeight = actualWeight;
            }           
        }
        if(chosen == null)
        {
            return null;
        }
        List<string> ret = new();
        ret.Add(chosen["name"].As<string>());
        ret.Add(chosen["next"].As<string>());
        //[0] wybrana krawêdŸ, [1] nazwa node-a w którym eyl¹dujemy
        return ret;
    }

    private async Task TravelEdge(string EdgeName, string graphId)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx => { 
            string query = """"
                           MATCH ()-[r {graph_name: $graphId, name: $edgename}]->()
                           SET r.visited = coalesce(r.visited, 0) + 1,
                               r.weight = coalesce(r.weight, 0) + 1;
            """";
            await tx.RunAsync(query, new { graphId = graphId, edgename = EdgeName });
        });
        finalEdgeList.Add(EdgeName);
    }



    private async Task<string?> SearchForNearestEdge(string yurNode, string graphId, List<int> priorities)
    {
        await using var session = _driver.AsyncSession();

        var result = await session.RunAsync("""
        MATCH (start {name: $sourceNode, graph_name: $graph})

        MATCH (a {graph_name: $graph})-[targetEdge]-(b {graph_name: $graph})
        WHERE (targetEdge.priority IN $prios OR targetEdge.addon = 0.6) 
          AND targetEdge.visited = 0
          AND type(targetEdge) IN ['TWO_WAY', 'ONE_WAY']

        WITH start, targetEdge, a, b,
        CASE
            WHEN type(targetEdge) = 'ONE_WAY' THEN [startNode(targetEdge)]
            ELSE [a, b]
        END AS possibleTargets

        UNWIND possibleTargets AS target

        CALL gds.shortestPath.dijkstra.stream('roadsGraph', {
            sourceNode: start,
            targetNode: target,
            relationshipWeightProperty: 'weight'
        })
        YIELD totalCost, path

        WITH targetEdge, target, totalCost, path,
             [node IN nodes(path) | node.name] AS nodeNames

        CALL {
            WITH nodeNames
            UNWIND CASE
                WHEN size(nodeNames) >= 2 THEN range(0, size(nodeNames) - 2)
                ELSE []
            END AS i

            MATCH (x {graph_name: $graph, name: nodeNames[i]})
            MATCH (y {graph_name: $graph, name: nodeNames[i + 1]})
            MATCH (x)-[realEdge]-(y)

            WHERE realEdge.graph_name = $graph
              AND (
                  type(realEdge) = 'TWO_WAY'
                  OR (
                      type(realEdge) = 'ONE_WAY'
                      AND startNode(realEdge) = x
                      AND endNode(realEdge) = y
                  )
              )

            WITH i, realEdge
            ORDER BY i

            RETURN collect(realEdge.name) AS pathEdgeNames
        }

        RETURN
            pathEdgeNames AS edgeNames,
            targetEdge.name AS targetEdgeName,
            type(targetEdge) AS targetEdgeType,
            totalCost AS costToEdge,
            target.name AS reachedEndpoint

        ORDER BY costToEdge
        LIMIT 1
        """,
            new
            {
                graph = graphId,
                sourceNode = yurNode,
                prios = priorities
            });

        var records = await result.ToListAsync();

        if (records.Count == 0)
        {
            return null;
        }

        var record = records[0];

        var edgeNames = record["edgeNames"]
            .As<List<string>>()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var targetEdgeName = record["targetEdgeName"].As<string>();
        var reachedEndpoint = record["reachedEndpoint"].As<string>();

        if (!edgeNames.Contains(targetEdgeName))
        {
            edgeNames.Add(targetEdgeName);
        }

        await session.ExecuteWriteAsync(async tx =>
        {
            string query = """
            UNWIND $traveled_edges AS edgeName
            MATCH ()-[edge]->()
            WHERE edge.graph_name = $graph
              AND edge.name = edgeName
              AND (edge.priority IN $prios OR edge.addon = 0.6)
              AND type(edge) IN ['TWO_WAY', 'ONE_WAY']
            SET edge.visited = coalesce(edge.visited, 0) + 1,
                edge.weight = coalesce(edge.weight, 0) + 1
            """;

            await tx.RunAsync(query, new
            {
                graph = graphId,
                traveled_edges = edgeNames,
                prios = priorities
            });
        });

        finalEdgeList.AddRange(edgeNames);

        result = await session.RunAsync("""
            MATCH (a {graph_name: $graphId})-[r {graph_name: $graphId, name: $edgeName}]-(b {graph_name: $graphId, name: $nodeName})
            RETURN a.name AS Name;
            """, new { graphId = graphId, edgeName = targetEdgeName, nodeName = reachedEndpoint});

        records = await result.ToListAsync();
        record = records[0];

        return record["Name"].As<string>();
    }


    public async Task ReturnGraph(string graphId)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx => {
            string query = """"
            MATCH ()-[r {graph_name: $name}]-()
            SET r.visited = 0,
                r.weight = (r.length/(r.speed/3.6))*(1+0.25*r.priority)
            MATCH (a:IMPORTANT_PLACE {graph_name: $name})
            SET a.visited = false
            """";
            await tx.RunAsync(query, new { name = graphId });
        });
    }

    public async Task<List<string>?> NaiveAlgorithm(string? startPoint, List<int>? priorities, List<string>? importatnt, string? graphId)
    {
        finalEdgeList = new List<string>();
        if (startPoint == null || priorities == null || graphId == null)
        {
            return null;
        }
        string? currentNode = startPoint;
        if (importatnt != null)
        {
            await RecreateGdsGraphAsync();
            await SetupGraph(importatnt, graphId, startPoint);
        }

        List<IRecord>? available;
        List<string>? choice;

        while (true)
        {
            available = await GetEdges(priorities, currentNode, graphId);
            choice = ChooseEdge(available);
            if (choice == null)
            {
                await RecreateGdsGraphAsync();
                currentNode = await SearchForNearestEdge(currentNode, graphId, priorities);
                if (currentNode == null)
                {
                    return finalEdgeList;
                }
            }
            else
            {
                currentNode = choice[1];
                await TravelEdge(choice[0], graphId);
            }
        }
            
    }

    private static string checkSafety(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Etykieta nie mo¿e byæ pusta.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            throw new ArgumentException($"Niepoprawna nazwa etykiety/typu relacji: {name}");

        return ":"+name;
    }
    private static string checkLabelsNode(List<string> labels)
    {
        string ret_val = "";
        foreach (var name in labels)
        {
            ret_val += checkSafety(name);
        }

        return ret_val;
    }

    public async Task<bool> GraphExistsAsync(string graphName)
    {
        await using var session = _driver.AsyncSession();

        var result = await session.RunAsync("""
        MATCH (n {graph_name: $graph_name})
        RETURN count(n) > 0 AS exists
        """,
            new
            {
                graph_name = graphName
            });

        var record = await result.SingleAsync();

        return record["exists"].As<bool>();
    }

    public async Task DeleteGraphAsync(string graphName)
    {
        await using var session = _driver.AsyncSession();

        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("""
            MATCH (n {graph_name: $graph_name})
            DETACH DELETE n
            """,
                new
                {
                    graph_name = graphName
                });
        });
    }

    public async Task ImportDataFromJSON(string path, string cityName)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var nodes = new List<IntersectionDto>();
        var edges = new List<RoadDto>();

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) {  continue; }

            using var doc = JsonDocument.Parse(line);
            string Type = doc.RootElement.GetProperty("type").GetString()!;

            if (Type == "node")
            {
                nodes.Add(JsonSerializer.Deserialize<IntersectionDto>(line, options)!);
            }
            else if (Type == "edge")
            {
                edges.Add(JsonSerializer.Deserialize<RoadDto>(line, options)!);
            }
        }


        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            string query = "";
            foreach (var node in nodes)
            {
                string Safelabels = checkLabelsNode(node.Label);

                if (node.Label.Contains("IMPORTANT_PLACE"))
                {
                    query = $$"""
                    MERGE (i{{Safelabels}} {graph_name: $graph_name, name: $name})
                    SET i.name = $name,i.visited = False
                    """;
                    
                } else
                {
                    query = $$"""
                    MERGE (i{{Safelabels}} {graph_name: $graph_name, name: $name})
                    SET i.name = $name
                    """;
                    
                }
                await tx.RunAsync(query,
                        new
                        {
                            graph_name = cityName,
                            name = node.Name,
                        });
            }

            foreach(var edge in edges)
            {
                string Safelabel = checkSafety(edge.Label);
                query = $$"""
                    MATCH (a {graph_name: $gn, name: $from})
                    MATCH (b {graph_name: $gn, name: $to})
                    MERGE (a)-[r{{Safelabel}} {graph_name: $gn, name: $name}]->(b)
                    SET r.name = $name,
                        r.length = $len, 
                        r.speed = $sped,
                        r.priority = $prior,
                        r.visited = 0,
                        r.addon = 1,
                        r.weight = ($len/($sped/3.6))*(1+0.25*$prior)
                    """;
                await tx.RunAsync(query,
                    new
                    {
                        label = edge.Label,
                        gn = cityName,
                        name = edge.Name,
                        len = edge.Length,
                        sped = edge.MaxSpeed,
                        prior = edge.Priority,
                        from = edge.From,
                        to = edge.To
                    });
            }
        });
    }

    public class GraphVisualItem
    {
        public string Name { get; set; } = "";
        public List<string> Labels { get; set; } = new();

        public string LabelsText => Labels.Count == 0
            ? ""
            : string.Join(", ", Labels);

        public override string ToString()
        {
            return Name;
        }
    }

    public async Task<List<string>> GetStartPointsAsync(string graphName)
    {
        await using var session = _driver.AsyncSession();
        var result = await session.RunAsync("""
            MATCH (a:START {graph_name: $graph_name})
            RETURN a.name AS Name ORDER BY Name;
            """, new { graph_name = graphName });

        var records = await result.ToListAsync();
        
        return records.Select(r => r["Name"].As<string>()).ToList();
    }

    public async Task<List<string>> GetImportantPlacesAsync(string graphName)
    {
        //zastanów siê czy nie ³atwiej by³oby te¿ zwracaæ <id>...
        await using var session = _driver.AsyncSession();
        var result = await session.RunAsync("""
            MATCH (a:IMPORTANT_PLACE {graph_name: $graph_name})
            RETURN a.name AS Name ORDER BY Name;
            """, new { graph_name = graphName });

        var records = await result.ToListAsync();

        return records.Select(r => r["Name"].As<string>()).ToList();
    }

    public async Task<AvaloniaGraphControl.Graph> GetGraphForVisualizationAsync(string graphName, int limit = 25)
    {
        await using var session = _driver.AsyncSession();

        var graph = new AvaloniaGraphControl.Graph
        {
            Orientation = AvaloniaGraphControl.Graph.Orientations.Vertical
        };

        var nodesByName = new Dictionary<string, GraphVisualItem>();

        var result = await session.RunAsync("""
        MATCH (a {graph_name: $graph_name})-[r]->(b {graph_name: $graph_name})
        RETURN a.name AS from,
               b.name AS to,
               r.name AS relName,
               type(r) AS relType,
               labels(a) AS fromLabels,
               labels(b) AS toLabels
        LIMIT $limits
        """,
            new
            {
                graph_name = graphName,
                limits = limit
            });

        var records = await result.ToListAsync();

        foreach (var record in records)
        {
            var fromName = record["from"].As<string>();
            var toName = record["to"].As<string>();

            var fromLabels = record["fromLabels"].As<List<string>>();
            var toLabels = record["toLabels"].As<List<string>>();

            if (!nodesByName.TryGetValue(fromName, out var fromNode))
            {
                fromNode = new GraphVisualItem
                {
                    Name = fromName,
                    Labels = fromLabels
                };

                nodesByName[fromName] = fromNode;
            }

            if (!nodesByName.TryGetValue(toName, out var toNode))
            {
                toNode = new GraphVisualItem
                {
                    Name = toName,
                    Labels = toLabels
                };

                nodesByName[toName] = toNode;
            }

            var relName = record["relName"].As<string?>();
            var relType = record["relType"].As<string>();

            var label = string.IsNullOrWhiteSpace(relName)
                ? relType
                : relName;

            if (relType == "TWO_WAY")
            {
                graph.Edges.Add(new AvaloniaGraphControl.Edge(fromNode, toNode, label, AvaloniaGraphControl.Edge.Symbol.None, AvaloniaGraphControl.Edge.Symbol.None));
            }
            else
            {
                graph.Edges.Add(new AvaloniaGraphControl.Edge(fromNode, toNode, label));
            }
        }

        return graph;
    }

    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync();
    }
}