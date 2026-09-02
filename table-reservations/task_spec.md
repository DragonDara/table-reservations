# Task: Organization-specific booking time range

We recently introduced multi-tenancy into the .NET project.

Now each organization should have its own booking time range, and the backend should use that organization-specific configuration to determine which time values are available to the user in the booking form.

## Goal

Implement organization-specific booking time availability.

The frontend should not hardcode available booking times.

The backend must determine available time slots based on the current organization / tenant.

## Requirements

### Organization configuration

Each organization should have its own booking time settings.

At minimum, support:

- booking start time
- booking end time
- booking slot duration in minutes

Example:

```text
Organization A
Start: 12:00
End: 23:00
Slot duration: 30 minutes
```
