namespace table_reservations.Models
{   
        public sealed class ReminderCandidate
        {
            public int SheetRowNumber { get; init; } // 2, 3, ... для Update "Брони!H{n}"
            public string TablesId { get; init; } = "";
            public string CustomerName { get; init; } = "";
            public string CustomerPhone { get; init; } = "";
            public string ScheduledAt { get; init; } = "";
            public string? Section { get; init; }
            public string RemindBeforeHourCell { get; init; } = ""; // G
            public string ReminderSentCell { get; init; } = "";     // H
        }
}
