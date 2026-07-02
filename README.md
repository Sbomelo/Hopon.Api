# Hopon
 
 Real-time trip notifications and bus tracking for intercity bus passengers in South Africa.
 

## The Problem
 
Intercity bus passengers in South Africa — particularly those travelling overnight on services like Intercape — have no reliable way to know when their bus will arrive at their stop.
 
This leads to real, everyday problems:
 
- Passengers arrive at dark, isolated bus stations hours too early because they don't know the bus's actual ETA.
- Families have no way to know when their loved one will arrive so they can arrange a safe pickup.
- Passengers fall asleep on the bus and miss their stop with no reminder system in place.
- Delays and incidents are not communicated, leaving passengers waiting with no information.

 
## The Solution
 
**Hopon** is a web-based system that gives bus passengers real-time access to their trip status, ETA notifications, and stop reminders — all secured behind their purchased ticket.
 
Key capabilities:
 
- **OTP-based login** — no account creation needed. Passengers log in using their phone number and a one-time PIN sent when they buy their ticket.
- **Live bus location** — see where the bus is on a map, restricted to your specific trip only.
- **Smart notifications** — receive alerts at 4 hours, 2 hours, 30 minutes, and 10 minutes before the bus reaches your stop, plus an arrival alarm.
- **Stop reminders** — get notified before your stop so you never sleep through it.
- **Emergency contact** — save a family member's number and they get automatically notified when you arrive.
- **Delay alerts** — instant notifications when the driver reports a delay, traffic stop, or incident.
- **Boarding confirmation** — optionally confirm when you board and when you leave the bus.
- **Trip history** — view past trips and receipts after your journey.

 
## Project Goals
 
This project serves two purposes:
 
1. **Pitch demo** — a fully working demo with realistic seeded data to present to intercity bus operators as a solution they can adopt or license.

 
## Tech Stack
 
| Layer | Technology |
|---|---|
| Backend | ASP.NET Core Web API (.NET 8) |
| Passenger Frontend | React (Vite) |
| Admin & Driver Frontend | Razor Pages  |
| ORM | Entity Framework Core (Code First) |
| Database | SQL Server |
| Realtime | SignalR |
| Authentication | JWT + OTP/PIN login |
| Maps | Leaflet.js + OpenStreetMap |
| SMS Notifications | Twilio (SMS fallback) |
| Hosting | Azure App Service / IIS |
 
**Why the frontend is split:**
- The passenger app is the public-facing, mobile-first showpiece of Hopon — React handles live updates, map interactions, and a smooth mobile experience naturally. 
- The admin and driver tools are internal desktop interfaces where Razor Pages is faster to build and easier to maintain.
