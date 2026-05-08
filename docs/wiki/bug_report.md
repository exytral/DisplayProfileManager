# Reporting a Bug

Before filing, check the [existing issues](/issues) to see if it's already been reported.

---

## What to include

A good bug report has three things: what you were doing, what went wrong, and enough files to reproduce or diagnose it.
### 1. Describe the failure

- What did you expect to happen?
- What actually happened?
- Does it happen every time, or intermittently?

### 2. Profile transition

Almost all display switching issues are transition-specific. Include:

- **Source profile** — the profile you were switching *from* (or the state you were in)
- **Destination profile** — the profile you were switching *to*

Attach both `.dpm` files. Profile files are stored at:

```
%AppData%\Roaming\DisplayProfileManager\Profiles\
```

### 3. Log file

DPM logs everything it does during a profile apply. The log file for the day the issue occurred is at:

```
%AppData%\Roaming\DisplayProfileManager\Logs\
```

Attach the relevant daily log file. If the issue is reproducible, reproduce it, then grab the log immediately.

---

## How to file

Use the [bug report form](/issues/new/choose) on GitHub. The form will prompt you for all of the above.
