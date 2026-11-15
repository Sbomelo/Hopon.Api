# Staff authentication (Task 1)

JWT remains the default API authentication scheme. Razor Pages explicitly use `StaffCookie`: `AdminOnly` for `/Admin`, `DriverOnly` for `/Driver`, and `StaffOnly` for shared staff pages. Login and access denied allow anonymous access. Shared pages still authenticate the cookie so POST logout validates the same antiforgery identity as the page that rendered its form.

Cookies are HttpOnly, SameSite=Lax, sliding with an eight-hour ticket lifetime, and Secure outside Development. Sessions use browser-session cookies. Driver/admin deactivation is checked on each cookie-authenticated request. Password reset affects future login; existing cookies and JWTs are not revoked by a password reset. API JWT behavior is unchanged.

## Run locally

From the backend directory, with the existing SQL Server and JWT user-secrets configured:

```powershell
dotnet ef database update --project Hopon.Api.csproj -- --environment Development
dotnet run --project Hopon.Api.csproj --launch-profile http
```

Before first startup, optionally configure both `Admin:Username` and `Admin:Password` using user-secrets; environment equivalents are `Admin__Username` and `Admin__Password`. Never put real credentials in source or a PR. The seeder runs only when AdminUsers is empty, counting inactive admins too. If either setting is missing in Development, it creates `admin` with a cryptographically generated password and logs that password once after saving. Keep the startup console output. Restarting does not regenerate or log it again. Outside Development, missing settings log a warning and skip bootstrap. Apply migrations before starting Hopon.

Migration: `20261115223234_AddAdminUsers` creates AdminUsers with a unique username index (100 characters) and a 255-character password hash. It inserts no admin data and modifies no existing records. Existing User seed dates were frozen to the model snapshot values to avoid migration churn.

Start the passenger frontend with `npm run dev` from its directory.

## Automated verification

```powershell
# Backend
dotnet build
dotnet test
# Passenger frontend
npm run lint
npm run build
```

Staff tests use isolated SQLite databases and real HTTP login forms/cookies through WebApplicationFactory. They cover anonymous redirects, cookie role isolation, JWT API regression, antiforgery and POST-only logout, driver management, claim-derived trip selection, foreign-stop rejection in HTTP and service calls, and first-admin bootstrap. Fixtures do not change local operational driver or trip data. SQL Server migration application is checked separately.

## Manual acceptance checks

1. With a clean browser session, visit `http://localhost:5101/Admin/Trips` and `http://localhost:5101/Driver/Trip`. Both redirect to `/Staff/Login`.
2. Log in as Admin. The destination is `/Admin/Trips`; the header shows the admin username, Trips/Drivers links, Logout and Theme. `/Driver/Trip` redirects to `/Staff/Denied`, which responds with 403. Check both themes.
3. Visit `/Admin/Drivers`. Create two active drivers with distinct usernames and passwords of at least eight characters. Reusing a username shows validation; a short password is rejected. Reset `driver1` here if its original seed password is unknown.
4. Assign separate Scheduled trips to the two drivers via `/Admin/Trips`. Use trips that already have TripStops for stop checks: creating TripStops for newly created trips remains Task 4. This PR makes no operational demo-data changes.
5. Log out, then log in with the Driver choice as `driver1` (or the first new driver). `/Driver/Trip` shows only that driver's active trip. Start it. Admin URLs show access denied. Copy a TripStopId from this driver's page in browser developer tools for step 7.
6. Use another browser profile/private window for the second driver. Confirm the first driver's trip is absent. Starting, delaying, completing or posting location operates only on the second driver's assigned trip, even if a `tripId` field is added to the POST.
7. On the second driver's page, replace a stop form's hidden `TripStopId` in developer tools with the first driver's stop id. Submit both MarkArrived and MarkDeparted. Both return HTTP 400 with `Stop not found.`; the other stop's status/timestamps remain unchanged and no stop broadcast is sent. Equivalent requests, using a cookie jar and antiforgery token from the second driver's session:

```powershell
curl.exe -i -b driver2.cookies --data-urlencode "__RequestVerificationToken=TOKEN_FROM_DRIVER2_PAGE" --data-urlencode "TripStopId=OTHER_DRIVERS_STOP_ID" "http://localhost:5101/Driver/Trip?handler=MarkArrived"
curl.exe -i -b driver2.cookies --data-urlencode "__RequestVerificationToken=TOKEN_FROM_DRIVER2_PAGE" --data-urlencode "TripStopId=OTHER_DRIVERS_STOP_ID" "http://localhost:5101/Driver/Trip?handler=MarkDeparted"
```

8. Deactivate a driver as Admin. Their existing cookie stops working on the next request, and login fails with `Invalid username or password`. Reactivate them; login works again. Reset their password; the old password fails and the new password works.
9. Use Logout; staff pages redirect to login. POST without an antiforgery token returns 400. GET Logout while authenticated returns 405 and leaves the session intact.
10. Existing passenger login/dashboard/map and driver API login continue using JWT bearer. A staff cookie alone gets 401 from `/api/trips/my-trips` and `/api/driver/my-trip`; a driver JWT does not open `/Driver/Trip`. Passenger/driver JWT role separation remains Task 2.

## Assumptions and limits

- Successful login always goes to the role's landing page; ReturnUrl is ignored.
- Each driver page chooses the driver's earliest active trip, preserving the existing selection rule.
- This simple bootstrap assumes a single startup instance. There is no admin-management UI or automatic credential rotation.
- The existing nullable warning in DriverController and passenger bundle-size warning are unrelated to Task 1.
