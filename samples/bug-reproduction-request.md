---
title: Bug Reproduction Request
description: Turns a vague bug report into a minimal, numbered reproduction with environment details.
tags:
  - review
  - copilot
collection: Engineering
rating: 4
---

# Prompt

Here is a bug report for {{repo_name}}:

---
{{report}}
---

Rewrite it as a minimal reproduction: numbered steps, expected vs actual result, environment (OS, version, browser). If a step is ambiguous, list the assumption you made instead of guessing silently.

# Notes

"List the assumption instead of guessing silently" is doing all the work here.
