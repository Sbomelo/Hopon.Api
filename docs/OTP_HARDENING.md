# Hopon OTP hardening (Task 3)

OTP request and verification normalize South African mobile numbers before looking up the passenger or an OTP. Supported examples for the same number: `0781234567`, `078 123 4567`, `+27 78 123 4567`, `27781234567`, `0027781234567`, `(078) 123-4567`. Storage uses `+27781234567`. Only nine national digits starting with 6, 7 or 8 are accepted. Spaces, parentheses and hyphens are formatting; letters, other punctuation, non-ASCII digits, landlines and other country codes are rejected.

Emergency-contact PUT uses the same normalizer in DTO model validation and in the service. Invalid input returns HTTP 400 with a `PhoneNumber` validation error; both new and updated contacts store E.164. Existing contact rows are normalized when next saved. No data migration is needed; seeded passengers already use E.164.

OTP codes use cryptographic randomness, six digits and BCrypt hashing. A successful new request marks every older unused request for that phone as used. Codes expire after ten minutes, five incorrect verification attempts block the right code, and at most three requests are accepted per ten minutes across all phone formats. Inactive passengers cannot request or verify a code. Codes are single-use.

## Deliberate demo code exposure

`Otp:ExposeCodeInResponse` defaults to `false`. `appsettings.Development.json` sets it to `true`. A hosted pitch demo can opt in with the environment variable `Otp__ExposeCodeInResponse=true`, regardless of hosting environment. The existing React login page displays `devOnlyCode` when present. The response property is null when disabled.

Task 6 now delivers OTPs through `ISmsSender`. Logging mode prints the SMS only in Development, independently of response exposure; Production and Staging redact the body. Twilio never logs message bodies. A failed send removes the new OTP row, restores older unused codes and does not consume the request limit. See [SMS setup](SMS.md).

Known-number requests return 200 while unknown-number requests return 400 with `Unable to process this request.` This still allows number enumeration and is an accepted demo limitation. Inactive numbers get the same generic error as unknown numbers. Existing expiry, wrong-code and rate-limit messages remain actionable.

## Automated verification

From `C:\dev\my-project\backend`:

```powershell
dotnet build
dotnet test
dotnet test --filter 'FullyQualifiedName~PhoneNumberNormalizerTests|FullyQualifiedName~OtpServiceTests|FullyQualifiedName~OtpApiTests'
```

From `C:\dev\my-project\frontend`:

```powershell
npm run lint
npm run build
```

The existing xUnit project is already in `Hopon.Api.slnx` and references the API. `TestDb` keeps an in-memory SQLite connection open for each service test; disposing the helper destroys that test database. HTTP tests reuse the existing `StaffApplication` factory. Tests cover normalization, stored hashes, invalidation, expiry, attempts, request limits, inactive users, response configuration, environment-specific logging and emergency-contact validation/storage. The Task 2 OTP → map-data → SignalR test still exercises the authenticated trip flow, now with a valid synthetic mobile number.

## Local acceptance check

From the backend directory, with the existing local user-secrets configured:

```powershell
dotnet ef database update -- --environment Development
dotnet run --launch-profile http
```

From the frontend directory, run `npm run dev` and open `http://localhost:5173`. Swagger is at `http://localhost:5101/swagger`.

Request with a seeded passenger's local format, then verify using a different format (PowerShell):

```powershell
$request = Invoke-RestMethod -Method Post -Uri http://localhost:5101/api/auth/otp/request -ContentType application/json -Body (@{ phoneNumber = '078 968 9875' } | ConvertTo-Json)
$login = Invoke-RestMethod -Method Post -Uri http://localhost:5101/api/auth/otp/verify -ContentType application/json -Body (@{ phoneNumber = '0027789689875'; code = $request.devOnlyCode } | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($login.token)" }
Invoke-RestMethod -Uri http://localhost:5101/api/trips/my-trips -Headers $headers
```

Expected: request and verification return 200, the returned phone number is E.164, and authenticated trips load. The existing local data may contain only past trips; the isolated integration test supplies an active ticketed trip for map-data and SignalR verification. `Hopon.Api.http` also contains request examples.

To exercise emergency-contact validation without changing stored contact data, PUT `{"name":"Test contact","phoneNumber":"0111234567"}` to `http://localhost:5101/api/emergency-contact` with that bearer token. Expect HTTP 400 and a `PhoneNumber` error. Automated SQLite tests cover valid create/update storage without editing local contacts.

No schema changes or migrations were added. Task 3b configuration cleanup and Task 6 SMS delivery are outside this change.
