# Reporting a Bug

Before filing, check the [existing issues](/issues) to see if the problem has already been reported.

---

## What to include

A useful bug report has three things: what you were doing, what went wrong, and enough information to reproduce or diagnose it.

### 1. Describe the failure

- What did you expect to happen?
- What actually happened?
- Does it happen every time, or intermittently?

### 2. Profile transition

Most display-switching problems depend on the transition between two states. Include:

- **Source profile** — the profile you were switching _from_, or the display state you were in
- **Destination profile** — the profile you were switching _to_

Attach both `.dpm` files when applicable. Profile files are stored at:

```text
%AppData%\Roaming\DisplayProfileManager\Profiles\
```

### 3. Log file

The application logs profile-apply activity to a daily log file. The logs are stored at:

```text
%AppData%\Roaming\DisplayProfileManager\Logs\
```

Attach the relevant log file from the day the problem occurred. For a reproducible problem, reproduce it first and then collect the log immediately afterward.

---

## How to file

Use the [bug report form](/issues/new/choose) on GitHub. The form prompts you for the information needed to investigate the problem.