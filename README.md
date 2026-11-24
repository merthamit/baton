# Baton

**See the deteriorating patient early. Hand over the shift without losing anything.**

> [!WARNING]
> **Baton is not a medical device and must not be used in clinical care.**
> It is a portfolio and research project. It runs on **synthetic (fake) patient data only**.
> No real patient data is used, stored or accepted. Nothing here is medical advice.

<!-- TODO(author): rewrite this story in your own words with one real (anonymised) memory from your placements. Do not keep any sentence that is not true for you. -->

## Why I am building this

I studied nursing and spent two years on clinical placements in a hospital before I moved into
software. On the ward, the hard part was rarely calculating an early warning score. The hard part
was everything after the number: who saw it, who came to look, what was decided, and whether that
story survived the shift change.

Baton is the tool I wished we had: a ward board that shows *why* a patient's score is rising,
records the chain of response around it, lets a nurse say "I am worried about this patient" even
when the numbers look fine, and drafts a handover where every sentence points back to its source.

## What it does (planned)

| Module | What it does |
|---|---|
| **NEWS2 engine** | Calculates the National Early Warning Score 2 exactly as published by the Royal College of Physicians (2017). Pure, versioned function; every band boundary is tested. SpO2 Scale 2 only with a recorded clinical decision. |
| **Ward board** | Bed-by-bed nurse cards: score with shape, risk words and trend (colour is never the only signal), the parameters raising it, concern flag, who was called and how long they have been waited for, and the next observation or how late it is. Patient view with the NEWS2 chart, events and every observation set. English and Turkish. |
| **Escalation chain** | Concern → called → arrived → assessed → plan, time-stamped and stored in FHIR. Response targets from NEWS2 guidance (60 min for 5–6 or a single 3, 30 min for 7+, configurable); when a target passes, Baton suggests the next tier but never calls anyone. Patients and families can raise a concern too. |
| **SBAR handover** | Rule-based handover built from FHIR data, sickest patient first. Every sentence links to its source, gaps are named instead of filled in, and the incoming nurse's confirmation stores it as a FHIR document. |

Status: **in development.** Done so far: NEWS2 engine, FHIR reading/writing, synthetic ward generator, FHIR server, API, the ward board and the escalation chain, with a Try mode where you play the nurse, the SBAR handover, and a live board.

## How the score is calculated

`Baton.Core` implements NEWS2 as a pure, versioned function (`News2Calculator.Calculate`,
algorithm version `RCP-NEWS2-2017`). Every band boundary of the RCP chart has its own test.

- **Seven parameters:** respiration rate, SpO2 (Scale 1 or Scale 2), air or oxygen, systolic blood
  pressure, pulse, consciousness (ACVPU) and temperature. Total 0–20.
- **Four risk levels and five responses:** total 0 (routine, at least 12-hourly) · 1–4 (ward-based
  response, at least 4–6-hourly) · a score of 3 in any single parameter (urgent ward-based response,
  at least hourly) · 5–6 (urgent response, at least hourly) · 7 or more (emergency response,
  continuous monitoring).
- **Missing data is never scored as zero.** If any parameter is missing or impossible, there is no
  total; the result lists what is missing, still shows any parameter that already scores 3, and
  gives a `MinimumTotal` (the sum of what could be scored; the real score can only be higher).
  Room air is never assumed.
- **SpO2 Scale 2** is only used with a recorded clinical decision (who and when). Without one,
  SpO2 is not scored; Baton never falls back to Scale 1 silently.
- **Confusion** scores 3 when new or when its onset is unknown, and 0 when known to be chronic.
- **Temperature** is rounded to one decimal (half up) before scoring, and the rounding is noted.

## FHIR

`Baton.Fhir` connects the engine to HL7 FHIR R4 (Firely .NET SDK):

- **Reading:** LOINC-coded vital-sign Observations (respiratory rate 9279-1, SpO2 59408-5/2708-6,
  systolic 8480-6 or the 85354-9 panel, heart rate 8867-4, body temperature 8310-5, oxygen flow 3151-8
  or inspired oxygen 3150-0, consciousness 67775-7/80288-4). For each parameter the most recent reading
  decides. If it cannot be used (no full time, in the future, stale, not measured, inexact, unknown unit,
  conflicting readings at the same time) the parameter stays empty with a note; an older reading is never
  used instead, and room air is never assumed.
- **Writing:** one NEWS2 Observation (SNOMED CT 1104051000000101) with the total in `valueQuantity`, the
  seven sub-scores in `component` (SNOMED CT sub-score codes) and the source readings in
  `derivedFrom`. Sources must belong to the same patient.
- **Escalation chain:** a nurse's concern is a `Flag`, a patient's or family's concern a `Communication`, each call a
  `Task` (status follows the chain; the response target in `restriction.period.end`, the NEWS2 Observation as its
  `focus`), and every resource has a `Provenance` (who, when).
- **Handover:** a confirmed SBAR is a document `Bundle`: the `Composition` first (LOINC 28651-8, the outgoing nurse as
  author, the incoming nurse as `professional` attester, one section per SBAR part with its sources as entries), then
  every resource it references as it stood at the handover moment. A `Provenance` beside it records Baton as the
  draft's assembler.
- **Validation:** every generated example passes the Firely validator in the tests and the official HL7
  validator in CI.

## Run locally

Needs the .NET 10 SDK. Docker is only needed for the FHIR server.

