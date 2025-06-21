using BuildABot2025.Enums;
using BuildABot2025.Models;

namespace BuildABot2025.Services;

public class BotService
{
    private Guid _botId;

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
        var command = new BotCommand { Action = BotAction.Right };

        if (bot == null)
            return command;

        if (bot.HeldPowerUp != null)
        {
            Console.WriteLine("Planned Action: UseItem (activating power-up)");
            return new BotCommand { Action = BotAction.UseItem };
        }

        Console.WriteLine($"Tick: {gameState.Tick}");
        Console.WriteLine($"Bot Position: ({bot.X}, {bot.Y})");

        var allPowerUps = gameState.Cells
            .Where(c =>
                c.Content == CellContent.PowerPellet ||
                c.Content == CellContent.ChameleonCloak ||
                c.Content == CellContent.Scavenger ||
                c.Content == CellContent.BigMooseJuice)
            .ToList();

        var nearbyPowerUps = allPowerUps
            .Where(c => Math.Abs(c.X - bot.X) + Math.Abs(c.Y - bot.Y) <= 5)
            .OrderBy(c => Math.Abs(c.X - bot.X) + Math.Abs(c.Y - bot.Y))
            .ToList();

        var pellets = gameState.Cells
            .Where(c => c.Content == CellContent.Pellet)
            .OrderBy(c => Math.Abs(c.X - bot.X) + Math.Abs(c.Y - bot.Y))
            .ToList();

        Cell? target = null;

        if (nearbyPowerUps.Any())
        {
            target = nearbyPowerUps.First();
        }
        else
        {
            var closestPowerUp = allPowerUps.OrderBy(c => Math.Abs(c.X - bot.X) + Math.Abs(c.Y - bot.Y)).FirstOrDefault();
            var closestPellet = pellets.FirstOrDefault();

            if (closestPowerUp != null && closestPellet != null)
            {
                var distPowerUp = Math.Abs(closestPowerUp.X - bot.X) + Math.Abs(closestPowerUp.Y - bot.Y);
                var distPellet = Math.Abs(closestPellet.X - bot.X) + Math.Abs(closestPellet.Y - bot.Y);
                target = distPowerUp <= distPellet ? closestPowerUp : closestPellet;
            }
            else
            {
                target = closestPowerUp ?? closestPellet;
            }
        }

        if (target == null)
            return command;

        int maxX = gameState.Cells.Max(c => c.X);
        int maxY = gameState.Cells.Max(c => c.Y);

        var dangerZones = new HashSet<(int X, int Y)>();
        int dangerRadius = 3; 

        foreach (var zk in gameState.Zookeepers)
        {
            for (int dx = -dangerRadius; dx <= dangerRadius; dx++)
            {
                for (int dy = -dangerRadius; dy <= dangerRadius; dy++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) > dangerRadius)
                        continue;

                    int zx = (zk.X + dx + maxX + 1) % (maxX + 1);
                    int zy = (zk.Y + dy + maxY + 1) % (maxY + 1);
                    dangerZones.Add((zx, zy));
                }
            }
        }

        var directions = new List<(BotAction action, int dx, int dy)>
        {
            (BotAction.Up, 0, -1),
            (BotAction.Down, 0, 1),
            (BotAction.Left, -1, 0),
            (BotAction.Right, 1, 0)
        };

        BotCommand? fallbackCommand = null;

        foreach (var (action, dx, dy) in directions)
        {
            int newX = bot.X + dx;
            int newY = bot.Y + dy;

            var cell = gameState.Cells.FirstOrDefault(c => c.X == newX && c.Y == newY);

            if (dangerZones.Contains((newX, newY)))
                Console.WriteLine($"Skipped {action} (danger zone)");

            if (cell != null && cell.Content != CellContent.Wall && !dangerZones.Contains((newX, newY)))
            {
                int currentDistance = Math.Abs(bot.X - target.X) + Math.Abs(bot.Y - target.Y);
                int newDistance = Math.Abs(newX - target.X) + Math.Abs(newY - target.Y);

                if (newDistance < currentDistance)
                {
                    command.Action = action;
                    Console.WriteLine($"Planned Action: {command.Action} (toward {(target.Content == CellContent.Pellet ? "pellet" : "power-up")})");
                    return command;
                }

                fallbackCommand ??= new BotCommand { Action = action };
            }
        }

        if (fallbackCommand != null)
        {
            command = fallbackCommand;
            Console.WriteLine($"Planned Action: {command.Action} (fallback)");
        }

        Console.WriteLine($"Planned Action: {command.Action}");
        return command;
    }
}