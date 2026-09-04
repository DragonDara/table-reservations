namespace table_reservations.Configuration
{
    /// <summary>
    /// Connection settings for the shared Turso (libSQL) database that backs all
    /// organizations. A single database is used for every tenant; rows are scoped
    /// by <c>organization_id</c>.
    /// </summary>
    public sealed class TursoOptions
    {
        public const string SectionName = "Turso";

        /// <summary>
        /// Database HTTP endpoint, e.g. <c>https://my-db-org.turso.io</c>.
        /// <c>libsql://</c> URLs are accepted and normalized to <c>https://</c>.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Turso database auth token (JWT) used as a bearer token.</summary>
        public string AuthToken { get; set; } = string.Empty;

        /// <summary>Request timeout for a single pipeline call.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(AuthToken);
    }
}
