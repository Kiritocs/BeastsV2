using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using AStar;
using AStar.Options;
using ExileCore;
using GameOffsets;

namespace BeastsV2.Navigation;

internal sealed class Navigator
{
    private readonly WorldGrid _worldGrid;
    private readonly PathFinder _pathFinder;

    public Navigator(GameController gameController)
    {
        var terrain = gameController.IngameState.Data.Terrain;

        var gridWidth = ((int)terrain.NumCols - 1) * 23;
        var gridHeight = ((int)terrain.NumRows - 1) * 23;
        if (gridWidth % 2 != 0)
        {
            gridWidth++;
        }

        _worldGrid = new WorldGrid(gridHeight, gridWidth + 1);
        _pathFinder = new PathFinder(_worldGrid, new PathFinderOptions
        {
            PunishChangeDirection = false,
            UseDiagonals = true,
            SearchLimit = gridWidth * gridHeight
        });

        PopulateWorldGrid(terrain, _worldGrid, gameController.Memory);
    }

    public List<Vector2> FindPath(Vector2 start, Vector2 end, int nodeSize)
    {
        var normalizedStart = FindNearestWalkablePoint(start);
        var normalizedEnd = FindNearestWalkablePoint(end);

        var pathPoints = _pathFinder.FindPath(
            new Point((int)normalizedStart.X, (int)normalizedStart.Y),
            new Point((int)normalizedEnd.X, (int)normalizedEnd.Y));
        if (pathPoints == null || pathPoints.Length == 0)
        {
            return null;
        }

        var pathVectors = new List<Vector2>(pathPoints.Length);
        foreach (var point in pathPoints)
        {
            pathVectors.Add(new Vector2(point.X, point.Y));
        }

        if (pathVectors.Count <= 2 || nodeSize <= 0)
        {
            return pathVectors;
        }

        var simplified = new List<Vector2> { pathVectors[0] };
        var lastKeptNode = pathVectors[0];
        for (var i = 1; i < pathVectors.Count - 1; i++)
        {
            var currentNode = pathVectors[i];
            if (Vector2.Distance(currentNode, lastKeptNode) < nodeSize)
            {
                continue;
            }

            simplified.Add(currentNode);
            lastKeptNode = currentNode;
        }

        simplified.Add(pathVectors[^1]);
        return simplified;
    }

    private Vector2 FindNearestWalkablePoint(Vector2 point)
    {
        var clampedX = Math.Clamp((int)point.X, 0, _worldGrid.Width - 1);
        var clampedY = Math.Clamp((int)point.Y, 0, _worldGrid.Height - 1);
        if (_worldGrid[clampedY, clampedX] > 0)
        {
            return new Vector2(clampedX, clampedY);
        }

        const int maxSearchRadius = 12;
        for (var radius = 1; radius <= maxSearchRadius; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                {
                    continue;
                }

                var x = clampedX + dx;
                var y = clampedY + dy;
                if (x < 0 || x >= _worldGrid.Width || y < 0 || y >= _worldGrid.Height)
                {
                    continue;
                }

                if (_worldGrid[y, x] > 0)
                {
                    return new Vector2(x, y);
                }
            }
        }

        return new Vector2(clampedX, clampedY);
    }

    private static void PopulateWorldGrid(TerrainData terrain, WorldGrid worldGrid, ExileCore.Shared.Interfaces.IMemory memory)
    {
        var layerMeleeBytes = memory.ReadBytes(terrain.LayerMelee.First, terrain.LayerMelee.Size);
        var currentByteOffset = 0;

        for (var row = 0; row < worldGrid.Height; row++)
        {
            for (var column = 0; column < worldGrid.Width; column += 2)
            {
                if (currentByteOffset + (column >> 1) >= layerMeleeBytes.Length)
                {
                    break;
                }

                var tileValue = layerMeleeBytes[currentByteOffset + (column >> 1)];
                var c1 = tileValue & 0xF;
                worldGrid[row, column] = (short)(c1 > 0 ? 1 : 0);

                if (column + 1 < worldGrid.Width)
                {
                    var c2 = tileValue >> 4;
                    worldGrid[row, column + 1] = (short)(c2 > 0 ? 1 : 0);
                }
            }

            currentByteOffset += terrain.BytesPerRow;
        }
    }
}
