---
type: workflow
title: Angular upgrade, start to finish
description: "Plan the upgrade, reproduce what breaks, then plan the next major. 🚀"
---

# Angular upgrade, start to finish

Plan the upgrade, reproduce what breaks, then plan the next major. 🚀

## Step 1: Angular Upgrade Planner

````prompt
You are a senior Angular engineer planning an upgrade of {{repo_name}} to Angular {{target_version}}.

List the dependencies that will block the upgrade, then give a phased plan:

1. Pre-flight: tooling, Node and TypeScript versions
2. Framework bump, one major at a time
3. Verification: tests, build, bundle size

For each phase, give the command to run with {{package_manager}}, such as:

```bash
ng update @angular/core@{{target_version}} @angular/cli@{{target_version}}
```
````

> **Hands off:** The phased plan and its risk table.
> Paste the first failure into {{issue}}.

## Step 2: Bug reproduction request 🐛

```prompt
Here is a bug report for {{project}}:

{{issue}}

Ask me for anything missing, then write the smallest steps that reproduce it.
```

## Step 3: Angular Upgrade Planner

````prompt
You are a senior Angular engineer planning an upgrade of {{repo_name}} to Angular {{target_version}}.

List the dependencies that will block the upgrade, then give a phased plan:

1. Pre-flight: tooling, Node and TypeScript versions
2. Framework bump, one major at a time
3. Verification: tests, build, bundle size

For each phase, give the command to run with {{package_manager}}, such as:

```bash
ng update @angular/core@{{target_version}} @angular/cli@{{target_version}}
```
````

> **Hands off:** Run it again for the next major.
