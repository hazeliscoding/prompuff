---
title: Claude Code Handoff
description: Packages a design into a handoff brief with files, intent, constraints and the order to build in.
tags:
  - claude
  - review
collection: Handoffs
rating: 4
---

# Prompt

You are picking up the {{project_name}} build from a designer.

Read the attached files first. Then write back a short plan before changing anything: what you will build, in what order, and which decisions you need from me. Tone: {{tone}}. Do not refactor unrelated code.

# Notes

"Write back a plan before changing anything" prevents the big unreviewable first commit.
