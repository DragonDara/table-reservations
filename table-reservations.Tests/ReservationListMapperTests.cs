using System.Text.Json;
using table_reservations.Configuration;
using table_reservations.Helpers;
using table_reservations.Models.Tenancy;

namespace table_reservations.Tests;

public class ReservationListMapperTests
{
    [Fact]
    public void RestaurantList_FiltersInvalidAndOtherDays_AndSortsChronologically()
    {
        IList<object>[] rows =
        [
            ["id1", "2,3", "Private name", "Private phone", "07/09/2026 19:00"],
            [],
            ["id2", "1", "Private name", "Private phone", "2026-09-07T12:00"],
            ["id3", "4", "Private name", "Private phone", "08.09.2026 12:00"],
            ["id4", "5", "Private name", "Private phone", "invalid"],
        ];

        var result = ReservationListMapper.Map(rows, new(), BusinessType.Restaurant, new(2026, 9, 7));

        Assert.Equal(["2026-09-07T12:00", "2026-09-07T19:00"], result.Select(r => r.ScheduledAt));
        Assert.Equal(["1", "2,3"], result.Select(r => r.TablesId));
        Assert.All(result, r => Assert.Empty(r.WashServiceType));
        Assert.DoesNotContain("Private", JsonSerializer.Serialize(result));
    }

    [Fact]
    public void CarWashList_UsesTenantSchema_AndDoesNotExposePlatesOrPhones()
    {
        var schema = new SheetSchemaOptions { ScheduledAtColumn = 2, ServiceTypeColumn = 4 };
        IList<object>[] rows = [["id", "Private plate", "07.09.2026 09:00", "Private phone", "Кузов"]];

        var result = ReservationListMapper.Map(rows, schema, BusinessType.CarWash, new(2026, 9, 7));

        var item = Assert.Single(result);
        Assert.Equal("Кузов", item.WashServiceType);
        Assert.Empty(item.TablesId);
        Assert.DoesNotContain("Private", JsonSerializer.Serialize(result));
    }

    [Fact]
    public void MissingOptionalColumns_ReturnEmptyFields()
    {
        var schema = new SheetSchemaOptions { ScheduledAtColumn = 0, ServiceTypeColumn = -1 };
        var result = ReservationListMapper.Map([["2026-09-07T09:00"]], schema, BusinessType.CarWash, new(2026, 9, 7));
        Assert.Empty(Assert.Single(result).WashServiceType);
        Assert.Empty(ReservationListMapper.Map([], schema, BusinessType.CarWash, new(2026, 9, 7)));
    }
}
