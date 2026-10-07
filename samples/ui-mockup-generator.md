---
title: UI Mockup Generator
description: Describes a screen in enough structural detail that a mockup tool produces something usable on the first pass.
tags:
  - design
  - mockup
  - claude
collection: Design
favorite: true
rating: 4
---

# Prompt

Design a high-fidelity mockup for {{project_name}}.

Screen: {{screen_name}}
Audience: {{audience}}

Lay out the screen as regions first (header, primary content, secondary panel), then specify each region: controls, states, copy. Use the existing design tokens and name every component you reuse. Flag anything that needs a new component.

# Notes

Regions first stops the model from inventing layout mid-stream.
