# `intent-cli` And External Automation Role Split

This note explains the current canonical ownership split between `intent-cli` and external / built-in automation.

## Current Canonical Orchestration Owner

External / built-in automation is the current canonical owner of continuous orchestration.

In the current baseline, that means automation is responsible for continuously moving work through the validated loops, including:

- issue selection and issue-to-PR progression
- request-update repair progression
- host-side rereview pickup and closeout progression

## What `intent-cli` Owns Canonically

`intent-cli` remains the canonical owner of the contract and metadata surfaces that define work before automation runs.

Its canonical ownership includes:

- packet generation
- queue lineage
- standalone child issue contract surfaces
- review / closeout metadata surfaces

Those surfaces define what downstream child work means and how accepted state is tracked.

## Why This Split Exists

The current smoke-validated baseline proved that continuous progression can be owned by external / built-in automation while `intent-cli` remains the source of truth for packetized work definition and lineage.

This split avoids treating runtime automation and packet metadata as competing orchestration owners.

## Why Parent-Backed Packetized Child Issue Creation Is Required

Parent-backed packetized child issue creation is required for full deterministic closeout.

That requirement exists because deterministic closeout depends on the parent-backed contract surfaces that `intent-cli` owns, including:

- packet generation that defines the child slice explicitly
- queue lineage that ties the slice back to accepted parent state
- review / closeout metadata that records how the child change should be evaluated and closed out

Without parent-backed packetized child issue creation, automation may still move work forward, but the full deterministic closeout path does not have its canonical contract and lineage surfaces.

## Operator Summary

- External / built-in automation: current canonical owner of continuous orchestration
- `intent-cli`: current canonical owner of packet, queue, issue-contract, and review / closeout metadata surfaces
- Parent-backed packetized child issue creation: required for full deterministic closeout

## Operator Notes

- The current model is an ownership split, not a conflict between two orchestration systems.
- Automation should be read as the runtime progression owner for the current baseline.
- `intent-cli` should be read as the canonical contract and metadata owner for child issue creation and accepted-state tracking.
- This document is documentation-only and does not change prompts, labels, or runtime behavior.
