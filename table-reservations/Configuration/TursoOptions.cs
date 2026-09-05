namespace table_reservations.Configuration
{
    /// <summary>
    /// Connection settings for the shared Turso (libSQL) database that backs all
    /// organizations. This schema maps one lounge and one carwash to separate tables.
    /// </summary>
    public sealed class TursoOptions
    {
        public const string SectionName = "Turso";

        /// <summary>
        /// Database HTTP endpoint, e.g. <c>https://my-db-org.turso.io</c>.
        /// <c>libsql://</c> URLs are accepted and normalized to <c>https://</c>.
        /// </summary>
        public string Url { get; set; } = string.Empty;
        public string DatabaseUrl { get; set; } = string.Empty;
        public string ConnectionUrl => string.IsNullOrWhiteSpace(DatabaseUrl) ? Url : DatabaseUrl;
        public string RestaurantOrganizationId { get; set; } = "thetochka";
        public string CarWashOrganizationId { get; set; } = "thetochka-carwasher";
        // The current catalog stores whole tenge in its price_minor column.
        public bool CatalogPricesAreKzt { get; set; } = true;
        public int? DefaultCarWashMinutes { get; set; } = 60;

        /// <summary>Turso database auth token (JWT) used as a bearer token.</summary>
        public string AuthToken { get; set; } = string.Empty;

        /// <summary>Request timeout for a single pipeline call.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ConnectionUrl) && !string.IsNullOrWhiteSpace(AuthToken);
    }
}
