# Privacy Policy

**CircleQuickLauncher does not collect, transmit, store, or sell any personal data.**

## Summary

- **No network activity.** The app makes zero outbound connections — no analytics, no crash reporting, no ads, no trackers, no update pings.
- **No telemetry.** Nothing about your usage, app list, or system is sent anywhere.
- **All data is local.** Configuration and icons live only in your user profile.

## What Is Stored Locally

| Location | Contents | Purpose |
| --- | --- | --- |
| `%APPDATA%\CircleQuickLauncher\settings.json` | Your chosen edge, sizes, delays, start-with-windows flag. | Persist your preferences. |
| `%APPDATA%\CircleQuickLauncher\apps.json` | List of pinned executables (paths, custom icon names, labels). | Persist your launcher contents. |
| `%APPDATA%\CircleQuickLauncher\icons\` | Copies of PNG icons you imported. | Display your custom icons. |
| `%APPDATA%\CircleQuickLauncher\logs\` | `startup.log`, optional crash dumps (exception text, OS/build info). | Diagnose local crashes. |

None of this leaves your machine. You can delete the entire folder to fully reset the app.

## What the App Does *Not* Do

- Does not read browser history, documents, photos, or other personal files.
- Does not record keystrokes or screen content.
- Does not install services, drivers, or scheduled tasks.
- Does not phone home for licensing, activation, or updates.
- Does not use cookies or web storage (it is not a browser app).

## Third-Party Dependencies

The app bundles the [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/) and .NET runtime (self-contained). These are Microsoft components with their own licenses; they are used locally and do not introduce network calls by virtue of this app.

## Changes

Any future change to this policy will be reflected in this file and called out in the [CHANGELOG](CHANGELOG.md). The goal is to remain a **zero-data** tool.

## Contact

Questions: open a GitHub issue or a Security Advisory (see [SECURITY.md](SECURITY.md)).
