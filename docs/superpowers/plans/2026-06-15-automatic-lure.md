# Automatic Lure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Spawn an automatic lure outside the active camera view near Tuna, then make it swim into view and pass close to Tuna.

**Architecture:** `AutomaticLureSpawner` owns spawn timing and camera-relative candidate validation. Each spawned prefab uses `AutomaticLureMotor` to pursue a moving pass-by point near Tuna with a steady, slightly rising retrieve. Lifetime expiry or departure distance starts a steep surface exit, and the lure is destroyed only after leaving the water.

**Tech Stack:** Unity 6, C#, Rigidbody physics, `Camera.WorldToViewportPoint`

---

### Task 1: Automatic lure movement

**Files:**
- Create: `Assets/Scripts/Gameplay/Lure/AutomaticLureMotor.cs`

- [x] Add Rigidbody-driven steady retrieve movement.
- [x] Keep the lure in steady retrieve with configurable visual sway.
- [x] Track a moving pass-by point near Tuna and depart after reaching it.
- [x] Keep normal travel horizontal with a small fixed upward angle.
- [x] Make lifetime expiry and departure distance trigger a steep surface exit.
- [x] Destroy the lure only after it crosses the configured water surface.

### Task 2: Camera-hidden spawning

**Files:**
- Create: `Assets/Scripts/Gameplay/Lure/AutomaticLureSpawner.cs`

- [x] Sample positions within a configurable radius around Tuna.
- [x] Reject positions inside the active camera viewport or overlapping obstacle layers.
- [x] Spawn a lure and configure a pass-by offset near Tuna.
- [x] Provide automatic timing plus a public `SpawnNow` method for final-stage integration.

### Task 3: Build verification

- [x] Compile the new scripts with the Unity-generated project references.
- [x] Confirm the build reports zero compilation errors.
- [x] Skip tests per repository instructions.
