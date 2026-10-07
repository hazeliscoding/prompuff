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
---

# Prompt

You are a senior Angular engineer planning an upgrade of {{repo_name}} to Angular {{target_version}}.

First, list the current major dependencies that will block the upgrade. Then produce a phased plan:

1. Pre-flight: tooling, Node and TypeScript versions
2. Framework bump, one major at a time
3. Deprecated API replacements, grouped by module
4. Verification: tests, build, bundle size

For each phase, call out breaking changes and the `ng update` command to run. Package manager: {{package_manager}}. Keep the tone {{tone}}. End with a risk table.

# Notes

Phasing by major version stopped the model from skipping steps. Asking for the exact ng update command made the output immediately actionable.
