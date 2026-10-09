---
title: Angular Upgrade Planner
description: Produces a phased migration plan from the current Angular version to a target, with breaking changes called out per phase.
tags:
  - angular
  - migration
  - copilot
collection: Engineering
favorite: true
rating: 5
createdAt: 2026-10-07T09:05:17Z
updatedAt: 2026-10-07T13:47:58Z
---

# Prompt

You are a senior Angular engineer planning an upgrade of {{repo_name}} to Angular {{target_version}}.

List the dependencies that will block the upgrade, then give a phased plan:

1. Pre-flight: tooling, Node and TypeScript versions
2. Framework bump, one major at a time
3. Verification: tests, build, bundle size

For each phase, give the command to run with {{package_manager}}, such as:

```bash
ng update @angular/core@{{target_version}} @angular/cli@{{target_version}}
```

# Notes

Phasing by major version stopped the model from skipping steps.

Asking for the exact command made the output immediately actionable.
