# Transport Data Quality Review

Review date: 2026-11-04

## Scope and evidence

This review compares `docs/data/verified-transport-data.md` with the checked-in
transport seed and fixture code. It does not independently verify the cited
operator websites, legal registrations, current routes, fares, timetables, or
station addresses. The manifest's verification claims therefore remain
unconfirmed by this repository review.

## Findings

| Area | Manifest/reference claim | Current repository state | Classification |
|---|---|---|---|
| Operators | Six named operators and corridor claims | Seed script resolves five operator codes, but does not validate their legal identity or evidence | Requires external/source verification |
| Route directions | Four corridors, both directions | Seeded `RouteStops` are linked only for `TRONGMINH-SGN-DLI`, `XENHA-SGN-NHA`, and `SAOVIET-HAN-SAP` | Incomplete |
| Stops | Operator-specific named pickup and drop-off stops | The seed adds points for the five operators, while stop linking omits reverse routes and most listed corridors | Incomplete |
| Stop permissions | Direction-specific pickup/drop-off rules | Seeded points use `PointType = 3` (Both); linked `RouteStops` apply per-stop pickup/drop-off flags for the three outbound directions only | Mixed: point capability is broad; route flags are partial |
| Schedule | Reference departures are listed in the manifest | The seed does not import the manifest departure times. `SandboxFixtureService` generates rolling generic demo departures | Not imported; demo-only data must not be presented as a published schedule |
| Fare | Reference fares are listed in the manifest | The seed does not import those fares; sandbox fixture pricing is generated separately | Not imported; must not be presented as a verified fare |
| Invalid combinations | The manifest lists route/operator combinations to exclude | A separate safeguarded deactivation script exists; no script was run as part of this review | Exclusion state not verified |
| Freshness | Manifest says last verified 2026-10-08 | The listed verification date is 27 days before this review and sources were not independently checked | Re-verification required before publication |

## Safe disposition

- Do not run or expand the seed scripts based on this manifest alone.
- Do not activate routes or turn reference schedules into bookable departures.
- Keep synthetic departures and payment flows explicitly sandbox-only.
- Require source evidence for each operator, route direction, stop address,
  departure time, and fare before importing that value.
- Represent unavailable values as unknown rather than substituting generic
  stops, times, fares, or contact details.
- Validate point capability and direction-specific `RouteStop` permissions
  together; a point marked as usable for both does not itself authorize both
  actions on every route.

## Follow-up needed

Obtain current operator-published evidence and an approved data owner to review
each manifest row. Then reconcile the evidence against the existing database
records in `TripgoDb_IsolatedTest` only. No database was queried or changed for
this report.
