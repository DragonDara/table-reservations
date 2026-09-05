using table_reservations.Models;
using table_reservations.Models.Tenancy;

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
        /// Builds the persistence record for a reservation of this business type.
        /// The caller assigns <see cref="ReservationRecord.OrganizationId"/>.
        /// </summary>
        ReservationRecord BuildRecord(ReservationInfo request, DateTime scheduledAt);

        bool HasConflict(ReservationInfo request, DateTime scheduledAt, ReservationRecord existing);

        string BuildNotificationLabel(ReservationInfo request, IReadOnlyList<TableInfo> tables);

        ReminderCandidate? MapReminderCandidate(ReservationRecord record);
    }
}
