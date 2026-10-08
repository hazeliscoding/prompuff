---
type: workflow
title: Angular upgrade, start to finish
description: Plan the upgrade, carry it out one phase at a time, then check nothing broke.
---

# Angular upgrade, start to finish

Plan the upgrade, carry it out one phase at a time, then check nothing broke.

## Step 1: Angular Upgrade Planner

```prompt
You are a senior Angular engineer planning an upgrade of {{repo_name}} to Angular {{target_version}}.

First, list the current major dependencies that will block the upgrade. Then produce a phased plan:

1. Pre-flight: tooling, Node and TypeScript versions
2. Framework bump, one major at a time
3. Deprecated API replacements, grouped by module
4. Verification: tests, build, bundle size

For each phase, call out breaking changes and the `ng update` command to run. Package manager: {{package_manager}}. Keep the tone {{tone}}. End with a risk table.
```

> **Hands off:** the phased plan. Paste it into `{{upgrade_plan}}` for the next step.

## Step 2: Angular Upgrade Phase Runner

```prompt
You are upgrading {{repo_name}} to Angular {{target_version}} with {{package_manager}}. This is the plan we agreed on:

{{upgrade_plan}}

Carry out phase {{phase}} only. Run each `ng update` command from the plan, fix what it breaks, and stop at the end of the phase. Commit the work in small, reviewable steps, and list anything you had to decide that the plan didn't cover.
```

> **Hands off:** the list of commits and open decisions. Paste it into `{{phase_report}}` for the check.

## Step 3: Angular Upgrade Checker

```prompt
Check the Angular {{target_version}} upgrade of {{repo_name}} after this phase:

{{phase_report}}

Run the tests and a production build. Compare the bundle size with the last release, and look for deprecation warnings in the build output. Report what passed, what failed and why, and whether the next phase can start.
```
