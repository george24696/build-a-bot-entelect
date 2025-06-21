using BuildABot2025.Enums;

namespace BuildABot2025.Models;

public class Cell
{
    public int X { get; set; }
    public int Y { get; set; }
    public CellContent Content { get; set; }
}