```bash
dotnet test                                   # all tests (the HAPI test runs only with BATON_FHIR_URL set)

# API on the generated in-memory ward, no Docker needed
dotnet run --project src/Baton.Api --launch-profile inmemory
curl "http://localhost:5188/api/ward?at=2026-11-23T23:30:00+03:00"

# Real FHIR server: HAPI FHIR on PostgreSQL (limited to 1.5 CPUs / 1.5 GB; takes about a minute to start)
docker compose up -d
dotnet run --project src/Baton.Scenarios.Cli -- load --fhir http://localhost:8080/fhir --seed 42
dotnet run --project src/Baton.Api --launch-profile hapi
BATON_FHIR_URL=http://localhost:8080/fhir dotnet test --filter HapiIntegrationTests
docker compose stop                           # free the CPU and memory when you are done
```

Endpoints: `GET /health` (shows the data source), `GET /api/ward?at=…`, `GET /api/patients/{id}?at=…`.
`at` must include a time zone offset. All data is synthetic. The in-memory source also takes `Baton:Beds` (1–30,
default 8) and `Baton:TreatmentDelayHours` (default 0), for example `Baton__Beds=30`. Response targets:
`Baton:MediumTargetMinutes` (60) and `Baton:HighTargetMinutes` (30).

## Run the board

Needs Node 22. Start the API (in-memory profile above), then:

```bash
cd web
npm ci
npm run dev        # http://localhost:5173, proxies /api to the API on port 5188
npm test           # component tests (Vitest)
```

**Try mode** (in-memory data only): choose a bed, press *Try* and you are the nurse from that moment. Raise a concern,
call a doctor (on-call, senior or critical care), record the assessment and plan; each action asks you to confirm the
patient and bed. The doctor's arrival is simulated, and a late call delays treatment so the patient gets worse on the
chart (calling earlier than the scenario does not make them better than its timely course). Endpoints: `POST /api/try`,
`POST /api/try/{id}/actions`, `DELETE /api/try/{id}`, and `?try={id}` on the ward and patient endpoints.

**Handover** (*Board | Handover* in the header): the ward ranked sickest first, then by what the record is missing.
Open a patient to read their SBAR for the last 12 hours: the current score and the shift's highest, what drives it,
concerns, every call with its arrival, assessment and plan, the next observation and one "if this, then that" step taken
from the NEWS2 and escalation rules. Each sentence has a *Source* button listing the FHIR records behind it; anything the
record does not hold (no plan, no arrival, an overdue observation) is listed as missing, never guessed. *Taken over*
asks you to confirm the patient and bed, then stores the handover as a FHIR document. In Try mode the handover can be
read but not stored. Endpoints: `GET /api/handover?at=…&lang=…` and `POST /api/handover/{patientId}`.

**Live board:** the board listens to `GET /api/live` (Server-Sent Events) and refreshes by itself when something
changes; the header says whether the live link is on. *Play the shift* runs the ward clock at 1×, 60× or 300× (moving
the slider pauses it), so the night plays out on the cards. Events only say "this patient changed"; the board then asks
the API again, which scores as always.

With the FHIR server, Baton writes four FHIR R4 rest-hook `Subscription`s at start-up (vital-sign Observations,
escalation Tasks, concern Flags, patient/family Communications), and HAPI calls `POST /api/fhir/notify` when one of
them changes. To see a new measurement turn a card red:

```bash
docker compose up -d                       # rest-hook subscriptions are enabled in docker-compose.yml
dotnet run --project src/Baton.Scenarios.Cli -- load --fhir http://localhost:8080/fhir
dotnet run --project src/Baton.Api --launch-profile hapi
# open the board at 15:15, then write a new set for bed 03 at 15:10:
dotnet run --project src/Baton.Scenarios.Cli -- observe --bed 03 --at 2026-11-23T15:10:00+03:00 \
  --rr 26 --spo2 91 --o2 2 --sbp 95 --hr 118 --temp 38.9 --acvpu A --fhir http://localhost:8080/fhir
```

The escalation chain is read from FHIR for both data sources. When the API reads from the FHIR server, other events
(Baton's alerts, notes) are not stored there yet, and the patient screen says so.

## Tech stack

ASP.NET Core (C#) with the Firely .NET SDK · React + TypeScript (Vite) ·
PostgreSQL + HAPI FHIR server · a seeded scenario generator for synthetic data ·
HL7 FHIR R4, LOINC, CDS Hooks, SMART on FHIR · Docker, GitHub Actions, AWS.

## Safety and scope

- Not a medical device. Not for clinical use. Synthetic data only.
- Software that raises time-sensitive clinical alerts is likely to be regulated as a medical device
  (for example under EU MDR Rule 11). Baton is a demonstration and will stay one.
- Only adults (16+) are in scope of NEWS2. NEWS2 is not validated for pregnancy or children.

## NEWS2 attribution

Baton's scoring follows the NEWS2 chart published by the Royal College of Physicians:

> Royal College of Physicians. *National Early Warning Score (NEWS) 2.* Updated report of a working
> party. London: RCP, 2017.

NEWS2 is © Royal College of Physicians 2017. Baton does not reproduce or redesign the official
NEWS2 chart. Baton is not affiliated with or endorsed by the RCP.

## Licence

[Apache License 2.0](LICENSE). The software is provided "as is", without warranty; it is not a medical
device and must not be used for patient care. Third-party components keep their own licences
(Firely .NET SDK: BSD-3-Clause; HAPI FHIR: Apache-2.0).
