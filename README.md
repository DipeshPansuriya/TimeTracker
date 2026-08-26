# Office Time Tracker

Local-first Windows desktop widget for working hours, work location and timesheet activity.
All data stays on the end user's machine; only the HRMS-required subset is ever sent, and
only after the user reviews it.

## Layout

| Project | What it is |
|---|---|
| `src/TimeTracker.Core` | Domain + time engine. **No UI, no I/O.** |
| `tests/TimeTracker.Core.Tests` | 37 tests, TDD |
| `prototype/index.html` | Driveable behaviour reference — open in a browser |

`TimeTracker.Core` having no UI reference is deliberate: it is what keeps the desktop
framework choice reversible.

## Build

```
dotnet test
```

Requires .NET SDK 10.

## The one idea worth knowing

Worked time is **derived, never counted**:

```
worked = now − timeIn − breaks
```

There is no Start button because there is no counter to start. Sleep, crash and power loss
are all non-events — reopen the widget and the figure is still correct.

## Design notes

Decisions, open questions and the process/workflow diagrams live in the hub workspace, not
here: `_project_timetrack/desktop-widget/` in the AI-Agent-Team repo.
