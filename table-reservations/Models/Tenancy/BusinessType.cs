namespace table_reservations.Models.Tenancy
{
    /// <summary>
    /// Type of business a tenant (organization) operates. Selects the pluggable
    /// reservation rules and Google Sheets schema used for that organization.
    /// </summary>
    public enum BusinessType
    {
        Restaurant,
        CarWash
    }
}
