using BuildABot2025.Enums;

namespace BuildABot2025.Models;

public class Animal
{
    public Guid Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int SpawnX { get; set; }
    public int SpawnY { get; set; }
    public int Score { get; set; }
    public int CapturedCounter { get; set; }
    public int DistanceCovered { get; set; }
    public bool IsViable { get; set; }
    public PowerUpType? HeldPowerUp { get; set; }
    public ActivePowerUp? ActivePowerUp { get; set; }
}
