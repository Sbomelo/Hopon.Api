# Hopon SMS (Task 6)

`ISmsSender.SendAsync(toE164, message, ct)` returns `SmsSendResult(Success, Error, ProviderMessageId)`.
The default provider is Logging in every environment. It logs `SMS to +27… : message`
at Information in Development, with the body redacted in other environments to
protect OTPs. Logging simulates success and never contacts a provider.

Twilio uses the official [Twilio 8.2.0 NuGet package](https://www.nuget.org/packages/Twilio/8.2.0),
the latest stable version compatible with .NET 8 verified for this task. HTTP and
send operations have a 10-second timeout. Provider errors, cancellation and timeouts
return failure; logs contain only failure type/code, never exception text or SMS
bodies. SDK debug logging is disabled. Messaging Service takes precedence if both
sender settings are present.

## Owner setup in Twilio

1. [Create a Twilio account](https://www.twilio.com/try-twilio), verify the account's email/phone and complete the Console onboarding. Record the Account SID and Auth Token privately.
2. In Console, open **Phone Numbers → Manage → Buy a number** (or use the assigned trial number). Choose an SMS-capable sender that Twilio supports for delivery to South African mobile numbers. A local +27 sender is not automatically required or available. Check the [South Africa SMS guidelines](https://www.twilio.com/en-us/guidelines/za/sms) and complete any registration Twilio requires for the selected sender.
3. Either set that number in `Twilio:FromNumber` in E.164, or open **Messaging → Services → Create Messaging Service**, select a notification use case, add the eligible SMS sender to its Sender Pool, and copy the `MG…` SID to `Twilio:MessagingServiceSid`. A service with no sender cannot deliver. See [Messaging Services](https://www.twilio.com/docs/messaging/services).
4. Open **Messaging → Settings → Geo permissions**, enable South Africa (+27), and save. See [SMS Geo Permissions](https://www.twilio.com/docs/messaging/guides/sms-geo-permissions).
5. On a trial account, verify each test recipient under the Console's **Verified Caller IDs / verified recipients** flow before sending. Use only the assigned trial sender and verified recipients. Trial onboarding, supported countries and message templates can restrict custom messages; if the current trial does not permit Hopon's exact OTP text or South African recipients, upgrade before the real OTP acceptance check. See the [SMS quickstart](https://www.twilio.com/docs/messaging/quickstart) and [trial guide](https://www.twilio.com/docs/usage/trials).
6. Configure Hopon below, keep notification SMS disabled initially, then request one OTP for a consenting ticket holder whose number is verified. Confirm receipt and inspect Twilio Console Messaging Logs. Upgrade/fund the account before delivery to unverified recipients. Enable notification SMS only when ready for its cost.

No account was created, paid SMS sent, or credentials changed by this task.

## Configuration

| Key | Default / requirement |
|---|---|
| `Sms:Provider` | `Logging`; also accepts `Twilio` (case insensitive) |
| `Twilio:AccountSid` | Required when Twilio is selected |
| `Twilio:AuthToken` | Required when Twilio is selected; secret |
| `Twilio:FromNumber` | Required unless a Messaging Service SID is supplied |
| `Twilio:MessagingServiceSid` | Alternative sender, takes precedence over FromNumber |
| `Notifications:SmsEnabled` | `false`; master switch for **notification** SMS |
| `Notifications:SmsTypes` | Names: `ThirtyMinutesBefore`, `TenMinutesBefore`, `Arrival`, `Delay`, `Emergency` |
| `Otp:ExposeCodeInResponse` | `false` by default, `true` in Development; independent of SMS provider |

Set real credentials with user-secrets locally; never commit them:

```powershell
# Run in C:\dev\my-project\backend; replace placeholders privately.
dotnet user-secrets set 'Sms:Provider' 'Twilio'
dotnet user-secrets set 'Twilio:AccountSid' '<account-sid>'
dotnet user-secrets set 'Twilio:AuthToken' '<auth-token>'
dotnet user-secrets set 'Twilio:MessagingServiceSid' '<messaging-service-sid>'
# Or set Twilio:FromNumber instead of the Messaging Service SID.
dotnet user-secrets set 'Notifications:SmsEnabled' 'false'
```

Hosting environment variables use double underscores: `Sms__Provider`,
`Twilio__AccountSid`, `Twilio__AuthToken`, `Twilio__FromNumber`,
`Twilio__MessagingServiceSid`, `Notifications__SmsEnabled`, and optional indexed
`Notifications__SmsTypes__0`, `Notifications__SmsTypes__1`, etc. See `.env.example`.
ASP.NET Core does not automatically load `.env` files. Restart after configuration changes.
Missing Twilio credentials/sender or an unknown provider/type fails startup clearly
before staff bootstrap/database access.

The notification allowlist replaces the defaults. For example:

```json
{
  "Notifications": {
    "SmsEnabled": true,
    "SmsTypes": ["Arrival", "Delay"]
  }
}
```

OTP delivery always uses the selected sender, even when notification SMS is off.
Its text is `Your Hopon code is 123456. It expires in 10 minutes.` with a random code.
A failed send deletes the newly saved OTP row, restores older unused OTPs and
returns HTTP 400 with `We couldn't send the SMS. Please try again.` No code is
exposed on failure and the failed send consumes no rate-limit slot.

## Notification behavior and migration

The scheduler, driver delay and destination arrival paths call
`INotificationService.CreateAsync`. Each requested, enabled channel gets one
NotificationLog. InApp is immediately Sent. SMS first saves Pending to claim the
unique key, sends to the passenger's stored phone, then saves Sent/SentAt or
Failed with null SentAt. Sent means accepted by the provider, not handset delivery;
delivery receipt callbacks are not implemented. No OTP body is stored in NotificationLog.

Apply the new `AddNotificationChannelUniqueness` migration:

```powershell
dotnet ef database update -- --environment Development
```

It replaces the filtered `(UserId, TripId, Type)` unique index with
`(UserId, TripId, Type, Channel)` for reminder/arrival types. Existing rows are
preserved and existing migrations are unchanged. Delay can repeat. Task 7 adds a
separate Emergency guard across channels; see `docs/EMERGENCY_ALERTS.md`.
A downgrade to Task 5's index requires reconciling rows with both channels first;
the Down migration deliberately does not delete notification history.

Reminder/arrival SMS attempts are once per channel, including Failed attempts.
They are not automatically retried, to avoid repeat costs after ambiguous timeouts.
Notifications already created before enabling SMS are not backfilled. There is no
outbox/crash recovery between claiming Pending and updating the result; an interrupted
send can remain Pending. Per-ticket insert failure isolation remains in place.
The demo clock offset controls due decisions only; log timestamps use actual injected UTC time.
Emergency-contact arrival delivery is implemented in Task 7; see
`docs/EMERGENCY_ALERTS.md` for its separate recipient, once-per-trip claim,
completion fallback and truthful Pending/Failed/Sent feed messages.

## Verification

```powershell
# Backend
dotnet build
dotnet test --no-build
dotnet test --no-build --filter 'FullyQualifiedName~SmsTests|FullyQualifiedName~TwilioSmsSenderTests|FullyQualifiedName~OtpServiceTests|FullyQualifiedName~NotificationSchedulerTests'
dotnet ef database update -- --environment Development
dotnet run --launch-profile http

# Frontend, in C:\dev\my-project\frontend
npm run lint
npm run build
npm run dev
```

Tests replace `ISmsSender` with a recording fake. Adapter tests inject an in-memory
HTTP handler into the official SDK; no test contacts Twilio or incurs charges.
Coverage includes OTP rollback and rate-limit recovery, exact OTP text, code exposure,
default/custom notification allowlists, the master switch, failed status/SentAt,
per-channel uniqueness, scheduler/arrival/delay integration, provider selection,
startup validation, both sender modes, redaction, errors, cancellation and timeout.

With Logging selected, open [Swagger](http://localhost:5101/swagger) and POST
`/api/auth/otp/request` with `{"phoneNumber":"0789689875"}`. In Development,
verify the returned `devOnlyCode` using POST `/api/auth/otp/verify`, then use its
bearer token at `/api/trips/my-trips` and `/api/notifications/my`. The passenger UI
is [localhost:5173](http://localhost:5173). For a real delivery check, select Twilio,
disable code exposure, request one OTP, and enter the code from the handset. Active
map/scheduler checks require existing destination-ticketed running trip data (Task 12).

To preview notification SMS without cost, keep `Sms:Provider=Logging`, temporarily
enable `Notifications:SmsEnabled`, and run a due scheduler tick or mark a destination
arrived in the driver page. Expect separate InApp/Sms logs at the feed endpoint.
Switch notification SMS off again after testing.
