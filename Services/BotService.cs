using BuildABot2025.Enums;
using BuildABot2025.Models;
using System.Collections.Generic;
using System.Linq;

namespace BuildABot2025.Services;

public class BotService
{
    private Guid _botId;
    // A queue to remember the last 4 positions to avoid getting stuck in loops
    private readonly Queue<(int X, int Y)> _recentPositions = new Queue<(int, int)>();

    public void SetBotId(Guid botId)
    {
        _botId = botId;
    }

    public Guid GetBotId()
    {
        return _botId;
    }

    public BotCommand ProcessState(GameState gameState)
    {
        var bot = gameState.Animals.FirstOrDefault(a => a.Id == _botId);
        var command = new BotCommand { Action = BotAction.Right }; // Default fallback

        if (bot == null)
            return command;

        // --- ANTI-LOOP LOGIC: Record current position ---
        // Add current position to our history
        _recentPositions.Enqueue((bot.X, bot.Y));
        // Keep the history limited to the last 4 ticks
        if (_recentPositions.Count > 4)
        {
            _recentPositions.Dequeue();
        }

        // --- #3 PROACTIVE EVASION (SURVIVAL FIRST) ---
        bool isCloakActive = bot.ActivePowerUp?.Type == PowerUpType.ChameleonCloak;
        if (!isCloakActive)
        {
            var closestZookeeper = gameState.Zookeepers
                .OrderBy(zk => Math.Abs(zk.X - bot.X) + Math.Abs(zk.Y - bot.Y))
                .FirstOrDefault();

            int fleeThreshold = 4;
            if (closestZookeeper != null && Math.Abs(closestZookeeper.X - bot.X) + Math.Abs(closestZookeeper.Y - bot.Y) <= fleeThreshold)
            {
                Console.WriteLine($"FLEE MODE: Zookeeper at ({closestZookeeper.X},{closestZookeeper.Y}) is too close!");
                return Flee(bot, closestZookeeper, gameState);
            }
        }

        // --- STRATEGIC POWER-UP USAGE ---
        if (bot.HeldPowerUp != null)
        {
            bool shouldUseItem = ShouldUsePowerUp(bot, gameState);
            if (shouldUseItem)
            {
                Console.WriteLine($"Planned Action: UseItem (strategically activating {bot.HeldPowerUp})");
                return new BotCommand { Action = BotAction.UseItem };
            }
        }

        // --- SMARTER TARGETING ---
        var allTargets = gameState.Cells.Where(c => GetCellValue(c.Content) > 0);
        if (!allTargets.Any())
        {
            // No targets left, move randomly but safely
            return FindSafestMove(bot, gameState, null);
        }

        var target = allTargets.OrderByDescending(t => CalculateDesirability(t, bot)).First();

        // --- EXECUTE MOVEMENT ---
        return FindSafestMove(bot, gameState, target);
    }

    private BotCommand Flee(Animal bot, Zookeeper zookeeper, GameState gameState)
    {
        var directions = GetDirections();
        var bestFleeMove = directions
            .Select(dir => new
            {
                Action = dir.action,
                NewX = bot.X + dir.dx,
                NewY = bot.Y + dir.dy,
                Cell = gameState.Cells.FirstOrDefault(c => c.X == bot.X + dir.dx && c.Y == bot.Y + dir.dy)
            })
            .Where(move => move.Cell != null && move.Cell.Content != CellContent.Wall) // Must be a valid, non-wall tile
            .OrderByDescending(move => Math.Abs(move.NewX - zookeeper.X) + Math.Abs(move.NewY - zookeeper.Y)) // Pick move that is furthest from zk
            .FirstOrDefault();

        if (bestFleeMove != null)
        {
            return new BotCommand { Action = bestFleeMove.Action };
        }

        // If trapped, try any safe move
        return FindSafestMove(bot, gameState, null);
    }

    private BotCommand FindSafestMove(Animal bot, GameState gameState, Cell? target)
    {
        var directions = GetDirections();
        int currentDistance = target != null ? Math.Abs(bot.X - target.X) + Math.Abs(bot.Y - target.Y) : int.MaxValue;

        var potentialMoves = directions.Select(dir =>
        {
            int newX = bot.X + dir.dx;
            int newY = bot.Y + dir.dy;
            var cell = gameState.Cells.FirstOrDefault(c => c.X == newX && c.Y == newY);
            bool isSafe = cell != null && cell.Content != CellContent.Wall;
            bool isRepeat = _recentPositions.Contains((newX, newY));
            int newDistance = target != null ? Math.Abs(newX - target.X) + Math.Abs(newY - target.Y) : int.MaxValue;

            return new
            {
                Action = dir.action,
                IsSafe = isSafe,
                IsRepeat = isRepeat,
                Distance = newDistance
            };
        }).ToList();

        // Prioritize moves: 1. Closer, not a repeat. 2. Closer, is a repeat. 3. Not closer, not a repeat. 4. Any safe move.
        var bestMove =
            potentialMoves.Where(m => m.IsSafe && !m.IsRepeat && m.Distance < currentDistance).OrderBy(m => m.Distance).FirstOrDefault() ??
            potentialMoves.Where(m => m.IsSafe && m.Distance < currentDistance).OrderBy(m => m.Distance).FirstOrDefault() ??
            potentialMoves.Where(m => m.IsSafe && !m.IsRepeat).FirstOrDefault() ??
            potentialMoves.Where(m => m.IsSafe).FirstOrDefault();

        if (bestMove != null)
        {
            var reason = target == null ? "(exploring)" : $"(toward {target.Content})";
            Console.WriteLine($"Planned Action: {bestMove.Action} {reason}");
            return new BotCommand { Action = bestMove.Action };
        }

        // Absolute last resort, should not happen on a valid map
        return new BotCommand { Action = BotAction.Right };
    }

    private bool ShouldUsePowerUp(Animal bot, GameState gameState)
    {
        if (bot.HeldPowerUp == null) return false;

        switch (bot.HeldPowerUp)
        {
            case PowerUpType.ChameleonCloak:
                var isZookeeperNear = gameState.Zookeepers.Any(zk => Math.Abs(zk.X - bot.X) + Math.Abs(zk.Y - bot.Y) < 5);
                return isZookeeperNear;
            case PowerUpType.BigMooseJuice:
            case PowerUpType.Scavenger:
                var pelletsNearby = gameState.Cells.Count(c => c.Content == CellContent.Pellet && Math.Abs(c.X - bot.X) + Math.Abs(c.Y - bot.Y) < 6);
                return pelletsNearby > 5;
            case PowerUpType.PowerPellet:
                return true;
            default:
                return false;
        }
    }

    private double GetCellValue(CellContent content)
    {
        switch (content)
        {
            case CellContent.PowerPellet: return 100.0;
            case CellContent.Scavenger:
            case CellContent.BigMooseJuice: return 50.0;
            case CellContent.ChameleonCloak: return 40.0;
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

    private List<(BotAction action, int dx, int dy)> GetDirections()
    {
        return new List<(BotAction, int, int)>
        {
            (BotAction.Up, 0, -1),
            (BotAction.Down, 0, 1),
            (BotAction.Left, -1, 0),
            (BotAction.Right, 1, 0)
        };
    }
}