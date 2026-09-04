using table_reservations.Models;
using table_reservations.Models.Tenancy;
using table_reservations.Configuration;

namespace table_reservations.Services.BusinessTypes
{
    /// <summary>
    /// Result of validating a create-reservation request for a specific business type.
    /// </summary>
    public sealed class ReservationValidationResult
    {
        public bool IsValid { get; private init; }
        public string? Error { get; private init; }
        public DateTime ScheduledAt { get; private init; }

        public static ReservationValidationResult Invalid(string error) =>
            new() { IsValid = false, Error = error };

        public static ReservationValidationResult Valid(DateTime scheduledAt) =>
            new() { IsValid = true, ScheduledAt = scheduledAt };
    }

    /// <summary>
    /// Pluggable per-business-type behavior: request validation rules and the
    /// mapping of a reservation to a Google Sheets row. Selected per tenant based
    /// on <see cref="BusinessType"/>.
    /// </summary>
    public interface IBusinessTypeStrategy
    {
        BusinessType Type { get; }

        /// <summary>
        /// Validates the incoming request against this business type's rules
        /// (required fields, working hours, minimum lead time, etc.) and returns
        /// the parsed scheduled time when valid.
        /// </summary>
        ReservationValidationResult ValidateCreate(ReservationInfo request);

        /// <summary>
        /// Builds the ordered cell values for a new reservation row matching this
        /// business type's sheet schema.
        /// </summary>
        IList<object> BuildReservationRow(ReservationInfo request, DateTime scheduledAt);

        bool HasConflict(
            ReservationInfo request,
            DateTime scheduledAt,
            IList<object> existingRow,
            SheetSchemaOptions schema);

        string BuildNotificationLabel(ReservationInfo request, IReadOnlyList<TableInfo> tables);

        ReminderCandidate? MapReminderCandidate(
            IList<object> row,
            int sheetRowNumber,
            SheetSchemaOptions schema);
    }
}
