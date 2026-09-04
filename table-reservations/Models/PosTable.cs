namespace table_reservations.Models;

    public record PosTable
    (
        string Id,
    string Name,
    int Seats = 0,
    bool IsAvailable = true,
    int Number = 0,
    string SectionName = ""
    );

