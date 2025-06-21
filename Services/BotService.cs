using BuildABot2025.Enums;
using BuildABot2025.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildABot2025.Services;

public class BotService
{
    private Guid _botId;

    // State tracking to prevent loops and unproductive behavior
    private Cell? _currentTarget;
    private int _ticksStuckOnTarget = 0;
    private const int STUCK_THRESHOLD = 5; // After this many ticks without progress, find a new target
    private readonly HashSet<(int X, int Y)> _invalidatedTargets = new HashSet<(int, int)>();

    public void SetBotId(Guid botId)
    {
        _botId = botId;
    }

    public BotCommand ProcessState(GameState gameState)
    {
        var bot = gameState.Animals.FirstOrDefault(a => a.Id == _botId);
        if (bot == null) return new BotCommand { Action = BotAction.Right }; // Should not happen

        // =================================================================
        // HIERARCHY OF DECISIONS
        // =================================================================

        // 1. SURVIVAL: Flee from immediate danger
        var fleeCommand = FleeIfNecessary(bot, gameState);
        if (fleeCommand != null) return fleeCommand;

        // 2. STRATEGIC ITEM USAGE: Use a held power-up if it's a good time
        var useItemCommand = UseItemIfStrategic(bot, gameState);
        if (useItemCommand != null) return useItemCommand;

        // 3. TARGET MANAGEMENT: Decide what we should be chasing
        UpdateTarget(bot, gameState);

        // If after all logic, we have no target, explore safely
        if (_currentTarget == null)
        {
            Console.WriteLine("No valid targets. Exploring...");
            return FindSafestMove(bot, gameState, null);
        }

        // 4. MOVEMENT: Move towards the chosen target
        return MoveTowardsTarget(bot, gameState);
    }

    private void UpdateTarget(Animal bot, GameState gameState)
    {
        // Clear invalidated targets if they reappear (e.g., pellet respawn)
        _invalidatedTargets.RemoveWhere(t => gameState.Cells.Any(c => c.X == t.X && c.Y == t.Y && GetCellValue(c.Content) > 0));

        // Condition 1: Check if we've reached our current target
        if (_currentTarget != null && bot.X == _currentTarget.X && bot.Y == _currentTarget.Y)
        {
            _currentTarget = null;
            _ticksStuckOnTarget = 0;
        }

        // Condition 2: Check if we're stuck on the current target
        if (_currentTarget != null && _ticksStuckOnTarget > STUCK_THRESHOLD)
        {
            Console.WriteLine($"STUCK on target at ({_currentTarget.X},{_currentTarget.Y}). Invalidating and finding new target.");
            _invalidatedTargets.Add((_currentTarget.X, _currentTarget.Y));
            _currentTarget = null;
            _ticksStuckOnTarget = 0;
        }

        // Condition 3: If we don't have a target, find the best one
        if (_currentTarget == null)
        {
            _currentTarget = FindBestTarget(bot, gameState);
        }
    }

    private Cell? FindBestTarget(Animal bot, GameState gameState)
    {
        var allValidTargets = gameState.Cells
            .Where(c => GetCellValue(c.Content) > 0 && !_invalidatedTargets.Contains((c.X, c.Y)))
            .ToList();

        if (!allValidTargets.Any()) return null;

        // STRATEGY: If score streak is active, prioritize maintaining it above all else!
        if (bot.ScoreStreak > 0)
        {
            var closestPellet = allValidTargets
                .Where(t => t.Content == CellContent.Pellet)
                .OrderBy(t => Math.Abs(t.X - bot.X) + Math.Abs(t.Y - bot.Y))
                .FirstOrDefault();

            if (closestPellet != null)
            {
                Console.WriteLine($"Score streak active! Prioritizing nearest pellet at ({closestPellet.X},{closestPellet.Y})");
                return closestPellet;
            }
        }

        // DEFAULT STRATEGY: Hunt for the most valuable power-up or pellet
        return allValidTargets
            .OrderByDescending(t => CalculateDesirability(t, bot))
            .FirstOrDefault();
    }

    private BotCommand MoveTowardsTarget(Animal bot, GameState gameState)
    {
        var command = FindSafestMove(bot, gameState, _currentTarget);

        // Check if we made progress towards the target with the chosen move
        var moveDelta = GetMoveDelta(command.Action);
        int newX = bot.X + moveDelta.dx;
        int newY = bot.Y + moveDelta.dy;

        int oldDist = Math.Abs(bot.X - _currentTarget.X) + Math.Abs(bot.Y - _currentTarget.Y);
        int newDist = Math.Abs(newX - _currentTarget.X) + Math.Abs(newY - _currentTarget.Y);

        if (newDist >= oldDist)
        {
            _ticksStuckOnTarget++;
        }
        else
        {
            _ticksStuckOnTarget = 0; // We made progress, reset the counter
        }

        return command;
    }

    private BotCommand FindSafestMove(Animal bot, GameState gameState, Cell? target)
    {
        var directions = GetDirections();
        int currentDistance = target != null ? Math.Abs(bot.X - target.X) + Math.Abs(bot.Y - target.Y) : int.MaxValue;

        var bestMove = directions
            .Select(dir =>
            {
                int newX = bot.X + dir.dx;
                int newY = bot.Y + dir.dy;
                var cell = gameState.Cells.FirstOrDefault(c => c.X == newX && c.Y == newY);
                bool isSafe = cell != null && cell.Content != CellContent.Wall;
                int newDistance = target != null ? Math.Abs(newX - target.X) + Math.Abs(newY - target.Y) : int.MaxValue;
                return new { Action = dir.action, IsSafe = isSafe, Distance = newDistance };
            })
            .Where(m => m.IsSafe)
            .OrderBy(m => m.Distance) // Primary sort: get closer to target
            .ThenBy(m => Guid.NewGuid()) // Secondary sort: random to break ties and prevent simple loops
            .FirstOrDefault();

        if (bestMove != null)
        {
            var reason = target == null ? "(exploring)" : $"(toward {target.Content})";
            Console.WriteLine($"Planned Action: {bestMove.Action} {reason}");
            return new BotCommand { Action = bestMove.Action };
        }

        return new BotCommand { Action = BotAction.Right }; // Absolute fallback
    }

    private BotCommand? FleeIfNecessary(Animal bot, GameState gameState)
    {
        bool isCloakActive = bot.ActivePowerUp?.Type == PowerUpType.ChameleonCloak;
        if (isCloakActive) return null;

        var closestZookeeper = gameState.Zookeepers
            .OrderBy(zk => Math.Abs(zk.X - bot.X) + Math.Abs(zk.Y - bot.Y))
            .FirstOrDefault();

        int fleeThreshold = 4;
        if (closestZookeeper != null && Math.Abs(closestZookeeper.X - bot.X) + Math.Abs(closestZookeeper.Y - bot.Y) <= fleeThreshold)
        {
            Console.WriteLine($"FLEE MODE: Zookeeper at ({closestZookeeper.X},{closestZookeeper.Y}) is too close!");
            var bestFleeMove = GetDirections()
                .Select(dir => new { Action = dir.action, NewX = bot.X + dir.dx, NewY = bot.Y + dir.dy })
                .Where(move => {
                    var cell = gameState.Cells.FirstOrDefault(c => c.X == move.NewX && c.Y == move.NewY);
                    return cell != null && cell.Content != CellContent.Wall;
                })
                .OrderByDescending(move => Math.Abs(move.NewX - closestZookeeper.X) + Math.Abs(move.NewY - closestZookeeper.Y))
                .FirstOrDefault();

            if (bestFleeMove != null) return new BotCommand { Action = bestFleeMove.Action };
        }
        return null;
    }

    private BotCommand? UseItemIfStrategic(Animal bot, GameState gameState)
    {
        if (bot.HeldPowerUp == null) return null;
        bool shouldUse = false;
        switch (bot.HeldPowerUp)
        {
            case PowerUpType.ChameleonCloak:
                shouldUse = gameState.Zookeepers.Any(zk => Math.Abs(zk.X - bot.X) + Math.Abs(zk.Y - bot.Y) < 5);
                break;
            case PowerUpType.BigMooseJuice:
            case PowerUpType.Scavenger:
                shouldUse = gameState.Cells.Count(c => c.Content == CellContent.Pellet && Math.Abs(c.X - bot.X) + Math.Abs(c.Y - bot.Y) < 6) > 5;
                break;
            case PowerUpType.PowerPellet:
                shouldUse = true;
                break;
        }
        if (shouldUse)
        {
            Console.WriteLine($"Planned Action: UseItem (strategically activating {bot.HeldPowerUp})");
            return new BotCommand { Action = BotAction.UseItem };
        }
        return null;
    }

    private double GetCellValue(CellContent content)
    {
        // Drastically increased power-up values to ensure they are prioritized
        switch (content)
        {
            case CellContent.PowerPellet: return 1000.0;
            case CellContent.Scavenger:
            case CellContent.BigMooseJuice: return 800.0;
            case CellContent.ChameleonCloak: return 600.0;
            case CellContent.Pellet: return 10.0;
            default: return 0.0;
        }
    }

    private double CalculateDesirability(Cell target, Animal bot)
    {
        double distance = Math.Abs(target.X - bot.X) + Math.Abs(target.Y - bot.Y);
        if (distance == 0) distance = 0.1;
        double value = GetCellValue(target.Content);
        return value / distance;
    }

    private List<(BotAction action, int dx, int dy)> GetDirections() => new List<(BotAction, int, int)>
    {
        (BotAction.Up, 0, -1), (BotAction.Down, 0, 1), (BotAction.Left, -1, 0), (BotAction.Right, 1, 0)
    };

    private (int dx, int dy) GetMoveDelta(BotAction action)
    {
        switch (action)
        {
            case BotAction.Up: return (0, -1);
            case BotAction.Down: return (0, 1);
            case BotAction.Left: return (-1, 0);
            case BotAction.Right: return (1, 0);
            default: return (0, 0);
        }
    }
}